// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class WaveformBlockRingSpecifications
{
    private static readonly Guid SessionId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid InstanceId =
        Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ChannelId =
        Guid.Parse("33333333-3333-4333-8333-333333333333");

    public static Specification[] All =>
    [
        new(nameof(HostRingRetainsExactlySixtySeconds),
            HostRingRetainsExactlySixtySeconds),
        new(nameof(AppendRejectsGapsAndConflictingDuplicatesAtomically),
            AppendRejectsGapsAndConflictingDuplicatesAtomically),
        new(nameof(ReplayReportsWindowBoundariesAndReturnsCopies),
            ReplayReportsWindowBoundariesAndReturnsCopies),
        new(nameof(RingCheckpointRestoresRetentionAndFutureAppend),
            RingCheckpointRestoresRetentionAndFutureAppend),
    ];

    private static void HostRingRetainsExactlySixtySeconds()
    {
        WaveformBlockRing ring = CreateRing(
            WaveformBlockRing.HostRetentionBlockCount,
            0,
            0);
        ulong? finalEviction = null;
        for (ulong sequence = 0; sequence < 302; sequence++)
        {
            WaveformBlockAppendResult result = ring.Append(Wire(
                sequence,
                checked((long)sequence) * 200_000_000));
            finalEviction = result.EvictedBlockSequence;
        }

        Check.That(WaveformBlockRing.ClientHistoryBlockCount == 50 &&
            WaveformBlockRing.HostRetentionBlockCount == 300 &&
            ring.Count == 300 &&
            ring.OldestBlockSequence == 2 &&
            ring.NextBlockSequence == 302 &&
            finalEviction == 1,
            "the fixed client and host capacities must equal 10 and 60 seconds");
        WaveformBlockReplayResult retained = ring.ReadFrom(2, 300);
        Check.That(retained.Status == WaveformBlockReplayStatus.Available &&
            retained.Blocks.Count == 300 &&
            retained.Blocks[0].BlockSequence == 2 &&
            retained.Blocks[^1].BlockSequence == 301,
            "the host ring must retain exactly the newest 300 ordered blocks");

        WaveformBlockRing client = CreateRing(
            WaveformBlockRing.ClientHistoryBlockCount,
            0,
            0);
        for (ulong sequence = 0; sequence < 51; sequence++)
        {
            client.Append(Wire(
                sequence,
                checked((long)sequence) * 200_000_000));
        }

        Check.That(client.Count == 50 && client.OldestBlockSequence == 1,
            "the client ring must retain exactly the newest 50 ordered blocks");
    }

    private static void AppendRejectsGapsAndConflictingDuplicatesAtomically()
    {
        WaveformBlockRing ring = CreateRing(
            WaveformBlockRing.ClientHistoryBlockCount,
            10,
            0);
        byte[] first = Wire(10, sample: 1);
        byte[] duplicate = first.ToArray();
        WaveformBlockAppendStatus appended = ring.Append(first).Status;
        first[0] ^= 0xff;
        Check.That(appended == WaveformBlockAppendStatus.Appended &&
            ring.Append(duplicate).Status == WaveformBlockAppendStatus.AlreadyPresent &&
            ring.ReadFrom(10, 1).Blocks[0].RawEnvelope[0] == duplicate[0],
            "an identical epoch, sequence and content hash must be idempotent and immutable");
        WaveformBlockRingState afterFirst = ring.CaptureState();

        Check.That(Reason(() => ring.Append(Wire(10, sample: 2))) ==
                "WaveformBlockRing.ConflictingDuplicate" &&
            Equivalent(ring.CaptureState(), afterFirst),
            "a repeated sequence with another content identity must fail atomically");
        Check.That(Reason(() => ring.Append(Wire(12, startSimTimeNs: 400_000_000))) ==
                "WaveformBlockRing.SequenceGap" &&
            Equivalent(ring.CaptureState(), afterFirst),
            "a missing block cannot be concealed by appending a later sequence");
        Check.That(Reason(() => ring.Append(Wire(11, startSimTimeNs: 1))) ==
                "WaveformBlockRing.StartTimeDiscontinuous" &&
            Equivalent(ring.CaptureState(), afterFirst),
            "sequence continuity cannot conceal a discontinuous source-time grid");
        Check.That(Reason(() => ring.Append(Wire(11, streamEpoch: 8))) ==
                "WaveformBlockRing.IdentityMismatch" &&
            Equivalent(ring.CaptureState(), afterFirst),
            "a different stream epoch must use another ring identity");
    }

    private static void ReplayReportsWindowBoundariesAndReturnsCopies()
    {
        WaveformBlockRing ring = CreateRing(
            WaveformBlockRing.ClientHistoryBlockCount,
            5,
            1_000_000_000);
        for (ulong sequence = 5; sequence < 56; sequence++)
        {
            ring.Append(Wire(
                sequence,
                1_000_000_000 + checked((long)(sequence - 5)) * 200_000_000));
        }

        Check.That(ring.ReadFrom(5, 3).Status ==
                WaveformBlockReplayStatus.RecoverySnapshotRequired,
            "a gap older than the retained window must select snapshot recovery");
        Check.That(ring.ReadFrom(57, 3).Status ==
                WaveformBlockReplayStatus.AwaitFutureBlock,
            "a request beyond the producer cursor must wait for future data");
        Check.That(ring.ReadFrom(56, 3).Status == WaveformBlockReplayStatus.Available &&
            ring.ReadFrom(56, 3).Blocks.Count == 0,
            "the exact producer cursor must be an available empty replay");

        WaveformBlockReplayResult firstRead = ring.ReadFrom(54, 2);
        byte original = firstRead.Blocks[0].RawEnvelope[0];
        firstRead.Blocks[0].RawEnvelope[0] ^= 0xff;
        WaveformBlockReplayResult secondRead = ring.ReadFrom(54, 2);
        Check.That(secondRead.Blocks.Select(block => block.BlockSequence).SequenceEqual(
                new ulong[] { 54, 55 }) &&
            secondRead.Blocks[0].RawEnvelope[0] == original,
            "ordered replay snapshots must not expose mutable retained storage");
    }

    private static void RingCheckpointRestoresRetentionAndFutureAppend()
    {
        WaveformBlockRing original = CreateRing(
            WaveformBlockRing.ClientHistoryBlockCount,
            20,
            0);
        for (ulong sequence = 20; sequence < 71; sequence++)
        {
            original.Append(Wire(
                sequence,
                checked((long)(sequence - 20)) * 200_000_000));
        }

        WaveformBlockRingState checkpoint = original.CaptureState();
        var restored = WaveformBlockRing.Restore(checkpoint);
        byte[] next = Wire(71, 10_200_000_000);
        WaveformBlockAppendResult expected = original.Append(next);
        WaveformBlockAppendResult actual = restored.Append(next);
        Check.That(actual == expected &&
            Equivalent(restored.CaptureState(), original.CaptureState()),
            "restored slot order must produce the same eviction and future state");

        byte[][] corruptWires = checkpoint.RetainedRawBlocks
            .Select(bytes => bytes.ToArray())
            .ToArray();
        corruptWires[0][^1] ^= 1;
        WaveformBlockRingState corruptWire = checkpoint with
        {
            RetainedRawBlocks = corruptWires,
        };
        Check.That(Reason(() => WaveformBlockRing.Restore(corruptWire)) ==
            "WaveformBlockRing.InvalidCheckpoint",
            "checkpoint restore must revalidate transport and content integrity");

        WaveformBlockRingState corruptCursor = checkpoint with
        {
            NextBlockStartSimTimeNs = checkpoint.NextBlockStartSimTimeNs + 1,
        };
        Check.That(Reason(() => WaveformBlockRing.Restore(corruptCursor)) ==
            "WaveformBlockRing.InvalidCheckpoint",
            "checkpoint cursor must be proven by all retained 200 ms slots");
    }

    private static WaveformBlockRing CreateRing(
        int capacity,
        ulong firstBlockSequence,
        long firstBlockStartSimTimeNs) => WaveformBlockRing.Start(
            SessionId,
            InstanceId,
            3,
            7,
            firstBlockSequence,
            firstBlockStartSimTimeNs,
            capacity);

    private static byte[] Wire(
        ulong sequence,
        long? startSimTimeNs = null,
        short sample = 1,
        ulong streamEpoch = 7)
    {
        long start;
        if (startSimTimeNs is not null)
        {
            start = startSimTimeNs.Value;
        }
        else
        {
            ulong relativeSequence = sequence < 10 ? sequence : sequence - 10;
            start = checked((long)relativeSequence * 200_000_000);
        }

        return WaveformEnvelopeCodec.EncodeRaw(new WaveformEnvelope(
            SessionId,
            InstanceId,
            3,
            streamEpoch,
            sequence,
            11,
            start,
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
                    new short[] { sample },
                    Array.Empty<WaveformQualityRange>()),
            }));
    }

    private static bool Equivalent(WaveformBlockRingState left, WaveformBlockRingState right) =>
        left.SessionId == right.SessionId &&
        left.InstanceId == right.InstanceId &&
        left.TimebaseEpoch == right.TimebaseEpoch &&
        left.StreamEpoch == right.StreamEpoch &&
        left.Capacity == right.Capacity &&
        left.NextBlockSequence == right.NextBlockSequence &&
        left.NextBlockStartSimTimeNs == right.NextBlockStartSimTimeNs &&
        left.RetainedRawBlocks.Count == right.RetainedRawBlocks.Count &&
        left.RetainedRawBlocks.Zip(right.RetainedRawBlocks).All(pair =>
            pair.First.SequenceEqual(pair.Second));

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (WaveformBlockRingException exception)
        {
            return exception.ReasonCode;
        }
    }
}
