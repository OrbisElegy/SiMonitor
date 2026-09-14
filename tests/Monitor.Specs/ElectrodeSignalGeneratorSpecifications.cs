// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ElectrodeSignalGeneratorSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    private static ElectrodeWaveformPlan[] Electrodes(long ra = 0, long la = Q + 1) =>
        Enum.GetValues<EcgElectrode>().Select(electrode => new ElectrodeWaveformPlan(electrode,
            new EventWaveformBand[] { new(PhysiologyCycleEventKind.VentricularElectrical, 0, 80_000_000,
                new long[] { 0, electrode == EcgElectrode.RA ? ra : electrode == EcgElectrode.LA ? la : 0,
                    electrode == EcgElectrode.RA ? ra : electrode == EcgElectrode.LA ? la : 0, 0 }) })).ToArray();
    private static ElectrodeSignalGenerator Source(ElectrodeWaveformPlan[]? electrodes = null) =>
        ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, electrodes ?? Electrodes());
    public static Specification[] All =>
    [
        new(nameof(ProjectedSamplesShareClockAndRoundOnlyOnce), ProjectedSamplesShareClockAndRoundOnlyOnce),
        new(nameof(ProjectedSamplingPartitionsRestoreAcrossActiveWaves), ProjectedSamplingPartitionsRestoreAcrossActiveWaves),
        new(nameof(ProjectedSamplingFailuresPreserveBothClocks), ProjectedSamplingFailuresPreserveBothClocks),
        new(nameof(ProjectedSamplingOwnsAndRevalidatesCheckpoint), ProjectedSamplingOwnsAndRevalidatesCheckpoint),
    ];

    private static void Equal(ElectrodeSignalSample left, ElectrodeSignalSample right)
    {
        Check.That(left.Tick == right.Tick && left.MicrovoltValues.SequenceEqual(right.MicrovoltValues),
            "sample indices, times and all twelve quantized values must match");
        foreach (EcgLead lead in Enum.GetValues<EcgLead>())
        { Check.That(left.ExactLeads[lead] == right.ExactLeads[lead], "exact lead evidence must match across partitions"); }
    }

    private static void ProjectedSamplesShareClockAndRoundOnlyOnce()
    {
        var samples = Source().GenerateBefore(200_000_000, 50, 100);
        Check.That(samples.Count == 50, "200ms monitoring source contains 50 synchronous frames");
        for (int index = 0; index < samples.Count; index++)
        {
            var sample = samples[index];
            Check.That(sample.Tick.SampleIndex == (ulong)index && sample.Tick.SimTimeNs == index * 4_000_000L &&
                sample.MicrovoltValues.Count == 12, "all lead values belong to one native sample tick");
            var exact = sample.ExactLeads;
            Check.That(exact[EcgLead.III].Numerator == exact[EcgLead.II].Numerator - exact[EcgLead.I].Numerator &&
                2 * exact[EcgLead.AVR].Numerator == -exact[EcgLead.I].Numerator - exact[EcgLead.II].Numerator,
                "source projection retains exact lead identities");
        }
        Check.That(samples[45].MicrovoltValues[(int)EcgLead.AVR] == -1 &&
            samples[45].ExactLeads[EcgLead.AVR].ToQ32() == -Q / 2,
            "a value just below minus half a microvolt rounds once to minus one, not twice to zero");
    }

    private static void ProjectedSamplingPartitionsRestoreAcrossActiveWaves()
    {
        var whole = Source().GenerateBefore(1_600_000_000, 400, 100);
        var source = Source();
        List<ElectrodeSignalSample> split = [];
        foreach (long end in new long[] { 173_000_000, 201_000_000, 817_000_000, 979_000_000, 1_600_000_000 })
        {
            split.AddRange(source.GenerateBefore(end, 400, 100));
            source = ElectrodeSignalGenerator.Restore(source.CaptureState());
        }
        Check.That(split.Count == whole.Count, "non-sample-aligned batch boundaries cannot lose or duplicate frames");
        for (int index = 0; index < whole.Count; index++) { Equal(whole[index], split[index]); }
        Check.That(source.GenerateBefore(1_600_000_000, 1, 100).Count == 0, "equal cursor yields no repeated sample");
    }

    private static void ProjectedSamplingFailuresPreserveBothClocks()
    {
        var source = Source();
        var before = source.CaptureState();
        foreach (Action action in new Action[]
        {
            () => source.GenerateBefore(200_000_000, 49, 100),
            () => source.GenerateBefore(200_000_000, 50, 1),
            () => source.GenerateBefore(-1, 50, 100),
            () => source.GenerateBefore(200_000_000, 50, 100, new CancellationToken(true)),
        })
        {
            bool rejected = false;
            try { action(); }
            catch (Exception exception) when (exception is ArgumentException or OperationCanceledException) { rejected = true; }
            Check.That(rejected && source.CaptureState().Clock == before.Clock && source.CaptureState().Timeline == before.Timeline,
                "capacity, regression and cancellation must leave both cursors unchanged");
        }
        source = Source(Electrodes(-30_000 * Q, 30_000 * Q));
        before = source.CaptureState();
        bool overflow = false;
        try { _ = source.GenerateBefore(200_000_000, 50, 100); }
        catch (ElectrodeSignalException exception) { overflow = exception.ReasonCode == "ElectrodeSignal.AmplitudeOverflow"; }
        Check.That(overflow && source.CaptureState().Clock == before.Clock && source.CaptureState().Timeline == before.Timeline,
            "a late projected overflow discards earlier valid frames and all clock changes");
        Check.That(source.GenerateBefore(160_000_000, 40, 100).Count == 40, "failed batches do not poison subsequent valid generation");
    }

    private static void ProjectedSamplingOwnsAndRevalidatesCheckpoint()
    {
        var electrodes = Electrodes();
        var source = Source(electrodes);
        ((long[])electrodes[1].Bands[0].TableQ32)[1] = 999 * Q;
        var state = source.CaptureState();
        foreach (var invalid in new[] { state with { Timeline = state.Timeline with { CursorSimTimeNs = 1 } },
            state with { Clock = state.Clock with { ProfileId = "AcqResp125@1" } },
            state with { Electrodes = electrodes.Take(9).ToArray() } })
        {
            bool rejected = false;
            try { _ = ElectrodeSignalGenerator.Restore(invalid); }
            catch (ElectrodeSignalException exception) { rejected = exception.ReasonCode == "ElectrodeSignal.InvalidCheckpoint"; }
            Check.That(rejected, "restore must revalidate clock agreement, ECG profile and complete topology");
        }
        var expected = Source().GenerateBefore(200_000_000, 50, 100);
        var actual = ElectrodeSignalGenerator.Restore(state).GenerateBefore(200_000_000, 50, 100);
        for (int index = 0; index < expected.Count; index++) { Equal(expected[index], actual[index]); }
    }
}
