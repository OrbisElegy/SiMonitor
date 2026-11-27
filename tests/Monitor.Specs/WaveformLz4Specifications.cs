// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Numerics;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class WaveformLz4Specifications
{
    // MWS1 with independently packed LZ4 reference blocks including a quality range.
    private const string ReferenceFrame =
        "TVdTMQEAAQEAAAABKAAAANwHAABOCek540wqEQAAAAAAAAABERFBEYERERERERERAAAAAhERQRGBEREREREREQEAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAwusLAgBIADOEcC13aOLSFfuqvS5mkWY9j66kNRFtnx6UjjnaVQsfAAAAChERQRGBEREREREREfoAAAABAAAAAAAAAAAAAACQAQAAAAAAABEAAAAgAwAAAQAAAAEAAAAAAAAAAQAAAAEAAAAAAAAAAAAACxERQRGBEREREREREfoAAAABAAAAAAAAAAAAAABYAgAAEQAAABcAAAC8BAAAAQAAAAEAAAAAAAAAAQAAAAEBAQAAAAAATwAAAQAEAP///wdQAAAAAQAvAgACAP////+fwAMAAAACAAAACQAAAA==";

    internal static Specification[] All =>
    [
        new(nameof(IndependentLz4PlanesHaveCanonicalIdentity), IndependentLz4PlanesHaveCanonicalIdentity),
        new(nameof(Lz4AdmissionUsesActualSavingsAndBoundedExpansion), Lz4AdmissionUsesActualSavingsAndBoundedExpansion)
    ];

    private static void IndependentLz4PlanesHaveCanonicalIdentity()
    {
        byte[] reference = Convert.FromBase64String(ReferenceFrame);
        WaveformEnvelope decoded = WaveformEnvelopeCodec.Decode(reference, true);
        Check.That(decoded.Planes.Count == 2 && decoded.Planes[0].Samples.Count == 400 &&
            decoded.Planes[0].Samples.Select((value, index) => value == index % 2).All(value => value) &&
            decoded.Planes[1].Samples.Count == 600 && decoded.Planes[1].Samples.All(value => value == 2) &&
            decoded.Planes[1].QualityRanges.Single() == new WaveformQualityRange(3, 2, 9),
            "the negotiated decoder reads independently compressed reference planes and quality bytes");
        byte[] raw = WaveformEnvelopeCodec.EncodeRaw(decoded);
        byte[] packed = WaveformEnvelopeCodec.TryCompressRaw(raw)!;
        int hash = WaveformEnvelopeCodec.ContentSha256Offset;
        Check.That(packed[7] == 1 && packed.AsSpan(hash, 32).SequenceEqual(raw.AsSpan(hash, 32)) &&
            raw.AsSpan(hash, 32).SequenceEqual(reference.AsSpan(hash, 32)) &&
            raw.AsSpan().SequenceEqual(WaveformEnvelopeCodec.EncodeRaw(WaveformEnvelopeCodec.Decode(packed, true))),
            "encoding choice preserves every logical sample and normalized content hash");
        var ring = WaveformBlockRing.Start(decoded.SessionId, decoded.InstanceId, 1, 1, 0, 0, 50);
        Reject(() => ring.Append(reference), "BINARY_CODEC_VECTOR_UNSUPPORTED");
        ring.AppendTransport(packed, true, out _);
        Check.That(ring.AppendTransport(reference, true, out _).Status == WaveformBlockAppendStatus.AlreadyPresent &&
            ring.ReadFrom(0, 1).Blocks[0].RawEnvelope.AsSpan().SequenceEqual(raw),
            "the ring retains canonical raw blocks and recognizes alternate valid encodings as duplicates");
    }

    private static void Lz4AdmissionUsesActualSavingsAndBoundedExpansion()
    {
        byte[] reference = Convert.FromBase64String(ReferenceFrame);
        WaveformEnvelope template = WaveformEnvelopeCodec.Decode(reference, true);
        WaveformPlane plane = template.Planes[0];
        byte[] Wire(short[] samples) => WaveformEnvelopeCodec.EncodeRaw(template with { Planes = [plane with { Samples = samples }] });
        short[] noise = new short[2048];
        new Random(41).NextBytes(System.Runtime.InteropServices.MemoryMarshal.AsBytes(noise.AsSpan()));
        Check.That(WaveformEnvelopeCodec.TryCompressRaw(Wire(new short[511])) is null &&
            WaveformEnvelopeCodec.TryCompressRaw(Wire(new short[512])) is not null &&
            WaveformEnvelopeCodec.TryCompressRaw(Wire(noise)) is null, "size and net savings gate the optional codec");

        var ring = WaveformBlockRing.Start(template.SessionId, template.InstanceId, 1, 1, 0, 0, 50);
        byte[] invalid = [.. reference];
        invalid[^1] ^= 1;
        Reject(() => ring.AppendTransport(invalid, true, out _), "BINARY_PAYLOAD_CRC_MISMATCH");
        invalid = [.. reference];
        invalid[WaveformEnvelopeCodec.ContentSha256Offset] ^= 1;
        UpdateCrc(invalid);
        Reject(() => ring.AppendTransport(invalid, true, out _), "BINARY_CONTENT_HASH_MISMATCH");
        invalid = [.. reference];
        int payloadStart = WaveformEnvelopeCodec.PreludeSize + BinaryPrimitives.ReadUInt16LittleEndian(invalid.AsSpan(10));
        invalid[payloadStart] = 0;
        invalid[payloadStart + 1] = 0;
        invalid[payloadStart + 2] = 0;
        UpdateCrc(invalid);
        Reject(() => ring.AppendTransport(invalid, true, out _), "BINARY_LZ4_INVALID");
        invalid = [.. reference];
        BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(16), uint.MaxValue);
        UpdateCrc(invalid);
        Reject(() => ring.AppendTransport(invalid, true, out _), "BINARY_LENGTH_INVALID");
        Check.That(ring.NextBlockSequence == 0 && ring.Count == 0, "invalid packets never partially commit");
    }

    private static void UpdateCrc(byte[] wire)
    {
        int split = WaveformEnvelopeCodec.PreludeSize + BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(10));
        wire.AsSpan(20, 8).Clear();
        static uint Crc(ReadOnlySpan<byte> bytes)
        {
            uint value = uint.MaxValue;
            foreach (byte b in bytes) { value = BitOperations.Crc32C(value, b); }
            return ~value;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(20), Crc(wire.AsSpan(0, split)));
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(24), Crc(wire.AsSpan(split)));
    }

    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (WaveformEnvelopeException error) when (error.ReasonCode == reason) { return; }
        throw new InvalidOperationException($"Expected rejection {reason}.");
    }
}
