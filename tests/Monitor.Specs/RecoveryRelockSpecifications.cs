// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;

namespace Monitor.Specs;

internal static class RecoveryRelockSpecifications
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
        new(nameof(PlanRequiresHigherEpochAndFutureAlignedBoundary),
            PlanRequiresHigherEpochAndFutureAlignedBoundary),
        new(nameof(PreparedAckBindsEveryVerifiedIdentity),
            PreparedAckBindsEveryVerifiedIdentity),
        new(nameof(PredictionDecisionControlsPreparationEvidence),
            PredictionDecisionControlsPreparationEvidence),
        new(nameof(CommitOccursOnlyAtThePlannedBoundary),
            CommitOccursOnlyAtThePlannedBoundary),
        new(nameof(RejectedPlanReplacementAndCheckpointFailClosed),
            RejectedPlanReplacementAndCheckpointFailClosed),
    ];

    private static void PlanRequiresHigherEpochAndFutureAlignedBoundary()
    {
        RecoveryRelockCoordinator coordinator = Start();
        RecoveryResyncPlan plan = Plan();
        RecoveryRelockState accepted = coordinator.AcceptPlan(plan, 0);
        Check.That(accepted.Phase == RecoveryRelockPhase.Preparing &&
            accepted.Plan == plan &&
            accepted.PlanAcceptedAtSimTimeNs == 0 &&
            coordinator.AcceptPlan(plan, 0) == accepted,
            "an exact repeated ResyncPlan must be idempotent while preparing");

        RecoveryRelockCoordinator lateDuplicate = Start();
        _ = lateDuplicate.AcceptPlan(plan, 0);
        Check.That(lateDuplicate.AcceptPlan(plan, 2_000_000_000)
                .RejectionReason ==
                RecoveryRelockRejectionReason.PreparationDeadlineMissed,
            "an idempotent plan repeat cannot bypass preparation expiry");

        RecoveryRelockCoordinator stale = Start();
        RecoveryResyncPlan oldAuthority = Plan() with
        {
            NewAuthorityEpoch = 4,
        };
        Check.That(Reason(() => stale.AcceptPlan(oldAuthority, 0)) ==
                "RecoveryRelock.InvalidPlan" &&
            stale.Phase == RecoveryRelockPhase.AwaitingPlan,
            "an old Host authority epoch cannot enter preparation");

        RecoveryRelockCoordinator shortLead = Start();
        RecoveryResyncPlan tooSoon = Plan() with
        {
            RelockSimTimeNs = 800_000_000,
            PrerollUntilSimTimeNs = 800_000_000,
        };
        Check.That(Reason(() => shortLead.AcceptPlan(tooSoon, 0)) ==
            "RecoveryRelock.InvalidPlan",
            "relock must retain the frozen one-second preparation lead");

        RecoveryRelockCoordinator unaligned = Start();
        RecoveryResyncPlan fractional = Plan() with
        {
            RelockSimTimeNs = 2_000_000_001,
            PrerollUntilSimTimeNs = 2_000_000_001,
        };
        Check.That(Reason(() => unaligned.AcceptPlan(fractional, 0)) ==
            "RecoveryRelock.InvalidPlan",
            "relock and preroll boundaries must use complete 200 ms slots");
    }

    private static void PreparedAckBindsEveryVerifiedIdentity()
    {
        RecoveryRelockCoordinator coordinator = Preparing();
        RecoveryPreparationEvidence evidence = Evidence();
        RecoveryRelockState prepared = coordinator.RecordPrepared(evidence);
        RecoveryPreparedAcknowledgement acknowledgement =
            coordinator.CapturePreparedAcknowledgement();
        Check.That(prepared.Phase == RecoveryRelockPhase.Prepared &&
            acknowledgement.PlanId == PlanId &&
            acknowledgement.AuthorityEpoch == 5 &&
            acknowledgement.StreamEpoch == 8 &&
            acknowledgement.RelockSimTimeNs == 2_000_000_000 &&
            acknowledgement.HostStateSha256 == Hash('a') &&
            acknowledgement.PrerollContentSha256 == Hash('b') &&
            acknowledgement.PreparedAtSimTimeNs == 1_200_000_000,
            "PreparedAck must bind the plan, epochs, state, preroll and deadline");

        RecoveryRelockCoordinator mismatch = Preparing();
        RecoveryRelockState rejected = mismatch.RecordPrepared(evidence with
        {
            StreamEpoch = 9,
        });
        Check.That(rejected.Phase == RecoveryRelockPhase.Rejected &&
            rejected.RejectionReason ==
                RecoveryRelockRejectionReason.PreparationIdentityMismatch &&
            Reason(() => mismatch.CapturePreparedAcknowledgement()) ==
                "RecoveryRelock.InvalidTransition",
            "a mismatched preparation identity cannot emit PreparedAck");
    }

    private static void PredictionDecisionControlsPreparationEvidence()
    {
        RecoveryRelockCoordinator prediction = Preparing();
        RecoveryRelockState predictionMismatch = prediction.RecordPrepared(
            Evidence() with
            {
                LocalPredictionMatchedHost = false,
            });
        Check.That(predictionMismatch.RejectionReason ==
            RecoveryRelockRejectionReason.LocalPredictionMismatch,
            "CommitVerified must prove local prediction matches Host replay");

        RecoveryResyncPlan discardPlan = Plan() with
        {
            PlanId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
            LocalPredictionDecision = LocalPredictionDecision.Discard,
            ReasonCode = "LOCAL_PREDICTION_DISCARDED",
        };
        RecoveryRelockCoordinator discard = Start();
        _ = discard.AcceptPlan(discardPlan, 0);
        RecoveryRelockState discardPrepared = discard.RecordPrepared(
            Evidence() with
            {
                PlanId = discardPlan.PlanId,
                LocalPredictionMatchedHost = false,
            });
        Check.That(discardPrepared.Phase == RecoveryRelockPhase.Prepared,
            "Discard may prepare verified Host preroll without trusting prediction");

        RecoveryResyncPlan snapshotPlan = Plan() with
        {
            PlanId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"),
            LocalPredictionDecision = LocalPredictionDecision.FullSnapshotRequired,
            ResumeSnapshotSha256 = Hash('d'),
            ReasonCode = "FULL_SNAPSHOT_REQUIRED",
        };
        RecoveryRelockCoordinator snapshot = Start();
        _ = snapshot.AcceptPlan(snapshotPlan, 0);
        RecoveryRelockState snapshotRejected = snapshot.RecordPrepared(
            Evidence() with
            {
                PlanId = snapshotPlan.PlanId,
                ResumeSnapshotSha256 = Hash('e'),
            });
        Check.That(snapshotRejected.RejectionReason ==
            RecoveryRelockRejectionReason.SnapshotMismatch,
            "FullSnapshotRequired must bind the exact resume snapshot hash");
    }

    private static void CommitOccursOnlyAtThePlannedBoundary()
    {
        RecoveryRelockCoordinator coordinator = Prepared();
        RecoveryCommitCommand command = Commit();
        RecoveryRelockState beforeMismatch = coordinator.CaptureState();
        Check.That(Reason(() => coordinator.Commit(command with
        {
            AuthorityEpoch = 4,
        })) == "RecoveryRelock.CommitMismatch" &&
            coordinator.CaptureState() == beforeMismatch,
            "an old authority commit must reject without changing preparation");

        RecoveryRelockState committed = coordinator.Commit(command);
        Check.That(committed.Phase == RecoveryRelockPhase.Committed &&
            coordinator.AcceptedAuthorityEpoch == 5 &&
            coordinator.AcceptedStreamEpoch == 8 &&
            coordinator.LastAppliedCommitSequence == 101 &&
            committed.LastObservedSimTimeNs == 2_000_000_000,
            "the exact RecoveryCommit must atomically advance every frontier");

        RecoveryRelockCoordinator missedCommit = Prepared();
        Check.That(missedCommit.Advance(2_000_000_000).Phase ==
                RecoveryRelockPhase.Prepared &&
            missedCommit.Advance(2_000_000_001).RejectionReason ==
                RecoveryRelockRejectionReason.CommitDeadlineMissed,
            "Prepared may wait at the boundary but cannot commit after it");

        RecoveryRelockCoordinator missedPreparation = Preparing();
        Check.That(missedPreparation.Advance(2_000_000_000).RejectionReason ==
            RecoveryRelockRejectionReason.PreparationDeadlineMissed,
            "a client not Prepared before the boundary must require a new plan");
    }

    private static void RejectedPlanReplacementAndCheckpointFailClosed()
    {
        RecoveryRelockCoordinator coordinator = Preparing();
        _ = coordinator.RecordPrepared(Evidence() with
        {
            PrerollUntilSimTimeNs = 1_800_000_000,
        });
        RecoveryResyncPlan replacement = Plan() with
        {
            PlanId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"),
            RelockSimTimeNs = 3_000_000_000,
            PrerollUntilSimTimeNs = 3_000_000_000,
            ReasonCode = "EXPLICIT_LATER_BOUNDARY",
        };
        RecoveryResyncPlan reusedIdentity = Plan() with
        {
            RelockSimTimeNs = 3_000_000_000,
            PrerollUntilSimTimeNs = 3_000_000_000,
        };
        Check.That(Reason(() => coordinator.ReplaceRejectedPlan(
                reusedIdentity,
                1_200_000_000)) == "RecoveryRelock.ConflictingPlan",
            "a replacement cannot reuse a rejected plan identity");
        RecoveryRelockState replaced = coordinator.ReplaceRejectedPlan(
            replacement,
            1_200_000_000);
        var restored = RecoveryRelockCoordinator.Restore(
            replaced);
        Check.That(replaced.Phase == RecoveryRelockPhase.Preparing &&
            restored.CaptureState() == replaced &&
            restored.Advance(2_999_999_999).Phase ==
                RecoveryRelockPhase.Preparing,
            "an explicit later plan and its exact deadline must survive checkpointing");

        RecoveryRelockState corrupt = replaced with
        {
            AuthorityEpochBeforePlan = replacement.NewAuthorityEpoch,
        };
        Check.That(Reason(() => RecoveryRelockCoordinator.Restore(corrupt)) ==
            "RecoveryRelock.InvalidCheckpoint",
            "a checkpoint cannot erase proof that the new authority epoch advanced");

        RecoveryRelockCoordinator prepared = Prepared();
        RecoveryRelockState impossiblePreparation = prepared.CaptureState() with
        {
            PlanAcceptedAtSimTimeNs = 1_000_000_000,
            PreparationEvidence = prepared.CaptureState().PreparationEvidence! with
            {
                PreparedAtSimTimeNs = 900_000_000,
            },
        };
        Check.That(Reason(() => RecoveryRelockCoordinator.Restore(
                impossiblePreparation)) == "RecoveryRelock.InvalidCheckpoint",
            "Prepared evidence cannot predate acceptance of its plan");
        RecoveryRelockState futurePreparation = prepared.CaptureState() with
        {
            LastObservedSimTimeNs = 1_100_000_000,
        };
        Check.That(Reason(() => RecoveryRelockCoordinator.Restore(
                futurePreparation)) == "RecoveryRelock.InvalidCheckpoint",
            "Prepared evidence cannot be newer than the restored playhead");

        RecoveryRelockCoordinator rejectedClient = Start();
        RecoveryRelockState hostRejected = rejectedClient.AcceptPlan(Plan() with
        {
            PlanId = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"),
            LocalPredictionDecision = LocalPredictionDecision.RejectClient,
            ReasonCode = "CLIENT_INCOMPATIBLE",
        }, 0);
        Check.That(hostRejected.Phase == RecoveryRelockPhase.Rejected &&
            hostRejected.RejectionReason ==
                RecoveryRelockRejectionReason.ClientRejectedByHost,
            "RejectClient must never enter preparation or emit an acknowledgement");
    }

    private static RecoveryRelockCoordinator Start() =>
        RecoveryRelockCoordinator.Start(
            ClientId,
            SessionId,
            InstanceId,
            acceptedAuthorityEpoch: 4,
            acceptedStreamEpoch: 7,
            lastAppliedCommitSequence: 100,
            currentSimTimeNs: 0);

    private static RecoveryRelockCoordinator Preparing()
    {
        RecoveryRelockCoordinator coordinator = Start();
        _ = coordinator.AcceptPlan(Plan(), 0);
        return coordinator;
    }

    private static RecoveryRelockCoordinator Prepared()
    {
        RecoveryRelockCoordinator coordinator = Preparing();
        _ = coordinator.RecordPrepared(Evidence());
        return coordinator;
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
        LocalPredictionDecision.CommitVerified,
        ReasonCode: "HOST_RECOVERY",
        AcceptedThroughCommitSequence: 100,
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
        catch (RecoveryRelockException exception)
        {
            return exception.ReasonCode;
        }
    }
}
