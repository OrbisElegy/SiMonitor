// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepTraceCompositionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(EraseGapWrapsAsBackgroundOnly), EraseGapWrapsAsBackgroundOnly),
        new(nameof(NoDataCoverageAndGapFormAnExactPartition), NoDataCoverageAndGapFormAnExactPartition),
        new(nameof(PinnedViewsIgnoreLiveErasure), PinnedViewsIgnoreLiveErasure),
        new(nameof(CompositionIsFrameIndependentAndRestorable), CompositionIsFrameIndependentAndRestorable),
        new(nameof(CompositionRejectsInvalidStateWithoutMutation), CompositionRejectsInvalidStateWithoutMutation),
    ];

    private static SweepStateProjectionStateMachine Start(ulong gap = 2) =>
        SweepStateProjectionStateMachine.Start(new NoDataSweepPlan("ecg", 1, 2, 0, 10, gap, 30),
            1, 1, SessionRunState.Running, Continuity().CaptureState(), 0, 0);

    private static DataContinuityStateMachine Continuity() =>
        DataContinuityStateMachine.Start(LocalContinuationPolicy.DefaultDuration, 0);

    private static SweepTraceRegion Region(ulong start, ulong end, SweepTraceRegionKind kind) => new(start, end, kind);

    private static void EraseGapWrapsAsBackgroundOnly()
    {
        SweepStateProjectionStateMachine machine = Start();
        machine.Advance(9, 123);
        Check.That(SweepTraceComposition.Compose(machine.CaptureState()).Regions.SequenceEqual(new[]
        {
            Region(0, 1, SweepTraceRegionKind.BackgroundEraseGap),
            Region(1, 9, SweepTraceRegionKind.RetainSourceTrace),
            Region(9, 10, SweepTraceRegionKind.BackgroundEraseGap),
        }), "a wrapped gap has two background-only half-open pieces, never a patient baseline");
        machine.Advance(10, 124);
        Check.That(SweepTraceComposition.Compose(machine.CaptureState()).Regions.SequenceEqual(new[]
        {
            Region(0, 2, SweepTraceRegionKind.BackgroundEraseGap),
            Region(2, 10, SweepTraceRegionKind.RetainSourceTrace),
        }), "at wrap only the short front gap is erased");
        Check.That(SweepTraceComposition.Compose(Start(0).CaptureState()).Regions.SequenceEqual(new[]
        {
            Region(0, 10, SweepTraceRegionKind.RetainSourceTrace),
        }), "zero gap preserves the entire source trace");
    }

    private static void NoDataCoverageAndGapFormAnExactPartition()
    {
        SweepStateProjectionStateMachine machine = Start();
        machine.Advance(8, 42);
        machine.SynchronizeContinuity(Continuity().Disconnect(false, 1), 8, 42);
        Check.That(SweepTraceComposition.Compose(machine.CaptureState()).Regions.All(
            region => region.Kind != SweepTraceRegionKind.NoDataBaseline),
            "NoData onset does not immediately clear source history");
        machine.Advance(11, 42);
        Check.That(SweepTraceComposition.Compose(machine.CaptureState()).Regions.SequenceEqual(new[]
        {
            Region(0, 1, SweepTraceRegionKind.NoDataBaseline),
            Region(1, 3, SweepTraceRegionKind.BackgroundEraseGap),
            Region(3, 8, SweepTraceRegionKind.RetainSourceTrace),
            Region(8, 10, SweepTraceRegionKind.NoDataBaseline),
        }), "only passed trace becomes NoData while the head gap stays background");
        machine.Advance(17, 42);
        Check.That(SweepTraceComposition.Compose(machine.CaptureState()).Regions.SequenceEqual(new[]
        {
            Region(0, 7, SweepTraceRegionKind.NoDataBaseline),
            Region(7, 9, SweepTraceRegionKind.BackgroundEraseGap),
            Region(9, 10, SweepTraceRegionKind.NoDataBaseline),
        }), "erase gap takes precedence where it overlaps NoData coverage");
        machine.Advance(18, 42);
        Check.That(SweepTraceComposition.Compose(machine.CaptureState()).Regions.All(
            region => region.Kind != SweepTraceRegionKind.RetainSourceTrace),
            "one complete NoData duration leaves no old patient trace");
    }

    private static void PinnedViewsIgnoreLiveErasure()
    {
        foreach (bool review in new[] { false, true })
        {
            SweepStateProjectionStateMachine machine = Start();
            if (review) { machine.EnterReview("record.old", 0, 3, 15); }
            else { machine.EnterFrozen(3, 15); }
            machine.SynchronizeContinuity(Continuity().Disconnect(false, 1), 3, 15);
            machine.Advance(30, 15);
            Check.That(SweepTraceComposition.Compose(machine.CaptureState()).Regions.SequenceEqual(new[]
            {
                Region(0, 10, SweepTraceRegionKind.PinnedHistory),
            }), "background Live erasure must not affect Frozen or Review");
        }
    }

    private static void CompositionIsFrameIndependentAndRestorable()
    {
        SweepStateProjectionStateMachine direct = Start();
        SweepStateProjectionStateMachine chunked = Start();
        DataContinuityState noData = Continuity().Disconnect(false, 1);
        direct.SynchronizeContinuity(noData, 0, 0);
        chunked.SynchronizeContinuity(noData, 0, 0);
        for (long time = 1; time <= 100; time++)
        {
            chunked.Advance(time, 0);
            SweepTraceCompositionSnapshot composition = SweepTraceComposition.Compose(chunked.CaptureState());
            Check.That(composition.Regions[0].StartOffsetNs == 0 && composition.Regions[^1].EndExclusiveOffsetNs == 10 &&
                composition.Regions.All(region => region.StartOffsetNs < region.EndExclusiveOffsetNs) &&
                composition.Regions.Zip(composition.Regions.Skip(1)).All(pair =>
                    pair.First.EndExclusiveOffsetNs == pair.Second.StartOffsetNs && pair.First.Kind != pair.Second.Kind),
                "ten cycles remain an ordered, merged partition without gaps or overlaps");
        }

        direct.Advance(100, 0);
        SweepStateProjectionState checkpoint = chunked.CaptureState();
        Check.That(SweepTraceComposition.Compose(direct.CaptureState()).Regions.SequenceEqual(
            SweepTraceComposition.Compose(SweepStateProjectionStateMachine.Restore(checkpoint).CaptureState()).Regions),
            "late rendering and checkpoint restore reconstruct identical regions without frame accumulation");
    }

    private static void CompositionRejectsInvalidStateWithoutMutation()
    {
        SweepStateProjectionState state = Start().CaptureState();
        foreach (ulong gap in new ulong[] { 10, 11 })
        {
            try
            {
                SweepTraceComposition.Compose(state with { Plan = state.Plan with { EraseGapNs = gap } });
                throw new InvalidOperationException("whole-window gap must reject");
            }
            catch (SweepTraceCompositionException exception)
            {
                Check.That(exception.ReasonCode == "SweepComposition.GapCoversWindow", "stable invalid-gap reason");
            }
        }

        try
        {
            SweepTraceComposition.Compose(state with { LiveSweepClockNs = -1 });
            throw new InvalidOperationException("invalid checkpoint must reject");
        }
        catch (SweepStateProjectionException exception)
        {
            Check.That(exception.ReasonCode == "SweepState.InvalidCheckpoint", "composition revalidates source state");
        }

        Check.That(SweepStateProjectionStateMachine.Restore(state).CaptureState() == state,
            "failed pure composition leaves accepted source state unchanged");
    }
}
