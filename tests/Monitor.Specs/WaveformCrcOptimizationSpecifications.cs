// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class WaveformCrcOptimizationSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(EnvelopeChecksumsMatchBitwiseOracleAcrossBlockAndTailBoundaries),
            EnvelopeChecksumsMatchBitwiseOracleAcrossBlockAndTailBoundaries),
    ];

    private static void EnvelopeChecksumsMatchBitwiseOracleAcrossBlockAndTailBoundaries()
    {
        AssertEnvelope(Array.Empty<WaveformPlane>());
        int[] sampleCounts = [0, 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 127, 128, 129, 1023, 1024, 1025, 32768];
        foreach (int sampleCount in sampleCounts)
        {
            short[] samples = Enumerable.Range(0, sampleCount)
                .Select(index => unchecked((short)(index * 257 - 32768)))
                .ToArray();
            WaveformPlane plane = new(
                Guid.Parse("33333333-3333-4333-8333-333333333333"),
                500,
                1,
                0,
                1,
                1,
                0,
                1,
                WaveformQualityEncoding.None,
                samples,
                Array.Empty<WaveformQualityRange>());
            AssertEnvelope([plane]);
            if (sampleCount > 0)
            {
                AssertEnvelope([plane with
                {
                    QualityEncoding = WaveformQualityEncoding.Ranges,
                    QualityRanges = [new WaveformQualityRange(0, (uint)sampleCount, 0x8000_0001)],
                }]);
            }
        }
    }

    private static void AssertEnvelope(WaveformPlane[] planes)
    {
        WaveformEnvelope envelope = new(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            1,
            2,
            3,
            4,
            0,
            200_000_000,
            planes);
        byte[] wire = WaveformEnvelopeCodec.EncodeRaw(envelope);
        int headerEnd = WaveformEnvelopeCodec.PreludeSize +
            BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(10));
        byte[] normalizedHeader = wire.AsSpan(0, headerEnd).ToArray();
        normalizedHeader.AsSpan(20, 8).Clear();
        Check.That(BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(20)) == Crc32C(normalizedHeader),
            "the chained prelude/header CRC must match the independent bitwise polynomial oracle");
        Check.That(BinaryPrimitives.ReadUInt32LittleEndian(wire.AsSpan(24)) == Crc32C(wire.AsSpan(headerEnd)),
            "empty, short, word-sized and long payload CRCs must match the bitwise polynomial oracle");

        byte[] unalignedWire = new byte[wire.Length + 1];
        wire.CopyTo(unalignedWire, 1);
        WaveformEnvelope decoded = WaveformEnvelopeCodec.Decode(unalignedWire.AsSpan(1));
        Check.That(WaveformEnvelopeCodec.EncodeRaw(decoded).SequenceEqual(wire),
            "unaligned input must decode and retain byte-exact transport identity");
    }

    private static uint Crc32C(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0x82F63B78U);
            }
        }

        return ~crc;
    }
}
