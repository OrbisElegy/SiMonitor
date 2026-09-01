// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Continuity;

public sealed class RecoveryHandshakeException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public enum ContinuityReportIntegrityState
{
    PendingVerification,
    Verified,
    Rejected,
}

public enum ContinuityReportVerificationResult
{
    Verified,
    Rejected,
}

public enum RecoveryHandshakePhase
{
    AwaitingHello,
    AwaitingReport,
    VerifyingReport,
    ReadyForPlan,
    Rejected,
}

public enum RecoveryHandshakeRejectionReason
{
    ClientReportRejected,
    HostReplayRejected,
    HostReplayMismatch,
}

public sealed record RecoveryHelloMessage(
    Guid ClientId,
    Guid SessionId,
    Guid InstanceId,
    ulong ObservedAuthorityEpoch,
    ulong LastAppliedCommitSequence,
    ulong LastAppliedEventSequence,
    string? BaseSha256,
    string? LastDeltaSha256,
    Guid? LocalContinuationReportId,
    bool MomentaryInputsCleared);

public sealed record LocalContinuationSampleFrontier(
    string ChannelId,
    ulong LastSampleIndex);

public sealed record LocalContinuationReportMessage(
    Guid ReportId,
    Guid SessionId,
    Guid ClientId,
    Guid InstanceId,
    string BranchId,
    string BaseSha256,
    string? LastDeltaSha256,
    ulong FallbackEpoch,
    long StartSimTimeNs,
    long EndSimTimeNs,
    IReadOnlyList<LocalContinuationSampleFrontier> LastSampleIndexByChannel,
    ulong LastBlockSequence,
    ulong LastEventSequence,
    string RollingStateSha256,
    string OfflineActionChainSha256,
    IReadOnlyList<Guid> OfflineActionEventIds,
    ContinuityReportIntegrityState IntegrityState);

public sealed record ContinuityReportVerification(
    Guid ReportId,
    string BaseSha256,
    string? LastDeltaSha256,
    string ReplayedRollingStateSha256,
    string ReplayedOfflineActionChainSha256,
    ulong AcceptedThroughCommitSequence,
    ContinuityReportVerificationResult Result,
    string? ReasonCode);

public sealed record RecoveryPlanningInput(
    Guid ClientId,
    Guid SessionId,
    Guid InstanceId,
    ulong HostAuthorityEpoch,
    ulong ObservedAuthorityEpoch,
    ulong LastAppliedCommitSequence,
    ulong LastAppliedEventSequence,
    LocalContinuationReportMessage? LocalContinuationReport,
    ulong AcceptedThroughCommitSequence,
    bool LocalPredictionVerified);

public sealed record RecoveryHandshakeState(
    Guid ClientId,
    Guid SessionId,
    Guid InstanceId,
    ulong HostAuthorityEpoch,
    RecoveryHandshakePhase Phase,
    RecoveryHelloMessage? Hello,
    LocalContinuationReportMessage? Report,
    ContinuityReportVerification? Verification,
    RecoveryHandshakeRejectionReason? RejectionReason);

public sealed class RecoveryHandshakeCoordinator
{
    public const int MaximumChannelFrontiers = 128;
    public const int MaximumOfflineActionEvents = 10_000;

    private RecoveryHandshakePhase _phase;
    private RecoveryHelloMessage? _hello;
    private LocalContinuationReportMessage? _report;
    private ContinuityReportVerification? _verification;
    private RecoveryHandshakeRejectionReason? _rejectionReason;

    private RecoveryHandshakeCoordinator(RecoveryHandshakeState state)
    {
        ValidateState(state);
        ClientId = state.ClientId;
        SessionId = state.SessionId;
        InstanceId = state.InstanceId;
        HostAuthorityEpoch = state.HostAuthorityEpoch;
        _phase = state.Phase;
        _hello = state.Hello;
        _report = CopyReport(state.Report);
        _verification = state.Verification;
        _rejectionReason = state.RejectionReason;
    }

    public Guid ClientId { get; }

    public Guid SessionId { get; }

    public Guid InstanceId { get; }

    public ulong HostAuthorityEpoch { get; }

