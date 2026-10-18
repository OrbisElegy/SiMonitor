// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class EcgInterferenceSpecifications
{
    private static readonly string Seed = new('0', 64);
    internal static Specification[] All =>
    [
        new(nameof(InterferenceIsIndexedBoundedAndSeeded), InterferenceIsIndexedBoundedAndSeeded),
        new(nameof(InterferenceSurvivesPendingCheckpointsAndChanges), InterferenceSurvivesPendingCheckpointsAndChanges),
        new(nameof(InterferenceReachesLiveLeadsAndRecords), InterferenceReachesLiveLeadsAndRecords)
    ];

    private static void InterferenceIsIndexedBoundedAndSeeded()
    {
        var noise = new SeededEcgInterference(Seed, 5000, 300, 2000, 10000);
        var replay = new SeededEcgInterference(Seed, 5000, 300, 2000, 10000);
        var changed = new SeededEcgInterference(new string('1', 64), 5000, 300, 2000, 10000);
        long[] times = Enumerable.Range(0, 5000).Select(i => i * 4_000_000L).Append(long.MaxValue).ToArray();
        long[] values = times.Select(noise.LeadIIMicrovoltsQ32).ToArray();
        Check.That(values.SequenceEqual(times.Reverse().Select(replay.LeadIIMicrovoltsQ32).Reverse()),
            "same seed and times replay independently of call order, including extreme times");
        Check.That(!values.SequenceEqual(times.Select(changed.LeadIIMicrovoltsQ32)) &&
            values.All(value => Math.Abs(value) <= 7000L * FixedPointMath.Q32One), "different seed changes bounded interference");
        var baseline = new SeededEcgInterference(Seed, 0, 1000, 2000, 10000);
        Check.That(baseline.LeadIIMicrovoltsQ32(0) == 7_565_425_374_000 && baseline.LeadIIMicrovoltsQ32(10_000_000_000) == 579_078_564_000,
            "version-one seed derivation and indexed draws retain their golden values");
        Check.That(Math.Abs(baseline.LeadIIMicrovoltsQ32(10_000_000_000) - baseline.LeadIIMicrovoltsQ32(9_999_999_999)) < FixedPointMath.Q32One,
            "baseline is continuous at random control points without overflowing interpolation");
        var spikes = new SeededEcgInterference(Seed, 1000, 1000, 0, 200);
        long[] spikeValues = times.Take(5000).Select(time => spikes.ElectrodeMicrovoltsQ32(EcgElectrode.RA, time)).ToArray();
        Check.That(spikeValues.Count(value => value != 0) is >= 200 and <= 600,
            "full probability emits one narrow spike per 100 ms, not continuous noise");
        var disabled = new SeededEcgInterference(Seed, 5000, 0, 0, 200);
        Check.That(times.All(time => disabled.LeadIIMicrovoltsQ32(time) == 0), "zero probability and zero baseline disable artifacts");
        foreach (var action in new Action[]
        {
            () => _ = new SeededEcgInterference(Seed, -1, 0, 0, 200),
            () => _ = new SeededEcgInterference(Seed, 0, 1001, 0, 200),
            () => _ = new SeededEcgInterference(Seed, 0, 0, 2001, 200),
            () => _ = new SeededEcgInterference(Seed, 0, 0, 0, 10001),
            () => _ = new SeededEcgInterference("invalid", 0, 0, 0, 200)
        })
        {
            bool rejected = false;
            try { action(); }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "invalid interference inputs rejected before generation");
        }
    }

    private static void InterferenceSurvivesPendingCheckpointsAndChanges()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default with
        { EcgInterference = new(Seed, 1200, 200, 300, 2300) };
        var whole = PhysiologyIllustrationSource.Create(configuration);
        var split = PhysiologyIllustrationSource.Create(configuration);
        whole.AdvanceTo(196_000_000, 100, 1, 100);
        split.AdvanceTo(68_000_000, 100, 1, 100);
        split = PhysiologyWaveformGroup.Restore(JsonSerializer.Deserialize<PhysiologyWaveformGroupState>(
            JsonSerializer.Serialize(split.CaptureState()))!);
        split.AdvanceTo(196_000_000, 100, 1, 100);
        // Change with a partially assembled recording packet: old samples retain old settings.
        var clean = PhysiologyIllustrationSource.Create();
        whole.ContinueWith(clean);
        split.ContinueWith(clean);
        split = PhysiologyWaveformGroup.Restore(split.CaptureState());
        var first = whole.AdvanceTo(400_000_000, 100, 2, 100, out var taps);
        var second = split.AdvanceTo(400_000_000, 100, 2, 100, out var replay);
        Check.That(taps.SequenceEqual(replay) && first.Count == second.Count && first.Zip(second).All(pair => pair.First.SequenceEqual(pair.Second)),
            "batching, pending checkpoint restore and disabling interference preserve identical wire bytes");
        Check.That(SinusIllustrationConfiguration.Apply(configuration).EcgInterference!.Equals(configuration.EcgInterference),
            "conversion preserves the acquisition interference configuration");
    }

    private static void InterferenceReachesLiveLeadsAndRecords()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default with
        { EcgInterference = new(Seed, 1200, 250, 300, 1700) };
        var noisy = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), enableTwelveLead: true);
        var clean = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default, MonitorDisplayConfiguration.Default(), enableTwelveLead: true);
        var replay = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), enableTwelveLead: true);
        for (int i = 0; i < 40; i++) { noisy.Advance(50_000_000); clean.Advance(50_000_000); }
        for (int i = 0; i < 80; i++) { replay.Advance(25_000_000); }
        Check.That(noisy.Samples(0, 0, long.MaxValue).SequenceEqual(replay.Samples(0, 0, long.MaxValue)) &&
            !noisy.Samples(0, 0, long.MaxValue).SequenceEqual(clean.Samples(0, 0, long.MaxValue)),
            "live ECG changes reproducibly across session batch sizes");
        for (int channel = 1; channel < 7; channel++)
        { Check.That(noisy.Samples(channel, 0, long.MaxValue).SequenceEqual(clean.Samples(channel, 0, long.MaxValue)), "interference leaves other signal sources unchanged"); }
        Check.That(noisy.Samples(0, 0, long.MaxValue).SequenceEqual(noisy.EcgSamples(EcgLead.II, 0, long.MaxValue)), "monitor and twelve-lead II share acquired interference");
        foreach (var block in noisy.Blocks)
        {
            var plane = block.Planes.Single(item => item.ChannelId == PhysiologyIllustrationSource.ChannelId(0));
            Check.That(plane.Samples.Select(value => (double)value).SequenceEqual(noisy.Samples(0, block.StartSimTimeNs,
                block.StartSimTimeNs + block.DurationNs).Select(sample => sample.Value)), "recorded and immediate voltages agree");
        }
        var source = LiveEcgIllustrationSource.Create(configuration);
        foreach (var sample in source.GenerateBefore(200_000_000, 50, 100))
        {
            Check.That(sample.ExactLeads[EcgLead.I].Numerator + sample.ExactLeads[EcgLead.III].Numerator == sample.ExactLeads[EcgLead.II].Numerator,
                "electrode noise preserves the exact limb-lead identity");
        }
    }
}
