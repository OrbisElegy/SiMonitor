// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class WaveformSubscriberQueueSpecifications
{
    private static readonly Guid SessionId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid InstanceId =
        Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ChannelId =
        Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid SlowSubscriber =
        Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid FastSubscriber =
        Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    public static Specification[] All =>
    [
        new(nameof(SlowSubscriberIsolatedWithoutAffectingFastSubscriber),
            SlowSubscriberIsolatedWithoutAffectingFastSubscriber),
        new(nameof(DuplicateAndGapOffersHaveDistinctOutcomes),
            DuplicateAndGapOffersHaveDistinctOutcomes),
        new(nameof(EvictedPendingBlockRequiresSnapshotResync),
            EvictedPendingBlockRequiresSnapshotResync),
        new(nameof(QueueCheckpointAndExplicitResyncPreserveOrdering),
            QueueCheckpointAndExplicitResyncPreserveOrdering),
    ];

    private static void SlowSubscriberIsolatedWithoutAffectingFastSubscriber()
    {
        WaveformBlockRing ring = HostRing();
        var slow = WaveformSubscriberOutbox.Start(
            SlowSubscriber,
            ring,
            2,
            0);
        var fast = WaveformSubscriberOutbox.Start(
            FastSubscriber,
            ring,
            2,
            0);

        for (ulong sequence = 0; sequence < 3; sequence++)
        {
            ring.Append(Wire(sequence));
            WaveformSubscriberEnqueueResult slowResult = slow.Enqueue(sequence);
            Check.That(fast.Enqueue(sequence).Status ==
                WaveformSubscriberEnqueueStatus.Enqueued,
                "a healthy subscriber must accept each shared block reference");
            WaveformSubscriberReadResult sent = fast.ReadNext();
            Check.That(sent.Status == WaveformSubscriberReadStatus.Available &&
                sent.Block!.BlockSequence == sequence,
                "a healthy subscriber must drain without another endpoint's state");
            WaveformRetainedBlock sentBlock = sent.Block!;
            Check.That(fast.Acknowledge(
                    ring.StreamEpoch,
                    sequence,
                    sentBlock.ContentSha256).Status ==
                WaveformSubscriberAcknowledgeStatus.Acknowledged,
                "the exact application ACK must release the shared block reference");
            if (sequence == 2)
            {
                Check.That(slowResult.Status ==
                        WaveformSubscriberEnqueueStatus.RequiresResync &&
                    slow.IsolationReason ==
                        WaveformSubscriberIsolationReason.BackpressureLimitExceeded,
                    "the first offer beyond a slow endpoint's bound must isolate it");
            }
        }

        Check.That(slow.Status == WaveformSubscriberQueueStatus.IsolatedNeedsResync &&
            slow.Count == 0 &&
            fast.Status == WaveformSubscriberQueueStatus.Active &&
            ring.Count == 3 &&
            ring.NextBlockSequence == 3,
            "isolating one endpoint must not alter the shared ring or fast endpoint");
    }

    private static void DuplicateAndGapOffersHaveDistinctOutcomes()
    {
        WaveformBlockRing ring = HostRing();
        ring.Append(Wire(0));
        ring.Append(Wire(1));
        var queue = WaveformSubscriberOutbox.Start(
            SlowSubscriber,
            ring,
            4,
            0);
        Check.That(queue.Enqueue(0).Status == WaveformSubscriberEnqueueStatus.Enqueued &&
            queue.Enqueue(0).Status == WaveformSubscriberEnqueueStatus.AlreadyQueued,
            "an already queued shared block reference must be idempotent");
        WaveformSubscriberReadResult first = queue.ReadNext();
        Check.That(first.Status == WaveformSubscriberReadStatus.Available &&
            queue.Acknowledge(
                ring.StreamEpoch,
                0,
                first.Block!.ContentSha256).Status ==
                WaveformSubscriberAcknowledgeStatus.Acknowledged &&
            queue.Enqueue(0).Status == WaveformSubscriberEnqueueStatus.IgnoredStale,
            "an already sent reference must be ignored without re-enqueueing");

        var acknowledgementQueue = WaveformSubscriberOutbox.Start(
            FastSubscriber,
            ring,
            4,
            0);
        _ = acknowledgementQueue.Enqueue(0);
        _ = acknowledgementQueue.Enqueue(1);
        Check.That(acknowledgementQueue.ReadNext().Block!.BlockSequence == 0 &&
            acknowledgementQueue.ReadNext().Block!.BlockSequence == 0,
            "reading for transport must retain the block until application ACK");
        string blockOneHash = ring.ReadFrom(1, 1).Blocks[0].ContentSha256;
        Check.That(acknowledgementQueue.Acknowledge(
                ring.StreamEpoch,
                1,
                blockOneHash).Status ==
                WaveformSubscriberAcknowledgeStatus.RequiresResync &&
            acknowledgementQueue.IsolationReason ==
                WaveformSubscriberIsolationReason.AcknowledgementOutOfOrder,
            "an ACK cannot skip the oldest unacknowledged block");

        var hashQueue = WaveformSubscriberOutbox.Start(
            Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"),
            ring,
            4,
            0);
        _ = hashQueue.Enqueue(0);
        Check.That(hashQueue.Acknowledge(
                ring.StreamEpoch,
                0,
                new string('0', 64)).Status ==
                WaveformSubscriberAcknowledgeStatus.RequiresResync &&
            hashQueue.IsolationReason ==
                WaveformSubscriberIsolationReason.AcknowledgementHashMismatch,
            "an ACK with another content identity must require resynchronization");

        var epochQueue = WaveformSubscriberOutbox.Start(
            Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"),
            ring,
            4,
            0);
        _ = epochQueue.Enqueue(0);
        string blockZeroHash = ring.ReadFrom(0, 1).Blocks[0].ContentSha256;
        Check.That(epochQueue.Acknowledge(
                ring.StreamEpoch - 1,
                0,
                blockZeroHash).Status ==
                WaveformSubscriberAcknowledgeStatus.RequiresResync &&
            epochQueue.IsolationReason ==
                WaveformSubscriberIsolationReason.StreamChanged,
            "an ACK from another stream epoch must require resynchronization");

        Check.That(queue.Enqueue(2).Status ==
                WaveformSubscriberEnqueueStatus.RequiresResync &&
            queue.IsolationReason == WaveformSubscriberIsolationReason.SequenceGap &&
            queue.Count == 0,
            "skipping the exact next offer must clear backlog and require resync");
    }

    private static void EvictedPendingBlockRequiresSnapshotResync()
    {
        WaveformBlockRing ring = HostRing();
        ring.Append(Wire(0));
        var queue = WaveformSubscriberOutbox.Start(
            SlowSubscriber,
            ring,
            1,
            0);
        _ = queue.Enqueue(0);
        for (ulong sequence = 1; sequence <= 300; sequence++)
        {
            ring.Append(Wire(sequence));
        }

        WaveformSubscriberReadResult result = queue.ReadNext();
        Check.That(result.Status == WaveformSubscriberReadStatus.RequiresResync &&
            result.Block is null &&
            result.IsolationReason ==
                WaveformSubscriberIsolationReason.RetentionExpired &&
            queue.Count == 0,
            "a pending reference evicted from Host retention cannot be fabricated");
    }

    private static void QueueCheckpointAndExplicitResyncPreserveOrdering()
    {
        WaveformBlockRing ring = HostRing();
        for (ulong sequence = 0; sequence < 4; sequence++)
        {
            ring.Append(Wire(sequence));
        }

        var original = WaveformSubscriberOutbox.Start(
            SlowSubscriber,
            ring,
            3,
            0);
        _ = original.Enqueue(0);
        _ = original.Enqueue(1);
        WaveformSubscriberQueueState checkpoint = original.CaptureState();
        var restored = WaveformSubscriberOutbox.Restore(
            checkpoint,
            ring);
        WaveformSubscriberReadResult restoredFirst = restored.ReadNext();
        Check.That(restoredFirst.Block!.BlockSequence == 0 &&
            restored.Acknowledge(
                ring.StreamEpoch,
                0,
                restoredFirst.Block.ContentSha256).Status ==
                WaveformSubscriberAcknowledgeStatus.Acknowledged &&
            restored.ReadNext().Block!.BlockSequence == 1 &&
            restored.Acknowledge(
                ring.StreamEpoch,
                1,
                ring.ReadFrom(1, 1).Blocks[0].ContentSha256).Status ==
                WaveformSubscriberAcknowledgeStatus.Acknowledged,
            "restored queue slots must drain in the original order");

        WaveformSubscriberQueueState corrupt = checkpoint with
        {
            PendingBlockSequences = new ulong[] { 0, 2 },
        };
        Check.That(Reason(() => WaveformSubscriberOutbox.Restore(corrupt, ring)) ==
            "WaveformSubscriberQueue.InvalidCheckpoint",
            "checkpoint pending references must be exactly contiguous");

        _ = original.IsolateForStreamChange();
        var nextEpoch = WaveformBlockRing.Start(
            SessionId,
            InstanceId,
            3,
            5,
            20,
            4_000_000_000,
            WaveformBlockRing.HostRetentionBlockCount);
        WaveformSubscriberQueueState reset = original.ResetAfterResync(nextEpoch, 20);
        Check.That(reset.Status == WaveformSubscriberQueueStatus.Active &&
            reset.IsolationReason is null &&
            reset.TimebaseEpoch == 3 &&
            reset.StreamEpoch == 5 &&
            reset.NextEnqueueBlockSequence == 20 &&
            reset.PendingBlockSequences.Count == 0,
            "only explicit resync may bind an isolated subscriber to a new epoch");
    }

    private static WaveformBlockRing HostRing() => WaveformBlockRing.Start(
        SessionId,
        InstanceId,
        2,
        4,
        0,
        0,
        WaveformBlockRing.HostRetentionBlockCount);

    private static byte[] Wire(ulong sequence) =>
        WaveformEnvelopeCodec.EncodeRaw(new WaveformEnvelope(
            SessionId,
            InstanceId,
            2,
            4,
            sequence,
            7,
            checked((long)sequence * 200_000_000),
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
        catch (WaveformSubscriberQueueException exception)
        {
            return exception.ReasonCode;
        }
    }
}
