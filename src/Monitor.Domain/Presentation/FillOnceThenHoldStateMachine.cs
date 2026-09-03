// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;

namespace Monitor.Domain.Presentation;

public sealed class FillOnceThenHoldException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record FillOnceThenHoldPlan(
    string GroupId,
    string RecordRef,
    ulong SweepEpoch,
    ulong PresentationClockRevision,
    long RecordStartDataSimTimeNs,
    ulong RecordDurationNs,
    ulong RequiredRecordHistoryNs,
    IReadOnlyList<string> SlotIds);

public sealed record FillOnceCoverage(
    long RecordStartDataSimTimeNs,
    long AcquiredThroughDataSimTimeNs,
    long RecordEndExclusiveDataSimTimeNs,
    ulong AcquiredDurationNs,
    ulong RemainingDurationNs,
    uint WriteHeadPhasePpm,
    bool Complete);

public sealed record PinnedRecordRange(
    string GroupId,
    string RecordRef,
    ulong SweepEpoch,
    long StartDataSimTimeNs,
    long EndExclusiveDataSimTimeNs,
    IReadOnlyList<string> SlotIds);

public sealed record FillOnceThenHoldState(
    FillOnceThenHoldPlan Plan,
    ulong PlanRevision,
    ulong SweepRevision,
    long LastPresentationNs,
    SessionRunState SessionRunState,
    DataContinuityState ContinuityState,
    long LivePlayheadDataSimTimeNs,
    TemporalViewMode TemporalViewMode);

public sealed class FillOnceThenHoldStateMachine
{
    public const int StandardEcgSlotCount = 12;

    private readonly FillOnceThenHoldPlan _plan;
    private readonly ulong _planRevision;
    private ulong _sweepRevision;
    private long _lastPresentationNs;
    private SessionRunState _sessionRunState;
    private DataContinuityState _continuityState;
    private long _livePlayheadDataSimTimeNs;
    private TemporalViewMode _temporalViewMode;

    private FillOnceThenHoldStateMachine(FillOnceThenHoldState state)
    {
        FillOnceThenHoldPlan plan = ValidateAndCopyPlan(
            state.Plan,
            nameof(state));
        ValidateState(state, plan);
        _plan = plan;
        _planRevision = state.PlanRevision;
        _sweepRevision = state.SweepRevision;
        _lastPresentationNs = state.LastPresentationNs;
        _sessionRunState = state.SessionRunState;
        _continuityState = state.ContinuityState;
        _livePlayheadDataSimTimeNs = state.LivePlayheadDataSimTimeNs;
        _temporalViewMode = state.TemporalViewMode;
    }

    public static FillOnceThenHoldStateMachine Start(
        FillOnceThenHoldPlan plan,
        ulong planRevision,
        ulong sweepRevision,
        SessionRunState sessionRunState,
        DataContinuityState continuityState,
        long presentationNs,
        long playheadDataSimTimeNs)
    {
        FillOnceThenHoldPlan copiedPlan = ValidateAndCopyPlan(
            plan,
            nameof(plan));
        ValidateContinuity(continuityState, nameof(continuityState));
        if (!Enum.IsDefined(sessionRunState))
        {
            throw Error("FillOnce.InvalidRunState", nameof(sessionRunState));
        }

        if (playheadDataSimTimeNs != copiedPlan.RecordStartDataSimTimeNs)
        {
            throw Error("FillOnce.InvalidStart", nameof(playheadDataSimTimeNs));
        }

        return new FillOnceThenHoldStateMachine(new FillOnceThenHoldState(
            copiedPlan,
            planRevision,
            sweepRevision,
            presentationNs,
            sessionRunState,
            continuityState,
            playheadDataSimTimeNs,
            TemporalViewMode.AcquisitionFill));
    }

    public static FillOnceThenHoldStateMachine Restore(
        FillOnceThenHoldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new FillOnceThenHoldStateMachine(state);
    }

    public SweepStateProjectionSnapshot Advance(
        long presentationNs,
        long livePlayheadDataSimTimeNs) => Mutate(trial =>
    {
        trial.AdvanceCore(presentationNs, livePlayheadDataSimTimeNs);
    });

    public SweepStateProjectionSnapshot ChangeRunState(
        SessionRunState sessionRunState,
        long presentationNs,
        long livePlayheadDataSimTimeNs) => Mutate(trial =>
    {
        if (!Enum.IsDefined(sessionRunState))
        {
            throw Error("FillOnce.InvalidRunState", nameof(sessionRunState));
        }

        trial.AdvanceCore(presentationNs, livePlayheadDataSimTimeNs);
        if (trial._sessionRunState != sessionRunState)
        {
            trial._sessionRunState = sessionRunState;
            trial.IncrementSweepRevision();
        }
    });

