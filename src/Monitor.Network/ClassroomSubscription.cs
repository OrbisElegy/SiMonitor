// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Net.WebSockets;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

// The ring, outbox and replay cursor share the host publication lock. No socket
// operation runs under that lock. Historical replay and new live blocks meet
// at the sequence captured while the subscriber is registered.
internal sealed class ClassroomSubscription(Guid studentId, WaveformBlockRing ring, ulong from)
{
    private readonly ulong _liveStart = ring.NextBlockSequence;
    private ulong _cursor = from;

    internal WaveformSubscriberOutbox Outbox { get; } = WaveformSubscriberOutbox.Start(
        studentId, ring, WaveformSubscriberOutbox.MaximumCapacity, ring.NextBlockSequence);

    internal async Task SendAsync(WebSocket socket, object gate, Func<Task> publicationSignal, CancellationToken cancellationToken)
    {
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        byte[] ack = new byte[ClassroomWire.AcknowledgementBytes + 1];
        Task<bool> input = ReadAcknowledgementAsync(socket, ack, reading.Token);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task changed;
                WaveformRetainedBlock? block;
                lock (gate)
                {
                    block = ReadNext();
                    changed = publicationSignal();
                }

                if (block is null || input.IsCompleted)
                {
                    if (await Task.WhenAny(input, changed).ConfigureAwait(false) == input)
                    {
                        if (await input.ConfigureAwait(false))
                        {
                            throw new InvalidDataException("Unexpected acknowledgement.");
                        }
                        return;
                    }
                    continue;
                }

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    await socket.SendAsync(block.RawEnvelope.AsMemory(), WebSocketMessageType.Binary, true,
                        deadline.Token).ConfigureAwait(false);
                    if (!await input.WaitAsync(deadline.Token).ConfigureAwait(false))
                    {
                        return;
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "Acknowledgement deadline").ConfigureAwait(false);
                    return;
                }

                lock (gate) { Acknowledge(block, ack); }
                input = ReadAcknowledgementAsync(socket, ack, reading.Token);
            }
        }
        catch (InvalidDataException)
        {
            await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "Invalid acknowledgement or resync required").ConfigureAwait(false);
        }
        finally
        {
            await reading.CancelAsync().ConfigureAwait(false);
            try { await input.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (IOException) { }
            catch (InvalidDataException) { }
        }
    }

    private WaveformRetainedBlock? ReadNext()
    {
        if (Outbox.Status == WaveformSubscriberQueueStatus.IsolatedNeedsResync)
        {
            throw new InvalidDataException("Live subscriber requires resync.");
        }

        if (_cursor < _liveStart)
        {
            WaveformBlockReplayResult replay = ring.ReadFrom(_cursor, 1);
            return replay.Status == WaveformBlockReplayStatus.Available && replay.Blocks.Count == 1
                ? replay.Blocks[0]
                : throw new InvalidDataException("History expired during replay.");
        }

        WaveformSubscriberReadResult next = Outbox.ReadNext();
        return next.Status == WaveformSubscriberReadStatus.RequiresResync
            ? throw new InvalidDataException("Live subscriber requires resync.")
            : next.Block;
    }

    private void Acknowledge(WaveformRetainedBlock block, ReadOnlySpan<byte> ack)
    {
        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(ack);
        ulong sequence = BinaryPrimitives.ReadUInt64LittleEndian(ack[8..]);
        if (epoch != ring.StreamEpoch || sequence != block.BlockSequence ||
            !ack.Slice(16, 32).SequenceEqual(block.RawEnvelope.AsSpan(WaveformEnvelopeCodec.ContentSha256Offset, 32)))
        {
            throw new InvalidDataException("Acknowledgement does not match the sent waveform.");
        }
        if (_cursor >= _liveStart &&
            Outbox.Acknowledge(epoch, sequence, block.ContentSha256).Status != WaveformSubscriberAcknowledgeStatus.Acknowledged)
        {
            throw new InvalidDataException("Live subscriber requires resync.");
        }
        _cursor++;
    }

    private static async Task<bool> ReadAcknowledgementAsync(WebSocket socket, byte[] bytes, CancellationToken cancellationToken)
    {
        int count = 0;
        while (true)
        {
            ValueWebSocketReceiveResult result = await socket.ReceiveAsync(bytes.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) { return false; }
            count += result.Count;
            if (result.MessageType != WebSocketMessageType.Binary || count > ClassroomWire.AcknowledgementBytes)
            {
                throw new InvalidDataException("Unsupported acknowledgement frame.");
            }
            if (result.EndOfMessage)
            {
                if (count != ClassroomWire.AcknowledgementBytes)
                {
                    throw new InvalidDataException("Invalid acknowledgement length.");
                }
                return true;
            }
        }
    }

    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string reason)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await socket.CloseOutputAsync(status, reason, timeout.Token).ConfigureAwait(false);
        }
    }
}
