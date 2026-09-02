// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Continuity;

public sealed class DataContinuityException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public enum DataAvailability
{
    Authoritative,
    Buffered,
    LocalContinuation,
    NoData,
}

public enum AuthorityState
{
    Authoritative,
    Provisional,
}

public enum LocalContinuationPolicyKind
{
    Disabled,
    Duration,
    UntilScenarioEnd,
}

public enum ShadowVerificationState
{
    NotStarted,
    Pending,
    Verified,
    Rejected,
}

public enum LocalContinuationFailure
{
    CapsuleInvalid,
    ConsistencyMismatch,
    PerformanceInsufficient,
}

public enum ContinuationStopReason
{
    AwaitingShadowVerification,
    PolicyDisabled,
    ShadowMismatch,
    CapsuleInvalid,
    ConsistencyMismatch,
    PerformanceInsufficient,
    DurationExpired,
    ScenarioEnded,
    AwaitingRelockCommit,
}

public enum ConnectivityBannerMessage
{
    None,
    ConnectionSuspect,
    PlayingVerifiedBuffer,
    LocalContinuationProvisional,
    NoData,
    Relocking,
}

public enum ConnectivityTechnicalAudioPolicy
{
    Silent,
}

public sealed record LocalContinuationPolicy(
    LocalContinuationPolicyKind Kind,
    ulong DurationNs)
{
    public const ulong MaximumDurationNs = 1_800_000_000_000;

    public static LocalContinuationPolicy Disabled { get; } = new(
        LocalContinuationPolicyKind.Disabled,
        0);

    public static LocalContinuationPolicy DefaultDuration { get; } = new(
        LocalContinuationPolicyKind.Duration,
        900_000_000_000);

    public static LocalContinuationPolicy UntilScenarioEnd { get; } = new(
        LocalContinuationPolicyKind.UntilScenarioEnd,
        0);
}

public sealed record ConnectivityCriticalBannerProjection(
    bool Visible,
    ConnectivityBannerMessage Message,
    ConnectivityTechnicalAudioPolicy AudioPolicy);

public sealed record DataContinuityState(
    LocalContinuationPolicy Policy,
    long LastAuthorityMonotonicNs,
    ConnectionState ConnectionState,
    DataAvailability DataAvailability,
    AuthorityState AuthorityState,
    ShadowVerificationState ShadowVerification,
    bool BufferedDataRemaining,
    long? DisconnectedAtAuthorityMonotonicNs,
    long? LocalContinuationStartedAtAuthorityMonotonicNs,
    long? NoDataSinceAuthorityMonotonicNs,
    bool ScenarioEnded,
    ContinuationStopReason? ContinuationStopReason);

public sealed class DataContinuityStateMachine
{
    private long _lastAuthorityMonotonicNs;
    private ConnectionState _connectionState;
    private DataAvailability _dataAvailability;
    private AuthorityState _authorityState;
    private ShadowVerificationState _shadowVerification;
    private bool _bufferedDataRemaining;
    private long? _disconnectedAtAuthorityMonotonicNs;
    private long? _localContinuationStartedAtAuthorityMonotonicNs;
    private long? _noDataSinceAuthorityMonotonicNs;
    private bool _scenarioEnded;
    private ContinuationStopReason? _continuationStopReason;

    private DataContinuityStateMachine(DataContinuityState state)
    {
        ValidateState(state);
        Policy = state.Policy;
        _lastAuthorityMonotonicNs = state.LastAuthorityMonotonicNs;
        _connectionState = state.ConnectionState;
        _dataAvailability = state.DataAvailability;
        _authorityState = state.AuthorityState;
        _shadowVerification = state.ShadowVerification;
        _bufferedDataRemaining = state.BufferedDataRemaining;
        _disconnectedAtAuthorityMonotonicNs =
            state.DisconnectedAtAuthorityMonotonicNs;
        _localContinuationStartedAtAuthorityMonotonicNs =
            state.LocalContinuationStartedAtAuthorityMonotonicNs;
        _noDataSinceAuthorityMonotonicNs =
            state.NoDataSinceAuthorityMonotonicNs;
        _scenarioEnded = state.ScenarioEnded;
        _continuationStopReason = state.ContinuationStopReason;
    }

    public LocalContinuationPolicy Policy { get; }

