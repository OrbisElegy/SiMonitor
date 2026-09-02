// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Continuity;

public sealed class NoDataPresentationException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public enum LiveTracePresentation
{
    FollowSource,
    NoDataSweep,
}

public enum TraceSignalMeaning
{
    FollowSource,
    SignalUnavailable,
}

public enum LiveTraceClock
{
    SourcePlayhead,
    PresentationClock,
}

public enum NumericNoDataQuality
{
    FollowSource,
    Stale,
    Disconnected,
}

public enum NumericValuePresentation
{
    FollowSource,
    PreserveLastValue,
    UnavailableMarker,
}

public enum PatientAlarmSuspension
{
    FollowSource,
    SuspendedUnknown,
}

public enum PhysiologyEventPresentation
{
    FollowSource,
    Suppress,
}

public enum PhysiologyAudioPresentation
{
    FollowAlarmPolicy,
    Silent,
}

public sealed record NumericNoDataPolicy(
    string ParameterId,
    Guid SourceInstanceId,
    ulong StaleRetentionNs);

public sealed record NumericNoDataProjection(
    string ParameterId,
    Guid SourceInstanceId,
    NumericNoDataQuality Quality,
    NumericValuePresentation ValuePresentation);

public sealed record NoDataSafetyProjection(
    DataAvailability DataAvailability,
    LiveTracePresentation LiveTrace,
    TraceSignalMeaning TraceMeaning,
    LiveTraceClock LiveTraceClock,
    IReadOnlyList<NumericNoDataProjection> Numerics,
    PatientAlarmSuspension PatientAlarms,
    PhysiologyEventPresentation PhysiologyEvents,
    PhysiologyAudioPresentation PhysiologyAudio,
    bool ClearLiveTraceImmediately,
    bool PreservePinnedHistory,
    bool PreserveCalibrationGutter,
    bool ShowConnectivityExplanation);

public sealed record NoDataPresentationState(
    DataContinuityState ContinuityState,
    long PresentationAuthorityMonotonicNs);

public sealed class NoDataPresentationStateMachine
{
    public const int MaximumNumericChannels = 128;

    private readonly NumericNoDataPolicy[] _numericPolicies;
    private DataContinuityState _continuityState;
    private long _presentationAuthorityMonotonicNs;

    private NoDataPresentationStateMachine(
        IReadOnlyList<NumericNoDataPolicy> numericPolicies,
        NoDataPresentationState state)
    {
        _numericPolicies = ValidateAndCopyPolicies(numericPolicies);
        ValidateState(state);
        _continuityState = state.ContinuityState;
        _presentationAuthorityMonotonicNs =
            state.PresentationAuthorityMonotonicNs;
    }

    public long PresentationAuthorityMonotonicNs =>
        _presentationAuthorityMonotonicNs;

    public static NoDataPresentationStateMachine Start(
        IReadOnlyList<NumericNoDataPolicy> numericPolicies,
        DataContinuityState continuityState)
    {
        ArgumentNullException.ThrowIfNull(continuityState);
        return new NoDataPresentationStateMachine(
            numericPolicies,
            new NoDataPresentationState(
                continuityState,
                continuityState.LastAuthorityMonotonicNs));
    }

    public static NoDataPresentationStateMachine Restore(
        IReadOnlyList<NumericNoDataPolicy> numericPolicies,
        NoDataPresentationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new NoDataPresentationStateMachine(numericPolicies, state);
    }

    public NoDataSafetyProjection Synchronize(
        DataContinuityState continuityState)
    {
        ArgumentNullException.ThrowIfNull(continuityState);
        ValidateContinuityState(continuityState, nameof(continuityState));
        if (continuityState.LastAuthorityMonotonicNs <
            _presentationAuthorityMonotonicNs)
        {
            throw Error(
                "NoDataPresentation.TimeReversed",
                nameof(continuityState));
        }

        _continuityState = continuityState;
        _presentationAuthorityMonotonicNs =
            continuityState.LastAuthorityMonotonicNs;
        return CaptureProjection();
    }

    public NoDataSafetyProjection Advance(long authorityMonotonicNs)
    {
        if (authorityMonotonicNs < _presentationAuthorityMonotonicNs)
        {
            throw Error(
                "NoDataPresentation.TimeReversed",
                nameof(authorityMonotonicNs));
        }

        _presentationAuthorityMonotonicNs = authorityMonotonicNs;
        return CaptureProjection();
    }