    public SweepStateProjectionSnapshot SynchronizeContinuity(
        DataContinuityState continuityState,
        long presentationNs,
        long livePlayheadDataSimTimeNs) => Mutate(trial =>
    {
        ValidateContinuity(continuityState, nameof(continuityState));
        if (continuityState.LastAuthorityMonotonicNs <
            trial._continuityState.LastAuthorityMonotonicNs)
        {
            throw Error(
                "FillOnce.AuthorityTimeReversed",
                nameof(continuityState));
        }

        trial.AdvanceCore(presentationNs, livePlayheadDataSimTimeNs);
        if (trial._continuityState != continuityState)
        {
            trial._continuityState = continuityState;
            trial.IncrementSweepRevision();
        }
    });

    public SweepStateProjectionSnapshot CaptureProjection()
    {
        FillOnceCoverage coverage = CaptureCoverage();
        bool captured =
            _temporalViewMode == TemporalViewMode.CapturedRecord;
        return new SweepStateProjectionSnapshot(
            _planRevision,
            _sweepRevision,
            _plan.PresentationClockRevision,
            _sessionRunState,
            _temporalViewMode,
            _continuityState.DataAvailability,
            _continuityState.AuthorityState,
            coverage.AcquiredThroughDataSimTimeNs,
            CycleIndex: 0,
            coverage.WriteHeadPhasePpm,
            FreezeAnchorSimTimeNs: null,
            _plan.RecordRef,
            _continuityState.NoDataSinceAuthorityMonotonicNs,
            captured
                ? TraceHistoryPresentation.PinnedOriginalRange
                : TraceHistoryPresentation.RecordFill,
            captured
                ? TransientReplayPolicy.Suppress
                : TransientReplayPolicy.FollowLiveSource,
            NoDataCoverage: null);
    }

    public FillOnceCoverage CaptureCoverage()
    {
        long endExclusive = RecordEndExclusiveDataSimTimeNs;
        long acquiredThrough = Math.Min(
            _livePlayheadDataSimTimeNs,
            endExclusive);
        ulong acquired = checked((ulong)(
            acquiredThrough - _plan.RecordStartDataSimTimeNs));
        bool complete = acquired == _plan.RecordDurationNs;
        uint phase = complete
            ? NoDataSweepStateMachine.PhasePartsPerMillion - 1
            : checked((uint)(
                (UInt128)acquired *
                NoDataSweepStateMachine.PhasePartsPerMillion /
                _plan.RecordDurationNs));
        return new FillOnceCoverage(
            _plan.RecordStartDataSimTimeNs,
            acquiredThrough,
            endExclusive,
            acquired,
            _plan.RecordDurationNs - acquired,
            phase,
            complete);
    }

    public PinnedRecordRange CapturePinnedRecordRange()
    {
        if (_temporalViewMode != TemporalViewMode.CapturedRecord)
        {
            throw Error("FillOnce.RecordNotCaptured", nameof(_temporalViewMode));
        }

        return new PinnedRecordRange(
            _plan.GroupId,
            _plan.RecordRef,
            _plan.SweepEpoch,
            _plan.RecordStartDataSimTimeNs,
            RecordEndExclusiveDataSimTimeNs,
            CopySlots(_plan.SlotIds));
    }

    public FillOnceThenHoldState CaptureState() => new(
        CopyPlan(_plan),
        _planRevision,
        _sweepRevision,
        _lastPresentationNs,
        _sessionRunState,
        _continuityState,
        _livePlayheadDataSimTimeNs,
        _temporalViewMode);

    private long RecordEndExclusiveDataSimTimeNs =>
        AddDuration(
            _plan.RecordStartDataSimTimeNs,
            _plan.RecordDurationNs,
            "FillOnce.InvalidPlan",
            nameof(_plan));

    private void AdvanceCore(
        long presentationNs,
        long livePlayheadDataSimTimeNs)
    {
        if (presentationNs < _lastPresentationNs)
        {
            throw Error("FillOnce.TimeReversed", nameof(presentationNs));
        }

        if (livePlayheadDataSimTimeNs < _livePlayheadDataSimTimeNs)
        {
            throw Error(
                "FillOnce.PlayheadReversed",
                nameof(livePlayheadDataSimTimeNs));
        }

        if (_sessionRunState != SessionRunState.Running &&
            livePlayheadDataSimTimeNs != _livePlayheadDataSimTimeNs)
        {
            throw Error(
                "FillOnce.PlayheadAdvancedWhileStopped",
                nameof(livePlayheadDataSimTimeNs));
        }

        if (_continuityState.DataAvailability == DataAvailability.NoData &&
            livePlayheadDataSimTimeNs != _livePlayheadDataSimTimeNs)
        {
            throw Error(
                "FillOnce.PlayheadAdvancedWithoutData",
                nameof(livePlayheadDataSimTimeNs));
        }

        _lastPresentationNs = presentationNs;
        _livePlayheadDataSimTimeNs = livePlayheadDataSimTimeNs;
        if (_temporalViewMode == TemporalViewMode.AcquisitionFill &&
            livePlayheadDataSimTimeNs >= RecordEndExclusiveDataSimTimeNs)
        {
            _temporalViewMode = TemporalViewMode.CapturedRecord;
            IncrementSweepRevision();
        }
    }

