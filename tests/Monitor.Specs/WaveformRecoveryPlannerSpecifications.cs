// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class WaveformRecoveryPlannerSpecifications
{
    private static readonly Guid SessionId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid InstanceId =
        Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ChannelId =
        Guid.Parse("33333333-3333-4333-8333-333333333333");

    public static Specification[] All =>
    [
        new(nameof(RetainedGapProducesOneOrderedReplay),
            RetainedGapProducesOneOrderedReplay),
        new(nameof(EvictedGapUsesOneSecondFutureJoinBoundary),
            EvictedGapUsesOneSecondFutureJoinBoundary),
        new(nameof(YoungStreamWaitsUntilRequiredHistoryExists),
            YoungStreamWaitsUntilRequiredHistoryExists),
        new(nameof(InvalidIdentityAndUnrepresentableBoundariesFailClosed),
            InvalidIdentityAndUnrepresentableBoundariesFailClosed),
    ];

    private static void RetainedGapProducesOneOrderedReplay()
    {
        WaveformBlockRing ring = HostRing(10, 0);
        AppendRange(ring, 10, 14, 0);

        WaveformRecoveryPlan replay = WaveformRecoveryPlanner.Plan(
            ring,
            Request(
                11,
                requiredRenderHistoryNs: 10_000_000_000,
                availableRenderHistoryNs: 9_400_000_000));
        Check.That(replay.Mode == WaveformRecoveryMode.OrderedReplay &&
            replay.Reason == WaveformRecoveryReason.GapWithinRetention &&
            replay.JoinBoundary is null &&
            replay.ReplayBlocks.Select(block => block.BlockSequence).SequenceEqual(
                new ulong[] { 11, 12, 13 }),
            "a retained gap must return every available block in sequence order");

        WaveformRecoveryPlan current = WaveformRecoveryPlanner.Plan(
            ring,
            Request(
                14,
                requiredRenderHistoryNs: 10_000_000_000,
                availableRenderHistoryNs: 10_000_000_000));
        WaveformRecoveryPlan ahead = WaveformRecoveryPlanner.Plan(
            ring,
            Request(
                15,
                requiredRenderHistoryNs: 10_000_000_000,
                availableRenderHistoryNs: 10_000_000_000));
        Check.That(current.Mode == WaveformRecoveryMode.UpToDate &&
            current.ReplayBlocks.Count == 0 &&
            ahead.Mode == WaveformRecoveryMode.AwaitFutureBlock &&
            ahead.Reason == WaveformRecoveryReason.ClientCursorAhead,
            "the producer cursor and an impossible future cursor must remain distinct");
    }

    private static void EvictedGapUsesOneSecondFutureJoinBoundary()
    {
        WaveformBlockRing ring = HostRing(0, 0);
        AppendRange(ring, 0, 301, 0);

        WaveformRecoveryPlan plan = WaveformRecoveryPlanner.Plan(
            ring,
            Request(0, requiredRenderHistoryNs: 10_000_000_000));
        WaveformJoinBoundary boundary = plan.JoinBoundary!;
        Check.That(plan.Mode == WaveformRecoveryMode.SnapshotThenJoin &&
            plan.Reason == WaveformRecoveryReason.GapOutsideRetention &&
            plan.ReplayBlocks.Count == 0 &&
            boundary.BlockSequence == 306 &&
            boundary.SimTimeNs == 61_200_000_000 &&
            boundary.PrerollFromSimTimeNs == 51_200_000_000 &&
            boundary.RequiredRenderHistoryNs == 10_000_000_000 &&
            boundary.AvailableRenderHistoryNs == 60_000_000_000,
            "an evicted gap must select a block-aligned boundary at least one second ahead");
    }

    private static void YoungStreamWaitsUntilRequiredHistoryExists()
    {
        WaveformBlockRing empty = HostRing(40, 3_000_000_000);
        WaveformRecoveryRequest noHistory = Request(
            40,
            requiredRenderHistoryNs: 10_000_000_000,
            availableRenderHistoryNs: 0);
        WaveformRecoveryPlan plan = WaveformRecoveryPlanner.Plan(empty, noHistory);
        WaveformJoinBoundary boundary = plan.JoinBoundary!;
        Check.That(plan.Mode == WaveformRecoveryMode.SnapshotThenJoin &&
            plan.Reason == WaveformRecoveryReason.RenderHistoryIncomplete &&
            boundary.BlockSequence == 90 &&
            boundary.SimTimeNs == 13_000_000_000 &&
            boundary.PrerollFromSimTimeNs == 3_000_000_000 &&
            boundary.AvailableRenderHistoryNs == 10_000_000_000,
            "a young stream must not claim Prepared before complete render history exists");
    }

    private static void InvalidIdentityAndUnrepresentableBoundariesFailClosed()
    {
        WaveformBlockRing ring = HostRing(0, 0);
        WaveformRecoveryRequest wrongInstance = Request(0) with
        {
            InstanceId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
        };
        Check.That(Reason(() => WaveformRecoveryPlanner.Plan(ring, wrongInstance)) ==
            "WaveformRecovery.IdentityMismatch",
            "recovery cannot cross a patient instance identity");

        WaveformRecoveryRequest fractionalHistory = Request(0) with
        {
            RequiredRenderHistoryNs = 1,
            AvailableRenderHistoryNs = 1,
        };
        Check.That(Reason(() => WaveformRecoveryPlanner.Plan(ring, fractionalHistory)) ==
            "WaveformRecovery.InvalidRequest",
            "render history must use complete frozen 200 ms slots");

        var clientRing = WaveformBlockRing.Start(
            SessionId,
            InstanceId,
            2,
            4,
            0,
            0,
            WaveformBlockRing.ClientHistoryBlockCount);
        Check.That(Reason(() => WaveformRecoveryPlanner.Plan(clientRing, Request(0))) ==
            "WaveformRecovery.HostRingRequired",
            "a client history ring cannot promise Host recovery retention");

        WaveformRecoveryPlan changedEpoch = WaveformRecoveryPlanner.Plan(
            ring,
            Request(0, streamEpoch: 3));
        Check.That(changedEpoch.Mode == WaveformRecoveryMode.SnapshotThenJoin &&
            changedEpoch.Reason == WaveformRecoveryReason.EpochChanged,
            "history from another stream epoch must be replaced at a future boundary");

        WaveformBlockRing exhausted = HostRing(ulong.MaxValue - 4, 0);
        WaveformRecoveryRequest exhaustedEpoch = Request(ulong.MaxValue - 4) with
        {
            StreamEpoch = 3,
        };
        Check.That(Reason(() => WaveformRecoveryPlanner.Plan(exhausted, exhaustedEpoch)) ==
            "WaveformRecovery.StateOverflow",
            "an unrepresentable future join boundary must fail without wrapping sequence");
    }

    private static WaveformBlockRing HostRing(
        ulong firstBlockSequence,
        long firstBlockStartSimTimeNs) => WaveformBlockRing.Start(
            SessionId,
            InstanceId,
            2,
            4,
            firstBlockSequence,
            firstBlockStartSimTimeNs,
            WaveformBlockRing.HostRetentionBlockCount);

    private static WaveformRecoveryRequest Request(
        ulong nextBlockSequence,
        ulong timebaseEpoch = 2,
        ulong streamEpoch = 4,
        ulong requiredRenderHistoryNs = 0,
        ulong availableRenderHistoryNs = 0) => new(
            SessionId,
            InstanceId,
            timebaseEpoch,
            streamEpoch,
            nextBlockSequence,
            requiredRenderHistoryNs,
            availableRenderHistoryNs);

    private static void AppendRange(
        WaveformBlockRing ring,
        ulong firstSequence,
        ulong exclusiveSequence,
        long firstStartSimTimeNs)
    {
        for (ulong sequence = firstSequence; sequence < exclusiveSequence; sequence++)
        {
            long start = checked(firstStartSimTimeNs +
                (long)(sequence - firstSequence) * 200_000_000);
            ring.Append(Wire(sequence, start));
        }
    }

    private static byte[] Wire(ulong sequence, long startSimTimeNs) =>
        WaveformEnvelopeCodec.EncodeRaw(new WaveformEnvelope(
            SessionId,
            InstanceId,
            2,
            4,
            sequence,
            7,
            startSimTimeNs,
            WaveformBlockAssembler.BlockDurationNs,
            new[]
            {
                new WaveformPlane(
                    ChannelId,
                    5,
                    1,
                    sequence,
                    1,
                    1,
                    0,
                    1,
                    WaveformQualityEncoding.None,
                    new short[] { checked((short)sequence) },
                    Array.Empty<WaveformQualityRange>()),
            }));

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (WaveformRecoveryException exception)
        {
            return exception.ReasonCode;
        }
    }
}
