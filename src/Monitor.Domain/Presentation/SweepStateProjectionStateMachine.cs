// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;

namespace Monitor.Domain.Presentation;

public sealed class SweepStateProjectionException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public enum SessionRunState
{
    Running,
    Paused,
    Stopped,
}

public enum TemporalViewMode
{
    LiveSweep,
    AcquisitionFill,
    CapturedRecord,
    FrozenSnapshot,
    HistoricalReview,
}

public enum TraceHistoryPresentation
{
    LiveHistory,
    RecordFill,
    PinnedOriginalRange,
}

public enum TransientReplayPolicy
{
    FollowLiveSource,
    Suppress,
}

public sealed record SweepStateProjectionSnapshot(
    ulong PlanRevision,
    ulong SweepRevision,
    ulong PresentationClockRevision,
    SessionRunState SessionRunState,
    TemporalViewMode TemporalViewMode,
    DataAvailability DataAvailability,
    AuthorityState AuthorityState,
    long PlayheadDataSimTimeNs,
    ulong CycleIndex,
    uint WriteHeadPhasePpm,
    long? FreezeAnchorSimTimeNs,
    string? ReviewSegmentRef,
    long? NoDataSinceAuthorityMonotonicNs,
    TraceHistoryPresentation TraceHistory,
    TransientReplayPolicy TransientReplayPolicy,
    NoDataSweepCoverage? NoDataCoverage);

public sealed record SweepStateProjectionState(
    NoDataSweepPlan Plan,
    ulong PlanRevision,
    ulong SweepRevision,
    long LastPresentationNs,
    long LiveSweepClockNs,
    SessionRunState SessionRunState,
    TemporalViewMode TemporalViewMode,
    DataContinuityState ContinuityState,
    long LivePlayheadDataSimTimeNs,
    long ViewPlayheadDataSimTimeNs,
    long? FreezeAnchorSimTimeNs,
    string? ReviewSegmentRef,
    long? PinnedSweepClockNs,
    NoDataSweepState? NoDataSweepState);

public sealed class SweepStateProjectionStateMachine
{
    private NoDataSweepPlan _plan;
    private ulong _planRevision;
    private ulong _sweepRevision;
    private long _lastPresentationNs;
    private long _liveSweepClockNs;
    private SessionRunState _sessionRunState;
    private TemporalViewMode _temporalViewMode;
    private DataContinuityState _continuityState;
    private long _livePlayheadDataSimTimeNs;
    private long _viewPlayheadDataSimTimeNs;
    private long? _freezeAnchorSimTimeNs;
    private string? _reviewSegmentRef;
    private long? _pinnedSweepClockNs;
    private NoDataSweepStateMachine? _noDataSweep;

    private SweepStateProjectionStateMachine(SweepStateProjectionState state)
    {
        ValidateState(state);
        _plan = state.Plan;
        _planRevision = state.PlanRevision;
        _sweepRevision = state.SweepRevision;
        _lastPresentationNs = state.LastPresentationNs;
        _liveSweepClockNs = state.LiveSweepClockNs;
        _sessionRunState = state.SessionRunState;
        _temporalViewMode = state.TemporalViewMode;
        _continuityState = state.ContinuityState;
        _livePlayheadDataSimTimeNs = state.LivePlayheadDataSimTimeNs;
        _viewPlayheadDataSimTimeNs = state.ViewPlayheadDataSimTimeNs;
        _freezeAnchorSimTimeNs = state.FreezeAnchorSimTimeNs;
        _reviewSegmentRef = state.ReviewSegmentRef;
        _pinnedSweepClockNs = state.PinnedSweepClockNs;
        try
        {
            _noDataSweep = state.NoDataSweepState is null
                ? null
                : NoDataSweepStateMachine.Restore(
                    state.ContinuityState,
                    state.NoDataSweepState);
        }
        catch (NoDataSweepException)
        {
            throw Error("SweepState.InvalidCheckpoint", nameof(state));
        }
        if (_noDataSweep is not null &&
            _noDataSweep.CurrentSweepClockNs != _liveSweepClockNs)
        {
            throw Error("SweepState.InvalidCheckpoint", nameof(state));
        }
    }

