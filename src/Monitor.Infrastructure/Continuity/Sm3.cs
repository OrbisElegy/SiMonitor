// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Numerics;

namespace Monitor.Infrastructure.Continuity;

internal static class Sm3
{
    private static ReadOnlySpan<uint> InitialState =>
    [
        0x7380166f,
        0x4914b2b9,
        0x172442d7,
        0xda8a0600,
        0xa96f30bc,
        0x163138aa,
        0xe38dee4d,
        0xb0fb0e4e,
    ];

    public static byte[] HashData(ReadOnlySpan<byte> data)
    {
        int paddedLength = checked((data.Length + 1 + 8 + 63) / 64 * 64);
        byte[] padded = new byte[paddedLength];
        data.CopyTo(padded);
        padded[data.Length] = 0x80;
        BinaryPrimitives.WriteUInt64BigEndian(
            padded.AsSpan(paddedLength - 8),
            checked((ulong)data.Length * 8));

        Span<uint> state = stackalloc uint[8];
        InitialState.CopyTo(state);
        Span<uint> words = stackalloc uint[68];
        Span<uint> expanded = stackalloc uint[64];
        for (int offset = 0; offset < padded.Length; offset += 64)
        {
            Compress(padded.AsSpan(offset, 64), state, words, expanded);
        }

        byte[] result = new byte[32];
        for (int index = 0; index < state.Length; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(
                result.AsSpan(index * sizeof(uint)),
                state[index]);
        }

        return result;
    }

    private static void Compress(
        ReadOnlySpan<byte> block,
        Span<uint> state,
        Span<uint> words,
        Span<uint> expanded)
    {
        for (int index = 0; index < 16; index++)
        {
            words[index] = BinaryPrimitives.ReadUInt32BigEndian(
                block[(index * sizeof(uint))..]);
        }

        for (int index = 16; index < words.Length; index++)
        {
            words[index] = P1(
                    words[index - 16] ^
                    words[index - 9] ^
                    BitOperations.RotateLeft(words[index - 3], 15)) ^
                BitOperations.RotateLeft(words[index - 13], 7) ^
                words[index - 6];
        }

        for (int index = 0; index < expanded.Length; index++)
        {
            expanded[index] = words[index] ^ words[index + 4];
        }

        uint a = state[0];
        uint b = state[1];
        uint c = state[2];
        uint d = state[3];
        uint e = state[4];
        uint f = state[5];
        uint g = state[6];
        uint h = state[7];
        for (int round = 0; round < expanded.Length; round++)
        {
            uint roundConstant = round < 16 ? 0x79cc4519U : 0x7a879d8aU;
            uint rotatedA = BitOperations.RotateLeft(a, 12);
            uint ss1 = BitOperations.RotateLeft(
                unchecked(
                    rotatedA + e +
                    BitOperations.RotateLeft(roundConstant, round)),
                7);
            uint ss2 = ss1 ^ rotatedA;
            uint tt1 = unchecked(
                BooleanF(a, b, c, round) + d + ss2 + expanded[round]);
            uint tt2 = unchecked(
                BooleanG(e, f, g, round) + h + ss1 + words[round]);
            d = c;
            c = BitOperations.RotateLeft(b, 9);
            b = a;
            a = tt1;
            h = g;
            g = BitOperations.RotateLeft(f, 19);
            f = e;
            e = P0(tt2);
        }

        state[0] ^= a;
        state[1] ^= b;
        state[2] ^= c;
        state[3] ^= d;
        state[4] ^= e;
        state[5] ^= f;
        state[6] ^= g;
        state[7] ^= h;
    }

    private static uint BooleanF(uint x, uint y, uint z, int round) =>
        round < 16 ? x ^ y ^ z : (x & y) | (x & z) | (y & z);

    private static uint BooleanG(uint x, uint y, uint z, int round) =>
        round < 16 ? x ^ y ^ z : (x & y) | (~x & z);

    private static uint P0(uint value) =>
        value ^
        BitOperations.RotateLeft(value, 9) ^
        BitOperations.RotateLeft(value, 17);

    private static uint P1(uint value) =>
        value ^
        BitOperations.RotateLeft(value, 15) ^
        BitOperations.RotateLeft(value, 23);
}
