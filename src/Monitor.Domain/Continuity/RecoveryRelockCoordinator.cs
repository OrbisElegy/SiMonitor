// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Continuity;

public sealed class RecoveryRelockException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public enum LocalPredictionDecision
{
    CommitVerified,
    Discard,
    FullSnapshotRequired,
    RejectClient,
}

public enum RecoveryRelockPhase
{
    AwaitingPlan,
    Preparing,
    Prepared,
    Rejected,
    Committed,
}

public enum RecoveryRelockRejectionReason
{
    ClientRejectedByHost,
    PreparationDeadlineMissed,
    PreparationIdentityMismatch,
    LocalPredictionMismatch,
    PrerollMismatch,
    SnapshotMismatch,
    CommitDeadlineMissed,
}

public sealed record RecoveryResyncPlan(
    Guid PlanId,
    ulong NewAuthorityEpoch,
    ulong HostCheckpointSequence,
    string HostStateSha256,
    long RelockSimTimeNs,
    ulong NewStreamEpoch,
    long PrerollFromSimTimeNs,
    long PrerollUntilSimTimeNs,
    LocalPredictionDecision LocalPredictionDecision,
    string ReasonCode,
    ulong AcceptedThroughCommitSequence,
    string? ResumeSnapshotSha256);

public sealed record RecoveryPreparationEvidence(
    Guid PlanId,
    ulong AuthorityEpoch,
    ulong StreamEpoch,
    string HostStateSha256,
    long PrerollFromSimTimeNs,
    long PrerollUntilSimTimeNs,
    string PrerollContentSha256,
    string? ResumeSnapshotSha256,
    bool LocalPredictionMatchedHost,
    long PreparedAtSimTimeNs);

public sealed record RecoveryPreparedAcknowledgement(
    Guid PlanId,
    ulong AuthorityEpoch,
    ulong StreamEpoch,
    long RelockSimTimeNs,
    string HostStateSha256,
    string PrerollContentSha256,
    string? ResumeSnapshotSha256,
    long PreparedAtSimTimeNs);

public sealed record RecoveryCommitCommand(
    Guid PlanId,
    ulong AuthorityEpoch,
    ulong StreamEpoch,
    long RelockSimTimeNs,
    ulong CommitSequence,
    string HostStateSha256,
    string FirstAuthoritativeBlockSha256);

public sealed record RecoveryRelockState(
    Guid ClientId,
    Guid SessionId,
    Guid InstanceId,
    ulong AuthorityEpochBeforePlan,
    ulong StreamEpochBeforePlan,
    ulong CommitSequenceBeforePlan,
    long LastObservedSimTimeNs,
    RecoveryRelockPhase Phase,
    long? PlanAcceptedAtSimTimeNs,
    RecoveryResyncPlan? Plan,
    RecoveryPreparationEvidence? PreparationEvidence,
    RecoveryCommitCommand? CommitReceipt,
    RecoveryRelockRejectionReason? RejectionReason);

public sealed class RecoveryRelockCoordinator
{
    public const long RelockSlotDurationNs = 200_000_000;
    public const long MinimumPrepareLeadNs = 1_000_000_000;

    private long _lastObservedSimTimeNs;
    private RecoveryRelockPhase _phase;
    private long? _planAcceptedAtSimTimeNs;
    private RecoveryResyncPlan? _plan;
    private RecoveryPreparationEvidence? _preparationEvidence;
    private RecoveryCommitCommand? _commitReceipt;
    private RecoveryRelockRejectionReason? _rejectionReason;

    private RecoveryRelockCoordinator(RecoveryRelockState state)
    {
        ValidateState(state);
        ClientId = state.ClientId;
        SessionId = state.SessionId;
        InstanceId = state.InstanceId;
        AuthorityEpochBeforePlan = state.AuthorityEpochBeforePlan;
        StreamEpochBeforePlan = state.StreamEpochBeforePlan;
        CommitSequenceBeforePlan = state.CommitSequenceBeforePlan;
        _lastObservedSimTimeNs = state.LastObservedSimTimeNs;
        _phase = state.Phase;
        _planAcceptedAtSimTimeNs = state.PlanAcceptedAtSimTimeNs;
        _plan = state.Plan;
        _preparationEvidence = state.PreparationEvidence;
        _commitReceipt = state.CommitReceipt;
        _rejectionReason = state.RejectionReason;
    }

    public Guid ClientId { get; }

    public Guid SessionId { get; }

    public Guid InstanceId { get; }

    public ulong AuthorityEpochBeforePlan { get; }

    public ulong StreamEpochBeforePlan { get; }

    public ulong CommitSequenceBeforePlan { get; }

