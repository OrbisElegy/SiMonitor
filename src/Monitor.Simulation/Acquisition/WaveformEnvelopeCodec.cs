// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Monitor.Simulation.Acquisition;

public sealed class WaveformEnvelopeException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public enum WaveformQualityEncoding : byte
{
    None = 0,
    Ranges = 1,
}

public readonly record struct WaveformQualityRange(
    uint FirstSampleOffset,
    uint Count,
    uint QualityFlags);

public sealed record WaveformPlane(
    Guid ChannelId,
    uint SampleRateNumerator,
    uint SampleRateDenominator,
    ulong FirstSampleIndex,
    int ScaleNumerator,
    uint ScaleDenominator,
    int OffsetNumerator,
    uint OffsetDenominator,
    WaveformQualityEncoding QualityEncoding,
    IReadOnlyList<short> Samples,
    IReadOnlyList<WaveformQualityRange> QualityRanges);

public sealed record WaveformEnvelope(
    Guid SessionId,
    Guid InstanceId,
    ulong TimebaseEpoch,
    ulong StreamEpoch,
    ulong BlockSequence,
    ulong ConfigurationRevision,
    long StartSimTimeNs,
    uint DurationNs,
    IReadOnlyList<WaveformPlane> Planes);

public static class WaveformEnvelopeCodec
{
    public const int PreludeSize = 32;
    public const int FixedHeaderSize = 112;
    public const int PlaneDescriptorSize = 72;
    public const int QualityRangeSize = 12;
    public const int MaximumHeaderSize = 16_384;
    public const int MaximumPayloadSize = 262_144;
    public const int MaximumPlaneCount = 128;

    private static ReadOnlySpan<byte> Magic => "MWS1"u8;

