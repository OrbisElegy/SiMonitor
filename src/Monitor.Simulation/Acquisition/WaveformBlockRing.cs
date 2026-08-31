// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;

namespace Monitor.Simulation.Acquisition;

public sealed class WaveformBlockRingException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public enum WaveformBlockAppendStatus
{
    Appended,
    AlreadyPresent,
}

public enum WaveformBlockReplayStatus
{
    Available,
    AwaitFutureBlock,
    RecoverySnapshotRequired,
}

public readonly record struct WaveformBlockAppendResult(
    WaveformBlockAppendStatus Status,
    ulong BlockSequence,
    ulong? EvictedBlockSequence);

public sealed record WaveformRetainedBlock(
    ulong BlockSequence,
    ulong ConfigurationRevision,
    long StartSimTimeNs,
    string ContentSha256,
    byte[] RawEnvelope);

public sealed record WaveformBlockReplayResult(
    WaveformBlockReplayStatus Status,
    IReadOnlyList<WaveformRetainedBlock> Blocks);

public sealed record WaveformBlockRingState(
    Guid SessionId,
    Guid InstanceId,
    ulong TimebaseEpoch,
    ulong StreamEpoch,
    int Capacity,
    ulong NextBlockSequence,
    long NextBlockStartSimTimeNs,
    IReadOnlyList<byte[]> RetainedRawBlocks);

public sealed class WaveformBlockRing
{
    public const int ClientHistoryBlockCount = 50;
    public const int HostRetentionBlockCount = 300;
    public const int MaximumCapacity = HostRetentionBlockCount;

    private const int ContentHashOffset =
        WaveformEnvelopeCodec.PreludeSize + 80;

    private readonly StoredBlock?[] _slots;
    private int _head;
    private int _count;

    private WaveformBlockRing(
        Guid sessionId,
        Guid instanceId,
        ulong timebaseEpoch,
        ulong streamEpoch,
        int capacity,
        ulong nextBlockSequence,
        long nextBlockStartSimTimeNs)
    {
        SessionId = sessionId;
        InstanceId = instanceId;
        TimebaseEpoch = timebaseEpoch;
        StreamEpoch = streamEpoch;
        Capacity = capacity;
        NextBlockSequence = nextBlockSequence;
        NextBlockStartSimTimeNs = nextBlockStartSimTimeNs;
        _slots = new StoredBlock[capacity];
    }

    public Guid SessionId { get; }

    public Guid InstanceId { get; }

    public ulong TimebaseEpoch { get; }

    public ulong StreamEpoch { get; }

    public int Capacity { get; }

    public int Count => _count;

    public ulong NextBlockSequence { get; private set; }

    public long NextBlockStartSimTimeNs { get; private set; }

    public ulong OldestBlockSequence =>
        _count == 0 ? NextBlockSequence : GetSlot(0).BlockSequence;

    public static WaveformBlockRing Start(
        Guid sessionId,
        Guid instanceId,
        ulong timebaseEpoch,
        ulong streamEpoch,
        ulong firstBlockSequence,
        long firstBlockStartSimTimeNs,
        int capacity)
    {
        ValidateConfiguration(sessionId, instanceId, firstBlockStartSimTimeNs, capacity);
        return new WaveformBlockRing(
            sessionId,
            instanceId,
            timebaseEpoch,
            streamEpoch,
            capacity,
            firstBlockSequence,
            firstBlockStartSimTimeNs);
    }

