// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Monitor.Infrastructure.Identity;
using Monitor.Network;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class ExamReplicaSpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(OnlyMatchingDurableCopiesAdvanceTheExamHead), OnlyMatchingDurableCopiesAdvanceTheExamHead),
        new(nameof(ReplicaRetainsItsTermBackupAndPrefixAcrossRestart), ReplicaRetainsItsTermBackupAndPrefixAcrossRestart),
        new(nameof(StandbyReplaysCheckpointBeforeAcceptingIt), StandbyReplaysCheckpointBeforeAcceptingIt),
        new(nameof(FreshLeaseCanRecoverAnEarlierDurableReceipt), FreshLeaseCanRecoverAnEarlierDurableReceipt),
    ];

    private static void OnlyMatchingDurableCopiesAdvanceTheExamHead()
    {
        using var pair = new Pair();
        using var a = pair.OpenA();
        using var b = pair.OpenB();
        a.RecordElection(pair.PrimaryTerm);
        b.RecordElection(pair.PrimaryTerm);
        var request = new ExamReplicaCommand(Guid.NewGuid(), "Answer", "chosen option private"u8.ToArray());
        var first = a.Prepare(7, 22, request);
        Check.That(a.Head.Sequence == 0 && a.Find(request.RequestId) is null && a.Read(1) is null &&
            a.Pending?.Hash == first.Hash, "preparing on the primary must not publish an answer");
        ShouldThrow<InvalidOperationException>(() => b.Prepare(7, 22, request));
        ShouldThrow<InvalidDataException>(() => a.Confirm(first.Cursor with { Hash = new string('7', 64) }));
        ShouldThrow<InvalidDataException>(() => b.Apply(first with { Command = request with { Payload = [42] } }));
        ShouldThrow<InvalidDataException>(() => a.Prepare(7, 22, request with { Payload = [43] }));
        ShouldThrow<InvalidOperationException>(() => a.Prepare(7, 22, request with { RequestId = Guid.NewGuid() }));
        Execute(pair.B, "CREATE TRIGGER disk_failure BEFORE UPDATE ON exam_state BEGIN SELECT RAISE(ABORT,'disk unavailable'); END;");
        ShouldThrow<SqliteException>(() => b.Apply(first));
        Check.That(b.Head.Sequence == 0 && b.Pending is null && a.Head.Sequence == 0, "failed durable apply must roll back the whole standby transaction");
        Execute(pair.B, "DROP TRIGGER disk_failure");
        var receipt = b.Apply(first);
        Check.That(a.Confirm(receipt) == receipt && b.Apply(first) == receipt && a.Confirm(receipt) == receipt &&
            a.Prepare(7, 22, request).Cursor == receipt && a.Find(request.RequestId)?.Hash == receipt.Hash,
            "lost responses must retry the same transaction on both nodes");
        var second = a.Prepare(7, 22, request with { RequestId = Guid.NewGuid() });
        a.DiscardPending();
        Check.That(a.Head == receipt && a.Pending is null, "discard may release only an unacknowledged tail");
        var newTerm = pair.PrimaryTerm with { AuthorityEpoch = 8, FencingToken = 23, OwnerId = pair.BId };
        b.RecordElection(newTerm);
        var replacement = b.Prepare(8, 23, request with { RequestId = Guid.NewGuid(), Payload = [5, 6] });
        Check.That(second.Sequence == replacement.Sequence && second.Hash != replacement.Hash, "divergent unconfirmed suffixes have separate hashes");
        a.RecordElection(newTerm);
        ShouldThrow<InvalidOperationException>(() => a.Prepare(7, 22, request));
        b.Confirm(a.Apply(replacement));
        Check.That(a.Head == b.Head && a.Head.Sequence == 2, "a new term can extend its verified committed prefix");
        using var wal = new FileStream(pair.A + "-wal", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] data = new byte[checked((int)wal.Length)];
        wal.ReadExactly(data);
        Check.That(data.AsSpan().IndexOf(request.Payload) < 0 && Encoding.ASCII.GetString(data).IndexOf(Convert.ToBase64String(request.Payload), StringComparison.Ordinal) < 0,
            "the local WAL must not carry answer bytes or their JSON representation");
    }

    private static void ReplicaRetainsItsTermBackupAndPrefixAcrossRestart()
    {
        using var pair = new Pair();
        var request = new ExamReplicaCommand(Guid.NewGuid(), "Control", [3, 4]);
        string backup = Path.Combine(pair.Directory, "backup.db");
        using (var a = pair.OpenA())
        using (var b = pair.OpenB())
        {
            a.RecordElection(pair.PrimaryTerm);
            var row = a.Prepare(7, 22, request);
            a.Confirm(b.Apply(row));
            a.Prepare(7, 22, request with { RequestId = Guid.NewGuid() });
            var evidence = pair.PrimaryTerm with
            {
                AuthorityEpoch = 8,
                FencingToken = 23,
                OwnerId = pair.BId,
                Manual = true,
                OperatorName = "invigilator",
                Evidence = "OldPrimaryPoweredOff:ticket123"
            };
            a.RecordElection(evidence);
            a.BackupTo(backup);
            ShouldThrow<IOException>(() => a.BackupTo(backup));
            Check.That(a.ReadElectionAudit(2) == evidence && a.ElectionRevision == 2, "named fence evidence remains readable");
            ShouldThrow<InvalidDataException>(() => a.RecordElection(pair.PrimaryTerm));
        }
        using (var restored = new ExamReplicaStore(backup, pair.Cluster, pair.AId, pair.Keys))
        {
            Check.That(restored.Head.Sequence == 1 && restored.Find(request.RequestId) is not null &&
                restored.Pending?.Sequence == 2 && restored.Election.Manual, "backup restores the exact transactional frontier and election audit");
            restored.DiscardPending();
        }
        ShouldThrow<CryptographicException>(() => { using var swapped = new ExamReplicaStore(backup, pair.Cluster, pair.BId, pair.Keys); });
        Execute(pair.A, "UPDATE exam_log SET record=zeroblob(length(record)) WHERE sequence=1");
        ShouldThrow<CryptographicException>(() => { using var corrupt = pair.OpenA(); });
        Check.That(!File.Exists(pair.A + ".missing"), "fixture path must not have been initialized");
        ShouldThrow<SqliteException>(() => { using var missing = new ExamReplicaStore(pair.A + ".missing", pair.Cluster, pair.AId, pair.Keys); });
        Check.That(!File.Exists(pair.A + ".missing"), "starting a missing exam replica must not provision an empty one");
    }

    private static void FreshLeaseCanRecoverAnEarlierDurableReceipt()
    {
        using var pair = new Pair();
        using var a = pair.OpenA();
        using var b = pair.OpenB();
        a.RecordElection(pair.PrimaryTerm);
        var request = new ExamReplicaCommand(Guid.NewGuid(), "Answer", [5, 7]);
        var old = a.Prepare(7, 22, request);
        var retained = b.Apply(old);
        a.RecordElection(pair.PrimaryTerm with { AuthorityEpoch = 8, FencingToken = 23 });
        ShouldThrow<InvalidOperationException>(() => a.Confirm(retained));
        ShouldThrow<InvalidOperationException>(() => a.ConfirmReplicated(retained, 7, 22));
        ShouldThrow<InvalidDataException>(() => a.ConfirmReplicated(retained with { Hash = new string('0', 64) }, 8, 23));
        Check.That(a.Head.Sequence == 0 && a.ConfirmReplicated(retained, 8, 23) == retained && a.Head == b.Head,
            "only the current term plus an exact peer proof can publish a prepared transaction from an earlier term");
    }

    private static void StandbyReplaysCheckpointBeforeAcceptingIt()
    {
        using var pair = new Pair();
        using var a = pair.OpenA();
        using var b = pair.OpenB();
        a.RecordElection(pair.PrimaryTerm);
        var source = PhysiologyIllustrationSource.CreateForSession(Guid.NewGuid(), Guid.NewGuid());
        source.AdvanceTo(WaveformBlockAssembler.BlockDurationNs, 250, 1, 256);
        byte[] image = ExamReplicaCheckpoint.Capture(source);
        var request = new ExamReplicaCommand(Guid.NewGuid(), "Checkpoint", image);
        var damaged = JsonNode.Parse(image)!;
        damaged["NextBlockSha256"] = new string('0', 64);
        var badRequest = request with { Payload = Encoding.UTF8.GetBytes(damaged.ToJsonString()) };
        ShouldThrow<InvalidDataException>(() => a.Prepare(7, 22, badRequest));
        var badTransaction = ExamReplicaEntry.Create(pair.Cluster, b.Head, 7, 22, badRequest);
        ShouldThrow<InvalidDataException>(() => b.Apply(badTransaction));
        Check.That(a.Head.Sequence == 0 && b.Head.Sequence == 0, "a valid transaction hash alone cannot establish a valid waveform checkpoint");
        var entry = a.Prepare(7, 22, request);
        a.Confirm(b.Apply(entry));
        var replay = ExamReplicaCheckpoint.Restore(b.Read(entry.Sequence)!.Command.Payload);
        long next = WaveformBlockAssembler.BlockDurationNs * 2L;
        Check.That(replay.AdvanceTo(next, 250, 1, 256).Single().AsSpan().SequenceEqual(source.AdvanceTo(next, 250, 1, 256).Single()),
            "restoring on the standby generates the identical next MWS1 block");
    }

    private static void Execute(string path, string statement)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = statement;
        cmd.ExecuteNonQuery();
    }

    private static void ShouldThrow<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class Pair : IDisposable
    {
        internal string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("exam-replica-").FullName;
        internal Guid Cluster { get; } = Guid.NewGuid();
        internal Guid AId { get; } = Guid.NewGuid();
        internal Guid BId { get; } = Guid.NewGuid();
        internal string A => Path.Combine(Directory, "a.db");
        internal string B => Path.Combine(Directory, "b.db");
        internal AesGcmIdentityPayloadProtector Keys { get; } = new(RandomNumberGenerator.GetBytes(32));
        internal ExamReplicaElection PrimaryTerm => new(7, 22, AId, true, false, null, null);

        internal Pair()
        {
            ExamReplicaStore.Provision(A, Cluster, AId, Keys);
            ExamReplicaStore.Provision(B, Cluster, BId, Keys);
        }

        internal ExamReplicaStore OpenA() => new(A, Cluster, AId, Keys);
        internal ExamReplicaStore OpenB() => new(B, Cluster, BId, Keys);
        public void Dispose() { Keys.Dispose(); System.IO.Directory.Delete(Directory, recursive: true); }
    }
}