    public static byte[] EncodeRaw(WaveformEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(envelope.Planes);
        ValidatedPlane[] planes = ValidateAndCanonicalize(envelope.Planes);

        int headerSize = checked(FixedHeaderSize + planes.Length * PlaneDescriptorSize);
        int payloadSize = 0;
        foreach (ValidatedPlane plane in planes)
        {
            payloadSize = checked(payloadSize + plane.RawBytes);
        }

        if (headerSize > MaximumHeaderSize || payloadSize > MaximumPayloadSize)
        {
            throw Error("BINARY_LENGTH_INVALID", nameof(envelope));
        }

        byte[] header = new byte[headerSize];
        byte[] payload = new byte[payloadSize];
        WriteUuid(envelope.SessionId, header.AsSpan(0, 16));
        WriteUuid(envelope.InstanceId, header.AsSpan(16, 16));
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(32), envelope.TimebaseEpoch);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(40), envelope.StreamEpoch);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(48), envelope.BlockSequence);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(56), envelope.ConfigurationRevision);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(64), envelope.StartSimTimeNs);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(72), envelope.DurationNs);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(76), checked((ushort)planes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(78), PlaneDescriptorSize);

        int payloadOffset = 0;
        for (int index = 0; index < planes.Length; index++)
        {
            ValidatedPlane plane = planes[index];
            Span<byte> descriptor = header.AsSpan(
                FixedHeaderSize + index * PlaneDescriptorSize,
                PlaneDescriptorSize);
            WriteUuid(plane.ChannelId, descriptor);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[16..], plane.SampleRateNumerator);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[20..], plane.SampleRateDenominator);
            BinaryPrimitives.WriteUInt64LittleEndian(descriptor[24..], plane.FirstSampleIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(
                descriptor[32..],
                checked((uint)plane.Samples.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[36..], checked((uint)payloadOffset));
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[40..], checked((uint)plane.RawBytes));
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[44..], checked((uint)plane.RawBytes));
            BinaryPrimitives.WriteInt32LittleEndian(descriptor[48..], plane.ScaleNumerator);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[52..], plane.ScaleDenominator);
            BinaryPrimitives.WriteInt32LittleEndian(descriptor[56..], plane.OffsetNumerator);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor[60..], plane.OffsetDenominator);
            descriptor[64] = 1;
            descriptor[65] = (byte)plane.QualityEncoding;
            BinaryPrimitives.WriteUInt16LittleEndian(
                descriptor[66..],
                checked((ushort)plane.QualityRanges.Length));

            Span<byte> planePayload = payload.AsSpan(payloadOffset, plane.RawBytes);
            int planeOffset = 0;
            foreach (short sample in plane.Samples)
            {
                BinaryPrimitives.WriteInt16LittleEndian(planePayload[planeOffset..], sample);
                planeOffset += sizeof(short);
            }

            foreach (WaveformQualityRange range in plane.QualityRanges)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    planePayload[planeOffset..],
                    range.FirstSampleOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    planePayload[(planeOffset + 4)..],
                    range.Count);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    planePayload[(planeOffset + 8)..],
                    range.QualityFlags);
                planeOffset += QualityRangeSize;
            }

            payloadOffset += plane.RawBytes;
        }

        byte[] contentHash = ComputeSha256(header, payload);
        contentHash.CopyTo(header, 80);

        byte[] prelude = new byte[PreludeSize];
        Magic.CopyTo(prelude);
        prelude[4] = 1;
        prelude[5] = 0;
        prelude[6] = 1;
        prelude[7] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(prelude.AsSpan(10), checked((ushort)headerSize));
        BinaryPrimitives.WriteUInt32LittleEndian(prelude.AsSpan(12), checked((uint)payloadSize));
        BinaryPrimitives.WriteUInt32LittleEndian(prelude.AsSpan(16), checked((uint)payloadSize));
        BinaryPrimitives.WriteUInt32LittleEndian(prelude.AsSpan(24), ComputeCrc32C(payload));
        BinaryPrimitives.WriteUInt32LittleEndian(
            prelude.AsSpan(20),
            ComputeHeaderCrc32C(prelude, header));

        byte[] wire = new byte[PreludeSize + headerSize + payloadSize];
        prelude.CopyTo(wire, 0);
        header.CopyTo(wire, PreludeSize);
        payload.CopyTo(wire, PreludeSize + headerSize);
        return wire;
    }

    public static WaveformEnvelope Decode(ReadOnlySpan<byte> wire)
    {
        if (wire.Length < PreludeSize + FixedHeaderSize)
        {
            throw Error("BINARY_LENGTH_INVALID", nameof(wire));
        }

        if (!wire[..4].SequenceEqual(Magic))
        {
            throw Error("BINARY_MAGIC_INVALID", nameof(wire));
        }

        byte major = wire[4];
        byte minor = wire[5];
        byte messageType = wire[6];
        byte codec = wire[7];
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(wire[8..]);
        ushort headerSize = BinaryPrimitives.ReadUInt16LittleEndian(wire[10..]);
        uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(wire[12..]);
        uint rawPayloadSize = BinaryPrimitives.ReadUInt32LittleEndian(wire[16..]);
        uint declaredHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(wire[20..]);
        uint declaredPayloadCrc = BinaryPrimitives.ReadUInt32LittleEndian(wire[24..]);
        uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(wire[28..]);
        if (major != 1)
        {
            throw Error("SCHEMA_MAJOR_UNSUPPORTED", nameof(wire));
        }

        if (minor != 0)
        {
            throw Error("SCHEMA_MINOR_UNSUPPORTED", nameof(wire));
        }

        if (messageType != 1 || codec > 1 || flags != 0 || reserved != 0)
        {
            throw Error("BINARY_HEADER_INVALID", nameof(wire));
        }

        long expectedWireSize = PreludeSize + (long)headerSize + payloadSize;
        if (headerSize is < FixedHeaderSize or > MaximumHeaderSize ||
            payloadSize > MaximumPayloadSize ||
            rawPayloadSize > MaximumPayloadSize ||
            expectedWireSize != wire.Length)
        {
            throw Error("BINARY_LENGTH_INVALID", nameof(wire));
        }

        ReadOnlySpan<byte> prelude = wire[..PreludeSize];
        ReadOnlySpan<byte> header = wire.Slice(PreludeSize, headerSize);
        ReadOnlySpan<byte> payload = wire[(PreludeSize + headerSize)..];
        if (ComputeHeaderCrc32C(prelude, header) != declaredHeaderCrc)
        {
            throw Error("BINARY_HEADER_CRC_MISMATCH", nameof(wire));
        }

        if (ComputeCrc32C(payload) != declaredPayloadCrc)
        {
            throw Error("BINARY_PAYLOAD_CRC_MISMATCH", nameof(wire));
        }

        ushort planeCount = BinaryPrimitives.ReadUInt16LittleEndian(header[76..]);
        ushort descriptorSize = BinaryPrimitives.ReadUInt16LittleEndian(header[78..]);
        int expectedHeaderSize = checked(FixedHeaderSize + planeCount * PlaneDescriptorSize);
        if (planeCount > MaximumPlaneCount ||
            descriptorSize != PlaneDescriptorSize ||
            headerSize != expectedHeaderSize)
        {
            throw Error("BINARY_DESCRIPTOR_INVALID", nameof(wire));
        }

        if (codec != 0)
        {
            throw Error("BINARY_CODEC_VECTOR_UNSUPPORTED", nameof(wire));
        }

        if (payloadSize != rawPayloadSize)
        {
            throw Error("BINARY_LENGTH_INVALID", nameof(wire));
        }

        DecodedDescriptor[] descriptors = new DecodedDescriptor[planeCount];
        byte[]? previousChannel = null;
        uint expectedPayloadOffset = 0;
        for (int index = 0; index < planeCount; index++)
        {
            ReadOnlySpan<byte> descriptor = header.Slice(
                FixedHeaderSize + index * PlaneDescriptorSize,
                PlaneDescriptorSize);
            byte[] channelBytes = descriptor[..16].ToArray();
            if (previousChannel is not null && CompareBytes(channelBytes, previousChannel) <= 0)
            {
                throw Error("BINARY_CHANNEL_ORDER_INVALID", nameof(wire));
            }

            previousChannel = channelBytes;
            uint rateNumerator = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[16..]);
            uint rateDenominator = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[20..]);
            ulong firstSampleIndex = BinaryPrimitives.ReadUInt64LittleEndian(descriptor[24..]);
            uint sampleCount = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[32..]);
            uint payloadOffset = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[36..]);
            uint planeBytes = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[40..]);
            uint rawPlaneBytes = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[44..]);
            int scaleNumerator = BinaryPrimitives.ReadInt32LittleEndian(descriptor[48..]);
            uint scaleDenominator = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[52..]);
            int offsetNumerator = BinaryPrimitives.ReadInt32LittleEndian(descriptor[56..]);
            uint offsetDenominator = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[60..]);
            byte sampleFormat = descriptor[64];
            byte qualityEncoding = descriptor[65];
            ushort qualityRangeCount = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[66..]);
            uint descriptorReserved = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[68..]);
            ulong expectedRawBytes = (ulong)sampleCount * sizeof(short) +
                (ulong)qualityRangeCount * QualityRangeSize;
            ulong planeEnd = (ulong)payloadOffset + planeBytes;
            if (rateNumerator == 0 || rateDenominator == 0 ||
                scaleDenominator == 0 || offsetDenominator == 0 ||
                sampleFormat != 1 || qualityEncoding > 1 || descriptorReserved != 0 ||
                expectedRawBytes != rawPlaneBytes || planeBytes != rawPlaneBytes ||
                payloadOffset != expectedPayloadOffset || planeEnd > payloadSize)
            {
                throw Error("BINARY_DESCRIPTOR_INVALID", nameof(wire));
            }

            descriptors[index] = new DecodedDescriptor(
                ReadUuid(channelBytes),
                rateNumerator,
                rateDenominator,
                firstSampleIndex,
                sampleCount,
                payloadOffset,
                scaleNumerator,
                scaleDenominator,
                offsetNumerator,
                offsetDenominator,
                (WaveformQualityEncoding)qualityEncoding,
                qualityRangeCount);
            expectedPayloadOffset = checked(expectedPayloadOffset + planeBytes);
        }

        if (expectedPayloadOffset != payloadSize)
        {
            throw Error("BINARY_DESCRIPTOR_INVALID", nameof(wire));
        }

        byte[] canonicalHeader = header.ToArray();
        byte[] declaredContentHash = canonicalHeader.AsSpan(80, 32).ToArray();
        canonicalHeader.AsSpan(80, 32).Clear();
        byte[] actualContentHash = ComputeSha256(canonicalHeader, payload);
        if (!CryptographicOperations.FixedTimeEquals(declaredContentHash, actualContentHash))
        {
            throw Error("BINARY_CONTENT_HASH_MISMATCH", nameof(wire));
        }

        WaveformPlane[] planes = new WaveformPlane[planeCount];
        for (int index = 0; index < descriptors.Length; index++)
        {
            DecodedDescriptor descriptor = descriptors[index];
            int sampleCount = checked((int)descriptor.SampleCount);
            short[] samples = new short[sampleCount];
            ReadOnlySpan<byte> planePayload = payload[(int)descriptor.PayloadOffset..];
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                samples[sampleIndex] = BinaryPrimitives.ReadInt16LittleEndian(
                    planePayload[(sampleIndex * sizeof(short))..]);
            }

            WaveformQualityRange[] qualityRanges =
                new WaveformQualityRange[descriptor.QualityRangeCount];
            int qualityOffset = sampleCount * sizeof(short);
            ulong previousRangeEnd = 0;
            for (int rangeIndex = 0; rangeIndex < qualityRanges.Length; rangeIndex++)
            {
                uint firstSampleOffset = BinaryPrimitives.ReadUInt32LittleEndian(
                    planePayload[qualityOffset..]);
                uint count = BinaryPrimitives.ReadUInt32LittleEndian(
                    planePayload[(qualityOffset + 4)..]);
                uint qualityFlags = BinaryPrimitives.ReadUInt32LittleEndian(
                    planePayload[(qualityOffset + 8)..]);
                ulong rangeEnd = (ulong)firstSampleOffset + count;
                if (firstSampleOffset < previousRangeEnd || rangeEnd > descriptor.SampleCount)
                {
                    throw Error("BINARY_DESCRIPTOR_INVALID", nameof(wire));
                }

                qualityRanges[rangeIndex] = new WaveformQualityRange(
                    firstSampleOffset,
                    count,
                    qualityFlags);
                previousRangeEnd = rangeEnd;
                qualityOffset += QualityRangeSize;
            }

            planes[index] = new WaveformPlane(
                descriptor.ChannelId,
                descriptor.SampleRateNumerator,
                descriptor.SampleRateDenominator,
                descriptor.FirstSampleIndex,
                descriptor.ScaleNumerator,
                descriptor.ScaleDenominator,
                descriptor.OffsetNumerator,
                descriptor.OffsetDenominator,
                descriptor.QualityEncoding,
                Array.AsReadOnly(samples),
                Array.AsReadOnly(qualityRanges));
        }

        return new WaveformEnvelope(
            ReadUuid(header[..16]),
            ReadUuid(header[16..32]),
            BinaryPrimitives.ReadUInt64LittleEndian(header[32..]),
            BinaryPrimitives.ReadUInt64LittleEndian(header[40..]),
            BinaryPrimitives.ReadUInt64LittleEndian(header[48..]),
            BinaryPrimitives.ReadUInt64LittleEndian(header[56..]),
            BinaryPrimitives.ReadInt64LittleEndian(header[64..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[72..]),
            Array.AsReadOnly(planes));
    }

    private static ValidatedPlane[] ValidateAndCanonicalize(
        IReadOnlyList<WaveformPlane> sourcePlanes)
    {
        if (sourcePlanes.Count > MaximumPlaneCount)
        {
            throw Error("BINARY_DESCRIPTOR_INVALID", nameof(sourcePlanes));
        }

        ValidatedPlane[] planes = new ValidatedPlane[sourcePlanes.Count];
        long totalRawBytes = 0;
        for (int index = 0; index < sourcePlanes.Count; index++)
        {
            WaveformPlane plane = sourcePlanes[index] ??
                throw Error("BINARY_DESCRIPTOR_INVALID", nameof(sourcePlanes));
            ArgumentNullException.ThrowIfNull(plane.Samples);
            ArgumentNullException.ThrowIfNull(plane.QualityRanges);
            if (plane.SampleRateNumerator == 0 || plane.SampleRateDenominator == 0 ||
                plane.ScaleDenominator == 0 || plane.OffsetDenominator == 0 ||
                !Enum.IsDefined(plane.QualityEncoding) ||
                plane.QualityRanges.Count > ushort.MaxValue)
            {
                throw Error("BINARY_DESCRIPTOR_INVALID", nameof(sourcePlanes));
            }

            long rawBytes = (long)plane.Samples.Count * sizeof(short) +
                (long)plane.QualityRanges.Count * QualityRangeSize;
            totalRawBytes += rawBytes;
            if (totalRawBytes > MaximumPayloadSize)
            {
                throw Error("BINARY_LENGTH_INVALID", nameof(sourcePlanes));
            }

            short[] samples = new short[plane.Samples.Count];
            for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                samples[sampleIndex] = plane.Samples[sampleIndex];
            }

            WaveformQualityRange[] qualityRanges =
                new WaveformQualityRange[plane.QualityRanges.Count];
            for (int rangeIndex = 0; rangeIndex < qualityRanges.Length; rangeIndex++)
            {
                qualityRanges[rangeIndex] = plane.QualityRanges[rangeIndex];
            }

            ValidateQualityRanges(qualityRanges, checked((uint)samples.Length), nameof(sourcePlanes));
            byte[] channelBytes = new byte[16];
            WriteUuid(plane.ChannelId, channelBytes);
            planes[index] = new ValidatedPlane(
                plane.ChannelId,
                channelBytes,
                plane.SampleRateNumerator,
                plane.SampleRateDenominator,
                plane.FirstSampleIndex,
                plane.ScaleNumerator,
                plane.ScaleDenominator,
                plane.OffsetNumerator,
                plane.OffsetDenominator,
                plane.QualityEncoding,
                samples,
                qualityRanges,
                checked((int)rawBytes));
        }

        Array.Sort(planes, static (left, right) => CompareBytes(
            left.ChannelBytes,
            right.ChannelBytes));
        for (int index = 1; index < planes.Length; index++)
        {
            if (CompareBytes(planes[index - 1].ChannelBytes, planes[index].ChannelBytes) == 0)
            {
                throw Error("BINARY_CHANNEL_ORDER_INVALID", nameof(sourcePlanes));
            }
        }

        return planes;
    }

    private static void ValidateQualityRanges(
        IReadOnlyList<WaveformQualityRange> ranges,
        uint sampleCount,
        string parameterName)
    {
        ulong previousEnd = 0;
        foreach (WaveformQualityRange range in ranges)
        {
            ulong end = (ulong)range.FirstSampleOffset + range.Count;
            if (range.FirstSampleOffset < previousEnd || end > sampleCount)
            {
                throw Error("BINARY_DESCRIPTOR_INVALID", parameterName);
            }

            previousEnd = end;
        }
    }

    private static byte[] ComputeSha256(ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(header);
        hash.AppendData(payload);
        return hash.GetHashAndReset();
    }

    private static uint ComputeHeaderCrc32C(
        ReadOnlySpan<byte> prelude,
        ReadOnlySpan<byte> header)
    {
        Span<byte> normalizedPrelude = stackalloc byte[PreludeSize];
        prelude.CopyTo(normalizedPrelude);
        normalizedPrelude[20..28].Clear();
        uint crc = UpdateCrc32C(uint.MaxValue, normalizedPrelude);
        return ~UpdateCrc32C(crc, header);
    }

    private static uint ComputeCrc32C(ReadOnlySpan<byte> bytes) =>
        ~UpdateCrc32C(uint.MaxValue, bytes);

    private static uint UpdateCrc32C(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0x82F63B78U & (uint)-(int)(crc & 1));
            }
        }

        return crc;
    }

    private static int CompareBytes(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        for (int index = 0; index < left.Length; index++)
        {
            int comparison = left[index].CompareTo(right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static void WriteUuid(Guid value, Span<byte> destination)
    {
        if (!value.TryWriteBytes(destination, bigEndian: true, out int bytesWritten) ||
            bytesWritten != 16)
        {
            throw Error("BINARY_DESCRIPTOR_INVALID", nameof(value));
        }
    }

    private static Guid ReadUuid(ReadOnlySpan<byte> source) => new(source, bigEndian: true);

    private static WaveformEnvelopeException Error(string reasonCode, string parameterName) =>
        new(reasonCode, parameterName);

    private sealed record ValidatedPlane(
        Guid ChannelId,
        byte[] ChannelBytes,
        uint SampleRateNumerator,
        uint SampleRateDenominator,
        ulong FirstSampleIndex,
        int ScaleNumerator,
        uint ScaleDenominator,
        int OffsetNumerator,
        uint OffsetDenominator,
        WaveformQualityEncoding QualityEncoding,
        short[] Samples,
        WaveformQualityRange[] QualityRanges,
        int RawBytes);

    private sealed record DecodedDescriptor(
        Guid ChannelId,
        uint SampleRateNumerator,
        uint SampleRateDenominator,
        ulong FirstSampleIndex,
        uint SampleCount,
        uint PayloadOffset,
        int ScaleNumerator,
        uint ScaleDenominator,
        int OffsetNumerator,
        uint OffsetDenominator,
        WaveformQualityEncoding QualityEncoding,
        ushort QualityRangeCount);
}
