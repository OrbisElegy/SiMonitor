// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Net.WebSockets;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

public sealed class ClassroomWaveformSubscription : IDisposable, IAsyncDisposable
{
    private const int MaximumFrameBytes = WaveformEnvelopeCodec.PreludeSize + WaveformEnvelopeCodec.MaximumHeaderSize + WaveformEnvelopeCodec.MaximumPayloadSize;
    private readonly WebSocket _socket;
    private readonly bool _allowLz4;
    private readonly WaveformBlockRing _history;
    private readonly byte[] _buffer = new byte[MaximumFrameBytes + 1];
    private readonly byte[] _ack = new byte[ClassroomWire.AcknowledgementBytes];
    private ulong _nextBlockSequence;
    private int _reading;
    private int _disposed;

    internal ClassroomWaveformSubscription(WebSocket socket, WaveformBlockRing history, bool allowLz4 = false)
    {
        _allowLz4 = allowLz4;
        _socket = socket;
        _history = history;
        _nextBlockSequence = history.NextBlockSequence;
    }

    // This is the first block not yet returned to the caller, even if sending an ACK failed.
    internal bool LastBlockWasCompressed { get; private set; }

    public ulong NextBlockSequence => Volatile.Read(ref _nextBlockSequence);

    // Local history includes verified blocks. The delivery cursor advances only after the ACK succeeds.
    public WaveformBlockReplayResult ReadBufferedBlocks(ulong firstBlockSequence, int maximumBlocks)
    {
        lock (_history)
        {
            return _history.ReadFrom(firstBlockSequence, maximumBlocks);
        }
    }

    public async ValueTask<WaveformEnvelope?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _reading, 1) != 0)
        {
            throw new InvalidOperationException("A waveform receive is already in progress.");
        }

        try
        {
            if (_socket.State == WebSocketState.Closed) { return null; }
            int used = 0;
            bool assemblyStarted = false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            while (true)
            {
                ValueWebSocketReceiveResult part = await _socket.ReceiveAsync(_buffer.AsMemory(used), deadline.Token).ConfigureAwait(false);
                if (part.MessageType == WebSocketMessageType.Close)
                {
                    if (assemblyStarted || _socket.CloseStatus != WebSocketCloseStatus.NormalClosure)
                    {
                        throw new IOException($"The host closed the waveform stream ({_socket.CloseStatus}).");
                    }
                    deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, deadline.Token).ConfigureAwait(false);
                    return null;
                }
                used += part.Count;
                if (part.MessageType != WebSocketMessageType.Binary || used > MaximumFrameBytes)
                {
                    throw new InvalidDataException("Expected one bounded binary waveform envelope.");
                }
                if (part.EndOfMessage) { break; }
                // Allow a paused host to remain idle. Once any fragment arrives, bound the whole assembly.
                if (!assemblyStarted)
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(5));
                    assemblyStarted = true;
                }
            }

            WaveformEnvelope envelope;
            lock (_history)
            {
                WaveformBlockAppendResult append = _history.AppendTransport(_buffer.AsSpan(0, used), _allowLz4, out envelope);
                if (append.Status != WaveformBlockAppendStatus.Appended)
                {
                    throw new InvalidDataException("Unexpected duplicate waveform block.");
                }
            }
            BinaryPrimitives.WriteUInt64LittleEndian(_ack, envelope.StreamEpoch);
            BinaryPrimitives.WriteUInt64LittleEndian(_ack.AsSpan(8), envelope.BlockSequence);
            _buffer.AsSpan(WaveformEnvelopeCodec.ContentSha256Offset, 32).CopyTo(_ack.AsSpan(16));
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await _socket.SendAsync(_ack.AsMemory(), WebSocketMessageType.Binary, true, deadline.Token).ConfigureAwait(false);
            LastBlockWasCompressed = _buffer[7] == 1;
            Volatile.Write(ref _nextBlockSequence, _history.NextBlockSequence);
            return envelope;
        }
        catch
        {
            _socket.Abort();
            throw;
        }
        finally
        {
            Volatile.Write(ref _reading, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        try
        {
            if (_socket.State == WebSocketState.Open && Interlocked.CompareExchange(ref _reading, 1, 0) == 0)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
                catch (WebSocketException) { }
            }
        }
        finally
        {
            _socket.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) { _socket.Dispose(); }
    }
}
