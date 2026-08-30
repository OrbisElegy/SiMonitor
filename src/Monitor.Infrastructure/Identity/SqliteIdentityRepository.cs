// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monitor.Application.Identity;
using Monitor.Domain.Identity;

namespace Monitor.Infrastructure.Identity;

public sealed class SqliteIdentityRepository :
    IBootstrapIdentityRepository,
    IInstitutionAccountRepository
{
    private const string BootstrapPurpose = "identity-bootstrap-verifier-v1";
    private const string UsernameLookupPurpose = "identity-account-username-v1";

    private readonly SqliteIdentityDatabase _database;
    private readonly IIdentityPayloadProtector _protector;

    public SqliteIdentityRepository(string databasePath, IIdentityPayloadProtector protector)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _database = new SqliteIdentityDatabase(databasePath);
    }

    public bool TryInitializeBootstrap(PasswordVerifier bootstrapVerifier)
    {
        ArgumentNullException.ThrowIfNull(bootstrapVerifier);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(bootstrapVerifier);
        byte[] protectedVerifier;
        try
        {
            protectedVerifier = _protector.Protect(plaintext, BootstrapPurpose);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        using SqliteConnection connection = _database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO identity_bootstrap(
                singleton, lifecycle_state, protected_verifier, root_retired, revision)
            VALUES (1, $state, $verifier, 0, 1);
            """;
        command.Parameters.AddWithValue("$state", (int)BootstrapLifecycleState.BootstrapOnly);
        command.Parameters.Add("$verifier", SqliteType.Blob).Value = protectedVerifier;
        bool inserted = command.ExecuteNonQuery() == 1;
        transaction.Commit();
        return inserted;
    }

    public BootstrapIdentitySnapshot LoadBootstrap()
    {
        using SqliteConnection connection = _database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT lifecycle_state, protected_verifier, root_retired, revision
            FROM identity_bootstrap
            WHERE singleton = 1;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("The identity bootstrap state has not been initialized.");
        }

        return ReadBootstrap(reader);
    }

    public bool UsernameExists(string canonicalUsername)
    {
        string lookup = AccountLookup(canonicalUsername);
        using SqliteConnection connection = _database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM identity_accounts WHERE username_lookup = $lookup
            );
            """;
        command.Parameters.AddWithValue("$lookup", lookup);
        return Convert.ToInt64(command.ExecuteScalar(), null) == 1;
    }

    public InstitutionAccount? FindByCanonicalUsername(string canonicalUsername)
    {
        string lookup = AccountLookup(canonicalUsername);
        using SqliteConnection connection = _database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT protected_payload, revision
            FROM identity_accounts
            WHERE username_lookup = $lookup;
            """;
        command.Parameters.AddWithValue("$lookup", lookup);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        byte[] protectedPayload = reader.GetFieldValue<byte[]>(0);
        byte[] plaintext = _protector.Unprotect(protectedPayload, AccountPurpose(lookup));
        try
        {
            InstitutionAccount account = JsonSerializer.Deserialize<InstitutionAccount>(plaintext) ??
                throw new InvalidOperationException("The protected account payload is empty.");
            ulong storedRevision = CheckedRevision(reader.GetInt64(1));
            if (account.Revision != storedRevision ||
                !StringComparer.Ordinal.Equals(account.CanonicalUsername, canonicalUsername))
            {
                throw new InvalidOperationException("The protected account index is inconsistent.");
            }

            return account;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public BootstrapCommitResult TryCompleteBootstrap(BootstrapCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        ValidateCompletion(completion);

        string accountLookup = AccountLookup(completion.Account.CanonicalUsername);
        byte[] accountPlaintext = JsonSerializer.SerializeToUtf8Bytes(completion.Account);
        byte[] auditPlaintext = JsonSerializer.SerializeToUtf8Bytes(completion.Audit);
        byte[] protectedAccount;
        byte[] protectedAudit;
        try
        {
            protectedAccount = _protector.Protect(accountPlaintext, AccountPurpose(accountLookup));
            protectedAudit = _protector.Protect(
                auditPlaintext,
                AuditPurpose(completion.Audit.AuditId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(accountPlaintext);
            CryptographicOperations.ZeroMemory(auditPlaintext);
        }

        try
        {
            using SqliteConnection connection = _database.Open();
            using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
            BootstrapIdentitySnapshot current = LoadBootstrap(connection, transaction);
            if (current.State != BootstrapLifecycleState.BootstrapOnly ||
                current.RootPermanentlyRetired ||
                current.BootstrapVerifier is null ||
                current.Revision != completion.ExpectedRevision)
            {
                return BootstrapCommitResult.ConcurrencyConflict;
            }

            InsertAccount(connection, transaction, completion.Account, accountLookup, protectedAccount);
            InsertAudit(connection, transaction, completion.Audit, protectedAudit);
            if (!CompleteBootstrap(connection, transaction, completion.ExpectedRevision))
            {
                return BootstrapCommitResult.ConcurrencyConflict;
            }

            transaction.Commit();
            return BootstrapCommitResult.Committed;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            return BootstrapCommitResult.ConcurrencyConflict;
        }
        catch (SqliteException)
        {
            return BootstrapCommitResult.Failed;
        }
    }

    public bool AuditExists(Guid auditId)
    {
        if (auditId == Guid.Empty)
        {
            return false;
        }

        using SqliteConnection connection = _database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM identity_audit WHERE audit_id = $id);";
        command.Parameters.AddWithValue("$id", auditId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar(), null) == 1;
    }

    private BootstrapIdentitySnapshot LoadBootstrap(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT lifecycle_state, protected_verifier, root_retired, revision
            FROM identity_bootstrap
            WHERE singleton = 1;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("The identity bootstrap state has not been initialized.");
        }

        return ReadBootstrap(reader);
    }

    private BootstrapIdentitySnapshot ReadBootstrap(SqliteDataReader reader)
    {
        int rawState = reader.GetInt32(0);
        if (!Enum.IsDefined(typeof(BootstrapLifecycleState), rawState))
        {
            throw new InvalidOperationException("The identity bootstrap state is invalid.");
        }

        var state = (BootstrapLifecycleState)rawState;
        bool rootRetired = reader.GetInt32(2) switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidOperationException("The root retirement state is invalid."),
        };
        PasswordVerifier? verifier = null;
        if (!reader.IsDBNull(1))
        {
            byte[] protectedVerifier = reader.GetFieldValue<byte[]>(1);
            byte[] plaintext = _protector.Unprotect(protectedVerifier, BootstrapPurpose);
            try
            {
                verifier = JsonSerializer.Deserialize<PasswordVerifier>(plaintext) ??
                    throw new InvalidOperationException("The protected bootstrap verifier is empty.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        if ((state == BootstrapLifecycleState.BootstrapOnly && (rootRetired || verifier is null)) ||
            (state == BootstrapLifecycleState.Completed && (!rootRetired || verifier is not null)))
        {
            throw new InvalidOperationException("The identity bootstrap invariants are inconsistent.");
        }

        return new BootstrapIdentitySnapshot(
            state,
            verifier,
            rootRetired,
            CheckedRevision(reader.GetInt64(3)));
    }

    private static void InsertAccount(
        SqliteConnection connection,
        SqliteTransaction transaction,
        InstitutionAccount account,
        string lookup,
        byte[] protectedPayload)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO identity_accounts(username_lookup, protected_payload, revision)
            VALUES ($lookup, $payload, $revision);
            """;
        command.Parameters.AddWithValue("$lookup", lookup);
        command.Parameters.Add("$payload", SqliteType.Blob).Value = protectedPayload;
        command.Parameters.AddWithValue("$revision", checked((long)account.Revision));
        command.ExecuteNonQuery();
    }

    private static void InsertAudit(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BootstrapAuditRecord audit,
        byte[] protectedPayload)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO identity_audit(audit_id, protected_payload, recorded_at_ticks)
            VALUES ($id, $payload, $recorded);
            """;
        command.Parameters.AddWithValue("$id", audit.AuditId.ToString("D"));
        command.Parameters.Add("$payload", SqliteType.Blob).Value = protectedPayload;
        command.Parameters.AddWithValue("$recorded", audit.RecordedAtUtc.UtcTicks);
        command.ExecuteNonQuery();
    }

    private static bool CompleteBootstrap(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ulong expectedRevision)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE identity_bootstrap
            SET lifecycle_state = $completed,
                protected_verifier = NULL,
                root_retired = 1,
                revision = revision + 1
            WHERE singleton = 1
              AND lifecycle_state = $bootstrap_only
              AND root_retired = 0
              AND revision = $expected_revision;
            """;
        command.Parameters.AddWithValue("$completed", (int)BootstrapLifecycleState.Completed);
        command.Parameters.AddWithValue("$bootstrap_only", (int)BootstrapLifecycleState.BootstrapOnly);
        command.Parameters.AddWithValue("$expected_revision", checked((long)expectedRevision));
        return command.ExecuteNonQuery() == 1;
    }

    private static void ValidateCompletion(BootstrapCompletion completion)
    {
        if (!completion.PermanentlyRetireRoot ||
            completion.ExpectedRevision == 0 ||
            completion.Account.PrincipalId == Guid.Empty ||
            completion.Account.Revision == 0 ||
            completion.Audit.AuditId == Guid.Empty ||
            completion.Audit.CorrelationId == Guid.Empty ||
            completion.Audit.ActivatedPrincipalId != completion.Account.PrincipalId ||
            !StringComparer.Ordinal.Equals(
                completion.Audit.CanonicalUsername,
                completion.Account.CanonicalUsername))
        {
            throw new ArgumentException("The bootstrap completion is invalid.", nameof(completion));
        }
    }

    private string AccountLookup(string canonicalUsername)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalUsername);
        return _protector.LookupToken(canonicalUsername, UsernameLookupPurpose);
    }

    private static string AccountPurpose(string lookup) => $"identity-account-payload-v1:{lookup}";

    private static string AuditPurpose(Guid auditId) => $"identity-audit-payload-v1:{auditId:D}";

    private static ulong CheckedRevision(long revision) =>
        revision > 0
            ? checked((ulong)revision)
            : throw new InvalidOperationException("The identity revision is invalid.");
}
