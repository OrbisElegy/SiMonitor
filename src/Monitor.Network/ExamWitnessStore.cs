// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Data.Sqlite;

namespace Monitor.Network;

internal sealed record ExamWitnessLease(Guid ClusterId, Guid? OwnerId, long AuthorityEpoch, long FencingToken,
    long Revision, int RemainingMs);

internal sealed record ExamWitnessRequest(Guid ClusterId, long ExpectedRevision, long AuthorityEpoch, long FencingToken);

internal sealed class ExamWitnessStore : IDisposable
{
    internal const int LeaseDurationMs = 3000;
    internal const int RenewIntervalMs = 500;
    private const long LeaseTicks = LeaseDurationMs * TimeSpan.TicksPerMillisecond;

    private readonly object _gate = new();
    private readonly Guid _clusterId;
    private readonly SqliteConnection _db;
    private readonly TimeProvider _clock;
    private readonly long _started;
    private bool _recovering;
    private bool _disposed;

    internal ExamWitnessStore(string databasePath, Guid clusterId, TimeProvider? clock = null)
    {
        string path = ValidateLocation(databasePath, clusterId);
        _db = OpenExisting(path);
        try
        {
            _clusterId = clusterId;
            _clock = clock ?? TimeProvider.System;
            // Claim exclusive ownership before measuring the restart quarantine. An old
            // service must be unable to renew across this point, even on the same host.
            Execute(_db, "BEGIN EXCLUSIVE; COMMIT;");
            _started = _clock.GetTimestamp();
            _recovering = Read().OwnerId.HasValue;
        }
        catch
        {
            _db.Dispose();
            throw;
        }
    }