    public RecoveryHandshakePhase Phase => _phase;

    public static RecoveryHandshakeCoordinator Start(
        Guid clientId,
        Guid sessionId,
        Guid instanceId,
        ulong hostAuthorityEpoch)
    {
        if (clientId == Guid.Empty ||
            sessionId == Guid.Empty ||
            instanceId == Guid.Empty ||
            hostAuthorityEpoch == 0)
        {
            throw Error(
                "RecoveryHandshake.InvalidConfiguration",
                nameof(clientId));
        }

        return new RecoveryHandshakeCoordinator(new RecoveryHandshakeState(
            clientId,
            sessionId,
            instanceId,
            hostAuthorityEpoch,
            RecoveryHandshakePhase.AwaitingHello,
            null,
            null,
            null,
            null));
    }

    public static RecoveryHandshakeCoordinator Restore(RecoveryHandshakeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new RecoveryHandshakeCoordinator(state);
    }

    public RecoveryHandshakeState AcceptHello(RecoveryHelloMessage hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        if (_phase != RecoveryHandshakePhase.AwaitingHello)
        {
            if (_hello == hello)
            {
                return CaptureState();
            }

            throw Error("RecoveryHandshake.InvalidTransition", nameof(hello));
        }

        if (!ValidHello(
                hello,
                ClientId,
                SessionId,
                InstanceId,
                HostAuthorityEpoch))
        {
            throw Error("RecoveryHandshake.InvalidHello", nameof(hello));
        }

        _hello = hello;
        _phase = hello.LocalContinuationReportId is null
            ? RecoveryHandshakePhase.ReadyForPlan
            : RecoveryHandshakePhase.AwaitingReport;
        return CaptureState();
    }

    public RecoveryHandshakeState AcceptReport(
        LocalContinuationReportMessage report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(report.LastSampleIndexByChannel);
        ArgumentNullException.ThrowIfNull(report.OfflineActionEventIds);
        if (_phase != RecoveryHandshakePhase.AwaitingReport)
        {
            if (ReportsEqual(_report, report))
            {
                return CaptureState();
            }

            throw Error("RecoveryHandshake.InvalidTransition", nameof(report));
        }

        if (!ValidReport(report, _hello!))
        {
            throw Error("RecoveryHandshake.InvalidReport", nameof(report));
        }

        _report = CopyReport(report);
        if (report.IntegrityState == ContinuityReportIntegrityState.Rejected)
        {
            Reject(RecoveryHandshakeRejectionReason.ClientReportRejected);
        }
        else
        {
            _phase = RecoveryHandshakePhase.VerifyingReport;
        }

        return CaptureState();
    }

    public RecoveryHandshakeState RecordVerification(
        ContinuityReportVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        if (_phase != RecoveryHandshakePhase.VerifyingReport)
        {
            if (_verification == verification)
            {
                return CaptureState();
            }

            throw Error(
                "RecoveryHandshake.InvalidTransition",
                nameof(verification));
        }

        if (!ValidVerificationShape(verification))
        {
            throw Error(
                "RecoveryHandshake.InvalidVerification",
                nameof(verification));
        }

        _verification = verification;
        if (verification.Result == ContinuityReportVerificationResult.Rejected)
        {
            Reject(RecoveryHandshakeRejectionReason.HostReplayRejected);
            return CaptureState();
        }

        if (!VerificationMatches(verification, _hello!, _report!))
        {
            Reject(RecoveryHandshakeRejectionReason.HostReplayMismatch);
            return CaptureState();
        }

        _phase = RecoveryHandshakePhase.ReadyForPlan;
        return CaptureState();
    }

    public RecoveryPlanningInput CapturePlanningInput()
    {
        if (_phase != RecoveryHandshakePhase.ReadyForPlan)
        {
            throw Error(
                "RecoveryHandshake.InvalidTransition",
                nameof(CapturePlanningInput));
        }

        RecoveryHelloMessage hello = _hello!;
        return new RecoveryPlanningInput(
            ClientId,
            SessionId,
            InstanceId,
            HostAuthorityEpoch,
            hello.ObservedAuthorityEpoch,
            hello.LastAppliedCommitSequence,
            hello.LastAppliedEventSequence,
            CopyReport(_report),
            _verification?.AcceptedThroughCommitSequence ??
                hello.LastAppliedCommitSequence,
            _verification is not null);
    }