    public NoDataSafetyProjection CaptureProjection()
    {
        bool noData = _continuityState.DataAvailability == DataAvailability.NoData;
        NumericNoDataProjection[] numerics = _numericPolicies
            .Select(policy => ProjectNumeric(policy, noData))
            .ToArray();

        return new NoDataSafetyProjection(
            _continuityState.DataAvailability,
            noData
                ? LiveTracePresentation.NoDataSweep
                : LiveTracePresentation.FollowSource,
            noData
                ? TraceSignalMeaning.SignalUnavailable
                : TraceSignalMeaning.FollowSource,
            noData
                ? LiveTraceClock.PresentationClock
                : LiveTraceClock.SourcePlayhead,
            Array.AsReadOnly(numerics),
            noData
                ? PatientAlarmSuspension.SuspendedUnknown
                : PatientAlarmSuspension.FollowSource,
            noData
                ? PhysiologyEventPresentation.Suppress
                : PhysiologyEventPresentation.FollowSource,
            noData
                ? PhysiologyAudioPresentation.Silent
                : PhysiologyAudioPresentation.FollowAlarmPolicy,
            ClearLiveTraceImmediately: false,
            PreservePinnedHistory: true,
            PreserveCalibrationGutter: true,
            ShowConnectivityExplanation: noData);
    }

    public NoDataPresentationState CaptureState() => new(
        _continuityState,
        _presentationAuthorityMonotonicNs);

    private NumericNoDataProjection ProjectNumeric(
        NumericNoDataPolicy policy,
        bool noData)
    {
        if (!noData)
        {
            return new NumericNoDataProjection(
                policy.ParameterId,
                policy.SourceInstanceId,
                NumericNoDataQuality.FollowSource,
                NumericValuePresentation.FollowSource);
        }

        long noDataSince =
            _continuityState.NoDataSinceAuthorityMonotonicNs!.Value;
        ulong elapsed = checked((ulong)(
            _presentationAuthorityMonotonicNs - noDataSince));
        bool stale = elapsed < policy.StaleRetentionNs;
        return new NumericNoDataProjection(
            policy.ParameterId,
            policy.SourceInstanceId,
            stale ? NumericNoDataQuality.Stale : NumericNoDataQuality.Disconnected,
            stale
                ? NumericValuePresentation.PreserveLastValue
                : NumericValuePresentation.UnavailableMarker);
    }

    private static NumericNoDataPolicy[] ValidateAndCopyPolicies(
        IReadOnlyList<NumericNoDataPolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        if (policies.Count > MaximumNumericChannels)
        {
            throw Error(
                "NoDataPresentation.InvalidConfiguration",
                nameof(policies));
        }

        NumericNoDataPolicy[] canonical = [.. policies];
        if (canonical.Any(static policy => policy is null ||
                !IsStableId(policy.ParameterId) ||
                policy.SourceInstanceId == Guid.Empty ||
                policy.StaleRetentionNs > long.MaxValue))
        {
            throw Error(
                "NoDataPresentation.InvalidConfiguration",
                nameof(policies));
        }

        Array.Sort(canonical, ComparePolicies);
        for (int index = 1; index < canonical.Length; index++)
        {
            if (SameChannel(canonical[index - 1], canonical[index]))
            {
                throw Error(
                    "NoDataPresentation.InvalidConfiguration",
                    nameof(policies));
            }
        }

        return canonical;
    }

    private static void ValidateState(NoDataPresentationState state)
    {
        ArgumentNullException.ThrowIfNull(state.ContinuityState);
        ValidateContinuityState(state.ContinuityState, nameof(state));
        if (state.PresentationAuthorityMonotonicNs <
            state.ContinuityState.LastAuthorityMonotonicNs)
        {
            throw Error("NoDataPresentation.InvalidCheckpoint", nameof(state));
        }
    }

    private static void ValidateContinuityState(
        DataContinuityState state,
        string parameterName)
    {
        try
        {
            _ = DataContinuityStateMachine.Restore(state);
        }
        catch (DataContinuityException)
        {
            throw Error("NoDataPresentation.InvalidCheckpoint", parameterName);
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

    private static int ComparePolicies(
        NumericNoDataPolicy left,
        NumericNoDataPolicy right)
    {
        int parameter = StringComparer.Ordinal.Compare(
            left.ParameterId,
            right.ParameterId);
        return parameter != 0
            ? parameter
            : CompareGuid(left.SourceInstanceId, right.SourceInstanceId);
    }

    private static int CompareGuid(Guid left, Guid right) =>
        StringComparer.Ordinal.Compare(left.ToString("D"), right.ToString("D"));

    private static bool SameChannel(
        NumericNoDataPolicy left,
        NumericNoDataPolicy right) =>
        StringComparer.Ordinal.Equals(left.ParameterId, right.ParameterId) &&
        left.SourceInstanceId == right.SourceInstanceId;

    private static NoDataPresentationException Error(
        string reasonCode,
        string parameterName) => new(reasonCode, parameterName);
}