    private SweepStateProjectionSnapshot Mutate(
        Action<FillOnceThenHoldStateMachine> mutation)
    {
        FillOnceThenHoldStateMachine trial = new(CaptureState());
        mutation(trial);
        CopyFrom(trial);
        return CaptureProjection();
    }

    private void CopyFrom(FillOnceThenHoldStateMachine source)
    {
        _sweepRevision = source._sweepRevision;
        _lastPresentationNs = source._lastPresentationNs;
        _sessionRunState = source._sessionRunState;
        _continuityState = source._continuityState;
        _livePlayheadDataSimTimeNs = source._livePlayheadDataSimTimeNs;
        _temporalViewMode = source._temporalViewMode;
    }

    private void IncrementSweepRevision()
    {
        if (_sweepRevision == ulong.MaxValue)
        {
            throw Error("FillOnce.RevisionExhausted", nameof(_sweepRevision));
        }

        _sweepRevision++;
    }

    private static void ValidateState(
        FillOnceThenHoldState state,
        FillOnceThenHoldPlan plan)
    {
        ArgumentNullException.ThrowIfNull(state.ContinuityState);
        ValidateContinuity(state.ContinuityState, nameof(state));
        long endExclusive = AddDuration(
            plan.RecordStartDataSimTimeNs,
            plan.RecordDurationNs,
            "FillOnce.InvalidCheckpoint",
            nameof(state));
        bool validPhase = state.TemporalViewMode switch
        {
            TemporalViewMode.AcquisitionFill =>
                state.LivePlayheadDataSimTimeNs < endExclusive,
            TemporalViewMode.CapturedRecord =>
                state.LivePlayheadDataSimTimeNs >= endExclusive,
            _ => false,
        };
        if (!Enum.IsDefined(state.SessionRunState) ||
            state.LivePlayheadDataSimTimeNs <
                plan.RecordStartDataSimTimeNs ||
            !validPhase)
        {
            throw Error("FillOnce.InvalidCheckpoint", nameof(state));
        }
    }

    private static FillOnceThenHoldPlan ValidateAndCopyPlan(
        FillOnceThenHoldPlan plan,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.SlotIds is null)
        {
            throw Error("FillOnce.InvalidPlan", parameterName);
        }

        FillOnceThenHoldPlan copiedPlan = CopyPlan(plan);
        if (!IsStableId(copiedPlan.GroupId) ||
            !IsStableId(copiedPlan.RecordRef) ||
            copiedPlan.RecordStartDataSimTimeNs < 0 ||
            copiedPlan.RecordDurationNs == 0 ||
            copiedPlan.RequiredRecordHistoryNs <
                copiedPlan.RecordDurationNs ||
            copiedPlan.SlotIds.Count != StandardEcgSlotCount ||
            copiedPlan.SlotIds.Any(slot => !IsStableId(slot)) ||
            copiedPlan.SlotIds.Distinct(StringComparer.Ordinal).Count() !=
                StandardEcgSlotCount)
        {
            throw Error("FillOnce.InvalidPlan", parameterName);
        }

        _ = AddDuration(
            copiedPlan.RecordStartDataSimTimeNs,
            copiedPlan.RecordDurationNs,
            "FillOnce.InvalidPlan",
            parameterName);
        return copiedPlan;
    }

    private static FillOnceThenHoldPlan CopyPlan(FillOnceThenHoldPlan plan) =>
        plan with { SlotIds = CopySlots(plan.SlotIds) };

    private static System.Collections.ObjectModel.ReadOnlyCollection<string>
        CopySlots(IReadOnlyList<string> slotIds) =>
        Array.AsReadOnly(slotIds.ToArray());

    private static void ValidateContinuity(
        DataContinuityState state,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            _ = DataContinuityStateMachine.Restore(state);
        }
        catch (DataContinuityException)
        {
            throw Error("FillOnce.InvalidContinuity", parameterName);
        }
    }

    private static long AddDuration(
        long start,
        ulong duration,
        string reasonCode,
        string parameterName)
    {
        Int128 end = (Int128)start + duration;
        if (end > long.MaxValue)
        {
            throw Error(reasonCode, parameterName);
        }

        return (long)end;
    }

    private static bool IsStableId(string? value)
    {
        if (string.IsNullOrEmpty(value) ||
            value.Length > 128 ||
            !IsAsciiLetter(value[0]))
        {
            return false;
        }

        return value.All(static character =>
            IsAsciiLetter(character) ||
            char.IsAsciiDigit(character) ||
            character is '.' or '_' or ':' or '@' or '/' or '-');
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static FillOnceThenHoldException Error(
        string reasonCode,
        string parameterName) => new(reasonCode, parameterName);
}
