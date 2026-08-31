// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class WaveformEnvelopeCodecSpecifications
{
    private const string GoldenWireHex =
        "4d575331010001000000b80004000000040000006396ebfd15cb23f500000000" +
        "1111111111114111811111111111111122222222222242228222222222222222" +
        "0100000000000000010000000000000001000000000000000100000000000000" +
        "000000000000000000c2eb0b010048009c85d8ede5b9b5e71fe1ee98d7e80752" +
        "6b8f20495dc9b1d89c61330f1d4075cc33333333333343338333333333333333" +
        "0a00000001000000000000000000000002000000000000000400000004000000" +
        "01000000010000000000000001000000010000000000000064009cff";

    private const string GoldenContentHash =
        "9c85d8ede5b9b5e71fe1ee98d7e807526b8f20495dc9b1d89c61330f1d4075cc";

    public static Specification[] All =>
    [
        new(nameof(RawGoldenVectorRoundTripsByteForByte), RawGoldenVectorRoundTripsByteForByte),
        new(nameof(MultiPlaneEncodingIsCanonical), MultiPlaneEncodingIsCanonical),
        new(nameof(WireCorruptionFailsClosedInValidationOrder),
            WireCorruptionFailsClosedInValidationOrder),
        new(nameof(InvalidPlaneModelsAreRejectedBeforeEncoding),
            InvalidPlaneModelsAreRejectedBeforeEncoding),
    ];

    private static void RawGoldenVectorRoundTripsByteForByte()
    {
        byte[] golden = Convert.FromHexString(GoldenWireHex);
        WaveformEnvelope envelope = WaveformEnvelopeCodec.Decode(golden);
        Check.That(envelope.SessionId == Guid.Parse("11111111-1111-4111-8111-111111111111") &&
            envelope.InstanceId == Guid.Parse("22222222-2222-4222-8222-222222222222") &&
            envelope.TimebaseEpoch == 1 &&
            envelope.StreamEpoch == 1 &&
            envelope.BlockSequence == 1 &&
            envelope.ConfigurationRevision == 1 &&
            envelope.StartSimTimeNs == 0 &&
            envelope.DurationNs == 200_000_000,
            "the decoded fixed header must match the frozen golden vector");

        WaveformPlane plane = envelope.Planes.Single();
        Check.That(plane.ChannelId == Guid.Parse("33333333-3333-4333-8333-333333333333") &&
            plane.SampleRateNumerator == 10 &&
            plane.SampleRateDenominator == 1 &&
            plane.FirstSampleIndex == 0 &&
            plane.ScaleNumerator == 1 &&
            plane.ScaleDenominator == 1 &&
            plane.OffsetNumerator == 0 &&
            plane.OffsetDenominator == 1 &&
            plane.QualityEncoding == WaveformQualityEncoding.None &&
            plane.Samples.SequenceEqual(new short[] { 100, -100 }) &&
            plane.QualityRanges.Count == 0,
            "the decoded plane must preserve every frozen descriptor and sample field");
        Check.That(Convert.ToHexStringLower(golden.AsSpan(112, 32)) == GoldenContentHash,
            "the declared golden content identity must occupy the frozen header field");
        Check.That(WaveformEnvelopeCodec.EncodeRaw(envelope).SequenceEqual(golden),
            "decoding and re-encoding the raw golden vector must be byte exact");
    }

    private static void MultiPlaneEncodingIsCanonical()
    {
        WaveformPlane laterChannel = new(
            Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
            125,
            1,
            50,
            1,
            100,
            -2,
            5,
            WaveformQualityEncoding.Ranges,
            new short[] { short.MinValue, -1, 0, 1, short.MaxValue },
            new[]
            {
                new WaveformQualityRange(1, 2, 0x0000_0005),
                new WaveformQualityRange(4, 1, 0x8000_0000),
            });
        WaveformPlane earlierChannel = new(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            250,
            1,
            100,
            -3,
            10,
            7,
            2,
            WaveformQualityEncoding.None,
            new short[] { 7, 8, 9 },
            Array.Empty<WaveformQualityRange>());
        WaveformEnvelope source = new(
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"),
            3,
            9,
            11,
            15,
            400_000_000,
            200_000_000,
            new[] { laterChannel, earlierChannel });

        byte[] wire = WaveformEnvelopeCodec.EncodeRaw(source);
        WaveformEnvelope decoded = WaveformEnvelopeCodec.Decode(wire);
        Check.That(decoded.Planes.Select(plane => plane.ChannelId).SequenceEqual(new[]
        {
            earlierChannel.ChannelId,
            laterChannel.ChannelId,
        }),
            "planes must be encoded in unsigned RFC 4122 channel-byte order");
        Check.That(Equivalent(decoded.Planes[0], earlierChannel) &&
            Equivalent(decoded.Planes[1], laterChannel),
            "canonical sorting must not alter plane metadata, samples or quality ranges");
        Check.That(WaveformEnvelopeCodec.EncodeRaw(decoded).SequenceEqual(wire),
            "a canonical multi-plane wire image must remain byte stable");
    }

    private static void WireCorruptionFailsClosedInValidationOrder()
    {
        byte[] golden = Convert.FromHexString(GoldenWireHex);

        byte[] payloadCorrupt = [.. golden];
        payloadCorrupt[^1] ^= 1;
        Check.That(Reason(() => WaveformEnvelopeCodec.Decode(payloadCorrupt)) ==
            "BINARY_PAYLOAD_CRC_MISMATCH",
            "the frozen payload corruption vector must fail at the payload CRC gate");

        byte[] contentCorrupt = [.. payloadCorrupt];
        uint payloadCrc = Crc32C(contentCorrupt.AsSpan(216));
        BinaryPrimitives.WriteUInt32LittleEndian(contentCorrupt.AsSpan(24), payloadCrc);
        Check.That(Reason(() => WaveformEnvelopeCodec.Decode(contentCorrupt)) ==
            "BINARY_CONTENT_HASH_MISMATCH",
            "repaired transport CRC cannot conceal changed logical content");

        byte[] headerCorrupt = [.. golden];
        headerCorrupt[88] ^= 1;
        Check.That(Reason(() => WaveformEnvelopeCodec.Decode(headerCorrupt)) ==
            "BINARY_HEADER_CRC_MISMATCH",
            "a changed transmitted header must fail before descriptor parsing");

        byte[] wrongLength = [.. golden, 0];
        Check.That(Reason(() => WaveformEnvelopeCodec.Decode(wrongLength)) ==
            "BINARY_LENGTH_INVALID",
            "trailing bytes must not be silently ignored");
    }

    private static void InvalidPlaneModelsAreRejectedBeforeEncoding()
    {
        var channelId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        WaveformPlane overlappingRanges = new(
            channelId,
            250,
            1,
            0,
            1,
            1,
            0,
            1,
            WaveformQualityEncoding.Ranges,
            new short[] { 1, 2, 3 },
            new[]
            {
                new WaveformQualityRange(0, 2, 1),
                new WaveformQualityRange(1, 1, 2),
            });
        WaveformEnvelope invalidRanges = Envelope(overlappingRanges);
        Check.That(Reason(() => WaveformEnvelopeCodec.EncodeRaw(invalidRanges)) ==
            "BINARY_DESCRIPTOR_INVALID",
            "overlapping quality ranges must reject before bytes are emitted");

        WaveformPlane valid = overlappingRanges with
        {
            QualityRanges = Array.Empty<WaveformQualityRange>(),
            QualityEncoding = WaveformQualityEncoding.None,
        };
        WaveformEnvelope duplicateChannels = Envelope(valid, valid);
        Check.That(Reason(() => WaveformEnvelopeCodec.EncodeRaw(duplicateChannels)) ==
            "BINARY_CHANNEL_ORDER_INVALID",
            "duplicate channel identities must reject instead of relying on input order");

        WaveformPlane invalidRate = valid with
        {
            SampleRateNumerator = 0,
        };
        Check.That(Reason(() => WaveformEnvelopeCodec.EncodeRaw(Envelope(invalidRate))) ==
            "BINARY_DESCRIPTOR_INVALID",
            "a zero sample-rate component must reject before encoding");
    }

    private static WaveformEnvelope Envelope(params WaveformPlane[] planes) => new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"),
        Guid.Parse("22222222-2222-4222-8222-222222222222"),
        1,
        1,
        1,
        1,
        0,
        200_000_000,
        planes);

    private static bool Equivalent(WaveformPlane left, WaveformPlane right) =>
        left.ChannelId == right.ChannelId &&
        left.SampleRateNumerator == right.SampleRateNumerator &&
        left.SampleRateDenominator == right.SampleRateDenominator &&
        left.FirstSampleIndex == right.FirstSampleIndex &&
        left.ScaleNumerator == right.ScaleNumerator &&
        left.ScaleDenominator == right.ScaleDenominator &&
        left.OffsetNumerator == right.OffsetNumerator &&
        left.OffsetDenominator == right.OffsetDenominator &&
        left.QualityEncoding == right.QualityEncoding &&
        left.Samples.SequenceEqual(right.Samples) &&
        left.QualityRanges.SequenceEqual(right.QualityRanges);

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (WaveformEnvelopeException exception)
        {
            return exception.ReasonCode;
        }
    }

    private static uint Crc32C(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0x82F63B78U & (uint)-(int)(crc & 1));
            }
        }

        return ~crc;
    }
}
