// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Acquisition;

public sealed class WaveformSubscriberQueueException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public enum WaveformSubscriberQueueStatus
{
    Active,
    IsolatedNeedsResync,
}

public enum WaveformSubscriberIsolationReason
{
    BackpressureLimitExceeded,
    SequenceGap,
    RetentionExpired,
    StreamChanged,
    AcknowledgementOutOfOrder,
    AcknowledgementHashMismatch,
}

public enum WaveformSubscriberEnqueueStatus
{
    Enqueued,
    AlreadyQueued,
    IgnoredStale,
    AwaitingHostBlock,
    RequiresResync,
}

public enum WaveformSubscriberReadStatus
{
    Available,
    Empty,
    RequiresResync,
}

public enum WaveformSubscriberAcknowledgeStatus
{
    Acknowledged,
    IgnoredStale,
    Empty,
    RequiresResync,
}

public readonly record struct WaveformSubscriberEnqueueResult(
    WaveformSubscriberEnqueueStatus Status,
    ulong BlockSequence,
    WaveformSubscriberIsolationReason? IsolationReason);

public sealed record WaveformSubscriberReadResult(
    WaveformSubscriberReadStatus Status,
    WaveformRetainedBlock? Block,
    WaveformSubscriberIsolationReason? IsolationReason);

public readonly record struct WaveformSubscriberAcknowledgeResult(
    WaveformSubscriberAcknowledgeStatus Status,
    ulong BlockSequence,
    WaveformSubscriberIsolationReason? IsolationReason);

public sealed record WaveformSubscriberQueueState(
    Guid SubscriberId,
    Guid SessionId,
    Guid InstanceId,
    ulong TimebaseEpoch,
    ulong StreamEpoch,
    int Capacity,
    WaveformSubscriberQueueStatus Status,
    WaveformSubscriberIsolationReason? IsolationReason,
    ulong NextEnqueueBlockSequence,
    IReadOnlyList<ulong> PendingBlockSequences);

public sealed class WaveformSubscriberOutbox
{
    public const int MaximumCapacity = WaveformBlockRing.ClientHistoryBlockCount;

    private WaveformBlockRing _hostRing;
    private readonly ulong[] _slots;
    private int _head;
    private int _count;

    private WaveformSubscriberOutbox(
        WaveformSubscriberQueueState state,
        WaveformBlockRing hostRing)
    {
        ValidateState(state, hostRing);
        SubscriberId = state.SubscriberId;
        SessionId = state.SessionId;
        InstanceId = state.InstanceId;
        TimebaseEpoch = state.TimebaseEpoch;
        StreamEpoch = state.StreamEpoch;
        Capacity = state.Capacity;
        Status = state.Status;
        IsolationReason = state.IsolationReason;
        NextEnqueueBlockSequence = state.NextEnqueueBlockSequence;
        _hostRing = hostRing;
        _slots = new ulong[Capacity];
        for (int index = 0; index < state.PendingBlockSequences.Count; index++)
        {
            _slots[index] = state.PendingBlockSequences[index];
        }

        _count = state.PendingBlockSequences.Count;
    }

    public Guid SubscriberId { get; }

    public Guid SessionId { get; }

    public Guid InstanceId { get; }

    public ulong TimebaseEpoch { get; private set; }

    public ulong StreamEpoch { get; private set; }

    public int Capacity { get; }

    public int Count => _count;

    public WaveformSubscriberQueueStatus Status { get; private set; }

    public WaveformSubscriberIsolationReason? IsolationReason { get; private set; }

    public ulong NextEnqueueBlockSequence { get; private set; }

