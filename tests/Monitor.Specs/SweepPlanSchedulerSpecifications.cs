// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepPlanSchedulerSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(NextBoundaryIsStrictlyFutureAndUsesEffectiveClock), NextBoundaryIsStrictlyFutureAndUsesEffectiveClock),
        new(nameof(ScheduledPlanCommitsAtomicallyWithActualPatientFrontier), ScheduledPlanCommitsAtomicallyWithActualPatientFrontier),
        new(nameof(SkippedBoundaryAndInvalidInputsRetainPendingPlan), SkippedBoundaryAndInvalidInputsRetainPendingPlan),
        new(nameof(PendingCheckpointRevalidatesBoundaryAndPlan), PendingCheckpointRevalidatesBoundaryAndPlan),
        new(nameof(SchedulingRejectsUnsupportedAxesAndClockOverflow), SchedulingRejectsUnsupportedAxesAndClockOverflow),
    ];

    private static SweepStateProjectionStateMachine Presentation() => SweepStateProjectionStateMachine.Start(
        new NoDataSweepPlan("monitor.ecg", 7, 11, 0, 8_000_000_000, 100_000_000, 9_000_000_000),
        13, 17, SessionRunState.Running,
        DataContinuityStateMachine.Start(LocalContinuationPolicy.DefaultDuration, 0).CaptureState(), 0, 0);

    private static NoDataSweepPlan Replacement(SweepPlanScheduler scheduler) =>
        scheduler.CaptureState().Presentation.Plan with
        {
            SweepEpoch = 8,
            CycleOriginPresentationNs = scheduler.NextBoundary().SweepClockNs,
            VisibleDurationNs = 4_000_000_000,
            RequiredRenderHistoryNs = 5_000_000_000,
        };

    private static void NextBoundaryIsStrictlyFutureAndUsesEffectiveClock()
    {
        SweepStateProjectionStateMachine source = Presentation();
        var scheduler = SweepPlanScheduler.Start(source.CaptureState());
        Check.That(scheduler.NextBoundary() == new SweepCycleBoundary(8_000_000_000, 8_000_000_000),
            "the initial left edge schedules the next cycle, not the current origin");
        scheduler.Advance(8_000_000_000, 123);
        Check.That(scheduler.NextBoundary().PresentationNs == 16_000_000_000,
            "an exact wrap schedules strictly one full cycle ahead");
        source.ChangeRunState(SessionRunState.Paused, 2_000_000_000, 12);
        source.ChangeRunState(SessionRunState.Running, 5_000_000_000, 12);
        scheduler = SweepPlanScheduler.Start(source.CaptureState());
        Check.That(scheduler.NextBoundary() == new SweepCycleBoundary(11_000_000_000, 8_000_000_000),
            "pause offset separates raw presentation deadline from effective sweep origin");
        scheduler.Schedule(Replacement(scheduler), 14, 20);
        Check.That(scheduler.Advance(11_000_000_000, 15).WriteHeadPhasePpm == 0,
            "the paused-offset deadline commits the derived origin");
    }

    private static void ScheduledPlanCommitsAtomicallyWithActualPatientFrontier()
    {
        var direct = SweepPlanScheduler.Start(Presentation().CaptureState());
        var chunked = SweepPlanScheduler.Start(Presentation().CaptureState());
        foreach (SweepPlanScheduler scheduler in new[] { direct, chunked })
        {
            SweepStateProjectionState before = scheduler.CaptureState().Presentation;
            scheduler.Schedule(Replacement(scheduler), 14, 18);
            Check.That(scheduler.CaptureState().Presentation == before,
                "scheduling validates without publishing future clocks or revisions");
        }

        chunked.Advance(7_999_999_999, 23);
        Check.That(chunked.CaptureState().Presentation.PlanRevision == 13 && chunked.CaptureState().Pending is not null,
            "one nanosecond before the boundary retains the old plan");
        foreach (SweepPlanScheduler scheduler in new[] { direct, chunked })
        {
            SweepStateProjectionSnapshot result = scheduler.Advance(8_000_000_000, 29);
            Check.That(result.PlanRevision == 14 && result.SweepRevision == 18 &&
                result.WriteHeadPhasePpm == 0 && result.PlayheadDataSimTimeNs == 29 &&
                scheduler.CaptureState().Pending is null,
                "commit uses actual caller patient time and clears pending atomically");
        }

        Check.That(direct.CaptureState() == chunked.CaptureState(), "authority chunking does not affect final state");
    }

    private static void SkippedBoundaryAndInvalidInputsRetainPendingPlan()
    {
        var scheduler = SweepPlanScheduler.Start(Presentation().CaptureState());
        SweepPlanSchedulerState empty = scheduler.CaptureState();
        NoDataSweepPlan plan = Replacement(scheduler);
        Check.That(Reason(() => scheduler.Schedule(plan with { SweepEpoch = 7 }, 14, 18)) ==
            "SweepState.StalePlanReplacement" && scheduler.CaptureState() == empty,
            "invalid schedule never installs pending state");
        scheduler.Schedule(plan, 14, 18);
        scheduler.Advance(1, 10);
        SweepPlanSchedulerState before = scheduler.CaptureState();
        foreach ((Action action, string reason) in new (Action, string)[]
        {
            (() => scheduler.Schedule(plan, 14, 18), "SweepSchedule.AlreadyPending"),
            (() => scheduler.Advance(8_000_000_001, 11), "SweepSchedule.BoundarySkipped"),
            (() => scheduler.Advance(8_000_000_000, 9), "SweepState.PlayheadReversed"),
            (() => scheduler.Advance(0, 10), "SweepState.TimeReversed"),
        })
        {
            Check.That(Reason(action) == reason && scheduler.CaptureState() == before,
                "failure preserves both pending and presentation for retry");
        }

        Check.That(scheduler.Advance(8_000_000_000, 11).PlanRevision == 14,
            "an exact-boundary retry remains possible after a skipped invocation");
    }

    private static void PendingCheckpointRevalidatesBoundaryAndPlan()
    {
        var scheduler = SweepPlanScheduler.Start(Presentation().CaptureState());
        scheduler.Schedule(Replacement(scheduler), 14, 18);
        scheduler.Advance(3_000_000_000, 42);
        SweepPlanSchedulerState state = scheduler.CaptureState();
        var restored = SweepPlanScheduler.Restore(state);
        foreach (ScheduledSweepPlan corrupt in new[]
        {
            state.Pending! with { Boundary = new SweepCycleBoundary(8_000_000_001, 8_000_000_000) },
            state.Pending! with { Boundary = new SweepCycleBoundary(16_000_000_000, 16_000_000_000) },
            state.Pending! with { PlanRevision = 13 },
            state.Pending! with { Plan = state.Pending.Plan with { GroupId = "other" } },
            state.Pending! with { Plan = state.Pending.Plan with { RequiredRenderHistoryNs = 1 } },
        })
        {
            Check.That(Reason(() => SweepPlanScheduler.Restore(state with { Pending = corrupt })) ==
                "SweepSchedule.InvalidCheckpoint", "restore validates full pending identity, plan and next deadline");
        }

        Check.That(scheduler.Advance(8_000_000_000, 50) == restored.Advance(8_000_000_000, 50) &&
            SweepPlanScheduler.Restore(restored.CaptureState()).CaptureState() == restored.CaptureState(),
            "split run and completed checkpoint restore preserve exact projection");
    }

    private static void SchedulingRejectsUnsupportedAxesAndClockOverflow()
    {
        SweepStateProjectionStateMachine frozen = Presentation();
        frozen.EnterFrozen(0, 0);
        SweepStateProjectionStateMachine paused = Presentation();
        paused.ChangeRunState(SessionRunState.Paused, 0, 0);
        SweepStateProjectionStateMachine noData = Presentation();
        noData.SynchronizeContinuity(DataContinuityStateMachine.Start(
            LocalContinuationPolicy.DefaultDuration, 0).Disconnect(false, 1), 0, 0);
        foreach (SweepStateProjectionStateMachine source in new[] { frozen, paused, noData })
        {
            var scheduler = SweepPlanScheduler.Start(source.CaptureState());
            SweepPlanSchedulerState before = scheduler.CaptureState();
            Check.That(Reason(() => scheduler.NextBoundary()) == "SweepSchedule.Unavailable" &&
                scheduler.CaptureState() == before, "unsupported axes cannot reserve a boundary");
        }

        SweepStateProjectionState state = Presentation().CaptureState();
        var overflow = SweepPlanScheduler.Start(state with
        {
            LastPresentationNs = long.MaxValue,
            LiveSweepClockNs = long.MaxValue,
        });
        Check.That(Reason(() => overflow.NextBoundary()) == "SweepSchedule.BoundaryOutOfRange",
            "next-cycle arithmetic must fail closed instead of wrapping signed clocks");
    }

    private static string? Reason(Action action)
    {
        try { action(); return null; }
        catch (SweepPlanSchedulerException exception) { return exception.ReasonCode; }
        catch (SweepStateProjectionException exception) { return exception.ReasonCode; }
    }
}
