// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed class SweepPlanSchedulerException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record SweepCycleBoundary(long PresentationNs, long SweepClockNs);

public sealed record ScheduledSweepPlan(
    NoDataSweepPlan Plan,
    ulong PlanRevision,
    ulong SweepRevision,
    SweepCycleBoundary Boundary);

public sealed record SweepPlanSchedulerState(
    SweepStateProjectionState Presentation,
    ScheduledSweepPlan? Pending);

// Host use-case boundary; callers supply the actual patient frontier at every
// advancement. A UI frame must never substitute for the scheduled authority call.
public sealed class SweepPlanScheduler
{
    private SweepStateProjectionStateMachine _presentation;
    private ScheduledSweepPlan? _pending;

    private SweepPlanScheduler(SweepStateProjectionStateMachine presentation)
    {
        _presentation = presentation;
    }

    public static SweepPlanScheduler Start(SweepStateProjectionState presentation) =>
        new(SweepStateProjectionStateMachine.Restore(presentation));

    public static SweepPlanScheduler Restore(SweepPlanSchedulerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            SweepPlanScheduler restored = Start(state.Presentation);
            if (state.Pending is not null)
            {
                ScheduledSweepPlan pending = state.Pending;
                ScheduledSweepPlan validated = restored.Schedule(
                    pending.Plan, pending.PlanRevision, pending.SweepRevision);
                if (validated != pending)
                {
                    throw Error("SweepSchedule.InvalidCheckpoint", nameof(state));
                }
            }

            return restored;
        }
        catch (ArgumentException)
        {
            throw Error("SweepSchedule.InvalidCheckpoint", nameof(state));
        }
    }

    public SweepCycleBoundary NextBoundary()
    {
        SweepStateProjectionState state = _presentation.CaptureState();
        if (state.SessionRunState != SessionRunState.Running ||
            state.TemporalViewMode != TemporalViewMode.LiveSweep ||
            state.ContinuityState.DataAvailability != DataAvailability.Authoritative ||
            state.ContinuityState.AuthorityState != AuthorityState.Authoritative)
        {
            throw Error("SweepSchedule.Unavailable", nameof(state));
        }

        Int128 elapsed = (Int128)state.LiveSweepClockNs - state.Plan.CycleOriginPresentationNs;
        Int128 remaining = state.Plan.VisibleDurationNs - elapsed % state.Plan.VisibleDurationNs;
        Int128 presentation = (Int128)state.LastPresentationNs + remaining;
        Int128 sweepClock = (Int128)state.LiveSweepClockNs + remaining;
        if (presentation > long.MaxValue || sweepClock > long.MaxValue)
        {
            throw Error("SweepSchedule.BoundaryOutOfRange", nameof(state));
        }

        return new SweepCycleBoundary((long)presentation, (long)sweepClock);
    }

    public ScheduledSweepPlan Schedule(
        NoDataSweepPlan plan,
        ulong planRevision,
        ulong sweepRevision)
    {
        if (_pending is not null)
        {
            throw Error("SweepSchedule.AlreadyPending", nameof(plan));
        }

        SweepCycleBoundary boundary = NextBoundary();
        SweepStateProjectionState state = _presentation.CaptureState();
        var trial = SweepStateProjectionStateMachine.Restore(state);
        // Validate only: the trial is discarded, so no future patient time is
        // inferred or published while reserving the boundary.
        trial.ReplacePlanAtCycleBoundary(plan, planRevision, sweepRevision,
            boundary.PresentationNs, state.LivePlayheadDataSimTimeNs);
        ScheduledSweepPlan pending = new(plan, planRevision, sweepRevision, boundary);
        _pending = pending;
        return pending;
    }

    public SweepStateProjectionSnapshot Advance(long presentationNs, long livePlayheadDataSimTimeNs)
    {
        ScheduledSweepPlan? pending = _pending;
        if (pending is not null && presentationNs > pending.Boundary.PresentationNs)
        {
            throw Error("SweepSchedule.BoundarySkipped", nameof(presentationNs));
        }

        var trial =
            SweepStateProjectionStateMachine.Restore(_presentation.CaptureState());
        bool commit = pending is not null && presentationNs == pending.Boundary.PresentationNs;
        SweepStateProjectionSnapshot projection = commit
            ? trial.ReplacePlanAtCycleBoundary(pending!.Plan, pending.PlanRevision,
                pending.SweepRevision, presentationNs, livePlayheadDataSimTimeNs)
            : trial.Advance(presentationNs, livePlayheadDataSimTimeNs);
        _presentation = trial;
        if (commit)
        {
            _pending = null;
        }

        return projection;
    }

    public SweepPlanSchedulerState CaptureState() => new(_presentation.CaptureState(), _pending);

    private static SweepPlanSchedulerException Error(string reasonCode, string parameterName) =>
        new(reasonCode, parameterName);
}
