// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Net.WebSockets;
using Monitor.Network;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class ClassroomWaveformClientSpecifications
{
    private static readonly Guid SessionId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid InstanceId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid ChannelId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    public static Specification[] All =>
    [
        new(nameof(FragmentedWaveformProducesExactAcknowledgement), FragmentedWaveformProducesExactAcknowledgement),
        new(nameof(InvalidFramesNeverAdvanceOrAcknowledge), InvalidFramesNeverAdvanceOrAcknowledge),
        new(nameof(FailedAcknowledgementPreservesDeliveryCursor), FailedAcknowledgementPreservesDeliveryCursor)
    ];

    private static void FragmentedWaveformProducesExactAcknowledgement()
    {
        byte[] envelope = WaveformEnvelopeCodec.EncodeRaw(Block());
        byte[] frames = Frames(envelope, WebSocketMessageType.Binary);
        using var stream = new MemoryStream();
        stream.Write(frames);
        stream.Position = 0;
        using var subscription = Subscription(stream);
        WaveformEnvelope? received = subscription.ReceiveAsync().AsTask().GetAwaiter().GetResult();
        Check.That(received?.Planes[0].Samples[0] == 123 && subscription.NextBlockSequence == 8,
            "complete fragments deliver a validated sample and advance the cursor once");

        WaveformBlockReplayResult buffered = subscription.ReadBufferedBlocks(7, 1);
        Check.That(buffered.Status == WaveformBlockReplayStatus.Available && buffered.Blocks[0].RawEnvelope.AsSpan().SequenceEqual(envelope),
            "the local playout history retains the verified wire bytes");

        using var replies = new MemoryStream(stream.ToArray()[frames.Length..]);
        using var peer = WebSocket.CreateFromStream(replies, true, null, Timeout.InfiniteTimeSpan);
        byte[] ack = new byte[ClassroomWire.AcknowledgementBytes];
        ValueWebSocketReceiveResult message = peer.ReceiveAsync(ack.AsMemory(), CancellationToken.None).AsTask().GetAwaiter().GetResult();
        Check.That(message.MessageType == WebSocketMessageType.Binary && message.EndOfMessage && message.Count == ack.Length &&
            BinaryPrimitives.ReadUInt64LittleEndian(ack) == 2 && BinaryPrimitives.ReadUInt64LittleEndian(ack.AsSpan(8)) == 7 &&
            ack.AsSpan(16).SequenceEqual(envelope.AsSpan(WaveformEnvelopeCodec.ContentSha256Offset, 32)),
            "ACK binds the epoch, contiguous block sequence and validated canonical content hash");
    }

    private static void InvalidFramesNeverAdvanceOrAcknowledge()
    {
        byte[] envelope = WaveformEnvelopeCodec.EncodeRaw(Block());
        byte[] corrupt = (byte[])envelope.Clone();
        corrupt[^1] ^= 1;
        Reject<WaveformEnvelopeException>(Frames(corrupt, WebSocketMessageType.Binary));
        Reject<WaveformBlockRingException>(Frames(WaveformEnvelopeCodec.EncodeRaw(Block() with { InstanceId = ChannelId }), WebSocketMessageType.Binary));
        Reject<WaveformBlockRingException>(Frames(WaveformEnvelopeCodec.EncodeRaw(Block() with { BlockSequence = 8 }), WebSocketMessageType.Binary));
        Reject<WaveformBlockRingException>(Frames(WaveformEnvelopeCodec.EncodeRaw(Block() with { StartSimTimeNs = 0 }), WebSocketMessageType.Binary));
        Reject<InvalidDataException>(Frames("wrong message type"u8.ToArray(), WebSocketMessageType.Text));
        Reject<IOException>(Frames(envelope, WebSocketMessageType.Binary, closeMidMessage: true));
        int bound = WaveformEnvelopeCodec.PreludeSize + WaveformEnvelopeCodec.MaximumHeaderSize + WaveformEnvelopeCodec.MaximumPayloadSize;
        Reject<InvalidDataException>(Frames(new byte[bound + 1], WebSocketMessageType.Binary));
    }

    private static void FailedAcknowledgementPreservesDeliveryCursor()
    {
        byte[] frames = Frames(WaveformEnvelopeCodec.EncodeRaw(Block()), WebSocketMessageType.Binary);
        // A fixed-size stream can read the server frame and cannot append the outbound ACK.
        using var stream = new MemoryStream(frames);
        using var subscription = Subscription(stream);
        bool failed = false;
        try { _ = subscription.ReceiveAsync().AsTask().GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is IOException or WebSocketException or NotSupportedException) { failed = true; }
        Check.That(failed && subscription.NextBlockSequence == 7, "a failed ACK leaves the undelivered block available for resume");
    }

    private static void Reject<T>(byte[] frames) where T : Exception
    {
        using var stream = new MemoryStream();
        stream.Write(frames);
        stream.Position = 0;
        using var subscription = Subscription(stream);
        bool rejected = false;
        try { _ = subscription.ReceiveAsync().AsTask().GetAwaiter().GetResult(); }
        catch (T) { rejected = true; }
        Check.That(rejected && subscription.NextBlockSequence == 7 && stream.ToArray().Length == frames.Length,
            "an invalid waveform is rejected without an ACK or cursor advance");
    }

    private static ClassroomWaveformSubscription Subscription(Stream stream) => new(
        WebSocket.CreateFromStream(stream, false, null, Timeout.InfiniteTimeSpan),
        WaveformBlockRing.Start(SessionId, InstanceId, 1, 2, 7, 1_400_000_000, WaveformBlockRing.ClientHistoryBlockCount));

    private static WaveformEnvelope Block() => new(SessionId, InstanceId, 1, 2, 7, 1, 1_400_000_000,
        WaveformBlockAssembler.BlockDurationNs, [new WaveformPlane(ChannelId, 5, 1, 7, 1, 1, 0, 1,
            WaveformQualityEncoding.None, [123], [])]);

    private static byte[] Frames(byte[] payload, WebSocketMessageType messageType, bool closeMidMessage = false)
    {
        using var stream = new MemoryStream();
        using var peer = WebSocket.CreateFromStream(stream, true, null, Timeout.InfiniteTimeSpan);
        int middle = payload.Length / 2;
        peer.SendAsync(payload.AsMemory(0, middle), messageType, false, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        if (closeMidMessage)
        {
            peer.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).GetAwaiter().GetResult();
        }
        else
        {
            peer.SendAsync(payload.AsMemory(middle), messageType, true, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }
        return stream.ToArray();
    }
}
