// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Continuity;
using Monitor.Domain.Continuity;

namespace Monitor.Specs;

internal static class ClientRecoverySessionSpecifications
{
    private static readonly Guid ClientId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SessionId =
        Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid InstanceId =
        Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid PlanId =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    public static Specification[] All =>
    [
        new(nameof(PlanAndEvidenceGateProvisionalPreroll),
            PlanAndEvidenceGateProvisionalPreroll),
        new(nameof(CommitAtomicallyPromotesBothRecoveryAxes),
            CommitAtomicallyPromotesBothRecoveryAxes),
        new(nameof(RejectedPreparationReturnsToDisconnectedNoData),
            RejectedPreparationReturnsToDisconnectedNoData),
        new(nameof(MissedCommitClearsPrerollBeforeReplacement),
            MissedCommitClearsPrerollBeforeReplacement),
        new(nameof(CheckpointAndFailedTransitionCannotSplitTheMachines),
            CheckpointAndFailedTransitionCannotSplitTheMachines),
    ];

    private static void PlanAndEvidenceGateProvisionalPreroll()
    {
        ClientRecoverySession session = Start();
        ClientRecoverySessionState preparing = session.AcceptPlan(
            Plan(),
            currentSimTimeNs: 0,
            authorityMonotonicNs: 3);
        ClientRecoverySessionState retried = session.AcceptPlan(
            Plan(),
            currentSimTimeNs: 0,
            authorityMonotonicNs: 3);
        Check.That(preparing.Relock.Phase == RecoveryRelockPhase.Preparing &&
            retried == preparing &&
            preparing.Continuity.ConnectionState == ConnectionState.Relocking &&
            preparing.Continuity.DataAvailability == DataAvailability.NoData &&
            preparing.Continuity.AuthorityState == AuthorityState.Provisional &&
            preparing.Continuity.ContinuationStopReason ==
                ContinuationStopReason.AwaitingRelockCommit,
            "plan acceptance must enter relocking without claiming unverified preroll");

        Check.That(Reason(() => session.CapturePreparedAcknowledgement()) ==
            "RecoveryRelock.InvalidTransition",
            "a plan alone cannot emit PreparedAck");

        ClientRecoverySessionState prepared = session.RecordPrepared(
            Evidence(),
            authorityMonotonicNs: 4);
        RecoveryPreparedAcknowledgement acknowledgement =
            session.CapturePreparedAcknowledgement();
        Check.That(prepared.Relock.Phase == RecoveryRelockPhase.Prepared &&
            prepared.Continuity.ConnectionState == ConnectionState.Relocking &&
            prepared.Continuity.DataAvailability == DataAvailability.Buffered &&
            prepared.Continuity.AuthorityState == AuthorityState.Provisional &&
            prepared.Continuity.BufferedDataRemaining &&
            acknowledgement.PlanId == PlanId &&
            acknowledgement.PrerollContentSha256 == Hash('b'),
            "only verified preparation may expose provisional preroll and its ack");
    }

    private static void CommitAtomicallyPromotesBothRecoveryAxes()
    {
        ClientRecoverySession session = Prepared();
        ClientRecoverySessionState committed = session.Commit(
            Commit(),
            authorityMonotonicNs: 5);

        Check.That(committed.Relock.Phase == RecoveryRelockPhase.Committed &&
            committed.Relock.CommitReceipt == Commit() &&
            committed.Continuity.ConnectionState == ConnectionState.Connected &&
            committed.Continuity.DataAvailability ==
                DataAvailability.Authoritative &&
            committed.Continuity.AuthorityState == AuthorityState.Authoritative &&
            session.ConnectionState == ConnectionState.Connected &&
            session.AuthorityState == AuthorityState.Authoritative,
            "RecoveryCommit must advance protocol and presentation atomically");

        var restored = ClientRecoverySession.Restore(committed);
        Check.That(restored.CaptureState() == committed,
            "a committed checkpoint must preserve both promoted axes");
    }

    private static void RejectedPreparationReturnsToDisconnectedNoData()
    {
        ClientRecoverySession session = Start();
        _ = session.AcceptPlan(Plan(), 0, 3);
        ClientRecoverySessionState rejected = session.RecordPrepared(
            Evidence() with { StreamEpoch = 9 },
            authorityMonotonicNs: 4);

        Check.That(rejected.Relock.Phase == RecoveryRelockPhase.Rejected &&
            rejected.Relock.RejectionReason ==
                RecoveryRelockRejectionReason.PreparationIdentityMismatch &&
            rejected.Continuity.ConnectionState == ConnectionState.Disconnected &&
            rejected.Continuity.DataAvailability == DataAvailability.NoData &&
            rejected.Continuity.AuthorityState == AuthorityState.Provisional &&
            rejected.Continuity.ContinuationStopReason ==
                ContinuationStopReason.AwaitingShadowVerification,
            "rejected evidence must discard preroll and restart disconnected intake");

        RecoveryResyncPlan replacement = Plan() with
        {
            PlanId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
            RelockSimTimeNs = 3_000_000_000,
            PrerollUntilSimTimeNs = 3_000_000_000,
        };
        ClientRecoverySessionState replaced = session.ReplaceRejectedPlan(
            replacement,
            currentSimTimeNs: 1_300_000_000,
            authorityMonotonicNs: 5);
        Check.That(replaced.Relock.Phase == RecoveryRelockPhase.Preparing &&
            replaced.Continuity.ConnectionState == ConnectionState.Relocking &&
            replaced.Continuity.DataAvailability == DataAvailability.NoData,
            "an explicit later plan may start a fresh provisional attempt");
    }