    public static SweepStateProjectionStateMachine Start(
        NoDataSweepPlan plan,
        ulong planRevision,
        ulong sweepRevision,
        SessionRunState sessionRunState,
        DataContinuityState continuityState,
        long presentationNs,
        long playheadDataSimTimeNs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidatePlan(plan, nameof(plan));
        ValidateContinuity(continuityState, nameof(continuityState));
        NoDataSweepState? noDataSweep = null;
        if (continuityState.DataAvailability == DataAvailability.NoData)
        {
            noDataSweep = NoDataSweepStateMachine.Start(
                plan,
                continuityState,
                presentationNs).CaptureState();
        }

        return new SweepStateProjectionStateMachine(
            new SweepStateProjectionState(
                plan,
                planRevision,
                sweepRevision,
                presentationNs,
                presentationNs,
                sessionRunState,
                TemporalViewMode.LiveSweep,
                continuityState,
                playheadDataSimTimeNs,
                playheadDataSimTimeNs,
                null,
                null,
                null,
                noDataSweep));
    }

    public static SweepStateProjectionStateMachine Restore(
        SweepStateProjectionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new SweepStateProjectionStateMachine(state);
    }

    public SweepStateProjectionSnapshot Advance(
        long presentationNs,
        long livePlayheadDataSimTimeNs) => Mutate(trial =>
    {
        trial.AdvanceCore(presentationNs, livePlayheadDataSimTimeNs);
    });

    public SweepStateProjectionSnapshot SynchronizeContinuity(
        DataContinuityState continuityState,
        long presentationNs,
        long livePlayheadDataSimTimeNs) => Mutate(trial =>
    {
        trial.SynchronizeContinuityCore(
            continuityState,
            presentationNs,
            livePlayheadDataSimTimeNs);
        if (trial._continuityState != _continuityState)
        {
            trial.IncrementSweepRevision();
        }
    });

    public SweepStateProjectionSnapshot ChangeRunState(
        SessionRunState sessionRunState,
        long presentationNs,
        long livePlayheadDataSimTimeNs) => Mutate(trial =>
    {
        if (!Enum.IsDefined(sessionRunState))
        {
            throw Error(
                "SweepState.InvalidRunState",
                nameof(sessionRunState));
        }

        trial.AdvanceCore(presentationNs, livePlayheadDataSimTimeNs);
        if (trial._sessionRunState != sessionRunState)
        {
            trial._sessionRunState = sessionRunState;
            trial.IncrementSweepRevision();
        }
    });

    public SweepStateProjectionSnapshot EnterFrozen(
        long presentationNs,
        long livePlayheadDataSimTimeNs) => Mutate(trial =>
    {
        if (trial._temporalViewMode != TemporalViewMode.LiveSweep)
        {
            throw Error("SweepState.InvalidViewTransition", nameof(presentationNs));
        }

        trial.AdvanceCore(presentationNs, livePlayheadDataSimTimeNs);
        trial._temporalViewMode = TemporalViewMode.FrozenSnapshot;
        trial._freezeAnchorSimTimeNs = trial._livePlayheadDataSimTimeNs;
        trial._viewPlayheadDataSimTimeNs = trial._livePlayheadDataSimTimeNs;
        trial._pinnedSweepClockNs = trial.CurrentLiveSweepClockNs;
        trial.IncrementSweepRevision();
    });

    public SweepStateProjectionSnapshot ExitFrozen(
        long presentationNs,
        long livePlayheadDataSimTimeNs) => Mutate(trial =>
    {
        if (trial._temporalViewMode != TemporalViewMode.FrozenSnapshot)
        {
            throw Error("SweepState.InvalidViewTransition", nameof(presentationNs));
        }

        trial.AdvanceCore(presentationNs, livePlayheadDataSimTimeNs);
        trial.ReturnToLive();
        trial.IncrementSweepRevision();
    });

    public SweepStateProjectionSnapshot EnterReview(
        string reviewSegmentRef,
        long reviewPlayheadDataSimTimeNs,
        long presentationNs,
        long livePlayheadDataSimTimeNs) => Mutate(trial =>
    {
        if (trial._temporalViewMode != TemporalViewMode.LiveSweep ||
            !IsStableId(reviewSegmentRef) ||
            reviewPlayheadDataSimTimeNs < 0)
        {
            throw Error(
                "SweepState.InvalidViewTransition",
                nameof(reviewSegmentRef));
        }

        trial.AdvanceCore(presentationNs, livePlayheadDataSimTimeNs);
        trial._temporalViewMode = TemporalViewMode.HistoricalReview;
        trial._reviewSegmentRef = reviewSegmentRef;
        trial._viewPlayheadDataSimTimeNs = reviewPlayheadDataSimTimeNs;
        trial._pinnedSweepClockNs = trial.CurrentLiveSweepClockNs;
        trial.IncrementSweepRevision();
    });

