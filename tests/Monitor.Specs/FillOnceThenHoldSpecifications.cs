// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class FillOnceThenHoldSpecifications
{
    private const ulong Second = 1_000_000_000;

    public static Specification[] All =>
    [
        new(nameof(AcquisitionFillsOneSharedRecordFromLeftToRight),
            AcquisitionFillsOneSharedRecordFromLeftToRight),
        new(nameof(CompletionPinsTheExactRangeWithoutWrapping),
            CompletionPinsTheExactRangeWithoutWrapping),
        new(nameof(FrameChunkingCannotChangeFillProgress),
            FrameChunkingCannotChangeFillProgress),
        new(nameof(PauseAndNoDataHoldThePartialRecord),
            PauseAndNoDataHoldThePartialRecord),
        new(nameof(CapturedRecordSuppressesTransientReplay),
            CapturedRecordSuppressesTransientReplay),
        new(nameof(CheckpointAndFailedCompletionAreAtomic),
            CheckpointAndFailedCompletionAreAtomic),
        new(nameof(PlanRequiresTwelveUniqueSlotsAndFullHistory),
            PlanRequiresTwelveUniqueSlotsAndFullHistory),
    ];

    private static void AcquisitionFillsOneSharedRecordFromLeftToRight()
    {
        FillOnceThenHoldStateMachine machine = Start();

        SweepStateProjectionSnapshot half = machine.Advance(
            5 * (long)Second,
            5 * (long)Second);
        FillOnceCoverage coverage = machine.CaptureCoverage();

        Check.That(
            half.PlanRevision == 13 &&
            half.SweepRevision == 17 &&
            half.PresentationClockRevision == 11 &&
            half.TemporalViewMode == TemporalViewMode.AcquisitionFill &&
            half.PlayheadDataSimTimeNs == 5 * (long)Second &&
            half.CycleIndex == 0 &&
            half.WriteHeadPhasePpm == 500_000 &&
            half.ReviewSegmentRef == "record.ecg12-7" &&
            half.TraceHistory == TraceHistoryPresentation.RecordFill &&
            half.NoDataCoverage is null &&
            coverage is
            {
                AcquiredDurationNs: 5 * Second,
                RemainingDurationNs: 5 * Second,
                Complete: false,
            },
            "standard ECG acquisition must fill one shared range from the left");
    }

    private static void CompletionPinsTheExactRangeWithoutWrapping()
    {
        FillOnceThenHoldStateMachine machine = Start();
        FillOnceThenHoldStateMachine skippedBoundary = Start();

        SweepStateProjectionSnapshot complete = machine.Advance(
            10 * (long)Second,
            10 * (long)Second);
        SweepStateProjectionSnapshot completedAfterDroppedFrame =
            skippedBoundary.Advance(
                12 * (long)Second,
                12 * (long)Second);
        PinnedRecordRange range = machine.CapturePinnedRecordRange();
        SweepStateProjectionSnapshot muchLater = machine.Advance(
            35 * (long)Second,
            35 * (long)Second);

        Check.That(
            complete.TemporalViewMode == TemporalViewMode.CapturedRecord &&
            complete.SweepRevision == 18 &&
            complete.CycleIndex == 0 &&
            complete.WriteHeadPhasePpm == 999_999 &&
            completedAfterDroppedFrame.TemporalViewMode ==
                TemporalViewMode.CapturedRecord &&
            completedAfterDroppedFrame.PlayheadDataSimTimeNs ==
                10 * (long)Second &&
            completedAfterDroppedFrame.SweepRevision == 18 &&
            range.SweepEpoch == 7 &&
            range.StartDataSimTimeNs == 0 &&
            range.EndExclusiveDataSimTimeNs == 10 * (long)Second &&
            range.SlotIds.Count == 12 &&
            muchLater.TemporalViewMode == TemporalViewMode.CapturedRecord &&
            muchLater.PlayheadDataSimTimeNs == 10 * (long)Second &&
            muchLater.CycleIndex == 0 &&
            muchLater.WriteHeadPhasePpm == 999_999 &&
            muchLater.SweepRevision == 18,
            "a completed record must pin its exact interval and never wrap");
    }

    private static void FrameChunkingCannotChangeFillProgress()
    {
        FillOnceThenHoldStateMachine direct = Start();
        FillOnceThenHoldStateMachine chunked = Start();

        SweepStateProjectionSnapshot directProjection = direct.Advance(
            9_500_000_000,
            9_500_000_000);
        _ = chunked.Advance(1_000_000_000, 1_000_000_000);
        _ = chunked.Advance(7_250_000_000, 7_250_000_000);
        SweepStateProjectionSnapshot chunkedProjection = chunked.Advance(
            9_500_000_000,
            9_500_000_000);

        Check.That(
            directProjection == chunkedProjection &&
            direct.CaptureCoverage() == chunked.CaptureCoverage(),
            "fill progress must derive from data time rather than frame count");
    }

    private static void PauseAndNoDataHoldThePartialRecord()
    {
        FillOnceThenHoldStateMachine pausedMachine = Start();
        _ = pausedMachine.Advance(2 * (long)Second, 2 * (long)Second);
        _ = pausedMachine.ChangeRunState(
            SessionRunState.Paused,
            2 * (long)Second,
            2 * (long)Second);
        SweepStateProjectionSnapshot paused = pausedMachine.Advance(
            6 * (long)Second,
            2 * (long)Second);
        FillOnceThenHoldState pausedBeforeFailure =
            pausedMachine.CaptureState();

        Check.That(
            paused.WriteHeadPhasePpm == 200_000 &&
            paused.SweepRevision == 18 &&
            Reason(() => pausedMachine.Advance(
                6 * (long)Second,
                3 * (long)Second)) ==
                "FillOnce.PlayheadAdvancedWhileStopped" &&
            StateEqual(pausedBeforeFailure, pausedMachine.CaptureState()),
            "pause must not fill a record using presentation-clock age");

        DataContinuityStateMachine continuity = Continuity();
        FillOnceThenHoldStateMachine noDataMachine = Start(
            continuityState: continuity.CaptureState());
        _ = noDataMachine.Advance(
            2 * (long)Second,
            2 * (long)Second);
        DataContinuityState noData = continuity.Disconnect(false, 10);
        SweepStateProjectionSnapshot disconnected =
            noDataMachine.SynchronizeContinuity(
                noData,
                2 * (long)Second,
                2 * (long)Second);
        _ = noDataMachine.Advance(6 * (long)Second, 2 * (long)Second);
        FillOnceThenHoldState noDataBeforeFailure = noDataMachine.CaptureState();

        Check.That(
            disconnected.DataAvailability == DataAvailability.NoData &&
            disconnected.SweepRevision == 18 &&
            noDataMachine.CaptureCoverage().AcquiredDurationNs == 2 * Second &&
            Reason(() => noDataMachine.Advance(
                6 * (long)Second,
                3 * (long)Second)) ==
                "FillOnce.PlayheadAdvancedWithoutData" &&
            StateEqual(noDataBeforeFailure, noDataMachine.CaptureState()),
            "NoData must hold partial acquisition instead of inventing samples");
    }

    private static void CapturedRecordSuppressesTransientReplay()
    {
        FillOnceThenHoldStateMachine machine = Start();
        Check.That(
            Reason(() => machine.CapturePinnedRecordRange()) ==
                "FillOnce.RecordNotCaptured",
            "an incomplete acquisition must not expose a captured range");

        SweepStateProjectionSnapshot captured = machine.Advance(
            10 * (long)Second,
            10 * (long)Second);
        Check.That(
            captured.TraceHistory ==
                TraceHistoryPresentation.PinnedOriginalRange &&
            captured.TransientReplayPolicy == TransientReplayPolicy.Suppress &&
            captured.ReviewSegmentRef == "record.ecg12-7" &&
            captured.FreezeAnchorSimTimeNs is null,
            "CapturedRecord must use raw pinned history without replay transients");
    }

    private static void CheckpointAndFailedCompletionAreAtomic()
    {
        FillOnceThenHoldStateMachine original = Start();
        _ = original.Advance(4 * (long)Second, 4 * (long)Second);
        FillOnceThenHoldState checkpoint = original.CaptureState();
        var restored =
            FillOnceThenHoldStateMachine.Restore(checkpoint);

        Check.That(
            StateEqual(checkpoint, restored.CaptureState()) &&
            original.Advance(7 * (long)Second, 7 * (long)Second) ==
                restored.Advance(7 * (long)Second, 7 * (long)Second),
            "checkpoint restore must preserve the exact partial record cursor");

        FillOnceThenHoldState invalidPhase = checkpoint with
        {
            TemporalViewMode = TemporalViewMode.CapturedRecord,
        };
        Check.That(
            Reason(() => FillOnceThenHoldStateMachine.Restore(invalidPhase)) ==
                "FillOnce.InvalidCheckpoint",
            "a checkpoint cannot claim capture before the record is complete");

        FillOnceThenHoldStateMachine exhausted = Start(
            sweepRevision: ulong.MaxValue);
        FillOnceThenHoldState beforeFailure = exhausted.CaptureState();
        Check.That(
            Reason(() => exhausted.Advance(
                10 * (long)Second,
                10 * (long)Second)) == "FillOnce.RevisionExhausted" &&
            StateEqual(beforeFailure, exhausted.CaptureState()),
            "failed completion must not partially pin or advance the record");
    }

    private static void PlanRequiresTwelveUniqueSlotsAndFullHistory()
    {
        string[] mutableSlots = Slots().ToArray();
        FillOnceThenHoldStateMachine machine = Start(
            plan: Plan(slotIds: mutableSlots));
        mutableSlots[0] = "ecg.mutated";
        _ = machine.Advance(10 * (long)Second, 10 * (long)Second);

        FillOnceThenHoldPlan tooFew = Plan(
            slotIds: Slots().Take(11).ToArray());
        string[] duplicates = Slots().ToArray();
        duplicates[11] = duplicates[0];
        FillOnceThenHoldPlan tooLittleHistory = Plan() with
        {
            RequiredRecordHistoryNs = 9 * Second,
        };
        FillOnceThenHoldPlan overflowing = Plan() with
        {
            RecordStartDataSimTimeNs = long.MaxValue,
        };

        Check.That(
            machine.CapturePinnedRecordRange().SlotIds[0] == "ecg.I" &&
            Reason(() => Start(plan: tooFew)) == "FillOnce.InvalidPlan" &&
            Reason(() => Start(plan: Plan(slotIds: duplicates))) ==
                "FillOnce.InvalidPlan" &&
            Reason(() => Start(plan: tooLittleHistory)) ==
                "FillOnce.InvalidPlan" &&
            Reason(() => Start(plan: overflowing)) ==
                "FillOnce.InvalidPlan",
            "a standard record needs twelve stable unique slots and full history");
    }

    private static FillOnceThenHoldStateMachine Start(
        FillOnceThenHoldPlan? plan = null,
        ulong sweepRevision = 17,
        DataContinuityState? continuityState = null) =>
        FillOnceThenHoldStateMachine.Start(
            plan ?? Plan(),
            planRevision: 13,
            sweepRevision,
            SessionRunState.Running,
            continuityState ?? Continuity().CaptureState(),
            presentationNs: 0,
            playheadDataSimTimeNs:
                (plan ?? Plan()).RecordStartDataSimTimeNs);

    private static FillOnceThenHoldPlan Plan(
        IReadOnlyList<string>? slotIds = null) => new(
        "ecg12.standard",
        "record.ecg12-7",
        SweepEpoch: 7,
        PresentationClockRevision: 11,
        RecordStartDataSimTimeNs: 0,
        RecordDurationNs: 10 * Second,
        RequiredRecordHistoryNs: 10 * Second,
        slotIds ?? Slots());

    private static IReadOnlyList<string> Slots() =>
    [
        "ecg.I",
        "ecg.II",
        "ecg.III",
        "ecg.aVR",
        "ecg.aVL",
        "ecg.aVF",
        "ecg.V1",
        "ecg.V2",
        "ecg.V3",
        "ecg.V4",
        "ecg.V5",
        "ecg.V6",
    ];

    private static DataContinuityStateMachine Continuity() =>
        DataContinuityStateMachine.Start(
            LocalContinuationPolicy.DefaultDuration,
            0);

    private static bool StateEqual(
        FillOnceThenHoldState left,
        FillOnceThenHoldState right) =>
        left.Plan with { SlotIds = Array.Empty<string>() } ==
            right.Plan with { SlotIds = Array.Empty<string>() } &&
        left.Plan.SlotIds.SequenceEqual(
            right.Plan.SlotIds,
            StringComparer.Ordinal) &&
        left.PlanRevision == right.PlanRevision &&
        left.SweepRevision == right.SweepRevision &&
        left.LastPresentationNs == right.LastPresentationNs &&
        left.SessionRunState == right.SessionRunState &&
        left.ContinuityState == right.ContinuityState &&
        left.LivePlayheadDataSimTimeNs == right.LivePlayheadDataSimTimeNs &&
        left.TemporalViewMode == right.TemporalViewMode;

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (FillOnceThenHoldException exception)
        {
            return exception.ReasonCode;
        }
    }
}