    public long LastAuthorityMonotonicNs => _lastAuthorityMonotonicNs;

    public ConnectionState ConnectionState => _connectionState;

    public DataAvailability DataAvailability => _dataAvailability;

    public AuthorityState AuthorityState => _authorityState;

    public static DataContinuityStateMachine Start(
        LocalContinuationPolicy policy,
        long startAuthorityMonotonicNs)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!IsValidPolicy(policy))
        {
            throw Error("DataContinuity.InvalidPolicy", nameof(policy));
        }

        if (startAuthorityMonotonicNs < 0)
        {
            throw Error(
                "DataContinuity.InvalidConfiguration",
                nameof(startAuthorityMonotonicNs));
        }

        return new DataContinuityStateMachine(new DataContinuityState(
            policy,
            startAuthorityMonotonicNs,
            ConnectionState.Connected,
            DataAvailability.Authoritative,
            AuthorityState.Authoritative,
            ShadowVerificationState.NotStarted,
            false,
            null,
            null,
            null,
            false,
            null));
    }

    public static DataContinuityStateMachine Restore(DataContinuityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Policy);
        return new DataContinuityStateMachine(state);
    }

    public DataContinuityState ObserveHealthyConnection(
        ConnectionState connectionState,
        long authorityMonotonicNs)
    {
        if (connectionState is not (ConnectionState.Connected or ConnectionState.Suspect) ||
            _connectionState is not (ConnectionState.Connected or ConnectionState.Suspect))
        {
            throw Error(
                "DataContinuity.InvalidTransition",
                nameof(connectionState));
        }

        MoveTo(authorityMonotonicNs);
        _connectionState = connectionState;
        return CaptureState();
    }

    public DataContinuityState Disconnect(
        bool hasBufferedData,
        long authorityMonotonicNs)
    {
        EnsureTime(authorityMonotonicNs);
        if (_connectionState == ConnectionState.Disconnected)
        {
            MoveTo(authorityMonotonicNs);
            return CaptureState();
        }

        MoveTo(authorityMonotonicNs);
        _connectionState = ConnectionState.Disconnected;
        _shadowVerification = ShadowVerificationState.Pending;
        _bufferedDataRemaining = hasBufferedData;
        _disconnectedAtAuthorityMonotonicNs = authorityMonotonicNs;
        _localContinuationStartedAtAuthorityMonotonicNs = null;
        _noDataSinceAuthorityMonotonicNs = null;
        if (hasBufferedData)
        {
            _dataAvailability = DataAvailability.Buffered;
            _authorityState = AuthorityState.Authoritative;
            _continuationStopReason = null;
        }
        else
        {
            EnterNoData(
                ContinuationStopReason.AwaitingShadowVerification,
                authorityMonotonicNs);
        }

        return CaptureState();
    }

    public DataContinuityState RecordShadowVerification(
        bool matched,
        long authorityMonotonicNs)
    {
        if (_connectionState != ConnectionState.Disconnected ||
            _shadowVerification != ShadowVerificationState.Pending)
        {
            throw Error(
                "DataContinuity.InvalidTransition",
                nameof(matched));
        }

        MoveTo(authorityMonotonicNs);
        _shadowVerification = matched
            ? ShadowVerificationState.Verified
            : ShadowVerificationState.Rejected;
        if (!matched)
        {
            _continuationStopReason = ContinuationStopReason.ShadowMismatch;
            if (!_bufferedDataRemaining)
            {
                EnterNoData(
                    ContinuationStopReason.ShadowMismatch,
                    authorityMonotonicNs);
            }
        }
        else if (!_bufferedDataRemaining)
        {
            EnterLocalContinuationOrNoData(authorityMonotonicNs);
        }

        return CaptureState();
    }

    public DataContinuityState ExhaustAuthoritativeBuffer(long authorityMonotonicNs)
    {
        if (_connectionState != ConnectionState.Disconnected ||
            _dataAvailability != DataAvailability.Buffered ||
            !_bufferedDataRemaining)
        {
            throw Error(
                "DataContinuity.InvalidTransition",
                nameof(authorityMonotonicNs));
        }

        MoveTo(authorityMonotonicNs);
        _bufferedDataRemaining = false;
        EnterLocalContinuationOrNoData(authorityMonotonicNs);
        return CaptureState();
    }

    public DataContinuityState RecordContinuationFailure(
        LocalContinuationFailure failure,
        long authorityMonotonicNs)
    {
        if (!Enum.IsDefined(failure))
        {
            throw Error("DataContinuity.InvalidFailure", nameof(failure));
        }

        if (_connectionState != ConnectionState.Disconnected)
        {
            throw Error("DataContinuity.InvalidTransition", nameof(failure));
        }

        MoveTo(authorityMonotonicNs);
        _shadowVerification = ShadowVerificationState.Rejected;
        ContinuationStopReason reason = failure switch
        {
            LocalContinuationFailure.CapsuleInvalid =>
                ContinuationStopReason.CapsuleInvalid,
            LocalContinuationFailure.ConsistencyMismatch =>
                ContinuationStopReason.ConsistencyMismatch,
            LocalContinuationFailure.PerformanceInsufficient =>
                ContinuationStopReason.PerformanceInsufficient,
            _ => throw new InvalidOperationException("validated failure is undefined"),
        };
        _continuationStopReason = reason;
        if (!_bufferedDataRemaining)
        {
            EnterNoData(reason, authorityMonotonicNs);
        }

        return CaptureState();
    }

    public DataContinuityState RecordScenarioEnded(long authorityMonotonicNs)
    {
        MoveTo(authorityMonotonicNs);
        _scenarioEnded = true;
        if (_dataAvailability == DataAvailability.LocalContinuation)
        {
            EnterNoData(
                ContinuationStopReason.ScenarioEnded,
                authorityMonotonicNs);
        }

        return CaptureState();
    }

    public DataContinuityState BeginRelocking(
        bool hasVerifiedPreroll,
        long authorityMonotonicNs)
    {
        if (_connectionState != ConnectionState.Disconnected)
        {
            throw Error(
                "DataContinuity.InvalidTransition",
                nameof(authorityMonotonicNs));
        }

        MoveTo(authorityMonotonicNs);
        _connectionState = ConnectionState.Relocking;
        _authorityState = AuthorityState.Provisional;
        _bufferedDataRemaining = hasVerifiedPreroll;
        _continuationStopReason = hasVerifiedPreroll
            ? null
            : ContinuationStopReason.AwaitingRelockCommit;
        if (hasVerifiedPreroll)
        {
            _dataAvailability = DataAvailability.Buffered;
            _noDataSinceAuthorityMonotonicNs = null;
        }
        else
        {
            _dataAvailability = DataAvailability.NoData;
            _noDataSinceAuthorityMonotonicNs ??= authorityMonotonicNs;
        }

        return CaptureState();
    }

    public DataContinuityState RecordRelockPrerollVerified(
        long authorityMonotonicNs)
    {
        if (_connectionState != ConnectionState.Relocking ||
            _dataAvailability != DataAvailability.NoData ||
            _bufferedDataRemaining ||
            _continuationStopReason !=
                ContinuationStopReason.AwaitingRelockCommit)
        {
            throw Error(
                "DataContinuity.InvalidTransition",
                nameof(authorityMonotonicNs));
        }

        MoveTo(authorityMonotonicNs);
        _dataAvailability = DataAvailability.Buffered;
        _authorityState = AuthorityState.Provisional;
        _bufferedDataRemaining = true;
        _noDataSinceAuthorityMonotonicNs = null;
        _continuationStopReason = null;
        return CaptureState();
    }

    public DataContinuityState CompleteRelocking(long authorityMonotonicNs)
    {
        if (_connectionState != ConnectionState.Relocking)
        {
            throw Error(
                "DataContinuity.InvalidTransition",
                nameof(authorityMonotonicNs));
        }

        MoveTo(authorityMonotonicNs);
        _connectionState = ConnectionState.Connected;
        _dataAvailability = DataAvailability.Authoritative;
        _authorityState = AuthorityState.Authoritative;
        _shadowVerification = ShadowVerificationState.NotStarted;
        _bufferedDataRemaining = false;
        _disconnectedAtAuthorityMonotonicNs = null;
        _localContinuationStartedAtAuthorityMonotonicNs = null;
        _noDataSinceAuthorityMonotonicNs = null;
        _continuationStopReason = null;
        return CaptureState();
    }

    public DataContinuityState Advance(long authorityMonotonicNs)
    {
        MoveTo(authorityMonotonicNs);
        return CaptureState();
    }

    public ConnectivityCriticalBannerProjection CaptureBanner()
    {
        ConnectivityBannerMessage message = _connectionState switch
        {
            ConnectionState.Suspect => ConnectivityBannerMessage.ConnectionSuspect,
            ConnectionState.Relocking => ConnectivityBannerMessage.Relocking,
            _ => _dataAvailability switch
            {
                DataAvailability.Buffered =>
                    ConnectivityBannerMessage.PlayingVerifiedBuffer,
                DataAvailability.LocalContinuation =>
                    ConnectivityBannerMessage.LocalContinuationProvisional,
                DataAvailability.NoData => ConnectivityBannerMessage.NoData,
                _ => ConnectivityBannerMessage.None,
            },
        };
        return new ConnectivityCriticalBannerProjection(
            message != ConnectivityBannerMessage.None,
            message,
            ConnectivityTechnicalAudioPolicy.Silent);
    }

    public DataContinuityState CaptureState() => new(
        Policy,
        _lastAuthorityMonotonicNs,
        _connectionState,
        _dataAvailability,
        _authorityState,
        _shadowVerification,
        _bufferedDataRemaining,
        _disconnectedAtAuthorityMonotonicNs,
        _localContinuationStartedAtAuthorityMonotonicNs,
        _noDataSinceAuthorityMonotonicNs,
        _scenarioEnded,
        _continuationStopReason);

    private void MoveTo(long authorityMonotonicNs)
    {
        EnsureTime(authorityMonotonicNs);
        _lastAuthorityMonotonicNs = authorityMonotonicNs;
        if (_dataAvailability != DataAvailability.LocalContinuation ||
            Policy.Kind != LocalContinuationPolicyKind.Duration)
        {
            return;
        }

        long startedAt = _localContinuationStartedAtAuthorityMonotonicNs!.Value;
        ulong elapsed = checked((ulong)(authorityMonotonicNs - startedAt));
        if (elapsed >= Policy.DurationNs)
        {
            long expiredAt = checked(startedAt + (long)Policy.DurationNs);
            EnterNoData(ContinuationStopReason.DurationExpired, expiredAt);
        }
    }

    private void EnsureTime(long authorityMonotonicNs)
    {
        if (authorityMonotonicNs < _lastAuthorityMonotonicNs)
        {
            throw Error(
                "DataContinuity.TimeReversed",
                nameof(authorityMonotonicNs));
        }
    }

    private void EnterLocalContinuationOrNoData(long authorityMonotonicNs)
    {
        if (_shadowVerification == ShadowVerificationState.Pending)
        {
            EnterNoData(
                ContinuationStopReason.AwaitingShadowVerification,
                authorityMonotonicNs);
            return;
        }

        if (_shadowVerification == ShadowVerificationState.Rejected)
        {
            EnterNoData(
                _continuationStopReason ?? ContinuationStopReason.ShadowMismatch,
                authorityMonotonicNs);
            return;
        }

        if (_scenarioEnded)
        {
            EnterNoData(ContinuationStopReason.ScenarioEnded, authorityMonotonicNs);
            return;
        }

        if (Policy.Kind == LocalContinuationPolicyKind.Disabled)
        {
            EnterNoData(ContinuationStopReason.PolicyDisabled, authorityMonotonicNs);
            return;
        }

        if (Policy.Kind == LocalContinuationPolicyKind.Duration &&
            Policy.DurationNs == 0)
        {
            EnterNoData(ContinuationStopReason.DurationExpired, authorityMonotonicNs);
            return;
        }

        _dataAvailability = DataAvailability.LocalContinuation;
        _authorityState = AuthorityState.Provisional;
        _localContinuationStartedAtAuthorityMonotonicNs = authorityMonotonicNs;
        _noDataSinceAuthorityMonotonicNs = null;
        _continuationStopReason = null;
    }

    private void EnterNoData(
        ContinuationStopReason reason,
        long authorityMonotonicNs)
    {
        _dataAvailability = DataAvailability.NoData;
        _authorityState = AuthorityState.Provisional;
        _bufferedDataRemaining = false;
        _noDataSinceAuthorityMonotonicNs ??= authorityMonotonicNs;
        _continuationStopReason = reason;
    }

    private static void ValidateState(DataContinuityState state)
    {
        if (!IsValidPolicy(state.Policy) ||
            state.LastAuthorityMonotonicNs < 0 ||
            !Enum.IsDefined(state.ConnectionState) ||
            !Enum.IsDefined(state.DataAvailability) ||
            !Enum.IsDefined(state.AuthorityState) ||
            !Enum.IsDefined(state.ShadowVerification) ||
            (state.ContinuationStopReason is not null &&
                !Enum.IsDefined(state.ContinuationStopReason.Value)) ||
            !ValidTimestamp(
                state.DisconnectedAtAuthorityMonotonicNs,
                state.LastAuthorityMonotonicNs) ||
            !ValidTimestamp(
                state.LocalContinuationStartedAtAuthorityMonotonicNs,
                state.LastAuthorityMonotonicNs) ||
            !ValidTimestamp(
                state.NoDataSinceAuthorityMonotonicNs,
                state.LastAuthorityMonotonicNs))
        {
            throw InvalidCheckpoint(nameof(state));
        }

        if (state.ConnectionState is ConnectionState.Connected or ConnectionState.Suspect)
        {
            if (state.DataAvailability != DataAvailability.Authoritative ||
                state.AuthorityState != AuthorityState.Authoritative ||
                state.ShadowVerification != ShadowVerificationState.NotStarted ||
                state.BufferedDataRemaining ||
                state.DisconnectedAtAuthorityMonotonicNs is not null ||
                state.LocalContinuationStartedAtAuthorityMonotonicNs is not null ||
                state.NoDataSinceAuthorityMonotonicNs is not null ||
                state.ContinuationStopReason is not null)
            {
                throw InvalidCheckpoint(nameof(state));
            }

            return;
        }

        if (state.DisconnectedAtAuthorityMonotonicNs is null ||
            state.ShadowVerification == ShadowVerificationState.NotStarted ||
            (state.LocalContinuationStartedAtAuthorityMonotonicNs is not null &&
                state.LocalContinuationStartedAtAuthorityMonotonicNs <
                    state.DisconnectedAtAuthorityMonotonicNs) ||
            (state.NoDataSinceAuthorityMonotonicNs is not null &&
                state.NoDataSinceAuthorityMonotonicNs <
                    state.DisconnectedAtAuthorityMonotonicNs))
        {
            throw InvalidCheckpoint(nameof(state));
        }

        if (state.ConnectionState == ConnectionState.Relocking)
        {
            bool validRelocking = state.AuthorityState == AuthorityState.Provisional &&
                ((state.DataAvailability == DataAvailability.Buffered &&
                        state.BufferedDataRemaining &&
                        state.NoDataSinceAuthorityMonotonicNs is null &&
                        state.ContinuationStopReason is null) ||
                    (state.DataAvailability == DataAvailability.NoData &&
                        !state.BufferedDataRemaining &&
                        state.NoDataSinceAuthorityMonotonicNs is not null &&
                        state.ContinuationStopReason ==
                            ContinuationStopReason.AwaitingRelockCommit));
            if (!validRelocking)
            {
                throw InvalidCheckpoint(nameof(state));
            }

            return;
        }

        ValidateDisconnectedState(state);
    }

    private static void ValidateDisconnectedState(DataContinuityState state)
    {
        if (state.DataAvailability == DataAvailability.Buffered)
        {
            if (!state.BufferedDataRemaining ||
                state.AuthorityState != AuthorityState.Authoritative ||
                state.LocalContinuationStartedAtAuthorityMonotonicNs is not null ||
                state.NoDataSinceAuthorityMonotonicNs is not null ||
                (state.ShadowVerification == ShadowVerificationState.Rejected
                    ? !IsRejectedReason(state.ContinuationStopReason)
                    : state.ContinuationStopReason is not null))
            {
                throw InvalidCheckpoint(nameof(state));
            }

            return;
        }

        if (state.BufferedDataRemaining ||
            state.AuthorityState != AuthorityState.Provisional)
        {
            throw InvalidCheckpoint(nameof(state));
        }

        if (state.DataAvailability == DataAvailability.LocalContinuation)
        {
            if (state.ShadowVerification != ShadowVerificationState.Verified ||
                state.LocalContinuationStartedAtAuthorityMonotonicNs is null ||
                state.NoDataSinceAuthorityMonotonicNs is not null ||
                state.ContinuationStopReason is not null ||
                state.ScenarioEnded ||
                state.Policy.Kind == LocalContinuationPolicyKind.Disabled ||
                (state.Policy.Kind == LocalContinuationPolicyKind.Duration &&
                    (state.Policy.DurationNs == 0 ||
                        (ulong)(state.LastAuthorityMonotonicNs -
                            state.LocalContinuationStartedAtAuthorityMonotonicNs.Value) >=
                            state.Policy.DurationNs)))
            {
                throw InvalidCheckpoint(nameof(state));
            }

            return;
        }

        if (state.DataAvailability != DataAvailability.NoData ||
            state.NoDataSinceAuthorityMonotonicNs is null ||
            state.ContinuationStopReason is null ||
            !ValidNoDataReason(state))
        {
            throw InvalidCheckpoint(nameof(state));
        }
    }

    private static bool ValidNoDataReason(DataContinuityState state) =>
        state.ContinuationStopReason switch
        {
            ContinuationStopReason.AwaitingShadowVerification =>
                state.ShadowVerification == ShadowVerificationState.Pending &&
                state.LocalContinuationStartedAtAuthorityMonotonicNs is null,
            ContinuationStopReason.PolicyDisabled =>
                state.ShadowVerification == ShadowVerificationState.Verified &&
                state.Policy.Kind == LocalContinuationPolicyKind.Disabled &&
                state.LocalContinuationStartedAtAuthorityMonotonicNs is null,
            ContinuationStopReason.ShadowMismatch =>
                state.ShadowVerification == ShadowVerificationState.Rejected &&
                state.LocalContinuationStartedAtAuthorityMonotonicNs is null,
            ContinuationStopReason.CapsuleInvalid or
            ContinuationStopReason.ConsistencyMismatch or
            ContinuationStopReason.PerformanceInsufficient =>
                state.ShadowVerification == ShadowVerificationState.Rejected,
            ContinuationStopReason.DurationExpired =>
                ValidDurationExpiry(state),
            ContinuationStopReason.ScenarioEnded =>
                state.ShadowVerification == ShadowVerificationState.Verified &&
                state.ScenarioEnded,
            ContinuationStopReason.AwaitingRelockCommit => false,
            _ => false,
        };

    private static bool ValidDurationExpiry(DataContinuityState state)
    {
        if (state.ShadowVerification != ShadowVerificationState.Verified ||
            state.Policy.Kind != LocalContinuationPolicyKind.Duration)
        {
            return false;
        }

        if (state.Policy.DurationNs == 0)
        {
            return state.LocalContinuationStartedAtAuthorityMonotonicNs is null;
        }

        if (state.LocalContinuationStartedAtAuthorityMonotonicNs is null)
        {
            return false;
        }

        Int128 expiredWide =
            (Int128)state.LocalContinuationStartedAtAuthorityMonotonicNs.Value +
            state.Policy.DurationNs;
        return expiredWide <= long.MaxValue &&
            state.NoDataSinceAuthorityMonotonicNs == (long)expiredWide &&
            expiredWide <= state.LastAuthorityMonotonicNs;
    }

    private static bool IsRejectedReason(ContinuationStopReason? reason) =>
        reason is ContinuationStopReason.ShadowMismatch or
            ContinuationStopReason.CapsuleInvalid or
            ContinuationStopReason.ConsistencyMismatch or
            ContinuationStopReason.PerformanceInsufficient;

    private static bool IsValidPolicy(LocalContinuationPolicy? policy) =>
        policy is not null &&
        Enum.IsDefined(policy.Kind) &&
        policy.DurationNs <= LocalContinuationPolicy.MaximumDurationNs &&
        (policy.Kind == LocalContinuationPolicyKind.Duration || policy.DurationNs == 0);

    private static bool ValidTimestamp(long? timestamp, long upperBound) =>
        timestamp is null || timestamp is >= 0 && timestamp <= upperBound;

    private static DataContinuityException InvalidCheckpoint(string parameterName) =>
        Error("DataContinuity.InvalidCheckpoint", parameterName);

    private static DataContinuityException Error(
        string reasonCode,
        string parameterName) => new(reasonCode, parameterName);
}
