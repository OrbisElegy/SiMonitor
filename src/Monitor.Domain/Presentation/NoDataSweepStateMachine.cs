// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;

namespace Monitor.Domain.Presentation;

public sealed class NoDataSweepException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record NoDataSweepPlan(
    string GroupId,
    ulong SweepEpoch,
    ulong PresentationClockRevision,
    long CycleOriginPresentationNs,
    ulong VisibleDurationNs,
    ulong EraseGapNs,
    ulong RequiredRenderHistoryNs);

public sealed record SweepCoverageInterval(
    ulong StartOffsetNs,
    ulong EndOffsetNs);

public sealed record NoDataSweepCoverage(
    string GroupId,
    ulong SweepEpoch,
    ulong PresentationClockRevision,
    ulong CycleIndex,
    ulong WriteHeadOffsetNs,
    uint WriteHeadPhasePpm,
    ulong CoveredDurationNs,
    ulong RemainingPatientTraceDurationNs,
    bool FullyCovered,
    IReadOnlyList<SweepCoverageInterval> CoveredIntervals);

public sealed record NoDataSweepState(
    NoDataSweepPlan Plan,
    long NoDataSinceAuthorityMonotonicNs,
    long StartedAtPresentationNs,
    long CurrentPresentationNs);

public sealed class NoDataSweepStateMachine
{
    public const uint PhasePartsPerMillion = 1_000_000;

    private readonly NoDataSweepPlan _plan;
    private readonly long _noDataSinceAuthorityMonotonicNs;
    private readonly long _startedAtPresentationNs;
    private long _currentPresentationNs;

    private NoDataSweepStateMachine(NoDataSweepState state)
    {
        ValidateState(state);
        _plan = state.Plan;
        _noDataSinceAuthorityMonotonicNs =
            state.NoDataSinceAuthorityMonotonicNs;
        _startedAtPresentationNs = state.StartedAtPresentationNs;
        _currentPresentationNs = state.CurrentPresentationNs;
    }

    public long CurrentPresentationNs => _currentPresentationNs;

    public static NoDataSweepStateMachine Start(
        NoDataSweepPlan plan,
        DataContinuityState continuityState,
        long startedAtPresentationNs)
    {
        ValidateNoDataContinuity(continuityState, nameof(continuityState));
        ArgumentNullException.ThrowIfNull(plan);
        return new NoDataSweepStateMachine(new NoDataSweepState(
            plan,
            continuityState.NoDataSinceAuthorityMonotonicNs!.Value,
            startedAtPresentationNs,
            startedAtPresentationNs));
    }

    public static NoDataSweepStateMachine Restore(
        DataContinuityState continuityState,
        NoDataSweepState state)
    {
        ValidateNoDataContinuity(continuityState, nameof(continuityState));
        ArgumentNullException.ThrowIfNull(state);
        NoDataSweepStateMachine restored = new(state);
        if (restored._noDataSinceAuthorityMonotonicNs !=
            continuityState.NoDataSinceAuthorityMonotonicNs)
        {
            throw Error("NoDataSweep.EpisodeMismatch", nameof(continuityState));
        }

        return restored;
    }

    public NoDataSweepCoverage Advance(long presentationNs)
    {
        if (presentationNs < _currentPresentationNs)
        {
            throw Error("NoDataSweep.TimeReversed", nameof(presentationNs));
        }

        _currentPresentationNs = presentationNs;
        return CaptureCoverage();
    }

    public NoDataSweepCoverage CaptureCoverage()
    {
        ulong coveredDuration = Elapsed(
            _startedAtPresentationNs,
            _currentPresentationNs);
        if (coveredDuration > _plan.VisibleDurationNs)
        {
            coveredDuration = _plan.VisibleDurationNs;
        }

        (ulong cycleIndex, ulong writeHeadOffset) = Locate(
            _plan,
            _currentPresentationNs);
        uint phasePpm = checked((uint)(
            (UInt128)writeHeadOffset * PhasePartsPerMillion /
            _plan.VisibleDurationNs));
        SweepCoverageInterval[] intervals = BuildCoveredIntervals(
            PhaseOffset(_plan, _startedAtPresentationNs),
            coveredDuration,
            _plan.VisibleDurationNs);

        return new NoDataSweepCoverage(
            _plan.GroupId,
            _plan.SweepEpoch,
            _plan.PresentationClockRevision,
            cycleIndex,
            writeHeadOffset,
            phasePpm,
            coveredDuration,
            _plan.VisibleDurationNs - coveredDuration,
            coveredDuration == _plan.VisibleDurationNs,
            Array.AsReadOnly(intervals));
    }

