// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PulmonaryArterySpecifications
{
    private static RegularPhysiologyPlan Timeline => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    private static PulmonaryArteryPulsePlan Pulse => new(40_000_000, 640_000_000, 10, 15);
    private static readonly Guid Ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Pa = Guid.Parse("66666666-6666-4666-8666-666666666666");
    public static Specification[] All =>
    [
        new(nameof(PulmonarySeedHasIndependentPeakNotchAndTail), PulmonarySeedHasIndependentPeakNotchAndTail),
        new(nameof(PulmonaryTransitAndBaselineRemainExplicit), PulmonaryTransitAndBaselineRemainExplicit),
        new(nameof(PulmonaryNativePressureSurvivesWireRecovery), PulmonaryNativePressureSurvivesWireRecovery),
        new(nameof(PulmonaryInvalidPlansAndLateFailuresRejectAtomically), PulmonaryInvalidPlansAndLateFailuresRejectAtomically),
    ];

    private static IReadOnlyList<PhysiologySignalSample> Samples(PulmonaryArteryPulsePlan pulse)
    {
        var channel = pulse.CreateChannel(Timeline, Pa, 0);
        return PhysiologySignalGenerator.Start(Timeline, channel.Plane.ProfileId, 1, channel.Bands).GenerateBefore(1_600_000_000, 200, 100);
    }

    private static void PulmonarySeedHasIndependentPeakNotchAndTail()
    {
        var samples = Samples(Pulse);
        Check.That(samples.Take(36).All(sample => sample.NormalizedValue == 0) && samples[50].NormalizedValue == 1500 &&
            samples[68].NormalizedValue < samples[73].NormalizedValue && samples[73].NormalizedValue < samples[50].NormalizedValue,
            "PA has explicit mechanical arrival, systolic peak and a smaller notch recovery");
        short[] tail = samples.Skip(84).Take(32).Select(sample => sample.NormalizedValue).ToArray();
        Check.That(tail.Zip(tail.Skip(1)).All(pair => pair.First >= pair.Second) && tail[^1] == 0 &&
            samples.All(sample => sample.NormalizedValue is >= 0 and <= 1500), "adapted source tail does not inherit oscillation or negative pulse increments");
        var abp = new ArterialPulsePlan(40_000_000, 640_000_000, 10, 15).CreateChannel(Timeline, Pa, 0);
        Check.That(!abp.Bands[0].TableQ32.SequenceEqual(Pulse.CreateChannel(Timeline, Pa, 0).Bands[0].TableQ32),
            "same timing and pressure inputs retain an independent PA shape rather than scaled ABP");
    }

    private static void PulmonaryTransitAndBaselineRemainExplicit()
    {
        var samples = Samples(Pulse);
        var delayed = Samples(Pulse with { TransitDelayNs = 80_000_000 });
        Check.That(samples.Take(195).Zip(delayed.Skip(5)).All(pair => pair.First.NormalizedValue == pair.Second.NormalizedValue),
            "own PA transit shifts native pressure without changing cardiac timing");
        Check.That(samples.Select(sample => sample.NormalizedValue).SequenceEqual(Samples(Pulse with { BaselineMmHg = 5 }).Select(sample => sample.NormalizedValue)),
            "baseline remains an affine pressure component independent of pulse shape");
        var channel = Pulse.CreateChannel(Timeline, Pa, 0);
        var electrical = EventWaveformComposition.Restore(new(channel.Bands, [new(160_000_000, PhysiologyCycleEventKind.VentricularElectrical, 0)]));
        Check.That(electrical.EvaluateAt(400_000_000) == 0, "electrical events alone cannot produce pulmonary pulse pressure");
    }

    private static PhysiologyWaveformGroup Group() => PhysiologyWaveformGroup.Start(Ecg, Pa, 1, 1, 1, 0, 16,
        [new(Timeline, new(Ecg, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 10, 0),
         Pulse.CreateChannel(Timeline, Pa, 3)]);

    private static void PulmonaryNativePressureSurvivesWireRecovery()
    {
        var boundary = Group();
        Check.That(boundary.AdvanceTo(271_999_999, 68, 1, 100).Count == 0 && boundary.AdvanceTo(272_000_000, 1, 1, 100).Count == 1,
            "PA source retains native125Hz and80ms processing delay");
        var expected = Group().AdvanceTo(1_800_000_000, 450, 9, 100);
        var group = Group();
        List<byte[]> actual = [];
        for (int step = 1; step <= 9; step++)
        {
            actual.AddRange(group.AdvanceTo(step * 200_000_000L, 50, 1, 100));
            group = PhysiologyWaveformGroup.Restore(group.CaptureState());
        }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            "active PA pulse and processing delay preserve exact mixed ECG wire bytes across restore");
        var planes = actual.Select(bytes => WaveformEnvelopeCodec.Decode(bytes).Planes.Single(plane => plane.ChannelId == Pa)).ToArray();
        Check.That(planes.All(plane => plane.ScaleNumerator == 1 && plane.ScaleDenominator == 100 && plane.OffsetNumerator == 10 &&
            plane.OffsetDenominator == 1 && plane.SampleRateNumerator == 125) &&
            planes.SelectMany(plane => plane.Samples).SequenceEqual(Samples(Pulse).Select(sample => sample.NormalizedValue)),
            "PA wire pressure is its own10mmHg baseline plus hundredth-mmHg increments");
    }

    private static void PulmonaryInvalidPlansAndLateFailuresRejectAtomically()
    {
        foreach (var invalid in new[] { Pulse with { TransitDelayNs = -1 }, Pulse with { TransitDelayNs = long.MaxValue },
            Pulse with { DurationNs = 0 }, Pulse with { DurationNs = 800_000_001 }, Pulse with { BaselineMmHg = -1 }, Pulse with { PulseHeightMmHg = 328 } })
        {
            bool rejected = false;
            try { invalid.CreateChannel(Timeline, Pa, 0); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "PulmonaryArtery.InvalidPlan"; }
            Check.That(rejected, "invalid support and pressure bounds reject before channel publication");
        }
        var group = Group();
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(1_800_000_000, 450, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "late failure preserves both source and delay cursors");
    }
}