    private static void MissedCommitClearsPrerollBeforeReplacement()
    {
        ClientRecoverySession session = Prepared();
        ClientRecoverySessionState missed = session.Advance(
            currentSimTimeNs: 2_000_000_001,
            authorityMonotonicNs: 5);
        Check.That(missed.Relock.Phase == RecoveryRelockPhase.Rejected &&
            missed.Relock.RejectionReason ==
                RecoveryRelockRejectionReason.CommitDeadlineMissed &&
            missed.Continuity.ConnectionState == ConnectionState.Disconnected &&
            missed.Continuity.DataAvailability == DataAvailability.NoData &&
            !missed.Continuity.BufferedDataRemaining,
            "missing the exact commit boundary must discard prepared preroll");

        RecoveryResyncPlan later = Plan() with
        {
            PlanId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"),
            RelockSimTimeNs = 4_000_000_000,
            PrerollUntilSimTimeNs = 4_000_000_000,
        };
        ClientRecoverySessionState retry = session.ReplaceRejectedPlan(
            later,
            currentSimTimeNs: 2_200_000_000,
            authorityMonotonicNs: 6);
        Check.That(retry.Relock.Phase == RecoveryRelockPhase.Preparing &&
            retry.Continuity.ConnectionState == ConnectionState.Relocking &&
            retry.Continuity.DataAvailability == DataAvailability.NoData,
            "retry must prepare a new ID and later boundary from no data");
    }

    private static void CheckpointAndFailedTransitionCannotSplitTheMachines()
    {
        ClientRecoverySession session = Start();
        ClientRecoverySessionState before = session.CaptureState();
        Check.That(Reason(() => session.AcceptPlan(
                Plan(),
                currentSimTimeNs: 0,
                authorityMonotonicNs: 1)) == "DataContinuity.TimeReversed" &&
            session.CaptureState() == before,
            "a continuity-clock failure cannot retain the trial plan mutation");

        ClientRecoverySessionState preparing = session.AcceptPlan(Plan(), 0, 3);
        var restored = ClientRecoverySession.Restore(preparing);
        Check.That(restored.CaptureState() == preparing,
            "a valid paired checkpoint must restore without guessing");

        ClientRecoverySessionState split = preparing with
        {
            Continuity = before.Continuity,
        };
        Check.That(Reason(() => ClientRecoverySession.Restore(split)) ==
            "ClientRecovery.InvalidCheckpoint",
            "Preparing cannot restore beside a Disconnected continuity state");

        ClientRecoverySession prepared = Prepared();
        ClientRecoverySessionState beforeBadCommit = prepared.CaptureState();
        Check.That(Reason(() => prepared.Commit(
                Commit() with { HostStateSha256 = Hash('e') },
                authorityMonotonicNs: 5)) ==
                "RecoveryRelock.CommitMismatch" &&
            prepared.CaptureState() == beforeBadCommit,
            "a bad commit cannot promote continuity after protocol rejection");
    }

    private static ClientRecoverySession Prepared()
    {
        ClientRecoverySession session = Start();
        _ = session.AcceptPlan(Plan(), 0, 3);
        _ = session.RecordPrepared(Evidence(), 4);
        return session;
    }

    private static ClientRecoverySession Start()
    {
        var continuity =
            DataContinuityStateMachine.Start(
                LocalContinuationPolicy.DefaultDuration,
                startAuthorityMonotonicNs: 0);
        _ = continuity.Disconnect(hasBufferedData: false, 1);
        _ = continuity.RecordShadowVerification(matched: true, 2);
        return ClientRecoverySession.Start(
            continuity.CaptureState(),
            ClientId,
            SessionId,
            InstanceId,
            acceptedAuthorityEpoch: 4,
            acceptedStreamEpoch: 7,
            lastAppliedCommitSequence: 100,
            currentSimTimeNs: 0);
    }

    private static RecoveryResyncPlan Plan() => new(
        PlanId,
        NewAuthorityEpoch: 5,
        HostCheckpointSequence: 44,
        HostStateSha256: Hash('a'),
        RelockSimTimeNs: 2_000_000_000,
        NewStreamEpoch: 8,
        PrerollFromSimTimeNs: 0,
        PrerollUntilSimTimeNs: 2_000_000_000,
        LocalPredictionDecision: LocalPredictionDecision.CommitVerified,
        ReasonCode: "HOST_RECOVERY",
        AcceptedThroughCommitSequence: 101,
        ResumeSnapshotSha256: null);

    private static RecoveryPreparationEvidence Evidence() => new(
        PlanId,
        AuthorityEpoch: 5,
        StreamEpoch: 8,
        HostStateSha256: Hash('a'),
        PrerollFromSimTimeNs: 0,
        PrerollUntilSimTimeNs: 2_000_000_000,
        PrerollContentSha256: Hash('b'),
        ResumeSnapshotSha256: null,
        LocalPredictionMatchedHost: true,
        PreparedAtSimTimeNs: 1_200_000_000);

    private static RecoveryCommitCommand Commit() => new(
        PlanId,
        AuthorityEpoch: 5,
        StreamEpoch: 8,
        RelockSimTimeNs: 2_000_000_000,
        CommitSequence: 101,
        HostStateSha256: Hash('a'),
        FirstAuthoritativeBlockSha256: Hash('c'));

    private static string Hash(char character) => new(character, 64);

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (ClientRecoverySessionException exception)
        {
            return exception.ReasonCode;
        }
        catch (RecoveryRelockException exception)
        {
            return exception.ReasonCode;
        }
        catch (DataContinuityException exception)
        {
            return exception.ReasonCode;
        }
    }
}
