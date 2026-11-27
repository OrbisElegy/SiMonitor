// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Monitor.Network;

public sealed record ExamReplicaCursor(long Sequence, string Hash)
{
    internal static readonly ExamReplicaCursor Empty = new(0, new string('0', 64));
}

public sealed record ExamReplicaCommand(Guid RequestId, string Kind, byte[] Payload);
internal sealed record ExamReplicaElection(long AuthorityEpoch, long FencingToken, Guid? OwnerId, bool Qualified,
    bool Manual, string? OperatorName, string? Evidence)
{
    internal static readonly ExamReplicaElection Initial = new(0, 0, null, false, false, null, null);
}

internal sealed record ExamReplicaEntry(int Version, Guid ClusterId, long Sequence, long AuthorityEpoch,
    long FencingToken, string PreviousHash, string Hash, ExamReplicaCommand Command)
{
    internal const int MaxPayloadBytes = 1024 * 1024;
    internal ExamReplicaCursor Cursor => new(Sequence, Hash);

    internal static ExamReplicaEntry Create(Guid clusterId, ExamReplicaCursor previous, long epoch, long token, ExamReplicaCommand command)
    {
        var entry = new ExamReplicaEntry(1, clusterId, checked(previous.Sequence + 1), epoch, token, previous.Hash, "", command);
        return entry with { Hash = entry.ComputeHash() };
    }

    internal void Validate(Guid clusterId)
    {
        if (Version != 1 || ClusterId != clusterId || Sequence <= 0 || AuthorityEpoch <= 0 || FencingToken <= 0 ||
            !ClassroomArtifactDescriptor.IsContentHash(PreviousHash) || !ClassroomArtifactDescriptor.IsContentHash(Hash) ||
            Command is null)
        { throw new InvalidDataException("Invalid exam transaction header."); }
        ValidateCommand(Command);
        if (Hash != ComputeHash()) { throw new InvalidDataException("Exam transaction content hash mismatch."); }
    }

    internal static void ValidateCommand(ExamReplicaCommand command)
    {
        if (command is null || command.RequestId == Guid.Empty || command.Payload is not { Length: > 0 and <= MaxPayloadBytes } ||
            command.Kind is not ("Answer" or "Control" or "Checkpoint" or "Audit"))
        { throw new InvalidDataException("Unsupported exam transaction type or size."); }
    }

    internal static bool SameRequest(ExamReplicaCommand a, ExamReplicaCommand b) =>
        a.RequestId == b.RequestId && a.Kind == b.Kind && a.Payload.AsSpan().SequenceEqual(b.Payload);

    private string ComputeHash()
    {
        // The fixed-width header uses network-order UUIDs and big-endian integers.
        // Kind is NUL-terminated ASCII, followed by the exact versioned application payload.
        Span<byte> header = stackalloc byte[56];
        ClusterId.TryWriteBytes(header[..16], bigEndian: true, out _);
        Command.RequestId.TryWriteBytes(header.Slice(16, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteInt64BigEndian(header[32..], Sequence);
        BinaryPrimitives.WriteInt64BigEndian(header[40..], AuthorityEpoch);
        BinaryPrimitives.WriteInt64BigEndian(header[48..], FencingToken);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData("SiMonitor.ExamTransaction.v1\0"u8);
        digest.AppendData(header);
        digest.AppendData(Convert.FromHexString(PreviousHash));
        digest.AppendData(Encoding.ASCII.GetBytes(Command.Kind));
        digest.AppendData([0]);
        digest.AppendData(Command.Payload);
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }
}
