// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ArterialPulseSpecifications
{
    private static RegularPhysiologyPlan Timeline => new(0, 800_000_000, 160_000_000, 80_000_000,
        240_000_000, 3_750_000_000, 1_875_000_000);
    private static readonly Guid Ecg = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Abp = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static ArterialPulsePlan Pulse => new(80_000_000, 600_000_000, 80, 40);
    public static Specification[] All =>
    [
        new(nameof(ArterialSeedRetainsPressureScaleAndMechanicalTiming), ArterialSeedRetainsPressureScaleAndMechanicalTiming),
        new(nameof(ArterialNotchAndBaselineAreExplicit), ArterialNotchAndBaselineAreExplicit),
        new(nameof(ArterialWireAndRestorePreservePressure), ArterialWireAndRestorePreservePressure),
        new(nameof(ArterialInvalidPlansAndLateFailureAreAtomic), ArterialInvalidPlansAndLateFailureAreAtomic),
    ];

    private static IReadOnlyList<PhysiologySignalSample> Samples(ArterialPulsePlan pulse)
    {
        var channel = pulse.CreateChannel(Timeline, Abp, 0);
        return PhysiologySignalGenerator.Start(Timeline, channel.Plane.ProfileId, 1, channel.Bands)
            .GenerateBefore(1_600_000_000, 200, 100);
    }

    private static void ArterialSeedRetainsPressureScaleAndMechanicalTiming()
    {
        var channel = Pulse.CreateChannel(Timeline, Abp, 7);
        var samples = Samples(Pulse);
        Check.That(channel.Plane.ScaleNumerator == 1 && channel.Plane.ScaleDenominator == 100 &&
            channel.Plane.OffsetNumerator == 80 && channel.Plane.OffsetDenominator == 1,
            "affine wire pressure is baseline plus hundredth-mmHg counts");
        Check.That(samples.Take(41).All(sample => sample.NormalizedValue == 0) &&
            samples[53].NormalizedValue == 4000 && samples.All(sample => sample.NormalizedValue is >= 0 and <= 4000),
            "pressure begins after mechanical plus transit delay and reaches the explicit 120mmHg illustration peak");
        var electricalOnly = EventWaveformComposition.Restore(new(channel.Bands,
            [new(160_000_000, PhysiologyCycleEventKind.VentricularElectrical, 0)]));
        Check.That(electricalOnly.EvaluateAt(416_000_000) == 0, "electrical markers cannot create arterial pulse pressure");
    }

    private static void ArterialNotchAndBaselineAreExplicit()
    {
        var samples = Samples(Pulse);
        Check.That(samples[76].NormalizedValue < samples[75].NormalizedValue &&
            samples[76].NormalizedValue < samples[78].NormalizedValue && samples[78].NormalizedValue < samples[53].NormalizedValue,
            "documented adapted notch falls locally then recovers below the systolic peak");
        Check.That(samples.Select(sample => sample.NormalizedValue).SequenceEqual(Samples(Pulse with { BaselineMmHg = 60 }).Select(sample => sample.NormalizedValue)),
            "baseline is an affine pressure component, not pulse morphology or display gain");
        Check.That(Samples(Pulse with { PulseHeightMmHg = 0 }).All(sample => sample.NormalizedValue == 0),
            "zero pulse height retains the caller baseline without inventing pulse events");
    }

    private static PhysiologyWaveformGroup Group() => PhysiologyWaveformGroup.Start(Ecg, Abp, 1, 1, 1, 0, 16,
        [new(Timeline, new(Ecg, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 10, 0),
         Pulse.CreateChannel(Timeline, Abp, 7)]);

    private static void ArterialWireAndRestorePreservePressure()
    {
        var boundary = Group();
        Check.That(boundary.AdvanceTo(271_999_999, 68, 1, 100).Count == 0, "pressure plane retains its native80ms processing delay");
        Check.That(boundary.AdvanceTo(272_000_000, 1, 1, 100).Count == 1, "first complete block releases at272ms");
        var expected = Group().AdvanceTo(1_800_000_000, 450, 9, 100);
        var split = Group();
        List<byte[]> actual = [];
        for (int step = 1; step <= 9; step++)
        {
            actual.AddRange(split.AdvanceTo(step * 200_000_000L, 50, 1, 100));
            split = PhysiologyWaveformGroup.Restore(split.CaptureState());
        }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            "ECG/ABP blocks preserve exact bytes across active pulse and delay recovery");
        var planes = actual.Select(bytes => WaveformEnvelopeCodec.Decode(bytes).Planes.Single(plane => plane.ChannelId == Abp)).ToArray();
        Check.That(planes.All(plane => plane.ScaleNumerator == 1 && plane.ScaleDenominator == 100 && plane.OffsetNumerator == 80 &&
            plane.OffsetDenominator == 1 && plane.SampleRateNumerator == 125) &&
            planes.SelectMany(plane => plane.Samples).SequenceEqual(Samples(Pulse).Select(sample => sample.NormalizedValue)),
            "wire pressure metadata and counts preserve physical baseline and native amplitude resolution");
    }

    private static void ArterialInvalidPlansAndLateFailureAreAtomic()
    {
        foreach (var invalid in new[] { Pulse with { TransitDelayNs = -1 }, Pulse with { DurationNs = 0 },
            Pulse with { DurationNs = 800_000_001 }, Pulse with { BaselineMmHg = -1 },
            Pulse with { PulseHeightMmHg = 328 }, Pulse with { TransitDelayNs = long.MaxValue } })
        {
            bool rejected = false;
            try { invalid.CreateChannel(Timeline, Abp, 0); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "ArterialPulse.InvalidPlan"; }
            Check.That(rejected, "invalid support, overlap or raw pressure range rejects before channel publication");
        }
        var group = Group();
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(1_800_000_000, 450, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "late block rejection preserves both channel cursors");
    }
}
