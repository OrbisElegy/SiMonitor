// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class EventWaveformBatchSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly long[] Table = [0, -31 * Q + 1, 127 * Q - 3, 31 * Q + 7, 0, 9 * Q - 1, -17 * Q + 5, 0];
    private static readonly long[] PositiveTable = [0, Q + 1, 5 * Q - 7, 9 * Q + 3, 11 * Q - 1, 7 * Q + 11, 3 * Q - 1, 0];
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);

    internal static Specification[] All =>
    [
        new(nameof(BatchedPhaseSteppingMatchesScalarAtBoundaries), BatchedPhaseSteppingMatchesScalarAtBoundaries),
        new(nameof(BatchedMorphologySelectionsAndGainsMatchScalar), BatchedMorphologySelectionsAndGainsMatchScalar),
        new(nameof(BatchedRespiratoryShapesAndDynamicClocksMatchScalar), BatchedRespiratoryShapesAndDynamicClocksMatchScalar),
        new(nameof(BatchedContinuationPreservesTailsAndContinuousActivity), BatchedContinuationPreservesTailsAndContinuousActivity),
        new(nameof(BatchedContinuationSplitsOversizedEventWindows), BatchedContinuationSplitsOversizedEventWindows),
        new(nameof(BatchedSamplingRetainsAtomicFailureAndWideAccumulation), BatchedSamplingRetainsAtomicFailureAndWideAccumulation),
    ];

    private static void BatchedPhaseSteppingMatchesScalarAtBoundaries()
    {
        const long duration = 120_000_003;
        EventWaveformPhasePoint[] points = [new(0, 0), new(10_000_019, 2), new(70_000_031, 2), new(duration, 8)];
        var band = new EventWaveformBand(PhysiologyCycleEventKind.AtrialElectrical, 13, duration, Table);
        foreach (long origin in new[] { 0, 9_000_000_000_000_000_000 })
        {
            PhysiologyCycleEvent[] events = [new(origin, band.Trigger, 0), new(origin + 50_000_017, band.Trigger, 1)];
            foreach (var shape in new[] { band, band with { PhasePoints = points } })
            {
                var source = EventWaveformComposition.Restore(new([shape], events));
                foreach (long period in new[] { 1L, 1_000_003, 400_000_000 })
                {
                    foreach (long offset in new[] { 0L, 12, 13, 10_000_031, 70_000_043, duration + 12, duration + 13 })
                    { AssertMatches(source, origin + offset, period, period == 1 ? 7 : 257); }
                }
            }
        }
    }

    private static void BatchedMorphologySelectionsAndGainsMatchScalar()
    {
        var rate = new SeededCardiacRate(75, new string('1', 64), 37);
        var atrial = new EventWaveformBand(PhysiologyCycleEventKind.AtrialElectrical, 0, 840_000_000, Table)
        { CycleDurationRate = rate };
        var ventricular = new EventWaveformBand(PhysiologyCycleEventKind.VentricularElectrical, 17, 613_000_003, Table,
            [new(0, 0), new(130_000_001, 2), new(613_000_003, 8)]);
        var mechanical = new EventWaveformBand(PhysiologyCycleEventKind.VentricularMechanical, 31, 410_000_003, PositiveTable,
            [new(0, 0), new(170_000_019, 3), new(410_000_003, 8)],
            EjectionIllustration: AvConductionPattern.PrematureVentricularIllustration);
        EventWaveformBand[] bands =
        [
            atrial,
            ventricular with { VentricularCycles = new(5, 0b10101), TriggerCycleLimit = 5, TriggerCycleResume = 11 },
            ventricular with { AfBeatSelection = AtrialFibrillationBeatSelection.Ordinary },
            ventricular with { AfBeatSelection = AtrialFibrillationBeatSelection.LongShort, AfTiming = AtrialFibrillationReference.CreatePlan() },
            mechanical,
        ];
        var source = EventWaveformComposition.Restore(new(bands, Events(80, 251_000_003)));
        AssertMatches(source, 0, 4_000_003, 5100);
        AssertMatches(source, 4_123_456_789, 8_000_000, 1000);
    }

    private static void BatchedRespiratoryShapesAndDynamicClocksMatchScalar()
    {
        var clock = new PhysiologyRateClock(3_750_000_000, new(16,
            [new(1_800_000_003, 2_900_000_007, 28), new(5_400_000_011, 1_300_000_017, 9)]));
        var shapes = Enumerable.Range(0, 32).Select(slot => new RespiratoryCycleShape(
            110_000_000 + slot * 1_000_003L, 2_000_000_000 + slot * 10_000_003L, PositiveTable,
            [new(0, 0), new(600_000_019, 2), new(900_000_031, 2), new(2_000_000_000 + slot * 10_000_003L, 8)])).ToArray();
        var inspiration = new EventWaveformBand(PhysiologyCycleEventKind.InspirationStart, 100_000_000, 3_500_000_000,
            PositiveTable, DepthPattern: RespiratoryPattern.SpontaneousIllustration)
        { InspirationCycleGainsPermille = [731, 1000, 1193], RespiratoryCycleShapes = shapes };
        var expiration = new EventWaveformBand(PhysiologyCycleEventKind.ExpirationStart, 100_000_000, 3_500_000_000,
            PositiveTable, ExpirationCycleGainsPermille: [1000, 613, 0, 1537])
        {
            RespiratoryCycleShapes = shapes,
            ExpirationCycleOrigin = 2,
            ExpirationActivationGainsPermille = [411, 1000, 0, 911, 573, 1000, 811, 1000],
            CardiogenicRippleDurationNs = 230_000_003,
            CardiogenicRipplePermille = 89,
        };
        foreach (bool dynamic in new[] { false, true })
        {
            EventWaveformBand[] bands = dynamic
                ? [inspiration with { PhaseClock = clock, PhaseClockFixedDelayNs = 50_000_000 },
                   expiration with { PhaseClock = clock, PhaseClockFixedDelayNs = 50_000_000 }]
                : [inspiration, expiration];
            AssertMatches(EventWaveformComposition.Restore(new(bands, Events(48, 300_000_007))), 0, 8_000_003, 2100);
        }
        foreach (var depth in new[] { RespiratoryPattern.CheyneStokesIllustration, RespiratoryPattern.IntermittentIllustration })
        {
            AssertMatches(EventWaveformComposition.Restore(new([inspiration with { DepthPattern = depth }],
                Events(48, 300_000_007))), 117, 8_000_003, 2100);
        }
        var acceleratedClock = new PhysiologyRateClock(3_750_000_000, new(60, []));
        var accelerated = new EventWaveformBand(PhysiologyCycleEventKind.ExpirationStart, 0, 300_000_000, PositiveTable)
        {
            PhaseClock = acceleratedClock,
            CardiogenicRippleDurationNs = 300_000_000,
            CardiogenicRipplePermille = 200,
        };
        AssertGeneratorMatches(PhysiologySignalGenerator.Start(Plan with { RespiratoryRateClock = acceleratedClock },
            "AcqResp125@1", 5, [accelerated]), 6_000_000_000);
    }

    private static void BatchedContinuationPreservesTailsAndContinuousActivity()
    {
        var clock = new PhysiologyRateClock(3_750_000_000, new(16, [new(600_000_003, 2_300_000_007, 27)]));
        var breath = new EventWaveformBand(PhysiologyCycleEventKind.InspirationStart, 0, 3_750_000_000, PositiveTable)
        { PhaseClock = clock };
        var dynamicPlan = Plan with { RespiratoryRateClock = clock };
        var initial = PhysiologySignalGenerator.Start(dynamicPlan, "AcqResp125@1", 3, [breath]);
        _ = initial.GenerateBefore(2_004_000_003, 300, 1000);
        var next = PhysiologySignalGenerator.Start(dynamicPlan, "AcqResp125@1", 3,
            [breath with { TableQ32 = Table }]);
        AssertGeneratorMatches(initial.ContinueWith(next.CaptureState()), 7_000_000_000);

        var afPlan = AtrialFibrillationReference.CreatePlan();
        var continuous = new EventWaveformBand(PhysiologyCycleEventKind.AtrialFibrillationSegment, 0,
            AtrialFibrillationReference.SegmentDurationNs, Table);
        var beat = new EventWaveformBand(PhysiologyCycleEventKind.VentricularElectrical, 0, 900_000_003, Table);
        var fibrillation = PhysiologySignalGenerator.Start(afPlan, "AcqECGMonitor250@1", 4, [continuous, beat]);
        _ = fibrillation.GenerateBefore(6_052_000_003, 2000, 1000);
        var changed = PhysiologySignalGenerator.Start(afPlan, "AcqECGMonitor250@1", 4,
            [continuous with { TableQ32 = PositiveTable }, beat]);
        var continued = fibrillation.ContinueWith(changed.CaptureState());
        AssertGeneratorMatches(continued, 8_800_000_000);
        foreach (var segment in continued.CaptureState().History)
        {
            var source = new PhysiologySignalContinuation(segment);
            const long period = 4_000_000;
            long first = segment.ToExclusiveEventTimeNs - 100_000_003;
            long[] values = new long[100];
            source.EvaluateUniform(first, period, values, false);
            Check.That(values.Select((value, index) => value == source.EvaluateAt(first + index * period, false)).All(match => match),
                "a batch straddling retirement stops continuous activity and retains triggered tails at the exact boundary");
        }
    }

    private static void BatchedContinuationSplitsOversizedEventWindows()
    {
        var densePlan = Plan with
        {
            HeartPeriodNs = 1_100_003,
            VentricularElectricalOffsetNs = 160_000,
            AtrialMechanicalOffsetNs = 80_000,
            VentricularMechanicalOffsetNs = 240_000,
        };
        var band = new EventWaveformBand(PhysiologyCycleEventKind.AtrialElectrical, 13, 700_003, Table);
        const long period = 8_000_000;
        const long deadline = 4_000_000_000;
        bool oversized = false;
        try { RegularPhysiologyTimeline.Start(densePlan).AdvanceBefore(deadline, EventWaveformComposition.MaximumEventCount); }
        catch (PhysiologyTimelineException exception) { oversized = exception.ReasonCode == "PhysiologyTimeline.EventLimitExceeded"; }
        Check.That(oversized, "the requested batch requires more events than one bounded reconstruction window can contain");
        var continuation = new PhysiologySignalContinuation(new(densePlan, [band], null, null, 0, long.MaxValue, false));
        long[] actual = new long[deadline / period];
        continuation.EvaluateUniform(0, period, actual, false);
        Check.That(!actual.Where((value, index) => value != continuation.EvaluateAt(index * period, false)).Any() &&
            actual.Any(value => value != 0), "recursive event-window splitting preserves nonzero scalar contributions");

        var initial = PhysiologySignalGenerator.Start(densePlan, "AcqResp125@1", 6, [band]).CaptureState();
        var source = PhysiologySignalGenerator.Restore(initial with { ActiveFromEventTimeNs = 0 });
        List<long> split = [];
        long cursor = 0;
        while (cursor < deadline)
        {
            cursor = Math.Min(cursor + 101_000_003, deadline);
            split.AddRange(source.GenerateBefore(cursor, 100, 1000).Select(sample => sample.ValueQ32));
            source = PhysiologySignalGenerator.Restore(source.CaptureState());
        }
        Check.That(actual.SequenceEqual(split), "recursive reconstruction matches small generated batches restored after every irregular deadline");
    }

    private static void BatchedSamplingRetainsAtomicFailureAndWideAccumulation()
    {
        var positive = new EventWaveformBand(PhysiologyCycleEventKind.AtrialElectrical, 0, 20_000,
            [0, 32767 * Q, 32767 * Q, 32767 * Q]);
        var source = EventWaveformComposition.Restore(new(Enumerable.Repeat(positive, 32).ToArray(),
            Enumerable.Range(0, 4096).Select(index => new PhysiologyCycleEvent(index, positive.Trigger, (ulong)index)).ToArray()));
        bool rejected = false;
        try { source.EvaluateUniform(10_000, 1, new long[1]); }
        catch (EventWaveformException exception) { rejected = exception.ReasonCode == "EventWaveform.AmplitudeOverflow"; }
        Check.That(rejected, "wide contribution accumulation reports amplitude overflow instead of wrapping the accumulator");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        long[] output = [123];
        bool cancelled = false;
        try { source.EvaluateUniform(0, 1, output, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(cancelled && output[0] == 123, "cancelled batches do not publish output");
    }

    private static PhysiologyCycleEvent[] Events(int count, long period) => Enumerable.Range(0, count)
        .SelectMany(index => new[] { PhysiologyCycleEventKind.AtrialElectrical, PhysiologyCycleEventKind.VentricularElectrical,
            PhysiologyCycleEventKind.VentricularMechanical, PhysiologyCycleEventKind.InspirationStart, PhysiologyCycleEventKind.ExpirationStart }
            .Select(kind => new PhysiologyCycleEvent(index * period, kind, (ulong)index)))
        .OrderBy(item => item.SimTimeNs).ThenBy(item => item.Kind).ToArray();

    private static void AssertMatches(EventWaveformComposition source, long firstSimTimeNs, long samplePeriodNs, int count)
    {
        long[] actual = new long[count];
        source.EvaluateUniform(firstSimTimeNs, samplePeriodNs, actual);
        for (int sample = 0; sample < count; sample++)
        {
            long time = firstSimTimeNs + sample * samplePeriodNs;
            Check.That(actual[sample] == source.EvaluateAt(time), $"batch/scalar equality at {time}: {actual[sample]}");
        }
        long[] split = new long[count];
        int offset = 0;
        foreach (int size in new[] { 1, 7, 50, 3, count })
        {
            int length = Math.Min(size, count - offset);
            source.EvaluateUniform(firstSimTimeNs + offset * samplePeriodNs, samplePeriodNs, split.AsSpan(offset, length));
            offset += length;
        }
        Check.That(actual.SequenceEqual(split), "arbitrary sample partitions preserve exact phase and accumulation");
    }

    private static void AssertGeneratorMatches(PhysiologySignalGenerator source, long toExclusiveSimTimeNs)
    {
        var state = source.CaptureState();
        var whole = PhysiologySignalGenerator.Restore(state);
        var expected = whole.GenerateBefore(toExclusiveSimTimeNs, 2000, 1000);
        Check.That(expected.All(sample => sample.ValueQ32 == source.EvaluateAt(sample.Tick.SimTimeNs)),
            "generator batches reproduce the scalar continuation including all old and new contributions");
        var split = PhysiologySignalGenerator.Restore(state);
        List<PhysiologySignalSample> actual = [];
        long cursor = state.Clock.CursorSimTimeNs;
        while (cursor < toExclusiveSimTimeNs)
        {
            cursor = Math.Min(cursor + 137_000_003, toExclusiveSimTimeNs);
            actual.AddRange(split.GenerateBefore(cursor, 100, 1000));
            split = PhysiologySignalGenerator.Restore(split.CaptureState());
        }
        Check.That(expected.SequenceEqual(actual), "continuation batches survive irregular deadlines and restoration after every batch");
    }
}
