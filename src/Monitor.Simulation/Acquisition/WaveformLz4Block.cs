// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;

namespace Monitor.Simulation.Acquisition;

// Independent LZ4 Block sequences. The MWS1 descriptor supplies the exact output size.
internal static class WaveformLz4Block
{
    internal const int MatchTableSize = 4096;

    internal static bool TryEncode(ReadOnlySpan<byte> source, Span<byte> destination, Span<int> table, out int written)
    {
        written = 0;
        table.Fill(-1);
        int cursor = 0;
        int anchor = 0;
        while (cursor <= source.Length - 12)
        {
            uint word = BinaryPrimitives.ReadUInt32LittleEndian(source[cursor..]);
            int slot = (int)(unchecked(word * 2654435761u) >> 20);
            int previous = table[slot];
            table[slot] = cursor;
            if (previous < 0 || cursor - previous > ushort.MaxValue ||
                BinaryPrimitives.ReadUInt32LittleEndian(source[previous..]) != word)
            {
                cursor++;
                continue;
            }

            int length = 4;
            while (cursor + length < source.Length - 5 && source[cursor + length] == source[previous + length]) { length++; }
            if (!WriteSequence(source[anchor..cursor], cursor - previous, length, destination, ref written)) { return false; }
            cursor += length;
            anchor = cursor;
        }
        return WriteSequence(source[anchor..], 0, 0, destination, ref written);
    }

    private static bool WriteSequence(ReadOnlySpan<byte> literals, int distance, int matchLength, Span<byte> output, ref int count)
    {
        int matchTag = distance == 0 ? 0 : matchLength - 4;
        int required = 1 + ExtraBytes(literals.Length) + literals.Length + (distance == 0 ? 0 : 2 + ExtraBytes(matchTag));
        if (required > output.Length - count) { return false; }
        output[count++] = (byte)((Math.Min(15, literals.Length) << 4) | Math.Min(15, matchTag));
        WriteExtra(literals.Length, output, ref count);
        literals.CopyTo(output[count..]);
        count += literals.Length;
        if (distance != 0)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(output[count..], (ushort)distance);
            count += 2;
            WriteExtra(matchTag, output, ref count);
        }
        return true;
    }

    private static int ExtraBytes(int value) => value < 15 ? 0 : 1 + (value - 15) / 255;

    private static void WriteExtra(int value, Span<byte> output, ref int count)
    {
        if (value < 15) { return; }
        int remaining = value - 15;
        while (remaining >= 255) { output[count++] = 255; remaining -= 255; }
        output[count++] = (byte)remaining;
    }

    internal static bool TryDecode(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int read = 0;
        int written = 0;
        bool hadMatch = false;
        while (read < source.Length)
        {
            byte token = source[read++];
            if (!TryReadLength(token >> 4, source, ref read, destination.Length - written, out int literals) ||
                literals > source.Length - read)
            { return false; }
            source.Slice(read, literals).CopyTo(destination[written..]);
            read += literals;
            written += literals;
            if (read == source.Length)
            {
                return written == destination.Length && (!hadMatch || literals >= 5);
            }
            if (source.Length - read < 2 || destination.Length - written < 12) { return false; }
            int distance = BinaryPrimitives.ReadUInt16LittleEndian(source[read..]);
            read += 2;
            if (distance == 0 || distance > written ||
                !TryReadLength(token & 15, source, ref read, destination.Length - written - 4, out int matchTag))
            { return false; }
            int length = matchTag + 4;
            // Forward copies intentionally make new bytes available for overlapping matches.
            for (int i = 0; i < length; i++) { destination[written + i] = destination[written + i - distance]; }
            written += length;
            hadMatch = true;
        }
        return false;
    }

    private static bool TryReadLength(int tag, ReadOnlySpan<byte> source, ref int read, int available, out int length)
    {
        length = tag;
        if (length > available) { return false; }
        if (tag != 15) { return true; }
        byte extra;
        do
        {
            if (read >= source.Length) { return false; }
            extra = source[read++];
            if (extra > available - length) { return false; }
            length += extra;
        }
        while (extra == 255);
        return true;
    }
}
