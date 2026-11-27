// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monitor.Infrastructure.Identity;

namespace Monitor.Network;

// A single prepared row can exist above the visible head. Confirming that row never
// appends data: it advances the protected head only after comparing the peer receipt.
internal sealed class ExamReplicaStore : IDisposable
{
    private sealed record State(ExamReplicaCursor Head, long AuditSequence);
    private readonly object _gate = new();
    private readonly SqliteConnection _db;
    private readonly IIdentityPayloadProtector _protector;
    private readonly string _purpose;
    private readonly Guid _cluster;
    private readonly Guid _node;

    internal ExamReplicaStore(string databasePath, Guid clusterId, Guid nodeId, IIdentityPayloadProtector protector)
        : this(databasePath, clusterId, nodeId, protector, initialize: false) { }

    private ExamReplicaStore(string databasePath, Guid clusterId, Guid nodeId, IIdentityPayloadProtector protector, bool initialize)
    {
        ArgumentNullException.ThrowIfNull(protector);
        if (clusterId == Guid.Empty || nodeId == Guid.Empty) { throw new ArgumentException("Expected provisioned cluster and node IDs."); }
        _cluster = clusterId;
        _node = nodeId;
        _purpose = $"exam.replica.v1.{clusterId:N}.{nodeId:N}.";
        _protector = protector;
        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = LocalPath(databasePath),
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 1
        }.ToString());
        try
        {
            _db.Open();
            using var settings = Sql(null, initialize ? "PRAGMA journal_mode=WAL" : "PRAGMA journal_mode");
            if (!string.Equals(settings.ExecuteScalar() as string, "wal", StringComparison.OrdinalIgnoreCase))
            { throw new InvalidDataException("The exam replica requires a local SQLite WAL database."); }
            settings.CommandText = "PRAGMA synchronous=FULL; PRAGMA trusted_schema=OFF; PRAGMA synchronous";
            if (Convert.ToInt32(settings.ExecuteScalar(), CultureInfo.InvariantCulture) != 2)
            { throw new InvalidOperationException("The exam replica requires FULL durability."); }
            if (initialize) { Initialize(); }
            Verify();
        }
        catch { _db.Dispose(); throw; }
    }

    internal static void Provision(string databasePath, Guid clusterId, Guid nodeId, IIdentityPayloadProtector protector)
    {
        string path = LocalPath(databasePath);
        ArgumentNullException.ThrowIfNull(protector);
        if (clusterId == Guid.Empty || nodeId == Guid.Empty) { throw new ArgumentException("Expected provisioned cluster and node IDs."); }
        var settings = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) { settings.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
        using (File.Open(path, settings)) { }
        using var created = new ExamReplicaStore(path, clusterId, nodeId, protector, initialize: true);
    }

    internal static string LocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        { throw new ArgumentException("Configure an absolute local path for the exam replica database.", nameof(path)); }
        string full = Path.GetFullPath(path);
        var drive = DriveInfo.GetDrives().Where(candidate => Path.GetRelativePath(candidate.Name, full) is var relative &&
            !Path.IsPathFullyQualified(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .MaxBy(candidate => candidate.Name.Length);
        if (drive?.DriveType == DriveType.Network) { throw new ArgumentException("Shared storage is unsuitable for an exam replica.", nameof(path)); }
        return full;
    }

    private void Initialize()
    {
        using var tx = _db.BeginTransaction();
        using var cmd = Sql(tx, """
            CREATE TABLE exam_state (id INTEGER PRIMARY KEY CHECK (id=1), record BLOB NOT NULL) STRICT;
            CREATE TABLE exam_log (sequence INTEGER PRIMARY KEY CHECK(sequence>0), request TEXT NOT NULL UNIQUE, record BLOB NOT NULL) STRICT;
            CREATE TABLE exam_election (revision INTEGER PRIMARY KEY CHECK(revision>0), record BLOB NOT NULL) STRICT;
            INSERT INTO exam_state VALUES(1,$state);
            """);
        cmd.Parameters.AddWithValue("$state", Protect(new State(ExamReplicaCursor.Empty, 0), "state"));
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    internal ExamReplicaCursor Head { get { lock (_gate) { return GetState(null).Head; } } }
    internal ExamReplicaElection Election { get { lock (_gate) { return ElectionAt(null, GetState(null).AuditSequence); } } }
    internal long ElectionRevision { get { lock (_gate) { return GetState(null).AuditSequence; } } }
    internal ExamReplicaElection? ReadElectionAudit(long revision)
    {
        lock (_gate) { return revision > 0 && revision <= GetState(null).AuditSequence ? ElectionAt(null, revision) : null; }
    }
    internal ExamReplicaEntry? Pending
    {
        get { lock (_gate) { return EntryAt(null, checked(GetState(null).Head.Sequence + 1)); } }
    }

    internal ExamReplicaEntry? Read(long sequence)
    {
        lock (_gate) { return sequence > 0 && sequence <= GetState(null).Head.Sequence ? EntryAt(null, sequence) : null; }
    }

    internal ExamReplicaEntry? Find(Guid requestId)
    {
        lock (_gate)
        {
            ExamReplicaEntry? row = ByRequest(null, requestId);
            return row?.Sequence <= GetState(null).Head.Sequence ? row : null;
        }
    }

    internal void RecordElection(ExamReplicaElection next)
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            State state = GetState(tx);
            ExamReplicaElection previous = ElectionAt(tx, state.AuditSequence);
            if (previous == next) { return; }
            ValidateElection(previous, next);
            long revision = checked(state.AuditSequence + 1);
            using var cmd = Sql(tx, "INSERT INTO exam_election VALUES($revision,$record)");
            cmd.Parameters.AddWithValue("$revision", revision);
            cmd.Parameters.AddWithValue("$record", Protect(next, ElectionKey(revision)));
            cmd.ExecuteNonQuery();
            PutState(tx, state with { AuditSequence = revision });
            tx.Commit();
        }
    }

    private static void ValidateElection(ExamReplicaElection previous, ExamReplicaElection next)
    {
        if (next is null || next.AuthorityEpoch < previous.AuthorityEpoch || next.FencingToken < previous.FencingToken ||
            (next.AuthorityEpoch == previous.AuthorityEpoch && (next.OwnerId != previous.OwnerId || next.FencingToken != previous.FencingToken)) ||
            (next.AuthorityEpoch > previous.AuthorityEpoch && next.FencingToken <= previous.FencingToken) ||
            (next.AuthorityEpoch > 0) != next.OwnerId.HasValue || next.OwnerId == Guid.Empty ||
            next.OperatorName?.Length > 256 || next.Evidence?.Length > 4096 ||
            (next.Manual && (string.IsNullOrWhiteSpace(next.OperatorName) || string.IsNullOrWhiteSpace(next.Evidence))))
        { throw new InvalidDataException("Invalid or stale exam authority transition."); }
    }

    internal ExamReplicaEntry Prepare(long epoch, long token, ExamReplicaCommand command)
    {
        ExamReplicaEntry.ValidateCommand(command);
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            State state = GetState(tx);
            RequireLocalTerm(tx, state, epoch, token);
            if (ByRequest(tx, command.RequestId) is { } existing)
            {
                if (!ExamReplicaEntry.SameRequest(existing.Command, command))
                { throw new InvalidDataException("The exam request ID already names different content."); }
                return existing;
            }
            if (EntryAt(tx, checked(state.Head.Sequence + 1)) is not null)
            { throw new InvalidOperationException("Replicate the previous durable transaction before accepting another."); }
            var entry = ExamReplicaEntry.Create(_cluster, state.Head, epoch, token, command);
            ValidateNext(tx, state, entry);
            Insert(tx, entry);
            tx.Commit();
            return entry;
        }
    }

    internal ExamReplicaCursor Apply(ExamReplicaEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Validate(_cluster);
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            State state = GetState(tx);
            ExamReplicaEntry? existing = EntryAt(tx, entry.Sequence);
            if (existing is not null && existing.Hash != entry.Hash)
            { throw new InvalidDataException("The exam log contains a conflicting transaction."); }
            if (entry.Sequence <= state.Head.Sequence)
            {
                if (existing is null) { throw new InvalidDataException("The committed exam log is incomplete."); }
                return existing.Cursor;
            }
            ValidateNext(tx, state, entry);
            if (existing is null)
            {
                if (ByRequest(tx, entry.Command.RequestId) is not null) { throw new InvalidDataException("Duplicate exam request ID."); }
                Insert(tx, entry);
            }
            PutState(tx, state with { Head = entry.Cursor });
            tx.Commit();
            return entry.Cursor;
        }
    }

    internal ExamReplicaCursor Confirm(ExamReplicaCursor peerReceipt) => Confirm(peerReceipt, null);

    // A new leader may finish an earlier local preparation after its authenticated
    // peer proves that it durably committed that exact hash. Check the new term in
    // the same transaction that publishes the earlier row.
    internal ExamReplicaCursor ConfirmReplicated(ExamReplicaCursor durable, long currentEpoch, long currentToken) =>
        Confirm(durable, (currentEpoch, currentToken));

    private ExamReplicaCursor Confirm(ExamReplicaCursor peerReceipt, (long Epoch, long Token)? current)
    {
        ArgumentNullException.ThrowIfNull(peerReceipt);
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            State state = GetState(tx);
            if (current is { } term) { RequireLocalTerm(tx, state, term.Epoch, term.Token); }
            if (peerReceipt.Sequence <= 0 || EntryAt(tx, peerReceipt.Sequence) is not { } row || row.Cursor != peerReceipt)
            { throw new InvalidDataException("Standby durable receipt does not match the local exam transaction."); }
            if (row.Sequence > state.Head.Sequence)
            {
                if (current is null) { RequireLocalTerm(tx, state, row.AuthorityEpoch, row.FencingToken); }
                if (row.Sequence != checked(state.Head.Sequence + 1) || row.PreviousHash != state.Head.Hash)
                { throw new InvalidDataException("Invalid local exam frontier."); }
                PutState(tx, state with { Head = peerReceipt });
                tx.Commit();
            }
            return peerReceipt;
        }
    }

    private void RequireLocalTerm(SqliteTransaction tx, State state, long epoch, long token)
    {
        ExamReplicaElection term = ElectionAt(tx, state.AuditSequence);
        if (epoch <= 0 || token <= 0 || term.AuthorityEpoch != epoch || term.FencingToken != token || term.OwnerId != _node)
        { throw new InvalidOperationException("This node does not hold the persisted exam term."); }
    }

    internal void DiscardPending()
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            State state = GetState(tx);
            using var cmd = Sql(tx, "DELETE FROM exam_log WHERE sequence=$sequence");
            cmd.Parameters.AddWithValue("$sequence", checked(state.Head.Sequence + 1));
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
    }

    private void ValidateNext(SqliteTransaction tx, State state, ExamReplicaEntry entry)
    {
        entry.Validate(_cluster);
        if (entry.Sequence != checked(state.Head.Sequence + 1) || entry.PreviousHash != state.Head.Hash)
        { throw new InvalidDataException("Exam replica needs the exact preceding transaction."); }
        if (state.Head.Sequence > 0)
        {
            var previous = EntryAt(tx, state.Head.Sequence) ?? throw new InvalidDataException("Missing committed exam head.");
            ValidateTerm(previous, entry);
        }
        if (entry.Command.Kind == "Checkpoint") { ExamReplicaCheckpoint.Restore(entry.Command.Payload); }
    }

    private static void ValidateTerm(ExamReplicaEntry previous, ExamReplicaEntry next)
    {
        if (next.AuthorityEpoch < previous.AuthorityEpoch || (next.AuthorityEpoch == previous.AuthorityEpoch
            ? next.FencingToken != previous.FencingToken : next.FencingToken <= previous.FencingToken))
        { throw new InvalidDataException("Exam transaction chain contains an invalid authority transition."); }
    }

    internal void Verify()
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction(IsolationLevel.Serializable, deferred: true);
            State state = GetState(tx);
            ExamReplicaCursor cursor = ExamReplicaCursor.Empty;
            ExamReplicaEntry? previous = null;
            ExamReplicaCursor? observedHead = state.Head.Sequence == 0 ? cursor : null;
            using (var cmd = Sql(tx, "SELECT sequence,request,record FROM exam_log ORDER BY sequence"))
            using (var rows = cmd.ExecuteReader())
            {
                while (rows.Read())
                {
                    var entry = ReadEntry(rows);
                    if (entry.Sequence != checked(cursor.Sequence + 1) || entry.PreviousHash != cursor.Hash)
                    { throw new InvalidDataException("Corrupt exam replica chain."); }
                    if (previous is not null) { ValidateTerm(previous, entry); }
                    if (entry.Command.Kind == "Checkpoint") { ExamReplicaCheckpoint.Restore(entry.Command.Payload); }
                    cursor = entry.Cursor;
                    previous = entry;
                    if (cursor.Sequence == state.Head.Sequence) { observedHead = cursor; }
                }
            }
            if (observedHead != state.Head || cursor.Sequence - state.Head.Sequence > 1)
            { throw new InvalidDataException("Exam replica frontier differs from the stored chain."); }
            long revision = 0;
            var prior = ExamReplicaElection.Initial;
            using (var cmd = Sql(tx, "SELECT revision,record FROM exam_election ORDER BY revision"))
            using (var rows = cmd.ExecuteReader())
            {
                while (rows.Read())
                {
                    revision = checked(revision + 1);
                    if (rows.GetInt64(0) != revision) { throw new InvalidDataException("Incomplete exam election history."); }
                    var next = Unprotect<ExamReplicaElection>((byte[])rows[1], ElectionKey(revision));
                    ValidateElection(prior, next);
                    prior = next;
                }
            }
            if (revision != state.AuditSequence) { throw new InvalidDataException("Incomplete exam election audit."); }
            tx.Commit();
        }
    }

    private ExamReplicaElection ElectionAt(SqliteTransaction? tx, long revision)
    {
        if (revision == 0) { return ExamReplicaElection.Initial; }
        using var cmd = Sql(tx, "SELECT record FROM exam_election WHERE revision=$revision");
        cmd.Parameters.AddWithValue("$revision", revision);
        return Unprotect<ExamReplicaElection>((byte[]?)cmd.ExecuteScalar() ?? throw new InvalidDataException("Missing election record."), ElectionKey(revision));
    }

    private State GetState(SqliteTransaction? tx)
    {
        using var cmd = Sql(tx, "SELECT record FROM exam_state WHERE id=1");
        return Unprotect<State>((byte[]?)cmd.ExecuteScalar() ?? throw new InvalidDataException("Missing exam replica metadata."), "state");
    }

    private void PutState(SqliteTransaction tx, State state)
    {
        using var cmd = Sql(tx, "UPDATE exam_state SET record=$state WHERE id=1");
        cmd.Parameters.AddWithValue("$state", Protect(state, "state"));
        if (cmd.ExecuteNonQuery() != 1) { throw new InvalidDataException("Missing exam replica metadata."); }
    }

    private ExamReplicaEntry? ByRequest(SqliteTransaction? tx, Guid requestId)
    {
        using var cmd = Sql(tx, "SELECT sequence,request,record FROM exam_log WHERE request=$key");
        cmd.Parameters.AddWithValue("$key", RequestKey(requestId));
        using var row = cmd.ExecuteReader();
        return row.Read() ? ReadEntry(row) : null;
    }

    private ExamReplicaEntry? EntryAt(SqliteTransaction? tx, long sequence)
    {
        using var cmd = Sql(tx, "SELECT sequence,request,record FROM exam_log WHERE sequence=$seq");
        cmd.Parameters.AddWithValue("$seq", sequence);
        using var row = cmd.ExecuteReader();
        return row.Read() ? ReadEntry(row) : null;
    }

    private ExamReplicaEntry ReadEntry(SqliteDataReader row)
    {
        long sequence = row.GetInt64(0);
        var entry = Unprotect<ExamReplicaEntry>((byte[])row[2], EntryKey(sequence));
        entry.Validate(_cluster);
        if (entry.Sequence != sequence || RequestKey(entry.Command.RequestId) != row.GetString(1))
        { throw new InvalidDataException("Corrupt exam replica index."); }
        return entry;
    }

    private void Insert(SqliteTransaction tx, ExamReplicaEntry entry)
    {
        using var cmd = Sql(tx, "INSERT INTO exam_log VALUES($seq,$request,$record)");
        cmd.Parameters.AddWithValue("$seq", entry.Sequence);
        cmd.Parameters.AddWithValue("$request", RequestKey(entry.Command.RequestId));
        cmd.Parameters.AddWithValue("$record", Protect(entry, EntryKey(entry.Sequence)));
        cmd.ExecuteNonQuery();
    }

    private SqliteCommand Sql(SqliteTransaction? tx, string query)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = query;
        cmd.Transaction = tx;
        return cmd;
    }

    private string RequestKey(Guid request) => _protector.LookupToken(request.ToString("N"), _purpose + "requests");
    private static string EntryKey(long sequence) => "entry." + sequence.ToString(CultureInfo.InvariantCulture);
    private static string ElectionKey(long revision) => "election." + revision.ToString(CultureInfo.InvariantCulture);

    private byte[] Protect<T>(T value, string key)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(value);
        try { return _protector.Protect(plain, _purpose + key); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    internal void BackupTo(string newLocalPath)
    {
        lock (_gate)
        {
            string path = LocalPath(newLocalPath);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var settings = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) { settings.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
            using (File.Open(temporary, settings)) { }
            try
            {
                using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = temporary, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
                {
                    destination.Open();
                    _db.BackupDatabase(destination);
                }
                using (var verified = new ExamReplicaStore(temporary, _cluster, _node, _protector)) { }
                File.Move(temporary, path, overwrite: false);
            }
            finally { File.Delete(temporary); }
        }
    }

    private T Unprotect<T>(byte[] bytes, string key)
    {
        byte[] plain = _protector.Unprotect(bytes, _purpose + key);
        try { return JsonSerializer.Deserialize<T>(plain) ?? throw new InvalidDataException("Invalid protected exam data."); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public void Dispose() { lock (_gate) { _db.Dispose(); } }
}
