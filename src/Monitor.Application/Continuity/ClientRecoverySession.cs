// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;

namespace Monitor.Application.Continuity;

public sealed class ClientRecoverySessionException : ArgumentException
{
    public ClientRecoverySessionException(
        string reasonCode,
        string parameterName,
        Exception? innerException = null)
        : base(reasonCode, parameterName, innerException)
    {
        ReasonCode = reasonCode;
    }

    public string ReasonCode { get; }
}

public sealed record ClientRecoverySessionState(
    RecoveryRelockState Relock,
    DataContinuityState Continuity);

public sealed class ClientRecoverySession
{
    private RecoveryRelockCoordinator _relock;
    private DataContinuityStateMachine _continuity;

    private ClientRecoverySession(
        RecoveryRelockCoordinator relock,
        DataContinuityStateMachine continuity)
    {
        ValidatePair(relock.CaptureState(), continuity.CaptureState());
        _relock = relock;
        _continuity = continuity;
    }

    public RecoveryRelockPhase Phase => _relock.Phase;

    public DataAvailability DataAvailability => _continuity.DataAvailability;

    public AuthorityState AuthorityState => _continuity.AuthorityState;

    public ConnectionState ConnectionState => _continuity.ConnectionState;

    public static ClientRecoverySession Start(
        DataContinuityState disconnectedContinuity,
        Guid clientId,
        Guid sessionId,
        Guid instanceId,
        ulong acceptedAuthorityEpoch,
        ulong acceptedStreamEpoch,
        ulong lastAppliedCommitSequence,
        long currentSimTimeNs)
    {
        ArgumentNullException.ThrowIfNull(disconnectedContinuity);
        DataContinuityStateMachine continuity;
        try
        {
            continuity = DataContinuityStateMachine.Restore(
                disconnectedContinuity);
        }
        catch (ArgumentException exception)
        {
            throw Error(
                "ClientRecovery.InvalidConfiguration",
                nameof(disconnectedContinuity),
                exception);
        }

        if (continuity.ConnectionState != ConnectionState.Disconnected)
        {
            throw Error(
                "ClientRecovery.InvalidConfiguration",
                nameof(disconnectedContinuity));
        }

        RecoveryRelockCoordinator relock;
        try
        {
            relock = RecoveryRelockCoordinator.Start(
                clientId,
                sessionId,
                instanceId,
                acceptedAuthorityEpoch,
                acceptedStreamEpoch,
                lastAppliedCommitSequence,
                currentSimTimeNs);
        }
        catch (ArgumentException exception)
        {
            throw Error(
                "ClientRecovery.InvalidConfiguration",
                nameof(clientId),
                exception);
        }

        return new ClientRecoverySession(relock, continuity);
    }