    public NoDataSweepState CaptureState() => new(
        _plan,
        _noDataSinceAuthorityMonotonicNs,
        _startedAtPresentationNs,
        _currentPresentationNs);

    private static SweepCoverageInterval[] BuildCoveredIntervals(
        ulong startOffset,
        ulong coveredDuration,
        ulong visibleDuration)
    {
        if (coveredDuration == 0)
        {
            return [];
        }

        if (coveredDuration == visibleDuration)
        {
            return [new SweepCoverageInterval(0, visibleDuration)];
        }

        UInt128 unwrappedEnd = (UInt128)startOffset + coveredDuration;
        if (unwrappedEnd <= visibleDuration)
        {
            return
            [
                new SweepCoverageInterval(startOffset, (ulong)unwrappedEnd),
            ];
        }

        ulong wrappedEnd = (ulong)(unwrappedEnd - visibleDuration);
        return
        [
            new SweepCoverageInterval(0, wrappedEnd),
            new SweepCoverageInterval(startOffset, visibleDuration),
        ];
    }

    private static (ulong CycleIndex, ulong OffsetNs) Locate(
        NoDataSweepPlan plan,
        long presentationNs)
    {
        UInt128 elapsed = ElapsedWide(
            plan.CycleOriginPresentationNs,
            presentationNs);
        return (
            checked((ulong)(elapsed / plan.VisibleDurationNs)),
            checked((ulong)(elapsed % plan.VisibleDurationNs)));
    }

    private static ulong PhaseOffset(
        NoDataSweepPlan plan,
        long presentationNs) => Locate(plan, presentationNs).OffsetNs;

    private static ulong Elapsed(long start, long current) =>
        checked((ulong)ElapsedWide(start, current));

    private static UInt128 ElapsedWide(long start, long current)
    {
        Int128 elapsed = (Int128)current - start;
        if (elapsed < 0)
        {
            throw Error("NoDataSweep.InvalidCheckpoint", nameof(current));
        }

        return checked((UInt128)elapsed);
    }

    private static void ValidateState(NoDataSweepState state)
    {
        ArgumentNullException.ThrowIfNull(state.Plan);
        ValidatePlan(state.Plan, nameof(state));
        if (state.NoDataSinceAuthorityMonotonicNs < 0 ||
            state.StartedAtPresentationNs <
                state.Plan.CycleOriginPresentationNs ||
            state.CurrentPresentationNs < state.StartedAtPresentationNs)
        {
            throw Error("NoDataSweep.InvalidCheckpoint", nameof(state));
        }
    }

    private static void ValidatePlan(NoDataSweepPlan plan, string parameterName)
    {
        bool renderHistoryOverflow =
            plan.EraseGapNs > ulong.MaxValue - plan.VisibleDurationNs;
        if (!IsStableId(plan.GroupId) ||
            plan.VisibleDurationNs == 0 ||
            renderHistoryOverflow ||
            plan.RequiredRenderHistoryNs <
                plan.VisibleDurationNs + plan.EraseGapNs)
        {
            throw Error("NoDataSweep.InvalidPlan", parameterName);
        }
    }

    private static void ValidateNoDataContinuity(
        DataContinuityState continuityState,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(continuityState);
        try
        {
            _ = DataContinuityStateMachine.Restore(continuityState);
        }
        catch (DataContinuityException)
        {
            throw Error("NoDataSweep.InvalidContinuity", parameterName);
        }

        if (continuityState.DataAvailability != DataAvailability.NoData)
        {
            throw Error("NoDataSweep.NotNoData", parameterName);
        }
    }

    private static bool IsStableId(string value)
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

    private static NoDataSweepException Error(
        string reasonCode,
        string parameterName) => new(reasonCode, parameterName);
}