    public SweepStateProjectionSnapshot SeekReview(
        long reviewPlayheadDataSimTimeNs) => Mutate(trial =>
    {
        if (trial._temporalViewMode != TemporalViewMode.HistoricalReview ||
            reviewPlayheadDataSimTimeNs < 0)
        {
            throw Error(
                "SweepState.InvalidViewTransition",
                nameof(reviewPlayheadDataSimTimeNs));
        }

        if (trial._viewPlayheadDataSimTimeNs != reviewPlayheadDataSimTimeNs)
        {
            trial._viewPlayheadDataSimTimeNs = reviewPlayheadDataSimTimeNs;
            trial.IncrementSweepRevision();
        }
    });

    public SweepStateProjectionSnapshot ExitReview(
        long presentationNs,
        long livePlayheadDataSimTimeNs) => Mutate(trial =>
    {
        if (trial._temporalViewMode != TemporalViewMode.HistoricalReview)
        {
            throw Error("SweepState.InvalidViewTransition", nameof(presentationNs));
        }

        trial.AdvanceCore(presentationNs, livePlayheadDataSimTimeNs);
        trial.ReturnToLive();
        trial.IncrementSweepRevision();
    });

    public SweepStateProjectionSnapshot CaptureProjection()
    {
        long projectedSweepClock = _temporalViewMode == TemporalViewMode.LiveSweep
            ? CurrentLiveSweepClockNs
            : _pinnedSweepClockNs!.Value;
        (ulong cycleIndex, uint phasePpm) = Locate(projectedSweepClock);
        bool historical = _temporalViewMode is
            TemporalViewMode.FrozenSnapshot or
            TemporalViewMode.HistoricalReview;
        return new SweepStateProjectionSnapshot(
            _planRevision,
            _sweepRevision,
            _plan.PresentationClockRevision,
            _sessionRunState,
            _temporalViewMode,
            _continuityState.DataAvailability,
            _continuityState.AuthorityState,
            _temporalViewMode == TemporalViewMode.LiveSweep
                ? _livePlayheadDataSimTimeNs
                : _viewPlayheadDataSimTimeNs,
            cycleIndex,
            phasePpm,
            _freezeAnchorSimTimeNs,
            _reviewSegmentRef,
            _continuityState.NoDataSinceAuthorityMonotonicNs,
            historical
                ? TraceHistoryPresentation.PinnedOriginalRange
                : TraceHistoryPresentation.LiveHistory,
            _temporalViewMode == TemporalViewMode.HistoricalReview
                ? TransientReplayPolicy.Suppress
                : TransientReplayPolicy.FollowLiveSource,
            _temporalViewMode == TemporalViewMode.LiveSweep
                ? _noDataSweep?.CaptureCoverage()
                : null);
    }

    public SweepStateProjectionState CaptureState() => new(
        _plan,
        _planRevision,
        _sweepRevision,
        _lastPresentationNs,
        _liveSweepClockNs,
        _sessionRunState,
        _temporalViewMode,
        _continuityState,
        _livePlayheadDataSimTimeNs,
        _viewPlayheadDataSimTimeNs,
        _freezeAnchorSimTimeNs,
        _reviewSegmentRef,
        _pinnedSweepClockNs,
        _noDataSweep?.CaptureState());

    private long CurrentLiveSweepClockNs =>
        _noDataSweep?.CurrentSweepClockNs ?? _liveSweepClockNs;

