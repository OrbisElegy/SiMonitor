// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Monitor.Simulation.Determinism;

public sealed record DeterminismCheckpoint(
    long SimTimeNs,
    ulong PhaseU64,
    long FilterYQ32,
    IReadOnlyList<DeterministicStreamState> Streams);

public sealed class DeterminismCheckpointException(string reasonCode) : Exception(reasonCode)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed class DeterminismCheckpointCodec
{
    private const ushort MajorVersion = 1;
    private const ushort MinorVersion = 0;
    private const int FixedSize = 66;
    private const int StreamFixedSize = 42;

    private static ReadOnlySpan<byte> Magic => "MDET"u8;

    private readonly byte[] _profileHash;

    public DeterminismCheckpointCodec(ReadOnlySpan<byte> profileContentSha256)
    {
        if (profileContentSha256.Length != 32)
        {
            throw new ArgumentException("The determinism profile hash must be 32 bytes.",
                nameof(profileContentSha256));
        }

        _profileHash = profileContentSha256.ToArray();
    }

    public byte[] Serialize(DeterminismCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(checkpoint.Streams);
        if (checkpoint.Streams.Count > ushort.MaxValue)
        {
            throw new ArgumentException("The checkpoint contains too many streams.", nameof(checkpoint));
        }

        DeterministicStreamState[] streams = [.. checkpoint.Streams.OrderBy(
            stream => stream.StreamId,
            StringComparer.Ordinal)];
        int length = FixedSize;
        string? previous = null;
        foreach (DeterministicStreamState stream in streams)
        {
            byte[] id = DeterministicStreamFactory.ValidateStreamId(stream.StreamId);
            if (StringComparer.Ordinal.Equals(previous, stream.StreamId))
            {
                throw new ArgumentException("Checkpoint stream IDs must be unique.", nameof(checkpoint));
            }

            previous = stream.StreamId;
            length = checked(length + StreamFixedSize + id.Length);
        }

        byte[] bytes = new byte[length];
        int offset = 0;
        Write(Magic);
        WriteUInt16(MajorVersion);
        WriteUInt16(MinorVersion);
        Write(_profileHash);
        WriteInt64(checkpoint.SimTimeNs);
        WriteUInt64(checkpoint.PhaseU64);
        WriteInt64(checkpoint.FilterYQ32);
        WriteUInt16(checked((ushort)streams.Length));
        foreach (DeterministicStreamState stream in streams)
        {
            byte[] id = Encoding.ASCII.GetBytes(stream.StreamId);
            WriteUInt16(checked((ushort)id.Length));
            Write(id);
            WriteUInt64(stream.S0);
            WriteUInt64(stream.S1);
            WriteUInt64(stream.S2);
            WriteUInt64(stream.S3);
            WriteUInt64(stream.DrawCount);
        }

        return bytes;

        void Write(ReadOnlySpan<byte> value)
        {
            value.CopyTo(bytes.AsSpan(offset));
            offset += value.Length;
        }

        void WriteUInt16(ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
            offset += sizeof(ushort);
        }

        void WriteUInt64(ulong value)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
            offset += sizeof(ulong);
        }

        void WriteInt64(long value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset), value);
            offset += sizeof(long);
        }
    }

    public DeterminismCheckpoint Restore(ReadOnlySpan<byte> bytes)
    {
        byte[] data = bytes.ToArray();
        int offset = 0;
        if (!Take(Magic.Length).SequenceEqual(Magic))
        {
            throw Reject("Checkpoint.BadMagic");
        }

        if (ReadUInt16() != MajorVersion || ReadUInt16() != MinorVersion)
        {
            throw Reject("Checkpoint.UnsupportedVersion");
        }

        if (!CryptographicOperations.FixedTimeEquals(Take(32), _profileHash))
        {
            throw Reject("Checkpoint.ProfileHashMismatch");
        }

        long simTimeNs = ReadInt64();
        ulong phase = ReadUInt64();
        long filterY = ReadInt64();
        int streamCount = ReadUInt16();
        List<DeterministicStreamState> streams = new(streamCount);
        string? previous = null;
        for (int index = 0; index < streamCount; index++)
        {
            int idLength = ReadUInt16();
            string streamId = Encoding.ASCII.GetString(Take(idLength));
            try
            {
                DeterministicStreamFactory.ValidateStreamId(streamId);
            }
            catch (ArgumentException)
            {
                throw Reject("Checkpoint.InvalidStreamId");
            }

            if (previous is not null && StringComparer.Ordinal.Compare(previous, streamId) >= 0)
            {
                throw Reject("Checkpoint.StreamOrder");
            }

            previous = streamId;
            streams.Add(new DeterministicStreamState(
                streamId,
                ReadUInt64(),
                ReadUInt64(),
                ReadUInt64(),
                ReadUInt64(),
                ReadUInt64()));
        }

        if (offset != data.Length)
        {
            throw Reject("Checkpoint.TrailingBytes");
        }

        return new DeterminismCheckpoint(simTimeNs, phase, filterY, streams);

        ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || offset > data.Length - count)
            {
                throw Reject("Checkpoint.Truncated");
            }

            ReadOnlySpan<byte> value = data.AsSpan(offset, count);
            offset += count;
            return value;
        }

        ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(sizeof(ushort)));

        ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(sizeof(ulong)));

        long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Take(sizeof(long)));
    }

    private static DeterminismCheckpointException Reject(string reasonCode) => new(reasonCode);
}