    public long LastObservedSimTimeNs => _lastObservedSimTimeNs;

    public RecoveryRelockPhase Phase => _phase;

    public ulong AcceptedAuthorityEpoch => _phase == RecoveryRelockPhase.Committed
        ? _plan!.NewAuthorityEpoch
        : AuthorityEpochBeforePlan;

    public ulong AcceptedStreamEpoch => _phase == RecoveryRelockPhase.Committed
        ? _plan!.NewStreamEpoch
        : StreamEpochBeforePlan;

    public ulong LastAppliedCommitSequence => _phase == RecoveryRelockPhase.Committed
        ? _commitReceipt!.CommitSequence
        : CommitSequenceBeforePlan;

    public static RecoveryRelockCoordinator Start(
        Guid clientId,
        Guid sessionId,
        Guid instanceId,
        ulong acceptedAuthorityEpoch,
        ulong acceptedStreamEpoch,
        ulong lastAppliedCommitSequence,
        long currentSimTimeNs)
    {
        if (clientId == Guid.Empty ||
            sessionId == Guid.Empty ||
            instanceId == Guid.Empty ||
            currentSimTimeNs < 0)
        {
            throw Error(
                "RecoveryRelock.InvalidConfiguration",
                nameof(clientId));
        }

        return new RecoveryRelockCoordinator(new RecoveryRelockState(
            clientId,
            sessionId,
            instanceId,
            acceptedAuthorityEpoch,
            acceptedStreamEpoch,
            lastAppliedCommitSequence,
            currentSimTimeNs,
            RecoveryRelockPhase.AwaitingPlan,
            null,
            null,
            null,
            null,
            null));
    }

    public static RecoveryRelockCoordinator Restore(RecoveryRelockState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new RecoveryRelockCoordinator(state);
    }

    public RecoveryRelockState AcceptPlan(
        RecoveryResyncPlan plan,
        long currentSimTimeNs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_phase != RecoveryRelockPhase.AwaitingPlan)
        {
            if (_plan == plan)
            {
                return Advance(currentSimTimeNs);
            }

            throw Error("RecoveryRelock.InvalidTransition", nameof(plan));
        }