    private void AdvanceCore(
        long presentationNs,
        long livePlayheadDataSimTimeNs)
    {
        if (presentationNs < _lastPresentationNs)
        {
            throw Error("SweepState.TimeReversed", nameof(presentationNs));
        }

        if (livePlayheadDataSimTimeNs < _livePlayheadDataSimTimeNs)
        {
            throw Error(
                "SweepState.PlayheadReversed",
                nameof(livePlayheadDataSimTimeNs));
        }

        if (_sessionRunState != SessionRunState.Running &&
            livePlayheadDataSimTimeNs != _livePlayheadDataSimTimeNs)
        {
            throw Error(
                "SweepState.PlayheadAdvancedWhileStopped",
                nameof(livePlayheadDataSimTimeNs));
        }

        if (_continuityState.DataAvailability == DataAvailability.NoData &&
            livePlayheadDataSimTimeNs != _livePlayheadDataSimTimeNs)
        {
            throw Error(
                "SweepState.PlayheadAdvancedWithoutData",
                nameof(livePlayheadDataSimTimeNs));
        }

        ulong elapsedPresentationNs = Elapsed(
            _lastPresentationNs,
            presentationNs);
        if (_sessionRunState == SessionRunState.Running)
        {
            _liveSweepClockNs = AddElapsed(
                _liveSweepClockNs,
                elapsedPresentationNs);
            _livePlayheadDataSimTimeNs = livePlayheadDataSimTimeNs;
        }

        if (_noDataSweep is not null)
        {
            _ = _noDataSweep.Advance(presentationNs);
            _liveSweepClockNs = _noDataSweep.CurrentSweepClockNs;
        }

        if (_temporalViewMode == TemporalViewMode.LiveSweep)
        {
            _viewPlayheadDataSimTimeNs = _livePlayheadDataSimTimeNs;
        }

        _lastPresentationNs = presentationNs;
    }

    private void SynchronizeContinuityCore(
        DataContinuityState continuityState,
        long presentationNs,
        long livePlayheadDataSimTimeNs)
    {
        ValidateContinuity(continuityState, nameof(continuityState));
        if (continuityState.LastAuthorityMonotonicNs <
            _continuityState.LastAuthorityMonotonicNs)
        {
            throw Error(
                "SweepState.AuthorityTimeReversed",
                nameof(continuityState));
        }

        AdvanceCore(presentationNs, livePlayheadDataSimTimeNs);
        bool nextNoData =
            continuityState.DataAvailability == DataAvailability.NoData;
        bool sameNoDataEpisode = nextNoData &&
            _noDataSweep is not null &&
            continuityState.NoDataSinceAuthorityMonotonicNs ==
                _continuityState.NoDataSinceAuthorityMonotonicNs;
        if (nextNoData && !sameNoDataEpisode)
        {
            _noDataSweep = NoDataSweepStateMachine.StartAtSweepClock(
                _plan,
                continuityState,
                presentationNs,
                _liveSweepClockNs);
        }
        else if (!nextNoData)
        {
            _liveSweepClockNs = CurrentLiveSweepClockNs;
            _noDataSweep = null;
        }

        _continuityState = continuityState;
    }

    private SweepStateProjectionSnapshot Mutate(
        Action<SweepStateProjectionStateMachine> mutation)
    {
        SweepStateProjectionStateMachine trial = new(CaptureState());
        mutation(trial);
        CopyFrom(trial);
        return CaptureProjection();
    }

    private void CopyFrom(SweepStateProjectionStateMachine source)
    {
        _plan = source._plan;
        _planRevision = source._planRevision;
        _sweepRevision = source._sweepRevision;
        _lastPresentationNs = source._lastPresentationNs;
        _liveSweepClockNs = source._liveSweepClockNs;
        _sessionRunState = source._sessionRunState;
        _temporalViewMode = source._temporalViewMode;
        _continuityState = source._continuityState;
        _livePlayheadDataSimTimeNs = source._livePlayheadDataSimTimeNs;
        _viewPlayheadDataSimTimeNs = source._viewPlayheadDataSimTimeNs;
        _freezeAnchorSimTimeNs = source._freezeAnchorSimTimeNs;
        _reviewSegmentRef = source._reviewSegmentRef;
        _pinnedSweepClockNs = source._pinnedSweepClockNs;
        _noDataSweep = source._noDataSweep;
    }

    private void ReturnToLive()
    {
        _temporalViewMode = TemporalViewMode.LiveSweep;
        _viewPlayheadDataSimTimeNs = _livePlayheadDataSimTimeNs;
        _freezeAnchorSimTimeNs = null;
        _reviewSegmentRef = null;
        _pinnedSweepClockNs = null;
    }

    private void IncrementSweepRevision()
    {
        if (_sweepRevision == ulong.MaxValue)
        {
            throw Error("SweepState.RevisionExhausted", nameof(_sweepRevision));
        }

        _sweepRevision++;
    }

    private (ulong CycleIndex, uint PhasePpm) Locate(long sweepClockNs)
    {
        UInt128 elapsed = ElapsedWide(
            _plan.CycleOriginPresentationNs,
            sweepClockNs);
        ulong cycle = checked((ulong)(elapsed / _plan.VisibleDurationNs));
        ulong offset = checked((ulong)(elapsed % _plan.VisibleDurationNs));
        uint phase = checked((uint)(
            (UInt128)offset * NoDataSweepStateMachine.PhasePartsPerMillion /
            _plan.VisibleDurationNs));
        return (cycle, phase);
    }

