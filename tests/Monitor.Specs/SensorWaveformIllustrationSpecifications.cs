// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class SensorWaveformIllustrationSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(OverdampingChangesOnlyMeasuredArterialContour), OverdampingChangesOnlyMeasuredArterialContour),
        new(nameof(LowPerfusionPreservesPulseTimingAndOtherChannels), LowPerfusionPreservesPulseTimingAndOtherChannels),
        new(nameof(MotionContaminationPropagatesPoorOpticalQuality), MotionContaminationPropagatesPoorOpticalQuality),
        new(nameof(SensorModesPreservePendingSamplesAcrossContinuationAndRestore), SensorModesPreservePendingSamplesAcrossContinuationAndRestore),
        new(nameof(OverdampingReconstructsEarlierPressureDefinitions), OverdampingReconstructsEarlierPressureDefinitions),
        new(nameof(SensorIllustrationValidationRejectsInvalidModesAndChannels), SensorIllustrationValidationRejectsInvalidModesAndChannels),
        new(nameof(CardiacTherapyRetainsNonCardiacTemplates), CardiacTherapyRetainsNonCardiacTemplates),
        new(nameof(PlethGainTransitionsPreserveTheCurrentPulse), PlethGainTransitionsPreserveTheCurrentPulse),
        new(nameof(PlethRapidReversalsKeepTheCurrentBlend), PlethRapidReversalsKeepTheCurrentBlend),
        new(nameof(PlethTransitionsReplayAcrossPartitionsAndCheckpoints), PlethTransitionsReplayAcrossPartitionsAndCheckpoints),
        new(nameof(MotionExitRetainsPoorQualityUntilTheArtifactEnds), MotionExitRetainsPoorQualityUntilTheArtifactEnds),
        new(nameof(PlethGainTransitionsDoNotMultiplyDetectedPulses), PlethGainTransitionsDoNotMultiplyDetectedPulses),
    ];

    private static void PlethGainTransitionsPreserveTheCurrentPulse()
    {
        long boundary = RisingPlethTimeNs();
        var source = PhysiologyIllustrationSource.Create();
        _ = AdvancePleth(source, boundary);
        var reference = source.Fork();
        source.ContinueWith(PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with
        { PlethShape = PlethIllustration.LowPerfusion }));
        var reduced = AdvancePleth(source, boundary + 1_600_000_000).Samples;
        var original = AdvancePleth(reference, boundary + 1_600_000_000).Samples;
        Check.That(reduced[0] == original[0] && reduced[0].NormalizedValue > 200,
            "a low-perfusion switch on the upstroke preserves the first acquired point instead of dropping it by 92 percent");
        double[] ratios = reduced.Zip(original)
            .Where(pair => pair.First.SourceSimTimeNs < boundary + 1_000_000_000 && pair.Second.NormalizedValue > 100)
            .Select(pair => pair.First.NormalizedValue / (double)pair.Second.NormalizedValue).ToArray();
        Check.That(ratios.Any(ratio => ratio is > 0.25 and < 0.75) &&
            ratios.All(ratio => ratio is >= 0.07 and <= 1.01) &&
            ratios.Zip(ratios.Skip(1)).All(pair => pair.Second <= pair.First + 0.02),
            "the current pulse passes through intermediate gains and approaches low perfusion without envelope overshoot");
        Check.That(reduced.Zip(original).Where(pair => pair.First.SourceSimTimeNs >= boundary + 1_000_000_000)
            .All(pair => Math.Abs(pair.First.NormalizedValue - pair.Second.NormalizedValue * 0.08) <= 1),
            "one second after selection the pulse has the same low-perfusion amplitude as an initially selected template");

        var stillLow = source.Fork();
        source.ContinueWith(PhysiologyIllustrationSource.Create());
        var recovered = AdvancePleth(source, boundary + 3_200_000_000).Samples;
        var low = AdvancePleth(stillLow, boundary + 3_200_000_000).Samples;
        var normal = AdvancePleth(reference, boundary + 3_200_000_000).Samples;
        Check.That(recovered[0] == low[0], "returning to reference begins at the current low-perfusion pulse value");
        Check.That(recovered.Zip(normal).Where(pair => pair.First.SourceSimTimeNs >= boundary + 2_600_000_000)
            .All(pair => pair.First == pair.Second), "the reverse transition restores the same pulse phase and amplitude after one second");
    }

    private static void PlethRapidReversalsKeepTheCurrentBlend()
    {
        long boundary = RisingPlethTimeNs();
        var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with
        { PlethShape = PlethIllustration.LowPerfusion });
        _ = AdvancePleth(source, boundary);
        source.ContinueWith(PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with
        { PlethShape = PlethIllustration.MotionArtifact }));
        _ = AdvancePleth(source, boundary + 400_000_000);
        var continuingBlend = source.Fork();
        source.ContinueWith(PhysiologyIllustrationSource.Create());
        var unchangedSelection = source.Fork();
        source.ContinueWith(PhysiologyIllustrationSource.Create());
        var reversed = AdvancePleth(source, boundary + 1_600_000_000);
        var continued = AdvancePleth(continuingBlend, boundary + 408_000_000).Samples;
        var unchanged = AdvancePleth(unchangedSelection, boundary + 1_600_000_000);
        Check.That(reversed.Samples[0] == continued[0],
            "a rapid reversal retains both the partially restored pulse and the partially introduced motion artifact");
        Check.That(SameWire(reversed.Wire, unchanged.Wire), "reapplying the current target does not restart an in-progress transition");
        var normal = AdvancePleth(PhysiologyIllustrationSource.Create(), boundary + 1_600_000_000).Samples
            .Where(sample => sample.SourceSimTimeNs >= boundary + 1_400_000_000).ToArray();
        Check.That(reversed.Samples.Where(sample => sample.SourceSimTimeNs >= boundary + 1_400_000_000).SequenceEqual(normal),
            "a rapid reversal settles at the latest target without retained motion or pulse gain from the abandoned target");

        foreach (var (initial, intermediate) in new[]
        {
            (PlethIllustration.Reference, PlethIllustration.MotionArtifact),
            (PlethIllustration.LowPerfusion, PlethIllustration.MotionArtifact),
            (PlethIllustration.MotionArtifact, PlethIllustration.LowPerfusion)
        })
        {
            var configuration = PhysiologyIllustrationConfiguration.Default with { PlethShape = initial };
            var cancelled = PhysiologyIllustrationSource.Create(configuration);
            _ = AdvancePleth(cancelled, boundary);
            var untouched = cancelled.Fork();
            cancelled.ContinueWith(PhysiologyIllustrationSource.Create(configuration with { PlethShape = intermediate }));
            cancelled.ContinueWith(PhysiologyIllustrationSource.Create(configuration));
            Check.That(SameWire(AdvancePleth(cancelled, boundary + 1_600_000_000).Wire,
                AdvancePleth(untouched, boundary + 1_600_000_000).Wire),
                "same-time reversal emits neither a gain discontinuity nor an artifact-quality flash");
        }
    }

    private static void PlethTransitionsReplayAcrossPartitionsAndCheckpoints()
    {
        var source = PhysiologyIllustrationSource.Create();
        _ = AdvancePleth(source, 1_000_000_000);
        source.ContinueWith(PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with
        { PlethShape = PlethIllustration.LowPerfusion }));
        _ = AdvancePleth(source, 1_237_000_000);
        source.ContinueWith(PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with
        { PlethShape = PlethIllustration.MotionArtifact }));
        _ = AdvancePleth(source, 1_419_000_000);
        source.ContinueWith(PhysiologyIllustrationSource.Create());
        var state = JsonSerializer.Deserialize<PhysiologyWaveformGroupState>(JsonSerializer.Serialize(source.CaptureState()))!;
        var restored = PhysiologyWaveformGroup.Restore(state);
        var whole = AdvancePleth(source, 3_000_000_000);
        var partitioned = AdvancePleth(restored, 3_000_000_000, 37_000_000);
        Check.That(SameWire(whole.Wire, partitioned.Wire) && whole.Samples.SequenceEqual(partitioned.Samples),
            "a serialized checkpoint with pending samples and overlapping transitions replays the same wire, values, and quality under different advance partitions");
    }

    private static void MotionExitRetainsPoorQualityUntilTheArtifactEnds()
    {
        const long boundary = 1_200_000_000;
        var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with
        { PlethShape = PlethIllustration.MotionArtifact });
        _ = AdvancePleth(source, boundary);
        var continuingMotion = source.Fork();
        source.ContinueWith(PhysiologyIllustrationSource.Create());
        var recovered = AdvancePleth(source, 2_800_000_000);
        var normal = AdvancePleth(PhysiologyIllustrationSource.Create(), 2_800_000_000).Samples
            .Where(sample => sample.SourceSimTimeNs >= boundary).ToArray();
        Check.That(recovered.Samples[0] == AdvancePleth(continuingMotion, boundary + 8_000_000).Samples[0],
            "leaving motion preserves the currently contaminated sample at the transition boundary");
        Check.That(recovered.Samples.Where(sample => sample.SourceSimTimeNs < boundary + 1_000_000_000)
            .All(sample => sample.QualityFlags != 0) &&
            recovered.Samples.Where(sample => sample.SourceSimTimeNs >= boundary + 1_000_000_000)
            .All(sample => sample.QualityFlags == 0), "poor optical quality lasts through the residual artifact and clears when the artifact is fully gone");
        Check.That(recovered.Samples.Zip(normal).Any(pair => pair.First.SourceSimTimeNs is > 1_300_000_000 and < 2_000_000_000 &&
            pair.First.NormalizedValue != pair.Second.NormalizedValue), "motion decays over the exit interval rather than disappearing at selection");
        Check.That(recovered.Samples.Where(sample => sample.SourceSimTimeNs >= boundary + 1_000_000_000)
            .SequenceEqual(normal.Where(sample => sample.SourceSimTimeNs >= boundary + 1_000_000_000)),
            "after motion has decayed, the original physiological pulse is recovered exactly");
        foreach (byte[] wire in recovered.Wire)
        {
            var block = WaveformEnvelopeCodec.Decode(wire);
            var plane = block.Planes.Single(plane => plane.ChannelId == PhysiologyIllustrationSource.ChannelId(2));
            for (int index = 0; index < plane.Samples.Count; index++)
            {
                bool poor = plane.QualityRanges.Any(range => (uint)index >= range.FirstSampleOffset &&
                    (ulong)index < (ulong)range.FirstSampleOffset + range.Count && range.QualityFlags != 0);
                Check.That(poor == (block.StartSimTimeNs + index * 8_000_000L < boundary + 1_000_000_000),
                    "wire quality uses the same transition end as the live sample tap");
            }
        }
    }

    private static void PlethGainTransitionsDoNotMultiplyDetectedPulses()
    {
        long boundary = RisingPlethTimeNs() + 4_000_000_000;
        var source = PhysiologyIllustrationSource.Create();
        List<byte[]> wire = [];
        wire.AddRange(AdvancePleth(source, boundary).Wire);
        source.ContinueWith(PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with
        { PlethShape = PlethIllustration.LowPerfusion }));
        wire.AddRange(AdvancePleth(source, boundary + 2_400_000_000).Wire);
        source.ContinueWith(PhysiologyIllustrationSource.Create());
        wire.AddRange(AdvancePleth(source, 16_000_000_000).Wire);
        var detector = new PlethPulseRateMeasurement(PhysiologyIllustrationSource.ChannelId(2));
        var pulses = wire.SelectMany(block => detector.Consume(block)).ToArray();
        var reference = new PlethPulseRateMeasurement(PhysiologyIllustrationSource.ChannelId(2));
        var expected = AdvancePleth(PhysiologyIllustrationSource.Create(), 16_000_000_000).Wire
            .SelectMany(block => reference.Consume(block)).ToArray();
        Check.That(pulses.Length <= expected.Length && pulses.Length >= expected.Length - 5,
            "gain transitions may hide weak pulses but must not add pulse detections to the unchanged mechanical rhythm");
        long[] intervals = pulses.Zip(pulses.Skip(1)).Select(pair => pair.Second.PeakTimeNs - pair.First.PeakTimeNs).ToArray();
        Check.That(!intervals.Zip(intervals.Skip(1)).Any(pair => pair.First < 600_000_000 && pair.Second < 600_000_000),
            "transition envelopes do not cause consecutive falsely shortened pulse intervals");
        Check.That(detector.Read(15_992_000_000).MilliBeatsPerMinute == reference.Read(15_992_000_000).MilliBeatsPerMinute,
            "settled reference pulses restore the measured pulse rate without any ECG or configured-rate input");
    }

    private static void CardiacTherapyRetainsNonCardiacTemplates()
    {
        var current = PhysiologyIllustrationConfiguration.VtPreset with
        {
            PressureMeasurement = PressureMeasurementIllustration.Overdamped,
            PlethShape = PlethIllustration.LowPerfusion,
            CapnogramIllustration = CapnogramIllustration.CurareCleft,
            RespirationIllustration = RespirationIllustration.InspiratoryEffort
        };
        var before = Read(current, 4_000_000_000);
        foreach (var next in new[]
        {
            SinusIllustrationConfiguration.Apply(current),
            PacingIllustrationConfiguration.Apply(current, PacingIllustration.RightVentricularVvi, new(80, 100, 1))
        })
        {
            Check.That(next.PressureMeasurement == current.PressureMeasurement && next.PlethShape == current.PlethShape,
                "cardiac therapy keeps pressure sensor damping and peripheral optical settings");
            var after = Read(next, 4_000_000_000);
            Check.That(Values(before, 1).SequenceEqual(Values(after, 1)) && Values(before, 4).SequenceEqual(Values(after, 4)),
                "conversion or pacing must not silently replace the selected respiratory and capnogram waveforms");
        }
    }

    private static void OverdampingChangesOnlyMeasuredArterialContour()
    {
        var reference = PhysiologyIllustrationConfiguration.Default;
        var configuration = reference with { PressureMeasurement = PressureMeasurementIllustration.Overdamped };
        Check.That(reference.ResolvePlan() == configuration.ResolvePlan(), "a transducer fault does not change ventricular mechanics");
        var ventilation = new VentilationTransportPlan(500000, 150000, 210000);
        var transport = PhysiologyIllustrationSource.CreateTransport(reference, ventilation, 70000);
        var illustratedTransport = PhysiologyIllustrationSource.CreateTransport(configuration with
        { PlethShape = PlethIllustration.MotionArtifact }, ventilation, 70000);
        Check.That(transport.CaptureState() == illustratedTransport.CaptureState() &&
            transport.Integrate(0, 12_000_000_000) == illustratedTransport.Integrate(0, 12_000_000_000),
            "sensor contour modes do not change delivered ventilation, effective blood flow, or inspired oxygen");
        var ordinary = Read(reference);
        var damped = Read(configuration);
        AssertOtherChannelsEqual(ordinary, damped, 3);
        short[] before = Values(ordinary, 3, 8_000_000_000);
        short[] after = Values(damped, 3, 8_000_000_000);
        Check.That(after.Max() < before.Max() && after.Min() > before.Min() &&
            after.Max() - after.Min() < (before.Max() - before.Min()) * 0.75,
            "overdamping narrows measured pulse pressure by rounding the systolic peak and raising the trough");
        Check.That(Math.Abs(after.Average(value => (double)value) - before.Average(value => (double)value)) < 80,
            "a unit-DC transducer response approximately preserves mean pressure over complete settled cycles");
        int beforeSlope = before.Zip(before.Skip(1)).Max(pair => Math.Abs(pair.First - pair.Second));
        int afterSlope = after.Zip(after.Skip(1)).Max(pair => Math.Abs(pair.First - pair.Second));
        Check.That(afterSlope < beforeSlope / 2, "transducer smoothing suppresses fast upstrokes and the dicrotic notch");
        Check.That(damped.SelectMany(block => block.Planes).All(plane => plane.QualityRanges.Count == 0),
            "the authored pressure trace remains available to the ordinary pressure measurement path");
    }

    private static void LowPerfusionPreservesPulseTimingAndOtherChannels()
    {
        var reference = PhysiologyIllustrationConfiguration.Default;
        var ordinary = Read(reference, 4_000_000_000);
        var low = Read(reference with { PlethShape = PlethIllustration.LowPerfusion }, 4_000_000_000);
        AssertOtherChannelsEqual(ordinary, low, 2);
        short[] before = Values(ordinary, 2);
        short[] after = Values(low, 2);
        Check.That(after.Max() > 0 && before.Zip(after).All(pair => Math.Abs(pair.Second - pair.First * 0.08) <= 1),
            "low peripheral perfusion scales the same optical pulse without removing ejections or changing timing");
        Check.That(low.All(block => block.Planes.Single(plane => plane.ChannelId == PhysiologyIllustrationSource.ChannelId(2)).QualityRanges.Count == 0),
            "low amplitude alone does not force the monitor to report poor quality");
    }

    private static void MotionContaminationPropagatesPoorOpticalQuality()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default with { PlethShape = PlethIllustration.MotionArtifact };
        var ordinary = Read(PhysiologyIllustrationConfiguration.Default, 4_000_000_000);
        var motion = Read(configuration, 4_000_000_000);
        var repeat = Read(configuration, 4_000_000_000);
        AssertOtherChannelsEqual(ordinary, motion, 2);
        Check.That(!Values(ordinary, 2).SequenceEqual(Values(motion, 2)) &&
            Values(motion, 2).SequenceEqual(Values(repeat, 2)), "motion artifact is nontrivial and deterministic");
        var pulseRate = new PlethPulseRateMeasurement(PhysiologyIllustrationSource.ChannelId(2));
        var optical = new PulseOximeterIllustrationSource(PhysiologyIllustrationSource.ChannelId(2),
            PhysiologyIllustrationSource.ChannelId(2), Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"), 98000);
        foreach (var block in motion)
        {
            byte[] wire = WaveformEnvelopeCodec.EncodeRaw(block);
            _ = pulseRate.Consume(wire);
            Check.That(pulseRate.Read(block.StartSimTimeNs + block.DurationNs - 8_000_000).Status == WaveformMeasurementStatus.PoorSignal,
                "contaminated PLETH must not yield a confident false pulse rate");
            var converted = WaveformEnvelopeCodec.Decode(optical.ConvertAcquiredPulse(wire));
            Check.That(converted.Planes.All(plane => plane.QualityRanges.Any(range => range.QualityFlags != 0)),
                "motion quality follows the acquired pulse into both optical wavelength channels");
        }
    }

    private static void SensorModesPreservePendingSamplesAcrossContinuationAndRestore()
    {
        var source = PhysiologyIllustrationSource.Create();
        _ = source.AdvanceTo(105_000_000, 50, 1, 100);
        var first = PhysiologyIllustrationConfiguration.Default with
        { PressureMeasurement = PressureMeasurementIllustration.Overdamped, PlethShape = PlethIllustration.LowPerfusion };
        source.ContinueWith(PhysiologyIllustrationSource.Create(first));
        _ = source.AdvanceTo(137_000_000, 50, 1, 100);
        source = PhysiologyWaveformGroup.Restore(source.CaptureState());
        source.ContinueWith(PhysiologyIllustrationSource.Create(first with { PlethShape = PlethIllustration.MotionArtifact }));
        _ = source.AdvanceTo(179_000_000, 50, 1, 100);
        var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
        var whole = source.AdvanceTo(200_000_000, 50, 1, 100);
        _ = restored.AdvanceTo(191_000_000, 50, 1, 100);
        var partitioned = restored.AdvanceTo(200_000_000, 50, 1, 100);
        Check.That(whole.Count == 1 && whole[0].SequenceEqual(partitioned.Single()),
            "within-block sensor changes retain raw old samples and replay exactly after checkpoint restore");
        var block = WaveformEnvelopeCodec.Decode(whole[0]);
        var pleth = block.Planes.Single(plane => plane.ChannelId == PhysiologyIllustrationSource.ChannelId(2));
        Check.That(pleth.QualityRanges.Count == 1 && pleth.QualityRanges[0].FirstSampleOffset == 18 &&
            pleth.QualityRanges[0].Count == 7, "motion quality begins at the first acquired sample after the mode boundary");
        source.ContinueWith(PhysiologyIllustrationSource.Create());
        _ = source.AdvanceTo(225_000_000, 50, 1, 100);
        _ = PhysiologyWaveformGroup.Restore(source.CaptureState());
    }

    private static void SensorIllustrationValidationRejectsInvalidModesAndChannels()
    {
        foreach (var configuration in new[]
        {
            PhysiologyIllustrationConfiguration.Default with { PressureMeasurement = (PressureMeasurementIllustration)99 },
            PhysiologyIllustrationConfiguration.Default with { PlethShape = (PlethIllustration)99 }
        })
        {
            bool rejected = false;
            try { _ = PhysiologyIllustrationSource.Create(configuration); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "invalid sensor modes are rejected before source publication");
        }
        var state = PhysiologyIllustrationSource.Create().CaptureState();
        var corrupted = state with
        {
            Channels = state.Channels.Select(channel => channel.ChannelId == PhysiologyIllustrationSource.ChannelId(0)
                ? channel with { SensorIllustrations = [new(0, PlethShape: PlethIllustration.MotionArtifact)] }
                : channel).ToArray()
        };
        bool wrongChannelRejected = false;
        try { _ = PhysiologyWaveformGroup.Restore(corrupted); } catch (ArgumentException) { wrongChannelRejected = true; }
        Check.That(wrongChannelRejected, "sensor modes cannot be restored onto an incompatible acquisition channel");
        foreach (var transition in new[]
        {
            new SensorWaveformIllustration(0) { PlethTransitionFromGainPpm = 79_999 },
            new SensorWaveformIllustration(0) { PlethTransitionFromGainPpm = 1_000_001 },
            new SensorWaveformIllustration(0) { PlethTransitionFromGainPpm = 1_000_000, PlethTransitionFromMotionPpm = -1 },
            new SensorWaveformIllustration(0) { PlethTransitionFromGainPpm = 1_000_000, PlethTransitionFromMotionPpm = 1_000_001 },
            new SensorWaveformIllustration(0) { PlethTransitionFromMotionPpm = 1 }
        })
        {
            var invalidTransition = state with
            {
                Channels = state.Channels.Select(channel => channel.ChannelId == PhysiologyIllustrationSource.ChannelId(2)
                    ? channel with { SensorIllustrations = [transition] } : channel).ToArray()
            };
            bool rejected = false;
            try { _ = PhysiologyWaveformGroup.Restore(invalidTransition); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "restored transitions reject out-of-range or incomplete optical weights");
        }
        var wrongTransitionChannel = state with
        {
            Channels = state.Channels.Select(channel => channel.ChannelId == PhysiologyIllustrationSource.ChannelId(0)
                ? channel with { SensorIllustrations = [new(0) { PlethTransitionFromGainPpm = 1_000_000 }] }
                : channel).ToArray()
        };
        bool wrongTransitionRejected = false;
        try { _ = PhysiologyWaveformGroup.Restore(wrongTransitionChannel); } catch (ArgumentException) { wrongTransitionRejected = true; }
        Check.That(wrongTransitionRejected, "even a reference-target optical transition belongs only to a PLETH acquisition channel");
    }

    private static void OverdampingReconstructsEarlierPressureDefinitions()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default;
        var reference = PhysiologyIllustrationSource.Create(configuration);
        var damped = PhysiologyIllustrationSource.Create(configuration with
        { PressureMeasurement = PressureMeasurementIllustration.Overdamped });
        List<short> original = [], filtered = [];
        for (int step = 1; step <= 20; step++)
        {
            long timeNs = step * 100_000_000L;
            _ = reference.AdvanceTo(timeNs, 50, 1, 100, out var ordinarySamples);
            _ = damped.AdvanceTo(timeNs, 50, 1, 100, out var dampedSamples);
            original.AddRange(ordinarySamples.Where(sample => sample.ChannelId == PhysiologyIllustrationSource.ChannelId(3))
                .Select(sample => sample.NormalizedValue));
            filtered.AddRange(dampedSamples.Where(sample => sample.ChannelId == PhysiologyIllustrationSource.ChannelId(3))
                .Select(sample => sample.NormalizedValue));
            if (step is 4 or 7)
            {
                configuration = configuration with { AbpPulsePermille = step == 4 ? 1500 : 600 };
                reference.ContinueWith(PhysiologyIllustrationSource.Create(configuration));
                damped.ContinueWith(PhysiologyIllustrationSource.Create(configuration with
                { PressureMeasurement = PressureMeasurementIllustration.Overdamped }));
            }
            reference = PhysiologyWaveformGroup.Restore(reference.CaptureState());
            damped = PhysiologyWaveformGroup.Restore(damped.CaptureState());
        }
        for (int index = 0; index < original.Count; index++)
        {
            long sum = 0;
            for (int tap = 0; tap < 39; tap++)
            { sum += original[Math.Max(0, index - 2 * tap)] * (tap < 20 ? tap + 1 : 39 - tap); }
            Check.That(Math.Abs(filtered[index] - sum / 400.0) <= 1,
                "a causal transducer uses the originally emitted pressure across source changes, including earlier event tails");
        }
    }

    private static WaveformEnvelope[] Read(PhysiologyIllustrationConfiguration configuration, long durationNs = 12_000_000_000)
    {
        var source = PhysiologyIllustrationSource.Create(configuration);
        List<WaveformEnvelope> output = [];
        for (long time = 200_000_000; time <= durationNs; time += 200_000_000)
        { output.AddRange(source.AdvanceTo(time, 50, 1, 100).Select(wire => WaveformEnvelopeCodec.Decode(wire))); }
        return output.ToArray();
    }

    private static long RisingPlethTimeNs()
    {
        var samples = AdvancePleth(PhysiologyIllustrationSource.Create(), 2_000_000_000).Samples;
        int index = Enumerable.Range(1, samples.Length - 1).First(index => samples[index].SourceSimTimeNs >= 800_000_000 &&
            samples[index].NormalizedValue > 200 && samples[index].NormalizedValue - samples[index - 1].NormalizedValue > 8);
        return samples[index].SourceSimTimeNs;
    }

    private static (byte[][] Wire, PhysiologyWaveformSample[] Samples) AdvancePleth(PhysiologyWaveformGroup source,
        long endSimTimeNs, long maximumStepNs = 200_000_000)
    {
        long cursor = source.CaptureState().Channels[0].Generator.Clock.CursorSimTimeNs;
        List<byte[]> wire = [];
        List<PhysiologyWaveformSample> samples = [];
        while (cursor < endSimTimeNs)
        {
            cursor = Math.Min(cursor + maximumStepNs, endSimTimeNs);
            wire.AddRange(source.AdvanceTo(cursor, 50, 1, 100, out var immediate));
            samples.AddRange(immediate.Where(sample => sample.ChannelId == PhysiologyIllustrationSource.ChannelId(2)));
        }
        return (wire.ToArray(), samples.ToArray());
    }

    private static bool SameWire(byte[][] expected, byte[][] actual) => expected.Length == actual.Length &&
        expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second));

    private static short[] Values(WaveformEnvelope[] blocks, int row, long fromSimTimeNs = 0) =>
        blocks.Where(block => block.StartSimTimeNs >= fromSimTimeNs)
            .SelectMany(block => block.Planes.Single(plane => plane.ChannelId == PhysiologyIllustrationSource.ChannelId(row)).Samples).ToArray();

    private static void AssertOtherChannelsEqual(WaveformEnvelope[] expected, WaveformEnvelope[] actual, int modifiedRow)
    {
        for (int row = 0; row < 7; row++)
        {
            if (row == modifiedRow) { continue; }
            Check.That(Values(expected, row).SequenceEqual(Values(actual, row)), "a local sensor illustration leaves other physical channels unchanged");
        }
    }
}
