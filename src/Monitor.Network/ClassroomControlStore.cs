// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monitor.Infrastructure.Identity;

namespace Monitor.Network;

internal sealed record ClassroomControlRecord(long Generation, long FrontierSimTimeNs, int ReceiptCount, bool PrivateBranch, ulong Revision, ulong Commit, ulong ConfigRevision, Guid? Operator,
    string Profile, JsonElement Device, string[] Allowed, ClassroomControlPlan? Plan, ClassroomHeldAction? Held)
{
    public ClassroomControlPlan[] Queued { get; init; } = [];
    public ClassroomControlPlan[] Finished { get; init; } = [];
    public ClassroomConfigurationContent? ActiveContent { get; init; }
}

internal sealed record ClassroomHeldAction(Guid ClientId, Guid InteractionId, string Family, long ExpiresAtSafetyNs);

internal sealed record ClassroomControlReceipt(ClassroomActionIntent Intent, ClassroomActionResult Result);

internal sealed class ClassroomControlStore
{
    private readonly string _connection;
    private readonly string _scope;
    private readonly IIdentityPayloadProtector _protector;

    internal ClassroomControlStore(string path, IIdentityPayloadProtector protector, Guid sessionId, Guid instanceId)
    {
        _connection = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
        _protector = protector;
        _scope = sessionId.ToString("N") + "." + instanceId.ToString("N");
    }

    internal ClassroomControlRecord LoadOrCreate(ClassroomControlRecord initial)
    {
        using SqliteConnection db = Open();
        using SqliteTransaction tx = db.BeginTransaction();
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS classroom_control_state (
              scope TEXT PRIMARY KEY, generation INTEGER NOT NULL, protected BLOB NOT NULL) STRICT;
            CREATE TABLE IF NOT EXISTS classroom_action_receipts (
              scope TEXT NOT NULL, actor TEXT NOT NULL, intent TEXT NOT NULL, token TEXT NOT NULL, protected BLOB NOT NULL,
              PRIMARY KEY(scope,actor,intent), UNIQUE(scope,actor,token)) STRICT;
            CREATE TABLE IF NOT EXISTS classroom_retired_scopes (scope TEXT PRIMARY KEY, archived INTEGER NOT NULL) STRICT;
            INSERT OR IGNORE INTO classroom_control_state SELECT $scope,0,$initial
            WHERE NOT EXISTS (SELECT 1 FROM classroom_retired_scopes WHERE scope=$scope);
            """;
        command.Parameters.AddWithValue("$scope", _scope);
        command.Parameters.AddWithValue("$initial", Protect(initial, "state"));
        command.ExecuteNonQuery();
        command.CommandText = """
            SELECT protected FROM classroom_control_state WHERE scope=$scope AND
            NOT EXISTS (SELECT 1 FROM classroom_retired_scopes WHERE scope=$scope)
            """;
        byte[] stored = command.ExecuteScalar() as byte[] ?? throw new InvalidOperationException("The classroom branch is retired.");
        ClassroomControlRecord state = Unprotect<ClassroomControlRecord>(stored, "state");
        tx.Commit();
        return state;
    }

    internal ClassroomControlReceipt? Lookup(Guid actor, Guid intent, string? idempotencyKey)
    {
        using SqliteConnection db = Open();
        using var query = db.CreateCommand();
        query.CommandText = """
            SELECT intent,protected FROM classroom_action_receipts WHERE scope=$scope AND actor=$actor
              AND (intent=$intent OR token=$token) ORDER BY intent=$intent DESC LIMIT 1;
            """;
        query.Parameters.AddWithValue("$scope", _scope);
        query.Parameters.AddWithValue("$actor", actor.ToString("N"));
        query.Parameters.AddWithValue("$intent", intent.ToString("N"));
        query.Parameters.AddWithValue("$token", idempotencyKey is null ? DBNull.Value : LookupKey(actor, idempotencyKey));
        using var row = query.ExecuteReader();
        return row.Read() ? Unprotect<ClassroomControlReceipt>((byte[])row[1], "action." + actor.ToString("N") + "." + row.GetString(0)) : null;
    }

    internal void Commit(ClassroomControlRecord next, ClassroomControlReceipt? receipt)
    {
        using SqliteConnection db = Open();
        using SqliteTransaction tx = db.BeginTransaction();
        using var write = db.CreateCommand();
        write.Transaction = tx;
        write.CommandText = """
            UPDATE classroom_control_state SET generation=$next, protected=$state
            WHERE scope=$scope AND generation=$previous AND NOT EXISTS (SELECT 1 FROM classroom_retired_scopes WHERE scope=$scope);
            """;
        write.Parameters.AddWithValue("$next", next.Generation);
        write.Parameters.AddWithValue("$previous", checked(next.Generation - 1));
        write.Parameters.AddWithValue("$scope", _scope);
        write.Parameters.AddWithValue("$state", Protect(next, "state"));
        if (write.ExecuteNonQuery() != 1) { throw new InvalidOperationException("Classroom authority generation conflict."); }
        if (receipt is not null)
        {
            Guid actor = receipt.Intent.ClientId;
            string intent = receipt.Intent.IntentId.ToString("N");
            write.CommandText = "INSERT INTO classroom_action_receipts VALUES($scope,$actor,$intent,$token,$receipt)";
            write.Parameters.AddWithValue("$actor", actor.ToString("N"));
            write.Parameters.AddWithValue("$intent", intent);
            write.Parameters.AddWithValue("$token", LookupKey(actor, receipt.Intent.IdempotencyKey));
            write.Parameters.AddWithValue("$receipt", Protect(receipt, "action." + actor.ToString("N") + "." + intent));
            write.ExecuteNonQuery();
        }
        tx.Commit();
    }

    internal void Retire(bool archive, Action<SqliteConnection, SqliteTransaction>? writeArchive = null)
    {
        using SqliteConnection db = Open();
        using SqliteTransaction tx = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        writeArchive?.Invoke(db, tx);
        cmd.CommandText = "INSERT INTO classroom_retired_scopes VALUES ($scope,$archive)";
        cmd.Parameters.AddWithValue("$scope", _scope);
        cmd.Parameters.AddWithValue("$archive", archive ? 1 : 0);
        cmd.ExecuteNonQuery();
        if (!archive)
        {
            cmd.CommandText = "DELETE FROM classroom_action_receipts WHERE scope=$scope; DELETE FROM classroom_control_state WHERE scope=$scope;";
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private string LookupKey(Guid actor, string key) => _protector.LookupToken(key, _scope + ".lookup." + actor.ToString("N"));
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connection);
        try
        {
            db.Open();
            using var options = db.CreateCommand();
            options.CommandText = "PRAGMA synchronous=FULL;";
            options.ExecuteNonQuery();
            return db;
        }
        catch { db.Dispose(); throw; }
    }

    private byte[] Protect<T>(T payload, string purpose)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, ClassroomControlWire.Json);
        try { return _protector.Protect(bytes, "classroom." + _scope + "." + purpose); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private T Unprotect<T>(byte[] ciphertext, string purpose)
    {
        byte[] bytes = _protector.Unprotect(ciphertext, "classroom." + _scope + "." + purpose);
        try { return JsonSerializer.Deserialize<T>(bytes, ClassroomControlWire.Json) ?? throw new InvalidDataException("Invalid classroom state."); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
