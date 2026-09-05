// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepStateProjectionSpecifications
{
    private const ulong Second = 1_000_000_000;

    public static Specification[] All =>
    [
        new(nameof(AuthorityReplacesPlanAtExactCycleBoundary), AuthorityReplacesPlanAtExactCycleBoundary),
        new(nameof(ReplacementRejectsRoundedPhaseAndInvalidIdentityAtomically), ReplacementRejectsRoundedPhaseAndInvalidIdentityAtomically),
        new(nameof(ReplacementUsesHeldSweepClockAfterPause), ReplacementUsesHeldSweepClockAfterPause),
        new(nameof(ReplacementPreservesUnsupportedViewsAndContinuity), ReplacementPreservesUnsupportedViewsAndContinuity),
        new(nameof(ReplacementCheckpointAndRevisionLimitsRemainValid), ReplacementCheckpointAndRevisionLimitsRemainValid),
        new(nameof(RunningLiveProjectionTracksSharedClockAndPlayhead),
            RunningLiveProjectionTracksSharedClockAndPlayhead),
        new(nameof(PauseHoldsLiveHeadAndResumeContinuesWithoutJump),
            PauseHoldsLiveHeadAndResumeContinuesWithoutJump),
        new(nameof(StoppedAlsoRejectsPatientPlayheadProgress),
            StoppedAlsoRejectsPatientPlayheadProgress),
        new(nameof(FrozenPinsDisplayWhileBackgroundLiveAdvances),
            FrozenPinsDisplayWhileBackgroundLiveAdvances),
        new(nameof(ReviewSeekSuppressesTransientReplayAndLeavesLiveUntouched),
            ReviewSeekSuppressesTransientReplayAndLeavesLiveUntouched),
        new(nameof(NoDataAfterPauseContinuesFromTheHeldPhase),
            NoDataAfterPauseContinuesFromTheHeldPhase),
        new(nameof(NoDataDoesNotOverwriteAFrozenView),
            NoDataDoesNotOverwriteAFrozenView),
        new(nameof(ProjectionCheckpointAndFailedTransitionsAreAtomic),
            ProjectionCheckpointAndFailedTransitionsAreAtomic),
    ];

    private static NoDataSweepPlan Replacement(long origin = 8_000_000_000) => Plan() with
    {
        SweepEpoch = 8,
        CycleOriginPresentationNs = origin,
        VisibleDurationNs = 4 * Second,
        RequiredRenderHistoryNs = 5 * Second,
    };

    private static void AuthorityReplacesPlanAtExactCycleBoundary()
    {
        SweepStateProjectionStateMachine direct = Start();
        SweepStateProjectionStateMachine chunked = Start();
        chunked.Advance(3_000_000_000, 123);
        chunked.Advance(7_000_000_000, 456);
        foreach (SweepStateProjectionStateMachine machine in new[] { direct, chunked })
        {
            SweepStateProjectionSnapshot result = machine.ReplacePlanAtCycleBoundary(
                Replacement(), 14, 18, 8_000_000_000, 789);
            Check.That(result.CycleIndex == 0 && result.WriteHeadPhasePpm == 0 &&
                result.PlanRevision == 14 && result.SweepRevision == 18 &&
                result.PresentationClockRevision == 11 && result.PlayheadDataSimTimeNs == 789,
                "authority commits new plan and revisions together without substituting patient time");
        }

        Check.That(direct.CaptureState() == chunked.CaptureState() &&
            direct.Advance(9_000_000_000, 800).WriteHeadPhasePpm == 250_000,
            "frame chunking cannot change replacement or the new fixed duration");
    }

    private static void ReplacementRejectsRoundedPhaseAndInvalidIdentityAtomically()
    {
        SweepStateProjectionStateMachine machine = Start();
        SweepStateProjectionState before = machine.CaptureState();
        foreach (long time in new long[] { 0, 7_999_999_999, 8_000_000_001 })
        {
            Check.That(Reason(() => machine.ReplacePlanAtCycleBoundary(
                Replacement(time), 14, 18, time, 1)) == "SweepState.NotCycleBoundary" &&
                machine.CaptureState() == before,
                "initial origin and either nanosecond beside wrap reject without advancing clocks");
        }

        foreach (NoDataSweepPlan plan in new[]
        {
            Replacement() with { GroupId = "other" },
            Replacement() with { PresentationClockRevision = 12 },
            Replacement() with { SweepEpoch = 7 },
            Replacement() with { RequiredRenderHistoryNs = 4 * Second },
            Replacement() with { CycleOriginPresentationNs = 0 },
        })
        {
            Check.That(Reason(() => machine.ReplacePlanAtCycleBoundary(plan, 14, 18, 8_000_000_000, 1)) is not null &&
                machine.CaptureState() == before, "invalid plans and identities leave all fields unchanged");
        }

        foreach ((ulong planRevision, ulong sweepRevision) in new[] { (13UL, 18UL), (14UL, 17UL) })
        {
            Check.That(Reason(() => machine.ReplacePlanAtCycleBoundary(
                Replacement(), planRevision, sweepRevision, 8_000_000_000, 1)) == "SweepState.StalePlanReplacement" &&
                machine.CaptureState() == before, "both authority revisions must advance");
        }
    }

    private static void ReplacementUsesHeldSweepClockAfterPause()
    {
        SweepStateProjectionStateMachine machine = Start();
        machine.ChangeRunState(SessionRunState.Paused, 2_000_000_000, 25);
        machine.ChangeRunState(SessionRunState.Running, 5_000_000_000, 25);
        SweepStateProjectionState before = machine.CaptureState();
        Check.That(Reason(() => machine.ReplacePlanAtCycleBoundary(
            Replacement(), 14, 20, 8_000_000_000, 50)) == "SweepState.NotCycleBoundary" &&
            machine.CaptureState() == before, "raw presentation wrap is not the held Live sweep boundary");
        SweepStateProjectionSnapshot result = machine.ReplacePlanAtCycleBoundary(
            Replacement(), 14, 20, 11_000_000_000, 50);
        Check.That(result.WriteHeadPhasePpm == 0 && result.PlayheadDataSimTimeNs == 50 &&
            SweepStateProjectionStateMachine.Restore(machine.CaptureState()).CaptureState() == machine.CaptureState(),
            "effective sweep origin survives restoration after pause offset");
    }

    private static void ReplacementPreservesUnsupportedViewsAndContinuity()
    {
        SweepStateProjectionStateMachine frozen = Start();
        frozen.EnterFrozen(1, 1);
        SweepStateProjectionStateMachine review = Start();
        review.EnterReview("record.old", 0, 1, 1);
        foreach (SweepStateProjectionStateMachine machine in new[]
        {
            frozen, review, Start(SessionRunState.Paused), Start(SessionRunState.Stopped),
            Start(continuityState: Continuity().Disconnect(false, 10)),
        })
        {
            SweepStateProjectionState before = machine.CaptureState();
            Check.That(Reason(() => machine.ReplacePlanAtCycleBoundary(
                Replacement(), 14, 20, 8_000_000_000, 1)) == "SweepState.PlanReplacementUnavailable" &&
                machine.CaptureState() == before,
                "unsupported axes retain pinned history and NoData coverage exactly");
        }
    }

    private static void ReplacementCheckpointAndRevisionLimitsRemainValid()
    {
        SweepStateProjectionStateMachine machine = Start();
        machine.ReplacePlanAtCycleBoundary(Replacement() with { SweepEpoch = ulong.MaxValue },
            ulong.MaxValue, ulong.MaxValue, 8_000_000_000, 9);
        SweepStateProjectionState checkpoint = machine.CaptureState();
        var restored = SweepStateProjectionStateMachine.Restore(checkpoint);
        Check.That(restored.CaptureState() == checkpoint &&
            machine.Advance(9_000_000_000, 10) == restored.Advance(9_000_000_000, 10),
            "maximum revisions restore and ordinary advancement does not consume revisions");
        SweepStateProjectionState before = restored.CaptureState();
        Check.That(Reason(() => restored.ReplacePlanAtCycleBoundary(Replacement(12_000_000_000),
            0, 0, 12_000_000_000, 11)) == "SweepState.StalePlanReplacement" &&
            restored.CaptureState() == before, "exhausted revisions cannot wrap");
        Check.That(Reason(() => SweepStateProjectionStateMachine.Restore(checkpoint with
        {
            Plan = checkpoint.Plan with { CycleOriginPresentationNs = 8_000_000_001 },
        })) == "SweepState.InvalidCheckpoint", "replacement origin cannot be ahead of accepted clock");
    }

    private static void RunningLiveProjectionTracksSharedClockAndPlayhead()
    {
        SweepStateProjectionStateMachine machine = Start();

        SweepStateProjectionSnapshot projection = machine.Advance(
            1 * (long)Second,
            1 * (long)Second);

        Check.That(
            projection.PlanRevision == 13 &&
            projection.SweepRevision == 17 &&
            projection.PresentationClockRevision == 11 &&
            projection.SessionRunState == SessionRunState.Running &&
            projection.TemporalViewMode == TemporalViewMode.LiveSweep &&
            projection.DataAvailability == DataAvailability.Authoritative &&
            projection.AuthorityState == AuthorityState.Authoritative &&
            projection.PlayheadDataSimTimeNs == 1 * (long)Second &&
            projection.CycleIndex == 0 &&
            projection.WriteHeadPhasePpm == 125_000 &&
            projection.TraceHistory == TraceHistoryPresentation.LiveHistory &&
            projection.NoDataCoverage is null,
            "Running Live must expose the shared plan identity, phase, and playhead");
    }

    private static void PauseHoldsLiveHeadAndResumeContinuesWithoutJump()
    {
        SweepStateProjectionStateMachine machine = Start();
        _ = machine.Advance(2 * (long)Second, 2 * (long)Second);
        _ = machine.ChangeRunState(
            SessionRunState.Paused,
            2 * (long)Second,
            2 * (long)Second);

        SweepStateProjectionSnapshot paused = machine.Advance(
            5 * (long)Second,
            2 * (long)Second);
        SweepStateProjectionState beforeFailure = machine.CaptureState();
        Check.That(
            paused.WriteHeadPhasePpm == 250_000 &&
            paused.SweepRevision == 18 &&
            paused.PlayheadDataSimTimeNs == 2 * (long)Second &&
            Reason(() => machine.Advance(
                5 * (long)Second,
                3 * (long)Second)) ==
                "SweepState.PlayheadAdvancedWhileStopped" &&
            machine.CaptureState() == beforeFailure,
            "Paused must hold both Live head and data playhead atomically");

        _ = machine.ChangeRunState(
            SessionRunState.Running,
            5 * (long)Second,
            2 * (long)Second);
        SweepStateProjectionSnapshot resumed = machine.Advance(
            6 * (long)Second,
            3 * (long)Second);
        Check.That(
            resumed.WriteHeadPhasePpm == 375_000 &&
            resumed.SweepRevision == 19 &&
            resumed.PlayheadDataSimTimeNs == 3 * (long)Second,
            "resume must continue from the held phase instead of jumping by pause age");
    }

    private static void FrozenPinsDisplayWhileBackgroundLiveAdvances()
    {
        SweepStateProjectionStateMachine machine = Start();
        _ = machine.Advance(2 * (long)Second, 2 * (long)Second);
        SweepStateProjectionSnapshot entered = machine.EnterFrozen(
            2 * (long)Second,
            2 * (long)Second);
        SweepStateProjectionSnapshot frozen = machine.Advance(
            5 * (long)Second,
            5 * (long)Second);

        Check.That(
            entered.FreezeAnchorSimTimeNs == 2 * (long)Second &&
            entered.SweepRevision == 18 &&
            frozen.TemporalViewMode == TemporalViewMode.FrozenSnapshot &&
            frozen.PlayheadDataSimTimeNs == 2 * (long)Second &&
            frozen.WriteHeadPhasePpm == 250_000 &&
            frozen.TraceHistory ==
                TraceHistoryPresentation.PinnedOriginalRange,
            "Frozen must pin the displayed raw range while reception continues");

        SweepStateProjectionSnapshot live = machine.ExitFrozen(
            5 * (long)Second,
            5 * (long)Second);
        Check.That(
            live.TemporalViewMode == TemporalViewMode.LiveSweep &&
            live.PlayheadDataSimTimeNs == 5 * (long)Second &&
            live.WriteHeadPhasePpm == 625_000 &&
            live.SweepRevision == 19 &&
            live.FreezeAnchorSimTimeNs is null,
            "unfreeze must rebuild directly at current Live phase without catch-up");
    }

    private static void StoppedAlsoRejectsPatientPlayheadProgress()
    {
        SweepStateProjectionStateMachine machine = Start();
        _ = machine.Advance(1 * (long)Second, 1 * (long)Second);
        _ = machine.ChangeRunState(
            SessionRunState.Stopped,
            1 * (long)Second,
            1 * (long)Second);

        SweepStateProjectionSnapshot stopped = machine.Advance(
            3 * (long)Second,
            1 * (long)Second);
        Check.That(
            stopped.SessionRunState == SessionRunState.Stopped &&
            stopped.WriteHeadPhasePpm == 125_000 &&
            stopped.PlayheadDataSimTimeNs == 1 * (long)Second &&
            Reason(() => machine.Advance(
                3 * (long)Second,
                1_000_000_001)) ==
                "SweepState.PlayheadAdvancedWhileStopped",
            "Stopped must not accept a new patient playhead or move the Live head");
    }

    private static void ReviewSeekSuppressesTransientReplayAndLeavesLiveUntouched()
    {
        SweepStateProjectionStateMachine machine = Start();
        _ = machine.Advance(2 * (long)Second, 2 * (long)Second);
        _ = machine.EnterReview(
            "record.segment-7",
            reviewPlayheadDataSimTimeNs: 500_000_000,
            presentationNs: 2 * (long)Second,
            livePlayheadDataSimTimeNs: 2 * (long)Second);
        _ = machine.Advance(5 * (long)Second, 5 * (long)Second);

        SweepStateProjectionSnapshot review = machine.SeekReview(1 * (long)Second);
        Check.That(
            review.TemporalViewMode == TemporalViewMode.HistoricalReview &&
            review.ReviewSegmentRef == "record.segment-7" &&
            review.SweepRevision == 19 &&
            review.PlayheadDataSimTimeNs == 1 * (long)Second &&
            review.WriteHeadPhasePpm == 250_000 &&
            review.TransientReplayPolicy == TransientReplayPolicy.Suppress &&
            review.TraceHistory == TraceHistoryPresentation.PinnedOriginalRange,
            "review seek must move only its pinned playhead and suppress transients");

        SweepStateProjectionSnapshot live = machine.ExitReview(
            5 * (long)Second,
            5 * (long)Second);
        Check.That(
            live.PlayheadDataSimTimeNs == 5 * (long)Second &&
            live.WriteHeadPhasePpm == 625_000 &&
            live.SweepRevision == 20 &&
            live.ReviewSegmentRef is null &&
            live.TransientReplayPolicy == TransientReplayPolicy.FollowLiveSource,
            "leaving review must expose current Live without replaying the gap");
    }

    private static void NoDataAfterPauseContinuesFromTheHeldPhase()
    {
        DataContinuityStateMachine continuity = Continuity();
        SweepStateProjectionStateMachine machine = Start(
            continuityState: continuity.CaptureState());
        _ = machine.Advance(2 * (long)Second, 2 * (long)Second);
        _ = machine.ChangeRunState(
            SessionRunState.Paused,
            2 * (long)Second,
            2 * (long)Second);
        _ = machine.Advance(5 * (long)Second, 2 * (long)Second);
        DataContinuityState noData = continuity.Disconnect(false, 10);

        SweepStateProjectionSnapshot entered = machine.SynchronizeContinuity(
            noData,
            5 * (long)Second,
            2 * (long)Second);
        SweepStateProjectionSnapshot advanced = machine.Advance(
            6 * (long)Second,
            2 * (long)Second);

        Check.That(
            entered.WriteHeadPhasePpm == 250_000 &&
            entered.SweepRevision == 19 &&
            entered.NoDataCoverage is { CoveredDurationNs: 0 } &&
            advanced.SessionRunState == SessionRunState.Paused &&
            advanced.WriteHeadPhasePpm == 375_000 &&
            advanced.NoDataCoverage is
            { CoveredDurationNs: Second, FullyCovered: false },
            "NoData must erase from the held phase even while simulation stays paused");
    }

    private static void NoDataDoesNotOverwriteAFrozenView()
    {
        DataContinuityStateMachine continuity = Continuity();
        SweepStateProjectionStateMachine machine = Start(
            continuityState: continuity.CaptureState());
        _ = machine.Advance(2 * (long)Second, 2 * (long)Second);
        _ = machine.EnterFrozen(2 * (long)Second, 2 * (long)Second);
        DataContinuityState noData = continuity.Disconnect(false, 10);
        _ = machine.SynchronizeContinuity(
            noData,
            3 * (long)Second,
            3 * (long)Second);
        SweepStateProjectionSnapshot frozen = machine.Advance(
            5 * (long)Second,
            3 * (long)Second);

        Check.That(
            frozen.TemporalViewMode == TemporalViewMode.FrozenSnapshot &&
            frozen.WriteHeadPhasePpm == 250_000 &&
            frozen.NoDataCoverage is null &&
            frozen.NoDataSinceAuthorityMonotonicNs == 10,
            "NoData may progress in the background but cannot overwrite Frozen");

        SweepStateProjectionSnapshot live = machine.ExitFrozen(
            5 * (long)Second,
            3 * (long)Second);
        Check.That(
            live.WriteHeadPhasePpm == 625_000 &&
            live.NoDataCoverage is { CoveredDurationNs: 2 * Second },
            "unfreeze must rebuild the current NoData Live window directly");
    }

    private static void ProjectionCheckpointAndFailedTransitionsAreAtomic()
    {
        SweepStateProjectionStateMachine original = Start();
        _ = original.Advance(2 * (long)Second, 2 * (long)Second);
        _ = original.EnterFrozen(2 * (long)Second, 2 * (long)Second);
        SweepStateProjectionState checkpoint = original.CaptureState();
        var restored =
            SweepStateProjectionStateMachine.Restore(checkpoint);

        Check.That(
            restored.CaptureState() == checkpoint &&
            ProjectionEqual(
                original.Advance(4 * (long)Second, 4 * (long)Second),
                restored.Advance(4 * (long)Second, 4 * (long)Second)),
            "checkpoint restore must preserve pinned and background sweep clocks");

        SweepStateProjectionState beforeFailure = restored.CaptureState();
        Check.That(
            Reason(() => restored.EnterReview(
                "record.segment-7",
                1,
                4 * (long)Second,
                4 * (long)Second)) == "SweepState.InvalidViewTransition" &&
            restored.CaptureState() == beforeFailure,
            "invalid view transitions must reject without partial mutation");

        SweepStateProjectionState unsupported = beforeFailure with
        {
            TemporalViewMode = TemporalViewMode.AcquisitionFill,
        };
        Check.That(
            Reason(() => SweepStateProjectionStateMachine.Restore(unsupported)) ==
                "SweepState.InvalidCheckpoint",
            "SweepRaster state must not guess FillOnceThenHold record semantics");

        DataContinuityStateMachine continuity = Continuity();
        DataContinuityState noData = continuity.Disconnect(false, 10);
        SweepStateProjectionStateMachine noDataMachine = Start(
            continuityState: noData);
        SweepStateProjectionState corruptNested =
            noDataMachine.CaptureState() with
            {
                NoDataSweepState = noDataMachine.CaptureState().NoDataSweepState!
                    with
                { StartedAtPresentationNs = 1 },
            };
        Check.That(
            Reason(() => SweepStateProjectionStateMachine.Restore(corruptNested)) ==
                "SweepState.InvalidCheckpoint",
            "nested NoDataSweep corruption must use the projection rejection boundary");
    }

    private static SweepStateProjectionStateMachine Start(
        SessionRunState runState = SessionRunState.Running,
        DataContinuityState? continuityState = null) =>
        SweepStateProjectionStateMachine.Start(
            Plan(),
            planRevision: 13,
            sweepRevision: 17,
            runState,
            continuityState ?? Continuity().CaptureState(),
            presentationNs: 0,
            playheadDataSimTimeNs: 0);

    private static NoDataSweepPlan Plan() => new(
        "monitor.ecg",
        SweepEpoch: 7,
        PresentationClockRevision: 11,
        CycleOriginPresentationNs: 0,
        VisibleDurationNs: 8 * Second,
        EraseGapNs: Second / 10,
        RequiredRenderHistoryNs: 9 * Second);

    private static DataContinuityStateMachine Continuity() =>
        DataContinuityStateMachine.Start(
            LocalContinuationPolicy.DefaultDuration,
            0);

    private static bool ProjectionEqual(
        SweepStateProjectionSnapshot left,
        SweepStateProjectionSnapshot right)
    {
        NoDataSweepCoverage? ignored = null;
        return left with { NoDataCoverage = ignored } ==
            right with { NoDataCoverage = ignored };
    }

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (SweepStateProjectionException exception)
        {
            return exception.ReasonCode;
        }
    }
}