        ValidatePlan(plan, currentSimTimeNs, nameof(plan));
        SetPlan(plan, currentSimTimeNs);
        return CaptureState();
    }

    public RecoveryRelockState ReplaceRejectedPlan(
        RecoveryResyncPlan plan,
        long currentSimTimeNs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_phase != RecoveryRelockPhase.Rejected)
        {
            throw Error("RecoveryRelock.InvalidTransition", nameof(plan));
        }

        ValidatePlan(plan, currentSimTimeNs, nameof(plan));
        if (_plan is not null && plan.PlanId == _plan.PlanId)
        {
            throw Error("RecoveryRelock.ConflictingPlan", nameof(plan));
        }

        SetPlan(plan, currentSimTimeNs);
        return CaptureState();
    }

    public RecoveryRelockState RecordPrepared(
        RecoveryPreparationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ValidatePreparationShape(evidence, nameof(evidence));
        if (_phase != RecoveryRelockPhase.Preparing)
        {
            throw Error("RecoveryRelock.InvalidTransition", nameof(evidence));
        }

        EnsureTime(evidence.PreparedAtSimTimeNs);
        _lastObservedSimTimeNs = evidence.PreparedAtSimTimeNs;
        RecoveryResyncPlan plan = _plan!;
        if (evidence.PreparedAtSimTimeNs >= plan.RelockSimTimeNs)
        {
            Reject(RecoveryRelockRejectionReason.PreparationDeadlineMissed);
            return CaptureState();
        }

        if (evidence.PlanId != plan.PlanId ||
            evidence.AuthorityEpoch != plan.NewAuthorityEpoch ||
            evidence.StreamEpoch != plan.NewStreamEpoch ||
            !StringComparer.Ordinal.Equals(
                evidence.HostStateSha256,
                plan.HostStateSha256))
        {
            Reject(RecoveryRelockRejectionReason.PreparationIdentityMismatch);
            return CaptureState();
        }

        if (evidence.PrerollFromSimTimeNs != plan.PrerollFromSimTimeNs ||
            evidence.PrerollUntilSimTimeNs != plan.PrerollUntilSimTimeNs)
        {
            Reject(RecoveryRelockRejectionReason.PrerollMismatch);
            return CaptureState();
        }

        if (!StringComparer.Ordinal.Equals(
                evidence.ResumeSnapshotSha256,
                plan.ResumeSnapshotSha256))
        {
            Reject(RecoveryRelockRejectionReason.SnapshotMismatch);
            return CaptureState();
        }

        if (plan.LocalPredictionDecision == LocalPredictionDecision.CommitVerified &&
            !evidence.LocalPredictionMatchedHost)
        {
            Reject(RecoveryRelockRejectionReason.LocalPredictionMismatch);
            return CaptureState();
        }

        _preparationEvidence = evidence;
        _phase = RecoveryRelockPhase.Prepared;
        return CaptureState();
    }

    public RecoveryPreparedAcknowledgement CapturePreparedAcknowledgement()
    {
        if (_phase != RecoveryRelockPhase.Prepared)
        {
            throw Error(
                "RecoveryRelock.InvalidTransition",
                nameof(CapturePreparedAcknowledgement));
        }

        RecoveryResyncPlan plan = _plan!;
        RecoveryPreparationEvidence evidence = _preparationEvidence!;
        return new RecoveryPreparedAcknowledgement(
            plan.PlanId,
            plan.NewAuthorityEpoch,
            plan.NewStreamEpoch,
            plan.RelockSimTimeNs,
            plan.HostStateSha256,
            evidence.PrerollContentSha256,
            evidence.ResumeSnapshotSha256,
            evidence.PreparedAtSimTimeNs);
    }

    public RecoveryRelockState Commit(RecoveryCommitCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommitShape(command, nameof(command));
        if (_phase != RecoveryRelockPhase.Prepared)
        {
            throw Error("RecoveryRelock.InvalidTransition", nameof(command));
        }

        EnsureTime(command.RelockSimTimeNs);
        RecoveryResyncPlan plan = _plan!;
        if (command.PlanId != plan.PlanId ||
            command.AuthorityEpoch != plan.NewAuthorityEpoch ||
            command.StreamEpoch != plan.NewStreamEpoch ||
            command.RelockSimTimeNs != plan.RelockSimTimeNs ||
            command.CommitSequence < CommitSequenceBeforePlan ||
            command.CommitSequence < plan.AcceptedThroughCommitSequence ||
            !StringComparer.Ordinal.Equals(
                command.HostStateSha256,
                plan.HostStateSha256))
        {
            throw Error("RecoveryRelock.CommitMismatch", nameof(command));
        }

        _lastObservedSimTimeNs = command.RelockSimTimeNs;
        _commitReceipt = command;
        _phase = RecoveryRelockPhase.Committed;
        return CaptureState();
    }

    public RecoveryRelockState Advance(long currentSimTimeNs)
    {
        EnsureTime(currentSimTimeNs);
        _lastObservedSimTimeNs = currentSimTimeNs;
        if (_phase == RecoveryRelockPhase.Preparing &&
            currentSimTimeNs >= _plan!.RelockSimTimeNs)
        {
            Reject(RecoveryRelockRejectionReason.PreparationDeadlineMissed);
        }
        else if (_phase == RecoveryRelockPhase.Prepared &&
            currentSimTimeNs > _plan!.RelockSimTimeNs)
        {
            Reject(RecoveryRelockRejectionReason.CommitDeadlineMissed);
        }

        return CaptureState();
    }

    public RecoveryRelockState CaptureState() => new(
        ClientId,
        SessionId,
        InstanceId,
        AuthorityEpochBeforePlan,
        StreamEpochBeforePlan,
        CommitSequenceBeforePlan,
        _lastObservedSimTimeNs,
        _phase,
        _planAcceptedAtSimTimeNs,
        _plan,
        _preparationEvidence,
        _commitReceipt,
        _rejectionReason);

    private void SetPlan(RecoveryResyncPlan plan, long currentSimTimeNs)
    {
        _lastObservedSimTimeNs = currentSimTimeNs;
        _planAcceptedAtSimTimeNs = currentSimTimeNs;
        _plan = plan;
        _preparationEvidence = null;
        _commitReceipt = null;
        if (plan.LocalPredictionDecision == LocalPredictionDecision.RejectClient)
        {
            _phase = RecoveryRelockPhase.Rejected;
            _rejectionReason = RecoveryRelockRejectionReason.ClientRejectedByHost;
        }
        else
        {
            _phase = RecoveryRelockPhase.Preparing;
            _rejectionReason = null;
        }
    }

    private void Reject(RecoveryRelockRejectionReason reason)
    {
        _phase = RecoveryRelockPhase.Rejected;
        _preparationEvidence = null;
        _commitReceipt = null;
        _rejectionReason = reason;
    }

    private void EnsureTime(long currentSimTimeNs)
    {
        if (currentSimTimeNs < _lastObservedSimTimeNs)
        {
            throw Error("RecoveryRelock.TimeReversed", nameof(currentSimTimeNs));
        }
    }

    private void ValidatePlan(
        RecoveryResyncPlan plan,
        long currentSimTimeNs,
        string parameterName)
    {
        EnsureTime(currentSimTimeNs);
        if (!ValidPlan(
                plan,
                AuthorityEpochBeforePlan,
                StreamEpochBeforePlan,
                currentSimTimeNs))
        {
            throw Error("RecoveryRelock.InvalidPlan", parameterName);
        }
    }

    private static bool ValidPlan(
        RecoveryResyncPlan? plan,
        ulong authorityEpochBeforePlan,
        ulong streamEpochBeforePlan,
        long acceptedAtSimTimeNs)
    {
        if (plan is null ||
            plan.PlanId == Guid.Empty ||
            !Enum.IsDefined(plan.LocalPredictionDecision) ||
            plan.NewAuthorityEpoch <= authorityEpochBeforePlan ||
            plan.NewStreamEpoch <= streamEpochBeforePlan ||
            !IsSha256(plan.HostStateSha256) ||
            !IsReasonCode(plan.ReasonCode) ||
            plan.RelockSimTimeNs < 0 ||
            plan.PrerollFromSimTimeNs < 0 ||
            plan.PrerollUntilSimTimeNs != plan.RelockSimTimeNs ||
            plan.PrerollFromSimTimeNs > plan.PrerollUntilSimTimeNs ||
            !IsAligned(plan.RelockSimTimeNs) ||
            !IsAligned(plan.PrerollFromSimTimeNs) ||
            !IsAligned(plan.PrerollUntilSimTimeNs) ||
            (plan.ResumeSnapshotSha256 is not null &&
                !IsSha256(plan.ResumeSnapshotSha256)) ||
            (plan.LocalPredictionDecision ==
                    LocalPredictionDecision.FullSnapshotRequired &&
                plan.ResumeSnapshotSha256 is null))
        {
            return false;
        }

        Int128 minimumBoundary = (Int128)acceptedAtSimTimeNs +
            MinimumPrepareLeadNs;
        return plan.RelockSimTimeNs >= minimumBoundary;
    }

    private static void ValidatePreparationShape(
        RecoveryPreparationEvidence evidence,
        string parameterName)
    {
        if (evidence.PlanId == Guid.Empty ||
            !IsSha256(evidence.HostStateSha256) ||
            !IsSha256(evidence.PrerollContentSha256) ||
            (evidence.ResumeSnapshotSha256 is not null &&
                !IsSha256(evidence.ResumeSnapshotSha256)) ||
            evidence.PrerollFromSimTimeNs < 0 ||
            evidence.PrerollUntilSimTimeNs < 0 ||
            evidence.PreparedAtSimTimeNs < 0)
        {
            throw Error("RecoveryRelock.InvalidPreparation", parameterName);
        }
    }

    private static void ValidateCommitShape(
        RecoveryCommitCommand command,
        string parameterName)
    {
        if (command.PlanId == Guid.Empty ||
            command.RelockSimTimeNs < 0 ||
            !IsSha256(command.HostStateSha256) ||
            !IsSha256(command.FirstAuthoritativeBlockSha256))
        {
            throw Error("RecoveryRelock.InvalidCommit", parameterName);
        }
    }

    private static void ValidateState(RecoveryRelockState state)
    {
        if (state.ClientId == Guid.Empty ||
            state.SessionId == Guid.Empty ||
            state.InstanceId == Guid.Empty ||
            state.LastObservedSimTimeNs < 0 ||
            !Enum.IsDefined(state.Phase) ||
            (state.RejectionReason is not null &&
                !Enum.IsDefined(state.RejectionReason.Value)))
        {
            throw InvalidCheckpoint(nameof(state));
        }

        if (state.Phase == RecoveryRelockPhase.AwaitingPlan)
        {
            if (state.PlanAcceptedAtSimTimeNs is not null ||
                state.Plan is not null ||
                state.PreparationEvidence is not null ||
                state.CommitReceipt is not null ||
                state.RejectionReason is not null)
            {
                throw InvalidCheckpoint(nameof(state));
            }

            return;
        }

        if (state.PlanAcceptedAtSimTimeNs is null ||
            state.PlanAcceptedAtSimTimeNs < 0 ||
            state.PlanAcceptedAtSimTimeNs > state.LastObservedSimTimeNs ||
            !ValidPlan(
                state.Plan,
                state.AuthorityEpochBeforePlan,
                state.StreamEpochBeforePlan,
                state.PlanAcceptedAtSimTimeNs.Value))
        {
            throw InvalidCheckpoint(nameof(state));
        }

        RecoveryResyncPlan plan = state.Plan!;
        switch (state.Phase)
        {
            case RecoveryRelockPhase.Preparing:
                if (state.LastObservedSimTimeNs >= plan.RelockSimTimeNs ||
                    state.PreparationEvidence is not null ||
                    state.CommitReceipt is not null ||
                    state.RejectionReason is not null ||
                    plan.LocalPredictionDecision == LocalPredictionDecision.RejectClient)
                {
                    throw InvalidCheckpoint(nameof(state));
                }

                break;
            case RecoveryRelockPhase.Prepared:
                if (state.LastObservedSimTimeNs > plan.RelockSimTimeNs ||
                    !ValidPreparedEvidence(
                        state.PreparationEvidence,
                        plan,
                        state.PlanAcceptedAtSimTimeNs.Value,
                        state.LastObservedSimTimeNs) ||
                    state.CommitReceipt is not null ||
                    state.RejectionReason is not null)
                {
                    throw InvalidCheckpoint(nameof(state));
                }

                break;
            case RecoveryRelockPhase.Rejected:
                if (state.PreparationEvidence is not null ||
                    state.CommitReceipt is not null ||
                    state.RejectionReason is null ||
                    (plan.LocalPredictionDecision == LocalPredictionDecision.RejectClient) !=
                        (state.RejectionReason ==
                            RecoveryRelockRejectionReason.ClientRejectedByHost))
                {
                    throw InvalidCheckpoint(nameof(state));
                }

                break;
            case RecoveryRelockPhase.Committed:
                if (!ValidPreparedEvidence(
                        state.PreparationEvidence,
                        plan,
                        state.PlanAcceptedAtSimTimeNs.Value,
                        state.LastObservedSimTimeNs) ||
                    !ValidCommit(state.CommitReceipt, plan, state) ||
                    state.RejectionReason is not null ||
                    state.LastObservedSimTimeNs < plan.RelockSimTimeNs)
                {
                    throw InvalidCheckpoint(nameof(state));
                }

                break;
            default:
                throw InvalidCheckpoint(nameof(state));
        }
    }

    private static bool ValidPreparedEvidence(
        RecoveryPreparationEvidence? evidence,
        RecoveryResyncPlan plan,
        long planAcceptedAtSimTimeNs,
        long lastObservedSimTimeNs) =>
        evidence is not null &&
        evidence.PlanId == plan.PlanId &&
        evidence.AuthorityEpoch == plan.NewAuthorityEpoch &&
        evidence.StreamEpoch == plan.NewStreamEpoch &&
        StringComparer.Ordinal.Equals(
            evidence.HostStateSha256,
            plan.HostStateSha256) &&
        evidence.PrerollFromSimTimeNs == plan.PrerollFromSimTimeNs &&
        evidence.PrerollUntilSimTimeNs == plan.PrerollUntilSimTimeNs &&
        IsSha256(evidence.PrerollContentSha256) &&
        StringComparer.Ordinal.Equals(
            evidence.ResumeSnapshotSha256,
            plan.ResumeSnapshotSha256) &&
        evidence.PreparedAtSimTimeNs >= planAcceptedAtSimTimeNs &&
        evidence.PreparedAtSimTimeNs <= lastObservedSimTimeNs &&
        evidence.PreparedAtSimTimeNs < plan.RelockSimTimeNs &&
        (plan.LocalPredictionDecision != LocalPredictionDecision.CommitVerified ||
            evidence.LocalPredictionMatchedHost);

    private static bool ValidCommit(
        RecoveryCommitCommand? command,
        RecoveryResyncPlan plan,
        RecoveryRelockState state) =>
        command is not null &&
        command.PlanId == plan.PlanId &&
        command.AuthorityEpoch == plan.NewAuthorityEpoch &&
        command.StreamEpoch == plan.NewStreamEpoch &&
        command.RelockSimTimeNs == plan.RelockSimTimeNs &&
        command.CommitSequence >= state.CommitSequenceBeforePlan &&
        command.CommitSequence >= plan.AcceptedThroughCommitSequence &&
        StringComparer.Ordinal.Equals(
            command.HostStateSha256,
            plan.HostStateSha256) &&
        IsSha256(command.FirstAuthoritativeBlockSha256);

    private static bool IsAligned(long simTimeNs) =>
        simTimeNs % RelockSlotDurationNs == 0;

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

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

    private static RecoveryRelockException InvalidCheckpoint(string parameterName) =>
        Error("RecoveryRelock.InvalidCheckpoint", parameterName);

    private static RecoveryRelockException Error(
        string reasonCode,
        string parameterName) => new(reasonCode, parameterName);
}