    internal static void Initialize(string databasePath, Guid clusterId)
    {
        string path = ValidateLocation(databasePath, clusterId);
        string watermark = path + ".watermark";
        if (File.Exists(watermark)) { throw new IOException("A witness watermark already exists."); }
        // CreateNew prevents concurrent provisioning and never reinitializes existing state.
        // Interrupted provisioning intentionally leaves its files for operator inspection.
        using (File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        using (File.Open(watermark, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        using SqliteConnection db = OpenExisting(path);
        using var transaction = db.BeginTransaction();
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE main.witness (
                cluster_id TEXT PRIMARY KEY, lease_owner TEXT, authority_epoch INTEGER NOT NULL,
                fencing_token INTEGER NOT NULL, lease_expires_at_monotonic INTEGER NOT NULL,
                revision INTEGER NOT NULL,
                CHECK (revision >= 0 AND lease_expires_at_monotonic >= 0 AND
                    ((lease_owner IS NULL AND authority_epoch = 0 AND fencing_token = 0 AND revision = 0)
                     OR (lease_owner IS NOT NULL AND authority_epoch > 0 AND fencing_token > 0 AND revision > 0)))
            ) STRICT;
            CREATE TABLE watermark.anchor (cluster_id TEXT PRIMARY KEY, revision INTEGER NOT NULL CHECK(revision >= 0)) STRICT;
            INSERT INTO main.witness VALUES ($id, NULL, 0, 0, 0, 0);
            INSERT INTO watermark.anchor VALUES ($id, 0);
            """;
        command.Parameters.AddWithValue("$id", clusterId.ToString("D"));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static string ValidateLocation(string databasePath, Guid clusterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (clusterId == Guid.Empty) { throw new ArgumentException("Missing witness cluster identity.", nameof(clusterId)); }
        if (databasePath == ":memory:" || databasePath.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            databasePath.StartsWith(@"\\", StringComparison.Ordinal) || databasePath.StartsWith("//", StringComparison.Ordinal))
        { throw new ArgumentException("The witness requires a local database path.", nameof(databasePath)); }
        string path = Path.GetFullPath(databasePath);
        if (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)
        { throw new ArgumentException("Network drives cannot host witness state.", nameof(databasePath)); }
        return path;
    }

    private static SqliteConnection OpenExisting(string path)
    {
        string watermark = path + ".watermark";
        if (!File.Exists(path) || !File.Exists(watermark))
        { throw new FileNotFoundException("Both provisioned witness files are required."); }
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 1
        }.ToString());
        try
        {
            db.Open();
            using var attach = db.CreateCommand();
            attach.CommandText = "ATTACH DATABASE $watermark AS watermark";
            attach.Parameters.AddWithValue("$watermark", watermark);
            attach.ExecuteNonQuery();
            // SQLite commits both files using one super-journal in DELETE mode. EXTRA also
            // syncs the containing directory after unlinking the journal.
            Execute(db, """
                PRAGMA main.locking_mode=EXCLUSIVE;
                PRAGMA watermark.locking_mode=EXCLUSIVE;
                PRAGMA main.journal_mode=DELETE;
                PRAGMA watermark.journal_mode=DELETE;
                PRAGMA main.synchronous=EXTRA;
                PRAGMA watermark.synchronous=EXTRA;
                """);
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    private static void Execute(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long ElapsedTicks => _clock.GetElapsedTime(_started).Ticks;

    internal ExamWitnessLease Read()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ReadState(null).Lease;
        }
    }

    private (ExamWitnessLease Lease, long Expiry) ReadState(SqliteTransaction? transaction)
    {
        using var query = _db.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT w.lease_owner, w.authority_epoch, w.fencing_token, w.revision, w.lease_expires_at_monotonic
            FROM main.witness AS w JOIN watermark.anchor AS a ON a.cluster_id=w.cluster_id AND a.revision=w.revision
            WHERE w.cluster_id=$id;
            """;
        query.Parameters.AddWithValue("$id", _clusterId.ToString("D"));
        using var row = query.ExecuteReader();
        if (!row.Read()) { throw new InvalidDataException("Witness identity or durable revision does not match its watermark."); }
        Guid? owner = row.IsDBNull(0) ? null : Guid.ParseExact(row.GetString(0), "D");
        long expiry = row.GetInt64(4);
        long remaining = _recovering ? 0 : Math.Clamp(expiry - ElapsedTicks, 0, LeaseTicks);
        var lease = new ExamWitnessLease(_clusterId, owner, row.GetInt64(1), row.GetInt64(2),
            row.GetInt64(3), (int)(remaining / TimeSpan.TicksPerMillisecond));
        return (lease, expiry);
    }

    internal bool TryAcquire(Guid owner, ExamWitnessRequest request, out ExamWitnessLease lease) =>
        TryChange(owner, request, true, out lease);

    internal bool TryRenew(Guid owner, ExamWitnessRequest request, out ExamWitnessLease lease) =>
        TryChange(owner, request, false, out lease);

    private bool TryChange(Guid owner, ExamWitnessRequest request, bool acquire, out ExamWitnessLease lease)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (owner == Guid.Empty || request.ClusterId != _clusterId || request.ExpectedRevision < 0 ||
            request.AuthorityEpoch < 0 || request.FencingToken < 0)
        { throw new ArgumentException("Invalid witness request identity or fence."); }
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var transaction = _db.BeginTransaction();
            var current = ReadState(transaction);
            lease = current.Lease;
            long now = ElapsedTicks;
            bool active = !_recovering && lease.OwnerId.HasValue && now < current.Expiry;
            if (request.ExpectedRevision != lease.Revision) { return false; }
            if (acquire)
            {
                if (active || (_recovering && now < LeaseTicks)) { return false; }
            }
            else if (!active || lease.OwnerId != owner || lease.AuthorityEpoch != request.AuthorityEpoch ||
                lease.FencingToken != request.FencingToken) { return false; }

            long epoch = acquire ? checked(Math.Max(lease.AuthorityEpoch, request.AuthorityEpoch) + 1) : lease.AuthorityEpoch;
            long token = acquire ? checked(Math.Max(lease.FencingToken, request.FencingToken) + 1) : lease.FencingToken;
            long revision = checked(lease.Revision + 1);
            long expiry = checked(now + LeaseTicks);

            using var write = _db.CreateCommand();
            write.Transaction = transaction;
            write.CommandText = """
                UPDATE main.witness SET lease_owner=$owner, authority_epoch=$epoch, fencing_token=$token,
                    lease_expires_at_monotonic=$expiry, revision=$next WHERE cluster_id=$id AND revision=$previous;
                UPDATE watermark.anchor SET revision=$next WHERE cluster_id=$id AND revision=$previous;
                """;
            write.Parameters.AddWithValue("$id", _clusterId.ToString("D"));
            write.Parameters.AddWithValue("$owner", owner.ToString("D"));
            write.Parameters.AddWithValue("$epoch", epoch);
            write.Parameters.AddWithValue("$token", token);
            write.Parameters.AddWithValue("$expiry", expiry);
            write.Parameters.AddWithValue("$next", revision);
            write.Parameters.AddWithValue("$previous", lease.Revision);
            if (write.ExecuteNonQuery() != 2) { throw new InvalidDataException("The witness or its watermark lost the compare-and-swap."); }
            transaction.Commit();
            _recovering = false;
            // The commit can consume some or all of the lease. Never advertise that time.
            lease = ReadState(null).Lease;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true;
            _db.Dispose();
        }
    }
}