    public RecoveryHandshakeState CaptureState() => new(
        ClientId,
        SessionId,
        InstanceId,
        HostAuthorityEpoch,
        _phase,
        _hello,
        CopyReport(_report),
        _verification,
        _rejectionReason);

    private void Reject(RecoveryHandshakeRejectionReason reason)
    {
        _phase = RecoveryHandshakePhase.Rejected;
        _rejectionReason = reason;
    }

    private static void ValidateState(RecoveryHandshakeState state)
    {
        if (state.ClientId == Guid.Empty ||
            state.SessionId == Guid.Empty ||
            state.InstanceId == Guid.Empty ||
            state.HostAuthorityEpoch == 0 ||
            !Enum.IsDefined(state.Phase) ||
            (state.RejectionReason is not null &&
                !Enum.IsDefined(state.RejectionReason.Value)))
        {
            throw InvalidCheckpoint(nameof(state));
        }

        if (state.Phase == RecoveryHandshakePhase.AwaitingHello)
        {
            if (state.Hello is not null ||
                state.Report is not null ||
                state.Verification is not null ||
                state.RejectionReason is not null)
            {
                throw InvalidCheckpoint(nameof(state));
            }

            return;
        }

        if (!ValidHello(
                state.Hello,
                state.ClientId,
                state.SessionId,
                state.InstanceId,
                state.HostAuthorityEpoch))
        {
            throw InvalidCheckpoint(nameof(state));
        }

        RecoveryHelloMessage hello = state.Hello!;
        switch (state.Phase)
        {
            case RecoveryHandshakePhase.AwaitingReport:
                if (hello.LocalContinuationReportId is null ||
                    state.Report is not null ||
                    state.Verification is not null ||
                    state.RejectionReason is not null)
                {
                    throw InvalidCheckpoint(nameof(state));
                }

                break;
            case RecoveryHandshakePhase.VerifyingReport:
                if (!ValidReport(state.Report, hello) ||
                    state.Report!.IntegrityState ==
                        ContinuityReportIntegrityState.Rejected ||
                    state.Verification is not null ||
                    state.RejectionReason is not null)
                {
                    throw InvalidCheckpoint(nameof(state));
                }

                break;
            case RecoveryHandshakePhase.ReadyForPlan:
                ValidateReadyState(state, hello);
                break;
            case RecoveryHandshakePhase.Rejected:
                ValidateRejectedState(state, hello);
                break;
            default:
                throw InvalidCheckpoint(nameof(state));
        }
    }

    private static void ValidateReadyState(
        RecoveryHandshakeState state,
        RecoveryHelloMessage hello)
    {
        if (state.RejectionReason is not null)
        {
            throw InvalidCheckpoint(nameof(state));
        }

        if (hello.LocalContinuationReportId is null)
        {
            if (state.Report is not null || state.Verification is not null)
            {
                throw InvalidCheckpoint(nameof(state));
            }

            return;
        }

        if (!ValidReport(state.Report, hello) ||
            !ValidVerificationShape(state.Verification) ||
            state.Verification!.Result != ContinuityReportVerificationResult.Verified ||
            !VerificationMatches(state.Verification, hello, state.Report!))
        {
            throw InvalidCheckpoint(nameof(state));
        }
    }

    private static void ValidateRejectedState(
        RecoveryHandshakeState state,
        RecoveryHelloMessage hello)
    {
        if (!ValidReport(state.Report, hello) || state.RejectionReason is null)
        {
            throw InvalidCheckpoint(nameof(state));
        }

        bool valid = state.RejectionReason switch
        {
            RecoveryHandshakeRejectionReason.ClientReportRejected =>
                state.Report!.IntegrityState ==
                    ContinuityReportIntegrityState.Rejected &&
                state.Verification is null,
            RecoveryHandshakeRejectionReason.HostReplayRejected =>
                ValidVerificationShape(state.Verification) &&
                state.Verification!.Result ==
                    ContinuityReportVerificationResult.Rejected,
            RecoveryHandshakeRejectionReason.HostReplayMismatch =>
                ValidVerificationShape(state.Verification) &&
                state.Verification!.Result ==
                    ContinuityReportVerificationResult.Verified &&
                !VerificationMatches(state.Verification, hello, state.Report!),
            _ => false,
        };
        if (!valid)
        {
            throw InvalidCheckpoint(nameof(state));
        }
    }

