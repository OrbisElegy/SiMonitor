// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Continuity;
using Monitor.Domain.Continuity;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class RecoveryResyncPlanFactorySpecifications
{
    private static readonly Guid ClientId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SessionId =
        Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid InstanceId =
        Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid PlanId =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid ReportId =
        Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid EventA =
        Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid EventB =
        Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    public static Specification[] All =>
    [
        new(nameof(PredictionEvidenceDeterminesClientAcceptablePlan),
            PredictionEvidenceDeterminesClientAcceptablePlan),
        new(nameof(FullSnapshotOverridesVerifiedPrediction),
            FullSnapshotOverridesVerifiedPrediction),
        new(nameof(OnlyCompleteFutureSnapshotJoinCanBecomeAPlan),
            OnlyCompleteFutureSnapshotJoinCanBecomeAPlan),
        new(nameof(StaleHostFrontiersAndMalformedIdentityFailClosed),
            StaleHostFrontiersAndMalformedIdentityFailClosed),
    ];

    private static void PredictionEvidenceDeterminesClientAcceptablePlan()
    {
        var ring = HostRing();
        (RecoveryPlanningInput Input, LocalPredictionDecision Decision, ulong ThroughCommit)[] cases =
        [
            (VerifiedPlanningInput(), LocalPredictionDecision.CommitVerified, 101),
            (PlanningInput(localPredictionVerified: false), LocalPredictionDecision.Discard, 100),
        ];
        foreach (var item in cases)
        {
            RecoveryResyncPlan plan = RecoveryResyncPlanFactory.Create(
                item.Input,
                ring,
                Request(),
                Context());

            Check.That(plan.PlanId == PlanId &&
                plan.NewAuthorityEpoch == 5 &&
                plan.HostCheckpointSequence == 44 &&
                plan.HostStateSha256 == Hash('a') &&
                plan.RelockSimTimeNs == 11_000_000_000 &&
                plan.NewStreamEpoch == 8 &&
                plan.PrerollFromSimTimeNs == 11_000_000_000 &&
                plan.PrerollUntilSimTimeNs == 11_000_000_000 &&
                plan.LocalPredictionDecision ==
                    item.Decision &&
                plan.AcceptedThroughCommitSequence == item.ThroughCommit &&
                plan.ResumeSnapshotSha256 is null,
                "the Host plan binds verified/discarded prediction and the waveform boundary");

            var client = RecoveryRelockCoordinator.Start(
                ClientId,
                SessionId,
                InstanceId,
                acceptedAuthorityEpoch: 4,
                acceptedStreamEpoch: 7,
                lastAppliedCommitSequence: 100,
                currentSimTimeNs: 10_000_000_000);
            Check.That(client.AcceptPlan(plan, 10_000_000_000).Phase ==
                RecoveryRelockPhase.Preparing,
                "a constructed Host plan must pass the client relock gate unchanged");
        }
    }

    private static void FullSnapshotOverridesVerifiedPrediction()
    {
        RecoveryHostPlanContext snapshotContext = Context() with
        {
            RequireFullSnapshot = true,
            ResumeSnapshotSha256 = Hash('d'),
            ReasonCode = "FULL_SNAPSHOT_REQUIRED",
        };
        RecoveryResyncPlan plan = RecoveryResyncPlanFactory.Create(
            VerifiedPlanningInput(),
            HostRing(),
            Request(),
            snapshotContext);
        Check.That(plan.LocalPredictionDecision ==
                LocalPredictionDecision.FullSnapshotRequired &&
            plan.ResumeSnapshotSha256 == Hash('d'),
            "a required snapshot must override otherwise verified prediction");

        Check.That(Reason(() => RecoveryResyncPlanFactory.Create(
                VerifiedPlanningInput(),
                HostRing(),
                Request(),
                snapshotContext with { ResumeSnapshotSha256 = null })) ==
                "RecoveryPlan.InvalidHostContext",
            "a full snapshot plan must bind its exact canonical hash");
    }

    private static void OnlyCompleteFutureSnapshotJoinCanBecomeAPlan()
    {
        Check.That(Reason(() => RecoveryResyncPlanFactory.Create(
                PlanningInput(),
                HostRing(),
                Request(streamEpoch: 8),
                Context())) == "RecoveryPlan.InvalidWaveformContext",
            "ordinary same-epoch recovery cannot masquerade as a new relock plan");

        WaveformRecoveryRequest wrongIdentity = Request() with
        {
            InstanceId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"),
        };
        Check.That(Reason(() => RecoveryResyncPlanFactory.Create(
                PlanningInput(),
                HostRing(),
                wrongIdentity,
                Context())) == "RecoveryPlan.InvalidWaveformContext",
            "a recovery cursor for another patient instance cannot enter planning");

        Check.That(Reason(() => RecoveryResyncPlanFactory.Create(
                PlanningInput(),
                HostRing(),
                Request(),
                Context() with { CurrentSimTimeNs = 10_200_000_000 })) ==
                "RecoveryPlan.InvalidWaveformPlan",
            "a boundary must preserve the one-second preparation lead");
    }

    private static void StaleHostFrontiersAndMalformedIdentityFailClosed()
    {
        Check.That(Reason(() => RecoveryResyncPlanFactory.Create(
                VerifiedPlanningInput(),
                HostRing(),
                Request(),
                Context() with { HostDurableCommitSequence = 100 })) ==
                "RecoveryPlan.InvalidHostContext",
            "the Host durable frontier cannot trail accepted replay evidence");

        RecoveryPlanningInput staleAuthority = VerifiedPlanningInput() with
        {
            HostAuthorityEpoch = 4,
        };
        Check.That(Reason(() => RecoveryResyncPlanFactory.Create(
                staleAuthority,
                HostRing(),
                Request(),
                Context())) == "RecoveryPlan.InvalidPlanningInput",
            "planning input cannot erase the Host authority transition");

        RecoveryPlanningInput forgedVerification = PlanningInput() with
        {
            LocalPredictionVerified = true,
        };
        Check.That(Reason(() => RecoveryResyncPlanFactory.Create(
                forgedVerification,
                HostRing(),
                Request(),
                Context())) == "RecoveryPlan.InvalidPlanningInput",
            "verified local prediction must carry its Host-verified report");
    }

    private static RecoveryPlanningInput PlanningInput(
        bool localPredictionVerified = false)
    {
        if (localPredictionVerified)
        {
            return VerifiedPlanningInput();
        }

        RecoveryHandshakeCoordinator coordinator = Handshake();
        _ = coordinator.AcceptHello(Hello() with
        {
            BaseSha256 = null,
            LastDeltaSha256 = null,
            LocalContinuationReportId = null,
        });
        return coordinator.CapturePlanningInput();
    }

    private static RecoveryPlanningInput VerifiedPlanningInput()
    {
        RecoveryHandshakeCoordinator coordinator = Handshake();
        _ = coordinator.AcceptHello(Hello());
        _ = coordinator.AcceptReport(Report());
        _ = coordinator.RecordVerification(Verification());
        return coordinator.CapturePlanningInput();
    }

    private static RecoveryHandshakeCoordinator Handshake() =>
        RecoveryHandshakeCoordinator.Start(
            ClientId,
            SessionId,
            InstanceId,
            hostAuthorityEpoch: 5);

    private static RecoveryHelloMessage Hello() => new(
        ClientId,
        SessionId,
        InstanceId,
        ObservedAuthorityEpoch: 4,
        LastAppliedCommitSequence: 100,
        LastAppliedEventSequence: 90,
        BaseSha256: Hash('e'),
        LastDeltaSha256: Hash('f'),
        LocalContinuationReportId: ReportId,
        MomentaryInputsCleared: true);

    private static LocalContinuationReportMessage Report() => new(
        ReportId,
        SessionId,
        ClientId,
        InstanceId,
        BranchId: "shared.observer",
        BaseSha256: Hash('e'),
        LastDeltaSha256: Hash('f'),
        FallbackEpoch: 0,
        StartSimTimeNs: 10_000_000_000,
        EndSimTimeNs: 10_800_000_000,
        LastSampleIndexByChannel:
        [
            new LocalContinuationSampleFrontier("ECG.II", 2000),
            new LocalContinuationSampleFrontier("Pleth", 500),
        ],
        LastBlockSequence: 60,
        LastEventSequence: 95,
        RollingStateSha256: Hash('1'),
        OfflineActionChainSha256: Hash('2'),
        OfflineActionEventIds: new[] { EventA, EventB },
        IntegrityState: ContinuityReportIntegrityState.PendingVerification);

    private static ContinuityReportVerification Verification() => new(
        ReportId,
        BaseSha256: Hash('e'),
        LastDeltaSha256: Hash('f'),
        ReplayedRollingStateSha256: Hash('1'),
        ReplayedOfflineActionChainSha256: Hash('2'),
        AcceptedThroughCommitSequence: 101,
        Result: ContinuityReportVerificationResult.Verified,
        ReasonCode: null);

    private static RecoveryHostPlanContext Context() => new(
        PlanId,
        HostCheckpointSequence: 44,
        HostDurableCommitSequence: 101,
        HostStateSha256: Hash('a'),
        CurrentSimTimeNs: 10_000_000_000,
        RequireFullSnapshot: false,
        ResumeSnapshotSha256: null,
        ReasonCode: "HOST_RECOVERY");

    private static WaveformBlockRing HostRing() => WaveformBlockRing.Start(
        SessionId,
        InstanceId,
        timebaseEpoch: 2,
        streamEpoch: 8,
        firstBlockSequence: 100,
        firstBlockStartSimTimeNs: 10_000_000_000,
        WaveformBlockRing.HostRetentionBlockCount);

    private static WaveformRecoveryRequest Request(
        ulong streamEpoch = 7) => new(
            SessionId,
            InstanceId,
            TimebaseEpoch: 2,
            StreamEpoch: streamEpoch,
            NextBlockSequence: 100,
            RequiredRenderHistoryNs: 0,
            AvailableRenderHistoryNs: 0);

    private static string Hash(char character) => new(character, 64);

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (RecoveryResyncPlanFactoryException exception)
        {
            return exception.ReasonCode;
        }
    }
}