    public static ClientRecoverySession Restore(ClientRecoverySessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Relock);
        ArgumentNullException.ThrowIfNull(state.Continuity);
        try
        {
            return new ClientRecoverySession(
                RecoveryRelockCoordinator.Restore(state.Relock),
                DataContinuityStateMachine.Restore(state.Continuity));
        }
        catch (ClientRecoverySessionException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw Error(
                "ClientRecovery.InvalidCheckpoint",
                nameof(state),
                exception);
        }
    }

    public ClientRecoverySessionState AcceptPlan(
        RecoveryResyncPlan plan,
        long currentSimTimeNs,
        long authorityMonotonicNs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        RecoveryRelockCoordinator relock = CloneRelock();
        DataContinuityStateMachine continuity = CloneContinuity();
        RecoveryRelockState relockState = relock.AcceptPlan(
            plan,
            currentSimTimeNs);
        SynchronizeContinuity(relockState, continuity, authorityMonotonicNs);

        return CommitTrials(relock, continuity);
    }

    public ClientRecoverySessionState ReplaceRejectedPlan(
        RecoveryResyncPlan plan,
        long currentSimTimeNs,
        long authorityMonotonicNs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        RecoveryRelockCoordinator relock = CloneRelock();
        DataContinuityStateMachine continuity = CloneContinuity();
        RecoveryRelockState relockState = relock.ReplaceRejectedPlan(
            plan,
            currentSimTimeNs);
        SynchronizeContinuity(relockState, continuity, authorityMonotonicNs);

        return CommitTrials(relock, continuity);
    }

    public ClientRecoverySessionState RecordPrepared(
        RecoveryPreparationEvidence evidence,
        long authorityMonotonicNs)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        RecoveryRelockCoordinator relock = CloneRelock();
        DataContinuityStateMachine continuity = CloneContinuity();
        RecoveryRelockState relockState = relock.RecordPrepared(evidence);
        if (relockState.Phase == RecoveryRelockPhase.Prepared)
        {
            _ = continuity.RecordRelockPrerollVerified(authorityMonotonicNs);
        }
        else
        {
            _ = continuity.Disconnect(
                hasBufferedData: false,
                authorityMonotonicNs);
        }

        return CommitTrials(relock, continuity);
    }

    public RecoveryPreparedAcknowledgement CapturePreparedAcknowledgement()
    {
        ValidatePair(_relock.CaptureState(), _continuity.CaptureState());
        return _relock.CapturePreparedAcknowledgement();
    }

    public ClientRecoverySessionState Commit(
        RecoveryCommitCommand command,
        long authorityMonotonicNs)
    {
        ArgumentNullException.ThrowIfNull(command);
        RecoveryRelockCoordinator relock = CloneRelock();
        DataContinuityStateMachine continuity = CloneContinuity();
        _ = relock.Commit(command);
        _ = continuity.CompleteRelocking(authorityMonotonicNs);
        return CommitTrials(relock, continuity);
    }

    public ClientRecoverySessionState Advance(
        long currentSimTimeNs,
        long authorityMonotonicNs)
    {
        RecoveryRelockCoordinator relock = CloneRelock();
        DataContinuityStateMachine continuity = CloneContinuity();
        RecoveryRelockState relockState = relock.Advance(currentSimTimeNs);
        SynchronizeContinuity(relockState, continuity, authorityMonotonicNs);

        return CommitTrials(relock, continuity);
    }

    public ClientRecoverySessionState CaptureState() => new(
        _relock.CaptureState(),
        _continuity.CaptureState());

    private ClientRecoverySessionState CommitTrials(
        RecoveryRelockCoordinator relock,
        DataContinuityStateMachine continuity)
    {
        RecoveryRelockState relockState = relock.CaptureState();
        DataContinuityState continuityState = continuity.CaptureState();
        ValidatePair(relockState, continuityState);
        _relock = relock;
        _continuity = continuity;
        return new ClientRecoverySessionState(relockState, continuityState);
    }

    private RecoveryRelockCoordinator CloneRelock() =>
        RecoveryRelockCoordinator.Restore(_relock.CaptureState());

    private DataContinuityStateMachine CloneContinuity() =>
        DataContinuityStateMachine.Restore(_continuity.CaptureState());

    private static void SynchronizeContinuity(
        RecoveryRelockState relock,
        DataContinuityStateMachine continuity,
        long authorityMonotonicNs)
    {
        if (relock.Phase == RecoveryRelockPhase.Preparing &&
            continuity.ConnectionState == ConnectionState.Disconnected)
        {
            _ = continuity.BeginRelocking(
                hasVerifiedPreroll: false,
                authorityMonotonicNs);
        }
        else if (relock.Phase == RecoveryRelockPhase.Rejected &&
            continuity.ConnectionState == ConnectionState.Relocking)
        {
            _ = continuity.Disconnect(
                hasBufferedData: false,
                authorityMonotonicNs);
        }
        else
        {
            _ = continuity.Advance(authorityMonotonicNs);
        }
    }

    private static void ValidatePair(
        RecoveryRelockState relock,
        DataContinuityState continuity)
    {
        bool valid = relock.Phase switch
        {
            RecoveryRelockPhase.AwaitingPlan or
                RecoveryRelockPhase.Rejected =>
                continuity.ConnectionState == ConnectionState.Disconnected,
            RecoveryRelockPhase.Preparing =>
                continuity.ConnectionState == ConnectionState.Relocking &&
                continuity.DataAvailability == DataAvailability.NoData &&
                continuity.AuthorityState == AuthorityState.Provisional &&
                continuity.ContinuationStopReason ==
                    ContinuationStopReason.AwaitingRelockCommit,
            RecoveryRelockPhase.Prepared =>
                continuity.ConnectionState == ConnectionState.Relocking &&
                continuity.DataAvailability == DataAvailability.Buffered &&
                continuity.AuthorityState == AuthorityState.Provisional &&
                continuity.BufferedDataRemaining,
            RecoveryRelockPhase.Committed =>
                continuity.ConnectionState == ConnectionState.Connected &&
                continuity.DataAvailability == DataAvailability.Authoritative &&
                continuity.AuthorityState == AuthorityState.Authoritative,
            _ => false,
        };
        if (!valid)
        {
            throw Error(
                "ClientRecovery.InvalidCheckpoint",
                nameof(relock));
        }
    }

    private static ClientRecoverySessionException Error(
        string reasonCode,
        string parameterName,
        Exception? innerException = null) =>
        new(reasonCode, parameterName, innerException);
}