    private static void ValidateState(SweepStateProjectionState state)
    {
        ArgumentNullException.ThrowIfNull(state.Plan);
        ArgumentNullException.ThrowIfNull(state.ContinuityState);
        ValidatePlan(state.Plan, nameof(state));
        ValidateContinuity(state.ContinuityState, nameof(state));
        if (!Enum.IsDefined(state.SessionRunState) ||
            !Enum.IsDefined(state.TemporalViewMode) ||
            state.TemporalViewMode is TemporalViewMode.AcquisitionFill or
                TemporalViewMode.CapturedRecord ||
            state.LastPresentationNs < state.Plan.CycleOriginPresentationNs ||
            state.LiveSweepClockNs < state.Plan.CycleOriginPresentationNs ||
            state.LiveSweepClockNs > state.LastPresentationNs ||
            state.LivePlayheadDataSimTimeNs < 0 ||
            state.ViewPlayheadDataSimTimeNs < 0)
        {
            throw Error("SweepState.InvalidCheckpoint", nameof(state));
        }

        bool live = state.TemporalViewMode == TemporalViewMode.LiveSweep;
        bool frozen = state.TemporalViewMode == TemporalViewMode.FrozenSnapshot;
        bool review = state.TemporalViewMode == TemporalViewMode.HistoricalReview;
        bool validView = (live &&
                state.FreezeAnchorSimTimeNs is null &&
                state.ReviewSegmentRef is null &&
                state.PinnedSweepClockNs is null &&
                state.ViewPlayheadDataSimTimeNs ==
                    state.LivePlayheadDataSimTimeNs) ||
            (frozen &&
                state.FreezeAnchorSimTimeNs ==
                    state.ViewPlayheadDataSimTimeNs &&
                state.ReviewSegmentRef is null &&
                ValidPinnedClock(state)) ||
            (review &&
                state.FreezeAnchorSimTimeNs is null &&
                IsStableId(state.ReviewSegmentRef) &&
                ValidPinnedClock(state));
        bool noData =
            state.ContinuityState.DataAvailability == DataAvailability.NoData;
        bool validNoData = noData == (state.NoDataSweepState is not null) &&
            (state.NoDataSweepState is null ||
                (state.NoDataSweepState.Plan == state.Plan &&
                    state.NoDataSweepState.CurrentPresentationNs ==
                        state.LastPresentationNs &&
                    state.NoDataSweepState.SweepClockAtStartNs <=
                        state.LiveSweepClockNs));
        if (!validView || !validNoData)
        {
            throw Error("SweepState.InvalidCheckpoint", nameof(state));
        }
    }

    private static bool ValidPinnedClock(SweepStateProjectionState state) =>
        state.PinnedSweepClockNs >= state.Plan.CycleOriginPresentationNs &&
        state.PinnedSweepClockNs <= state.LastPresentationNs;

    private static void ValidateContinuity(
        DataContinuityState state,
        string parameterName)
    {
        try
        {
            _ = DataContinuityStateMachine.Restore(state);
        }
        catch (DataContinuityException)
        {
            throw Error("SweepState.InvalidContinuity", parameterName);
        }
    }

    private static void ValidatePlan(NoDataSweepPlan plan, string parameterName)
    {
        try
        {
            NoDataSweepStateMachine.ValidatePlanForUse(plan);
        }
        catch (NoDataSweepException)
        {
            throw Error("SweepState.InvalidPlan", parameterName);
        }
    }

    private static long AddElapsed(long start, ulong elapsed)
    {
        Int128 result = (Int128)start + elapsed;
        if (result > long.MaxValue)
        {
            throw Error("SweepState.ClockOutOfRange", nameof(elapsed));
        }

        return (long)result;
    }

    private static ulong Elapsed(long start, long current) =>
        checked((ulong)ElapsedWide(start, current));

    private static UInt128 ElapsedWide(long start, long current)
    {
        Int128 elapsed = (Int128)current - start;
        if (elapsed < 0)
        {
            throw Error("SweepState.TimeReversed", nameof(current));
        }

        return (UInt128)elapsed;
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

    private static SweepStateProjectionException Error(
        string reasonCode,
        string parameterName) => new(reasonCode, parameterName);
}