    public static WaveformBlockRing Restore(WaveformBlockRingState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.RetainedRawBlocks);
        try
        {
            ValidateConfiguration(
                state.SessionId,
                state.InstanceId,
                state.NextBlockStartSimTimeNs,
                state.Capacity);
            if (state.RetainedRawBlocks.Count > state.Capacity ||
                state.NextBlockSequence < (ulong)state.RetainedRawBlocks.Count)
            {
                throw InvalidCheckpoint(nameof(state));
            }

            Int128 firstStartWide = (Int128)state.NextBlockStartSimTimeNs -
                (Int128)state.RetainedRawBlocks.Count * WaveformBlockAssembler.BlockDurationNs;
            if (firstStartWide < 0)
            {
                throw InvalidCheckpoint(nameof(state));
            }

            ulong firstSequence = state.NextBlockSequence -
                (ulong)state.RetainedRawBlocks.Count;
            long firstStart = (long)firstStartWide;
            StoredBlock[] retained = new StoredBlock[state.RetainedRawBlocks.Count];
            for (int index = 0; index < retained.Length; index++)
            {
                byte[] source = state.RetainedRawBlocks[index] ??
                    throw InvalidCheckpoint(nameof(state));
                WaveformEnvelope envelope = WaveformEnvelopeCodec.Decode(source);
                ulong expectedSequence = firstSequence + (uint)index;
                long expectedStart = checked(firstStart +
                    (long)index * WaveformBlockAssembler.BlockDurationNs);
                ValidateEnvelopeIdentity(
                    envelope,
                    state.SessionId,
                    state.InstanceId,
                    state.TimebaseEpoch,
                    state.StreamEpoch,
                    nameof(state));
                if (envelope.BlockSequence != expectedSequence ||
                    envelope.StartSimTimeNs != expectedStart)
                {
                    throw InvalidCheckpoint(nameof(state));
                }

                retained[index] = Store(envelope, source);
            }

            WaveformBlockRing ring = new(
                state.SessionId,
                state.InstanceId,
                state.TimebaseEpoch,
                state.StreamEpoch,
                state.Capacity,
                state.NextBlockSequence,
                state.NextBlockStartSimTimeNs);
            for (int index = 0; index < retained.Length; index++)
            {
                ring._slots[index] = retained[index];
            }

            ring._count = retained.Length;
            return ring;
        }
        catch (WaveformBlockRingException exception)
            when (exception.ReasonCode != "WaveformBlockRing.InvalidCheckpoint")
        {
            throw InvalidCheckpoint(nameof(state));
        }
        catch (WaveformEnvelopeException)
        {
            throw InvalidCheckpoint(nameof(state));
        }
        catch (OverflowException)
        {
            throw InvalidCheckpoint(nameof(state));
        }
    }

    public WaveformBlockAppendResult Append(ReadOnlySpan<byte> rawEnvelope)
    {
        WaveformEnvelope envelope = WaveformEnvelopeCodec.Decode(rawEnvelope);
        ValidateEnvelopeIdentity(
            envelope,
            SessionId,
            InstanceId,
            TimebaseEpoch,
            StreamEpoch,
            nameof(rawEnvelope));

        if (envelope.BlockSequence < NextBlockSequence)
        {
            if (_count == 0 || envelope.BlockSequence < OldestBlockSequence)
            {
                throw Error("WaveformBlockRing.BlockTooOld", nameof(rawEnvelope));
            }

            StoredBlock existing = GetSlot(checked((int)(
                envelope.BlockSequence - OldestBlockSequence)));
            ReadOnlySpan<byte> incomingHash = rawEnvelope.Slice(ContentHashOffset, 32);
            if (!CryptographicOperations.FixedTimeEquals(existing.ContentSha256, incomingHash))
            {
                throw Error("WaveformBlockRing.ConflictingDuplicate", nameof(rawEnvelope));
            }

            return new WaveformBlockAppendResult(
                WaveformBlockAppendStatus.AlreadyPresent,
                envelope.BlockSequence,
                null);
        }

        if (envelope.BlockSequence > NextBlockSequence)
        {
            throw Error("WaveformBlockRing.SequenceGap", nameof(rawEnvelope));
        }

        if (envelope.StartSimTimeNs != NextBlockStartSimTimeNs)
        {
            throw Error("WaveformBlockRing.StartTimeDiscontinuous", nameof(rawEnvelope));
        }

        if (NextBlockSequence == ulong.MaxValue ||
            NextBlockStartSimTimeNs >
                long.MaxValue - WaveformBlockAssembler.BlockDurationNs)
        {
            throw Error("WaveformBlockRing.StateOverflow", nameof(rawEnvelope));
        }

        StoredBlock accepted = Store(envelope, rawEnvelope);
        ulong? evicted = null;
        if (_count == Capacity)
        {
            evicted = GetSlot(0).BlockSequence;
            _slots[_head] = accepted;
            _head = (_head + 1) % Capacity;
        }
        else
        {
            _slots[(_head + _count) % Capacity] = accepted;
            _count++;
        }

        NextBlockSequence++;
        NextBlockStartSimTimeNs += WaveformBlockAssembler.BlockDurationNs;
        return new WaveformBlockAppendResult(
            WaveformBlockAppendStatus.Appended,
            envelope.BlockSequence,
            evicted);
    }

    public WaveformBlockReplayResult ReadFrom(ulong firstBlockSequence, int maximumBlocks)
    {
        if (maximumBlocks is < 1 or > MaximumCapacity)
        {
            throw Error("WaveformBlockRing.InvalidReadLimit", nameof(maximumBlocks));
        }

        if (firstBlockSequence < OldestBlockSequence)
        {
            return new WaveformBlockReplayResult(
                WaveformBlockReplayStatus.RecoverySnapshotRequired,
                Array.Empty<WaveformRetainedBlock>());
        }

        if (firstBlockSequence > NextBlockSequence)
        {
            return new WaveformBlockReplayResult(
                WaveformBlockReplayStatus.AwaitFutureBlock,
                Array.Empty<WaveformRetainedBlock>());
        }

        if (firstBlockSequence == NextBlockSequence)
        {
            return new WaveformBlockReplayResult(
                WaveformBlockReplayStatus.Available,
                Array.Empty<WaveformRetainedBlock>());
        }

        int offset = checked((int)(firstBlockSequence - OldestBlockSequence));
        int resultCount = Math.Min(maximumBlocks, _count - offset);
        WaveformRetainedBlock[] result = new WaveformRetainedBlock[resultCount];
        for (int index = 0; index < result.Length; index++)
        {
            StoredBlock block = GetSlot(offset + index);
            result[index] = new WaveformRetainedBlock(
                block.BlockSequence,
                block.ConfigurationRevision,
                block.StartSimTimeNs,
                Convert.ToHexStringLower(block.ContentSha256),
                [.. block.RawEnvelope]);
        }

        return new WaveformBlockReplayResult(
            WaveformBlockReplayStatus.Available,
            Array.AsReadOnly(result));
    }

    public WaveformBlockRingState CaptureState()
    {
        byte[][] retained = new byte[_count][];
        for (int index = 0; index < retained.Length; index++)
        {
            retained[index] = [.. GetSlot(index).RawEnvelope];
        }

        return new WaveformBlockRingState(
            SessionId,
            InstanceId,
            TimebaseEpoch,
            StreamEpoch,
            Capacity,
            NextBlockSequence,
            NextBlockStartSimTimeNs,
            Array.AsReadOnly(retained));
    }

    private StoredBlock GetSlot(int offset) =>
        _slots[(_head + offset) % Capacity] ??
        throw new InvalidOperationException("waveform ring slot is unexpectedly empty");

    private static StoredBlock Store(WaveformEnvelope envelope, ReadOnlySpan<byte> wire) => new(
        envelope.BlockSequence,
        envelope.ConfigurationRevision,
        envelope.StartSimTimeNs,
        wire.Slice(ContentHashOffset, 32).ToArray(),
        wire.ToArray());

    private static void ValidateConfiguration(
        Guid sessionId,
        Guid instanceId,
        long firstBlockStartSimTimeNs,
        int capacity)
    {
        if (sessionId == Guid.Empty || instanceId == Guid.Empty ||
            firstBlockStartSimTimeNs < 0 ||
            (capacity != ClientHistoryBlockCount &&
                capacity != HostRetentionBlockCount))
        {
            throw Error("WaveformBlockRing.InvalidConfiguration", nameof(capacity));
        }
    }

    private static void ValidateEnvelopeIdentity(
        WaveformEnvelope envelope,
        Guid sessionId,
        Guid instanceId,
        ulong timebaseEpoch,
        ulong streamEpoch,
        string parameterName)
    {
        if (envelope.SessionId != sessionId ||
            envelope.InstanceId != instanceId ||
            envelope.TimebaseEpoch != timebaseEpoch ||
            envelope.StreamEpoch != streamEpoch)
        {
            throw Error("WaveformBlockRing.IdentityMismatch", parameterName);
        }

        if (envelope.DurationNs != WaveformBlockAssembler.BlockDurationNs)
        {
            throw Error("WaveformBlockRing.DurationInvalid", parameterName);
        }
    }

    private static WaveformBlockRingException InvalidCheckpoint(string parameterName) =>
        Error("WaveformBlockRing.InvalidCheckpoint", parameterName);

    private static WaveformBlockRingException Error(string reasonCode, string parameterName) =>
        new(reasonCode, parameterName);

    private sealed record StoredBlock(
        ulong BlockSequence,
        ulong ConfigurationRevision,
        long StartSimTimeNs,
        byte[] ContentSha256,
        byte[] RawEnvelope);
}
