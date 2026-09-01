// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;

namespace Monitor.Specs;

internal static class RecoveryHandshakeSpecifications
{
    private static readonly Guid ClientId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SessionId =
        Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid InstanceId =
        Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid ReportId =
        Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid EventA =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid EventB =
        Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    public static Specification[] All =>
    [
        new(nameof(HelloRequiresHigherHostEpochAndClearedMomentaryInputs),
            HelloRequiresHigherHostEpochAndClearedMomentaryInputs),
        new(nameof(ContinuationReportIsCanonicalAndNeverSelfVerifying),
            ContinuationReportIsCanonicalAndNeverSelfVerifying),
        new(nameof(HostReplayVerificationProducesBoundPlanningInput),
            HostReplayVerificationProducesBoundPlanningInput),
        new(nameof(RejectedOrMismatchedReplayCannotProduceAPlan),
            RejectedOrMismatchedReplayCannotProduceAPlan),
        new(nameof(HandshakeCheckpointAndInputCopiesFailClosed),
            HandshakeCheckpointAndInputCopiesFailClosed),
    ];

    private static void HelloRequiresHigherHostEpochAndClearedMomentaryInputs()
    {
        RecoveryHandshakeCoordinator direct = Start();
        RecoveryHelloMessage helloWithoutReport = Hello() with
        {
            LocalContinuationReportId = null,
        };
        RecoveryHandshakeState ready = direct.AcceptHello(helloWithoutReport);
        RecoveryPlanningInput input = direct.CapturePlanningInput();
        Check.That(ready.Phase == RecoveryHandshakePhase.ReadyForPlan &&
            input.HostAuthorityEpoch == 5 &&
            input.ObservedAuthorityEpoch == 4 &&
            input.AcceptedThroughCommitSequence == 100 &&
            input.LocalContinuationReport is null &&
            !input.LocalPredictionVerified,
            "a client without local prediction may proceed directly to Host planning");

        RecoveryHandshakeCoordinator uncleared = Start();
        Check.That(Reason(() => uncleared.AcceptHello(Hello() with
        {
            MomentaryInputsCleared = false,
        })) == "RecoveryHandshake.InvalidHello" &&
            uncleared.Phase == RecoveryHandshakePhase.AwaitingHello,
            "momentary therapy inputs must be cleared before recovery intake");

        RecoveryHandshakeCoordinator staleHost = Start();
        Check.That(Reason(() => staleHost.AcceptHello(Hello() with
        {
            ObservedAuthorityEpoch = 5,
        })) == "RecoveryHandshake.InvalidHello" &&
            staleHost.Phase == RecoveryHandshakePhase.AwaitingHello,
            "a Host epoch that does not advance cannot recover the client");
    }

    private static void ContinuationReportIsCanonicalAndNeverSelfVerifying()
    {
        RecoveryHandshakeCoordinator coordinator = AwaitingReport();
        LocalContinuationReportMessage report = Report() with
        {
            IntegrityState = ContinuityReportIntegrityState.Verified,
        };
        RecoveryHandshakeState verifying = coordinator.AcceptReport(report);
        Check.That(verifying.Phase == RecoveryHandshakePhase.VerifyingReport &&
            Reason(() => coordinator.CapturePlanningInput()) ==
                "RecoveryHandshake.InvalidTransition",
            "a client-declared Verified report still requires independent Host replay");

        RecoveryHandshakeCoordinator unsortedChannels = AwaitingReport();
        LocalContinuationReportMessage channelReport = Report() with
        {
            LastSampleIndexByChannel =
            [
                new LocalContinuationSampleFrontier("Pleth", 500),
                new LocalContinuationSampleFrontier("ECG.II", 2000),
            ],
        };
        Check.That(Reason(() => unsortedChannels.AcceptReport(channelReport)) ==
                "RecoveryHandshake.InvalidReport" &&
            unsortedChannels.Phase == RecoveryHandshakePhase.AwaitingReport,
            "sample frontiers must use canonical unique channel order");

        RecoveryHandshakeCoordinator unsortedActions = AwaitingReport();
        LocalContinuationReportMessage actionReport = Report() with
        {
            OfflineActionEventIds = new[] { EventB, EventA },
        };
        Check.That(Reason(() => unsortedActions.AcceptReport(actionReport)) ==
            "RecoveryHandshake.InvalidReport",
            "offline hash-chain event IDs must use canonical unique order");
    }

    private static void HostReplayVerificationProducesBoundPlanningInput()
    {
        RecoveryHandshakeCoordinator coordinator = VerifyingReport();
        ContinuityReportVerification verification = Verification();
        RecoveryHandshakeState ready = coordinator.RecordVerification(verification);
        RecoveryPlanningInput input = coordinator.CapturePlanningInput();
        Check.That(ready.Phase == RecoveryHandshakePhase.ReadyForPlan &&
            input.ClientId == ClientId &&
            input.SessionId == SessionId &&
            input.InstanceId == InstanceId &&
            input.LocalContinuationReport!.ReportId == ReportId &&
            input.AcceptedThroughCommitSequence == 101 &&
            input.LocalPredictionVerified,
            "only matching Host replay may expose local prediction to plan construction");

        RecoveryHandshakeState retried = coordinator.RecordVerification(verification);
        Check.That(retried.Phase == RecoveryHandshakePhase.ReadyForPlan &&
            retried.Verification == verification &&
            retried.RejectionReason is null,
            "an exact verification retry must be idempotent after readiness");
    }