    private static bool ValidHello(
        RecoveryHelloMessage? hello,
        Guid clientId,
        Guid sessionId,
        Guid instanceId,
        ulong hostAuthorityEpoch) =>
        hello is not null &&
        hello.ClientId == clientId &&
        hello.SessionId == sessionId &&
        hello.InstanceId == instanceId &&
        hello.ObservedAuthorityEpoch < hostAuthorityEpoch &&
        hello.MomentaryInputsCleared &&
        (hello.BaseSha256 is null || IsSha256(hello.BaseSha256)) &&
        (hello.LastDeltaSha256 is null || IsSha256(hello.LastDeltaSha256)) &&
        (hello.BaseSha256 is not null ||
            hello.LastDeltaSha256 is null &&
            hello.LocalContinuationReportId is null) &&
        (hello.LocalContinuationReportId is null ||
            hello.LocalContinuationReportId != Guid.Empty);

    private static bool ValidReport(
        LocalContinuationReportMessage? report,
        RecoveryHelloMessage hello)
    {
        if (report is null ||
            report.ReportId == Guid.Empty ||
            report.ReportId != hello.LocalContinuationReportId ||
            report.ClientId != hello.ClientId ||
            report.SessionId != hello.SessionId ||
            report.InstanceId != hello.InstanceId ||
            !IsStableId(report.BranchId) ||
            !IsSha256(report.BaseSha256) ||
            !StringComparer.Ordinal.Equals(report.BaseSha256, hello.BaseSha256) ||
            !StringComparer.Ordinal.Equals(
                report.LastDeltaSha256,
                hello.LastDeltaSha256) ||
            report.StartSimTimeNs < 0 ||
            report.EndSimTimeNs < report.StartSimTimeNs ||
            report.LastEventSequence < hello.LastAppliedEventSequence ||
            !IsSha256(report.RollingStateSha256) ||
            !IsSha256(report.OfflineActionChainSha256) ||
            !Enum.IsDefined(report.IntegrityState) ||
            report.LastSampleIndexByChannel is null ||
            report.LastSampleIndexByChannel.Count > MaximumChannelFrontiers ||
            report.OfflineActionEventIds is null ||
            report.OfflineActionEventIds.Count > MaximumOfflineActionEvents)
        {
            return false;
        }

        string? previousChannelId = null;
        foreach (LocalContinuationSampleFrontier frontier in
            report.LastSampleIndexByChannel)
        {
            if (frontier is null ||
                !IsStableId(frontier.ChannelId) ||
                (previousChannelId is not null &&
                    StringComparer.Ordinal.Compare(
                        previousChannelId,
                        frontier.ChannelId) >= 0))
            {
                return false;
            }

            previousChannelId = frontier.ChannelId;
        }

        Guid? previousEventId = null;
        foreach (Guid eventId in report.OfflineActionEventIds)
        {
            if (eventId == Guid.Empty ||
                (previousEventId is not null &&
                    CompareGuids(previousEventId.Value, eventId) >= 0))
            {
                return false;
            }

            previousEventId = eventId;
        }

        return true;
    }

    private static bool ValidVerificationShape(
        ContinuityReportVerification? verification) =>
        verification is not null &&
        verification.ReportId != Guid.Empty &&
        IsSha256(verification.BaseSha256) &&
        (verification.LastDeltaSha256 is null ||
            IsSha256(verification.LastDeltaSha256)) &&
        IsSha256(verification.ReplayedRollingStateSha256) &&
        IsSha256(verification.ReplayedOfflineActionChainSha256) &&
        Enum.IsDefined(verification.Result) &&
        (verification.Result == ContinuityReportVerificationResult.Verified
            ? verification.ReasonCode is null
            : IsReasonCode(verification.ReasonCode));