    public static WaveformSubscriberOutbox Start(
        Guid subscriberId,
        WaveformBlockRing hostRing,
        int capacity,
        ulong firstBlockSequence)
    {
        ArgumentNullException.ThrowIfNull(hostRing);
        if (subscriberId == Guid.Empty ||
            hostRing.Capacity != WaveformBlockRing.HostRetentionBlockCount ||
            capacity is < 1 or > MaximumCapacity ||
            firstBlockSequence < hostRing.OldestBlockSequence ||
            firstBlockSequence > hostRing.NextBlockSequence)
        {
            throw Error("WaveformSubscriberQueue.InvalidConfiguration", nameof(capacity));
        }

        return new WaveformSubscriberOutbox(new WaveformSubscriberQueueState(
            subscriberId,
            hostRing.SessionId,
            hostRing.InstanceId,
            hostRing.TimebaseEpoch,
            hostRing.StreamEpoch,
            capacity,
            WaveformSubscriberQueueStatus.Active,
            null,
            firstBlockSequence,
            Array.Empty<ulong>()),
            hostRing);
    }

    public static WaveformSubscriberOutbox Restore(
        WaveformSubscriberQueueState state,
        WaveformBlockRing hostRing)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(hostRing);
        ArgumentNullException.ThrowIfNull(state.PendingBlockSequences);
        return new WaveformSubscriberOutbox(state, hostRing);
    }

    public WaveformSubscriberEnqueueResult Enqueue(ulong blockSequence)
    {
        if (Status == WaveformSubscriberQueueStatus.IsolatedNeedsResync)
        {
            return EnqueueResult(
                WaveformSubscriberEnqueueStatus.RequiresResync,
                blockSequence);
        }

        if (blockSequence < NextEnqueueBlockSequence)
        {
            return EnqueueResult(
                Contains(blockSequence)
                    ? WaveformSubscriberEnqueueStatus.AlreadyQueued
                    : WaveformSubscriberEnqueueStatus.IgnoredStale,
                blockSequence);
        }

        if (blockSequence > NextEnqueueBlockSequence)
        {
            Isolate(WaveformSubscriberIsolationReason.SequenceGap);
            return EnqueueResult(
                WaveformSubscriberEnqueueStatus.RequiresResync,
                blockSequence);
        }

        if (blockSequence < _hostRing.OldestBlockSequence)
        {
            Isolate(WaveformSubscriberIsolationReason.RetentionExpired);
            return EnqueueResult(
                WaveformSubscriberEnqueueStatus.RequiresResync,
                blockSequence);
        }

        if (blockSequence >= _hostRing.NextBlockSequence)
        {
            return EnqueueResult(
                WaveformSubscriberEnqueueStatus.AwaitingHostBlock,
                blockSequence);
        }

        if (_count == Capacity)
        {
            Isolate(WaveformSubscriberIsolationReason.BackpressureLimitExceeded);
            return EnqueueResult(
                WaveformSubscriberEnqueueStatus.RequiresResync,
                blockSequence);
        }

        _slots[(_head + _count) % Capacity] = blockSequence;
        _count++;
        NextEnqueueBlockSequence++;
        return EnqueueResult(WaveformSubscriberEnqueueStatus.Enqueued, blockSequence);
    }

    public WaveformSubscriberReadResult ReadNext()
    {
        if (Status == WaveformSubscriberQueueStatus.IsolatedNeedsResync)
        {
            return new WaveformSubscriberReadResult(
                WaveformSubscriberReadStatus.RequiresResync,
                null,
                IsolationReason);
        }

        if (_count == 0)
        {
            return new WaveformSubscriberReadResult(
                WaveformSubscriberReadStatus.Empty,
                null,
                null);
        }

        ulong sequence = _slots[_head];
        WaveformBlockReplayResult replay = _hostRing.ReadFrom(sequence, 1);
        if (replay.Status != WaveformBlockReplayStatus.Available ||
            replay.Blocks.Count != 1)
        {
            Isolate(WaveformSubscriberIsolationReason.RetentionExpired);
            return new WaveformSubscriberReadResult(
                WaveformSubscriberReadStatus.RequiresResync,
                null,
                IsolationReason);
        }

        WaveformRetainedBlock block = replay.Blocks[0];
        return new WaveformSubscriberReadResult(
            WaveformSubscriberReadStatus.Available,
            block,
            null);
    }

    public WaveformSubscriberAcknowledgeResult Acknowledge(
        ulong streamEpoch,
        ulong blockSequence,
        string contentSha256)
    {
        if (!IsSha256(contentSha256))
        {
            throw Error(
                "WaveformSubscriberQueue.InvalidAcknowledgement",
                nameof(contentSha256));
        }

        if (Status == WaveformSubscriberQueueStatus.IsolatedNeedsResync)
        {
            return AcknowledgeResult(
                WaveformSubscriberAcknowledgeStatus.RequiresResync,
                blockSequence);
        }

        if (streamEpoch != StreamEpoch)
        {
            Isolate(WaveformSubscriberIsolationReason.StreamChanged);
            return AcknowledgeResult(
                WaveformSubscriberAcknowledgeStatus.RequiresResync,
                blockSequence);
        }

        if (_count == 0)
        {
            return AcknowledgeResult(
                WaveformSubscriberAcknowledgeStatus.Empty,
                blockSequence);
        }

        ulong expected = _slots[_head];
        if (blockSequence < expected)
        {
            return AcknowledgeResult(
                WaveformSubscriberAcknowledgeStatus.IgnoredStale,
                blockSequence);
        }

        if (blockSequence > expected)
        {
            Isolate(WaveformSubscriberIsolationReason.AcknowledgementOutOfOrder);
            return AcknowledgeResult(
                WaveformSubscriberAcknowledgeStatus.RequiresResync,
                blockSequence);
        }

        WaveformBlockReplayResult replay = _hostRing.ReadFrom(expected, 1);
        if (replay.Status != WaveformBlockReplayStatus.Available ||
            replay.Blocks.Count != 1)
        {
            Isolate(WaveformSubscriberIsolationReason.RetentionExpired);
            return AcknowledgeResult(
                WaveformSubscriberAcknowledgeStatus.RequiresResync,
                blockSequence);
        }

        if (!StringComparer.Ordinal.Equals(
                replay.Blocks[0].ContentSha256,
                contentSha256))
        {
            Isolate(WaveformSubscriberIsolationReason.AcknowledgementHashMismatch);
            return AcknowledgeResult(
                WaveformSubscriberAcknowledgeStatus.RequiresResync,
                blockSequence);
        }

        _slots[_head] = 0;
        _head = (_head + 1) % Capacity;
        _count--;
        return AcknowledgeResult(
            WaveformSubscriberAcknowledgeStatus.Acknowledged,
            blockSequence);
    }

    public WaveformSubscriberQueueState IsolateForStreamChange()
    {
        Isolate(WaveformSubscriberIsolationReason.StreamChanged);
        return CaptureState();
    }

    public WaveformSubscriberQueueState ResetAfterResync(
        WaveformBlockRing hostRing,
        ulong firstBlockSequence)
    {
        ArgumentNullException.ThrowIfNull(hostRing);
        if (Status != WaveformSubscriberQueueStatus.IsolatedNeedsResync ||
            hostRing.Capacity != WaveformBlockRing.HostRetentionBlockCount ||
            hostRing.SessionId != SessionId ||
            hostRing.InstanceId != InstanceId ||
            firstBlockSequence < hostRing.OldestBlockSequence ||
            firstBlockSequence > hostRing.NextBlockSequence)
        {
            throw Error("WaveformSubscriberQueue.InvalidResync", nameof(hostRing));
        }

        _hostRing = hostRing;
        TimebaseEpoch = hostRing.TimebaseEpoch;
        StreamEpoch = hostRing.StreamEpoch;
        NextEnqueueBlockSequence = firstBlockSequence;
        Status = WaveformSubscriberQueueStatus.Active;
        IsolationReason = null;
        _head = 0;
        _count = 0;
        Array.Clear(_slots);
        return CaptureState();
    }

    public WaveformSubscriberQueueState CaptureState()
    {
        ulong[] pending = new ulong[_count];
        for (int index = 0; index < pending.Length; index++)
        {
            pending[index] = _slots[(_head + index) % Capacity];
        }

        return new WaveformSubscriberQueueState(
            SubscriberId,
            SessionId,
            InstanceId,
            TimebaseEpoch,
            StreamEpoch,
            Capacity,
            Status,
            IsolationReason,
            NextEnqueueBlockSequence,
            Array.AsReadOnly(pending));
    }

    private void Isolate(WaveformSubscriberIsolationReason reason)
    {
        if (Status == WaveformSubscriberQueueStatus.IsolatedNeedsResync)
        {
            return;
        }

        Status = WaveformSubscriberQueueStatus.IsolatedNeedsResync;
        IsolationReason = reason;
        _head = 0;
        _count = 0;
        Array.Clear(_slots);
    }

    private bool Contains(ulong blockSequence)
    {
        for (int index = 0; index < _count; index++)
        {
            if (_slots[(_head + index) % Capacity] == blockSequence)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private WaveformSubscriberEnqueueResult EnqueueResult(
        WaveformSubscriberEnqueueStatus status,
        ulong blockSequence) => new(
            status,
            blockSequence,
            IsolationReason);

    private WaveformSubscriberAcknowledgeResult AcknowledgeResult(
        WaveformSubscriberAcknowledgeStatus status,
        ulong blockSequence) => new(
            status,
            blockSequence,
            IsolationReason);

    private static void ValidateState(
        WaveformSubscriberQueueState state,
        WaveformBlockRing hostRing)
    {
        if (state.SubscriberId == Guid.Empty ||
            state.SessionId == Guid.Empty ||
            state.InstanceId == Guid.Empty ||
            state.Capacity is < 1 or > MaximumCapacity ||
            !Enum.IsDefined(state.Status) ||
            (state.IsolationReason is not null &&
                !Enum.IsDefined(state.IsolationReason.Value)) ||
            state.PendingBlockSequences.Count > state.Capacity ||
            hostRing.Capacity != WaveformBlockRing.HostRetentionBlockCount ||
            state.SessionId != hostRing.SessionId ||
            state.InstanceId != hostRing.InstanceId ||
            state.TimebaseEpoch != hostRing.TimebaseEpoch ||
            state.StreamEpoch != hostRing.StreamEpoch ||
            (state.Status == WaveformSubscriberQueueStatus.Active &&
                state.IsolationReason is not null) ||
            (state.Status == WaveformSubscriberQueueStatus.IsolatedNeedsResync &&
                (state.IsolationReason is null || state.PendingBlockSequences.Count != 0)))
        {
            throw InvalidCheckpoint(nameof(state));
        }

        if (state.Status == WaveformSubscriberQueueStatus.Active)
        {
            if (state.NextEnqueueBlockSequence <
                    (ulong)state.PendingBlockSequences.Count ||
                state.NextEnqueueBlockSequence > hostRing.NextBlockSequence)
            {
                throw InvalidCheckpoint(nameof(state));
            }

            ulong firstPending = state.NextEnqueueBlockSequence -
                (ulong)state.PendingBlockSequences.Count;
            if (firstPending < hostRing.OldestBlockSequence)
            {
                throw InvalidCheckpoint(nameof(state));
            }

            for (int index = 0; index < state.PendingBlockSequences.Count; index++)
            {
                if (state.PendingBlockSequences[index] != firstPending + (uint)index)
                {
                    throw InvalidCheckpoint(nameof(state));
                }
            }
        }
    }

    private static WaveformSubscriberQueueException InvalidCheckpoint(string parameterName) =>
        Error("WaveformSubscriberQueue.InvalidCheckpoint", parameterName);

    private static WaveformSubscriberQueueException Error(
        string reasonCode,
        string parameterName) => new(reasonCode, parameterName);
}