    private static void RejectedOrMismatchedReplayCannotProduceAPlan()
    {
        RecoveryHandshakeCoordinator mismatch = VerifyingReport();
        RecoveryHandshakeState mismatched = mismatch.RecordVerification(
            Verification() with
            {
                ReplayedRollingStateSha256 = Hash('e'),
            });
        Check.That(mismatched.Phase == RecoveryHandshakePhase.Rejected &&
            mismatched.RejectionReason ==
                RecoveryHandshakeRejectionReason.HostReplayMismatch &&
            Reason(() => mismatch.CapturePlanningInput()) ==
                "RecoveryHandshake.InvalidTransition",
            "a replayed state mismatch cannot feed ResyncPlan construction");

        RecoveryHandshakeCoordinator rejected = VerifyingReport();
        RecoveryHandshakeState hostRejected = rejected.RecordVerification(
            Verification() with
            {
                Result = ContinuityReportVerificationResult.Rejected,
                ReasonCode = "OFFLINE_ACTION_REJECTED",
            });
        Check.That(hostRejected.RejectionReason ==
            RecoveryHandshakeRejectionReason.HostReplayRejected,
            "an explicit Host replay rejection must remain distinguishable");

        RecoveryHandshakeCoordinator clientRejected = AwaitingReport();
        RecoveryHandshakeState localRejected = clientRejected.AcceptReport(
            Report() with
            {
                IntegrityState = ContinuityReportIntegrityState.Rejected,
            });
        Check.That(localRejected.RejectionReason ==
            RecoveryHandshakeRejectionReason.ClientReportRejected,
            "a locally rejected report must never be replayed as trusted state");
    }

    private static void HandshakeCheckpointAndInputCopiesFailClosed()
    {
        RecoveryHandshakeCoordinator coordinator = AwaitingReport();
        List<LocalContinuationSampleFrontier> mutableFrontiers =
        [
            new("ECG.II", 2000),
            new("Pleth", 500),
        ];
        List<Guid> mutableEvents = [EventA, EventB];
        LocalContinuationReportMessage report = Report() with
        {
            LastSampleIndexByChannel = mutableFrontiers,
            OfflineActionEventIds = mutableEvents,
        };
        RecoveryHandshakeState verifying = coordinator.AcceptReport(report);
        mutableFrontiers.Clear();
        mutableEvents.Clear();
        Check.That(coordinator.CaptureState().Report!.LastSampleIndexByChannel.Count == 2 &&
            coordinator.CaptureState().Report!.OfflineActionEventIds.Count == 2 &&
            coordinator.AcceptReport(Report()).Phase ==
                RecoveryHandshakePhase.VerifyingReport,
            "accepted report collections must be copied and exact retries idempotent");

        var restored = RecoveryHandshakeCoordinator.Restore(
            verifying);
        Check.That(restored.Phase == RecoveryHandshakePhase.VerifyingReport &&
            restored.RecordVerification(Verification()).Phase ==
                RecoveryHandshakePhase.ReadyForPlan,
            "a verifying checkpoint must preserve its exact future decision");

        RecoveryHandshakeState corrupt = verifying with
        {
            HostAuthorityEpoch = 4,
        };
        Check.That(Reason(() => RecoveryHandshakeCoordinator.Restore(corrupt)) ==
            "RecoveryHandshake.InvalidCheckpoint",
            "a checkpoint cannot make the Host epoch stale relative to the client");
    }

    private static RecoveryHandshakeCoordinator Start() =>
        RecoveryHandshakeCoordinator.Start(
            ClientId,
            SessionId,
            InstanceId,
            hostAuthorityEpoch: 5);

    private static RecoveryHandshakeCoordinator AwaitingReport()
    {
        RecoveryHandshakeCoordinator coordinator = Start();
        _ = coordinator.AcceptHello(Hello());
        return coordinator;
    }

    private static RecoveryHandshakeCoordinator VerifyingReport()
    {
        RecoveryHandshakeCoordinator coordinator = AwaitingReport();
        _ = coordinator.AcceptReport(Report());
        return coordinator;
    }

    private static RecoveryHelloMessage Hello() => new(
        ClientId,
        SessionId,
        InstanceId,
        ObservedAuthorityEpoch: 4,
        LastAppliedCommitSequence: 100,
        LastAppliedEventSequence: 200,
        BaseSha256: Hash('a'),
        LastDeltaSha256: Hash('b'),
        LocalContinuationReportId: ReportId,
        MomentaryInputsCleared: true);

    private static LocalContinuationReportMessage Report() => new(
        ReportId,
        SessionId,
        ClientId,
        InstanceId,
        BranchId: "shared.observer",
        BaseSha256: Hash('a'),
        LastDeltaSha256: Hash('b'),
        FallbackEpoch: 0,
        StartSimTimeNs: 10_000_000_000,
        EndSimTimeNs: 12_000_000_000,
        LastSampleIndexByChannel:
        [
            new LocalContinuationSampleFrontier("ECG.II", 2000),
            new LocalContinuationSampleFrontier("Pleth", 500),
        ],
        LastBlockSequence: 60,
        LastEventSequence: 205,
        RollingStateSha256: Hash('c'),
        OfflineActionChainSha256: Hash('d'),
        OfflineActionEventIds: new[] { EventA, EventB },
        IntegrityState: ContinuityReportIntegrityState.PendingVerification);

    private static ContinuityReportVerification Verification() => new(
        ReportId,
        BaseSha256: Hash('a'),
        LastDeltaSha256: Hash('b'),
        ReplayedRollingStateSha256: Hash('c'),
        ReplayedOfflineActionChainSha256: Hash('d'),
        AcceptedThroughCommitSequence: 101,
        Result: ContinuityReportVerificationResult.Verified,
        ReasonCode: null);

    private static string Hash(char character) => new(character, 64);

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (RecoveryHandshakeException exception)
        {
            return exception.ReasonCode;
        }
    }
}