    private static bool VerificationMatches(
        ContinuityReportVerification verification,
        RecoveryHelloMessage hello,
        LocalContinuationReportMessage report) =>
        verification.ReportId == report.ReportId &&
        StringComparer.Ordinal.Equals(
            verification.BaseSha256,
            report.BaseSha256) &&
        StringComparer.Ordinal.Equals(
            verification.LastDeltaSha256,
            report.LastDeltaSha256) &&
        StringComparer.Ordinal.Equals(
            verification.ReplayedRollingStateSha256,
            report.RollingStateSha256) &&
        StringComparer.Ordinal.Equals(
            verification.ReplayedOfflineActionChainSha256,
            report.OfflineActionChainSha256) &&
        verification.AcceptedThroughCommitSequence >=
            hello.LastAppliedCommitSequence;

    private static LocalContinuationReportMessage? CopyReport(
        LocalContinuationReportMessage? report)
    {
        if (report is null)
        {
            return null;
        }

        LocalContinuationSampleFrontier[] frontiers =
            [.. report.LastSampleIndexByChannel];
        Guid[] eventIds = [.. report.OfflineActionEventIds];
        return report with
        {
            LastSampleIndexByChannel = Array.AsReadOnly(frontiers),
            OfflineActionEventIds = Array.AsReadOnly(eventIds),
        };
    }

    private static bool ReportsEqual(
        LocalContinuationReportMessage? left,
        LocalContinuationReportMessage? right) =>
        left is not null &&
        right is not null &&
        left.ReportId == right.ReportId &&
        left.SessionId == right.SessionId &&
        left.ClientId == right.ClientId &&
        left.InstanceId == right.InstanceId &&
        StringComparer.Ordinal.Equals(left.BranchId, right.BranchId) &&
        StringComparer.Ordinal.Equals(left.BaseSha256, right.BaseSha256) &&
        StringComparer.Ordinal.Equals(left.LastDeltaSha256, right.LastDeltaSha256) &&
        left.FallbackEpoch == right.FallbackEpoch &&
        left.StartSimTimeNs == right.StartSimTimeNs &&
        left.EndSimTimeNs == right.EndSimTimeNs &&
        left.LastSampleIndexByChannel.SequenceEqual(
            right.LastSampleIndexByChannel) &&
        left.LastBlockSequence == right.LastBlockSequence &&
        left.LastEventSequence == right.LastEventSequence &&
        StringComparer.Ordinal.Equals(
            left.RollingStateSha256,
            right.RollingStateSha256) &&
        StringComparer.Ordinal.Equals(
            left.OfflineActionChainSha256,
            right.OfflineActionChainSha256) &&
        left.OfflineActionEventIds.SequenceEqual(right.OfflineActionEventIds) &&
        left.IntegrityState == right.IntegrityState;

    private static int CompareGuids(Guid left, Guid right)
    {
        Span<byte> leftBytes = stackalloc byte[16];
        Span<byte> rightBytes = stackalloc byte[16];
        _ = left.TryWriteBytes(leftBytes, bigEndian: true, out _);
        _ = right.TryWriteBytes(rightBytes, bigEndian: true, out _);
        return leftBytes.SequenceCompareTo(rightBytes);
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsStableId(string? value)
    {
        if (value is not { Length: >= 1 and <= 128 } ||
            !IsAsciiLetter(value[0]))
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if (!IsAsciiLetter(character) &&
                character is not (>= '0' and <= '9') &&
                character is not ('.' or '_' or ':' or '@' or '/' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsReasonCode(string? value)
    {
        if (value is not { Length: >= 2 and <= 64 } ||
            value[0] is < 'A' or > 'Z')
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if (character is not (>= 'A' and <= 'Z') &&
                character is not (>= '0' and <= '9') &&
                character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static RecoveryHandshakeException InvalidCheckpoint(
        string parameterName) =>
        Error("RecoveryHandshake.InvalidCheckpoint", parameterName);

    private static RecoveryHandshakeException Error(
        string reasonCode,
        string parameterName) => new(reasonCode, parameterName);
}
