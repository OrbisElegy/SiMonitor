// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monitor.Application.Identity;
using Monitor.Domain.Identity;

namespace Monitor.Infrastructure.Identity;

public sealed class SqliteAuthenticationFailureTracker : IAuthenticationFailureTracker
{
    private const int AccountDimension = 1;
    private const int SourceDimension = 2;
    private const string AccountLookupPurpose = "authentication-failure-account-v1";
    private const string SourceLookupPurpose = "authentication-failure-source-v1";

    private readonly SqliteIdentityDatabase _database;
    private readonly IIdentityPayloadProtector _protector;

    public SqliteAuthenticationFailureTracker(
        string databasePath,
        IIdentityPayloadProtector protector)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _database = new SqliteIdentityDatabase(databasePath);
    }

    public AuthenticationLockState GetLockState(
        string canonicalUsername,
        string sourceAddress,
        DateTimeOffset nowUtc)
    {
        (string accountLookup, string sourceLookup) = Lookups(canonicalUsername, sourceAddress);
        using SqliteConnection connection = _database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        DeleteExpiredLocks(connection, transaction, nowUtc.UtcTicks);
        DateTimeOffset? lockedUntil = ReadLatestLock(
            connection,
            transaction,
            accountLookup,
            sourceLookup);
        transaction.Commit();
        return new AuthenticationLockState(lockedUntil is not null, lockedUntil);
    }

    public AuthenticationLockState RecordFailure(AuthenticationFailureAuditRecord audit)
    {
        ValidateAudit(audit);
        if (!StringComparer.Ordinal.Equals(audit.ReasonCode, "identity.password.invalid"))
        {
            throw new ArgumentException(
                "Only an invalid-password audit can increment authentication failures.",
                nameof(audit));
        }

        (string accountLookup, string sourceLookup) = Lookups(
            audit.CanonicalUsername,
            audit.SourceAddress);
        byte[] protectedAudit = ProtectAudit(audit);
        long nowTicks = audit.RecordedAtUtc.UtcTicks;
        long windowStartTicks = (audit.RecordedAtUtc - IdentityPolicy.FailureWindow).UtcTicks;
        long lockedUntilTicks = (audit.RecordedAtUtc + IdentityPolicy.LockDuration).UtcTicks;

        using SqliteConnection connection = _database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        DeleteExpiredLocks(connection, transaction, nowTicks);
        int accountCount = AddFailure(
            connection,
            transaction,
            AccountDimension,
            accountLookup,
            windowStartTicks,
            nowTicks);
        int sourceCount = AddFailure(
            connection,
            transaction,
            SourceDimension,
            sourceLookup,
            windowStartTicks,
            nowTicks);

        if (accountCount >= IdentityPolicy.FailureLimit)
        {
            UpsertLock(connection, transaction, AccountDimension, accountLookup, lockedUntilTicks);
        }

        if (sourceCount >= IdentityPolicy.FailureLimit)
        {
            UpsertLock(connection, transaction, SourceDimension, sourceLookup, lockedUntilTicks);
        }

        InsertAudit(connection, transaction, audit, accountLookup, sourceLookup, protectedAudit);
        DateTimeOffset? lockedUntil = ReadLatestLock(
            connection,
            transaction,
            accountLookup,
            sourceLookup);
        transaction.Commit();
        return new AuthenticationLockState(lockedUntil is not null, lockedUntil);
    }

    public void RecordRejection(AuthenticationFailureAuditRecord audit)
    {
        ValidateAudit(audit);
        if (StringComparer.Ordinal.Equals(audit.ReasonCode, "identity.password.invalid"))
        {
            throw new ArgumentException(
                "Invalid-password audits must update the failure window atomically.",
                nameof(audit));
        }

        (string accountLookup, string sourceLookup) = Lookups(
            audit.CanonicalUsername,
            audit.SourceAddress);
        byte[] protectedAudit = ProtectAudit(audit);
        using SqliteConnection connection = _database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        InsertAudit(connection, transaction, audit, accountLookup, sourceLookup, protectedAudit);
        transaction.Commit();
    }

    public AuthenticationFailureAuditRecord? FindAudit(Guid auditId)
    {
        if (auditId == Guid.Empty)
        {
            return null;
        }

        using SqliteConnection connection = _database.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT account_lookup, source_lookup, protected_payload, recorded_at_ticks
            FROM authentication_audit
            WHERE audit_id = $audit_id;
            """;
        command.Parameters.AddWithValue("$audit_id", auditId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        string accountLookup = reader.GetString(0);
        string sourceLookup = reader.GetString(1);
        byte[] protectedPayload = reader.GetFieldValue<byte[]>(2);
        byte[] plaintext = _protector.Unprotect(protectedPayload, AuditPurpose(auditId));
        try
        {
            AuthenticationFailureAuditRecord audit =
                JsonSerializer.Deserialize<AuthenticationFailureAuditRecord>(plaintext) ??
                throw new InvalidOperationException("The protected authentication audit is empty.");
            ValidateAudit(audit);
            if (audit.AuditId != auditId ||
                audit.RecordedAtUtc.UtcTicks != reader.GetInt64(3) ||
                !StringComparer.Ordinal.Equals(
                    accountLookup,
                    _protector.LookupToken(audit.CanonicalUsername, AccountLookupPurpose)) ||
                !StringComparer.Ordinal.Equals(
                    sourceLookup,
                    _protector.LookupToken(audit.SourceAddress, SourceLookupPurpose)))
            {
                throw new InvalidOperationException("The protected authentication audit index is inconsistent.");
            }

            return audit;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void ClearAccountFailures(string canonicalUsername)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalUsername);
        string accountLookup = _protector.LookupToken(canonicalUsername, AccountLookupPurpose);
        using SqliteConnection connection = _database.Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);

        using (SqliteCommand failures = connection.CreateCommand())
        {
            failures.Transaction = transaction;
            failures.CommandText = """
                DELETE FROM authentication_failures
                WHERE dimension = $dimension AND subject_lookup = $lookup;
                """;
            failures.Parameters.AddWithValue("$dimension", AccountDimension);
            failures.Parameters.AddWithValue("$lookup", accountLookup);
            failures.ExecuteNonQuery();
        }

        using (SqliteCommand locks = connection.CreateCommand())
        {
            locks.Transaction = transaction;
            locks.CommandText = """
                DELETE FROM authentication_locks
                WHERE dimension = $dimension AND subject_lookup = $lookup;
                """;
            locks.Parameters.AddWithValue("$dimension", AccountDimension);
            locks.Parameters.AddWithValue("$lookup", accountLookup);
            locks.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static int AddFailure(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int dimension,
        string lookup,
        long windowStartTicks,
        long nowTicks)
    {
        using (SqliteCommand prune = connection.CreateCommand())
        {
            prune.Transaction = transaction;
            prune.CommandText = """
                DELETE FROM authentication_failures
                WHERE dimension = $dimension
                  AND subject_lookup = $lookup
                  AND (occurred_at_ticks <= $window_start OR occurred_at_ticks > $now);
                """;
            prune.Parameters.AddWithValue("$dimension", dimension);
            prune.Parameters.AddWithValue("$lookup", lookup);
            prune.Parameters.AddWithValue("$window_start", windowStartTicks);
            prune.Parameters.AddWithValue("$now", nowTicks);
            prune.ExecuteNonQuery();
        }

        using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO authentication_failures(dimension, subject_lookup, occurred_at_ticks)
                VALUES ($dimension, $lookup, $now);
                """;
            insert.Parameters.AddWithValue("$dimension", dimension);
            insert.Parameters.AddWithValue("$lookup", lookup);
            insert.Parameters.AddWithValue("$now", nowTicks);
            insert.ExecuteNonQuery();
        }

        using SqliteCommand count = connection.CreateCommand();
        count.Transaction = transaction;
        count.CommandText = """
            SELECT COUNT(*)
            FROM authentication_failures
            WHERE dimension = $dimension AND subject_lookup = $lookup;
            """;
        count.Parameters.AddWithValue("$dimension", dimension);
        count.Parameters.AddWithValue("$lookup", lookup);
        return checked((int)Convert.ToInt64(count.ExecuteScalar(), null));
    }

    private static void UpsertLock(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int dimension,
        string lookup,
        long lockedUntilTicks)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO authentication_locks(dimension, subject_lookup, locked_until_ticks)
            VALUES ($dimension, $lookup, $locked_until)
            ON CONFLICT(dimension, subject_lookup) DO UPDATE SET
                locked_until_ticks = MAX(locked_until_ticks, excluded.locked_until_ticks);
            """;
        command.Parameters.AddWithValue("$dimension", dimension);
        command.Parameters.AddWithValue("$lookup", lookup);
        command.Parameters.AddWithValue("$locked_until", lockedUntilTicks);
        command.ExecuteNonQuery();
    }

    private static void InsertAudit(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AuthenticationFailureAuditRecord audit,
        string accountLookup,
        string sourceLookup,
        byte[] protectedPayload)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO authentication_audit(
                audit_id,
                account_lookup,
                source_lookup,
                protected_payload,
                recorded_at_ticks)
            VALUES ($audit_id, $account_lookup, $source_lookup, $payload, $recorded);
            """;
        command.Parameters.AddWithValue("$audit_id", audit.AuditId.ToString("D"));
        command.Parameters.AddWithValue("$account_lookup", accountLookup);
        command.Parameters.AddWithValue("$source_lookup", sourceLookup);
        command.Parameters.Add("$payload", SqliteType.Blob).Value = protectedPayload;
        command.Parameters.AddWithValue("$recorded", audit.RecordedAtUtc.UtcTicks);
        command.ExecuteNonQuery();
    }

    private static void DeleteExpiredLocks(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long nowTicks)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM authentication_locks WHERE locked_until_ticks <= $now;";
        command.Parameters.AddWithValue("$now", nowTicks);
        command.ExecuteNonQuery();
    }

    private static DateTimeOffset? ReadLatestLock(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountLookup,
        string sourceLookup)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT MAX(locked_until_ticks)
            FROM authentication_locks
            WHERE (dimension = $account_dimension AND subject_lookup = $account_lookup)
               OR (dimension = $source_dimension AND subject_lookup = $source_lookup);
            """;
        command.Parameters.AddWithValue("$account_dimension", AccountDimension);
        command.Parameters.AddWithValue("$account_lookup", accountLookup);
        command.Parameters.AddWithValue("$source_dimension", SourceDimension);
        command.Parameters.AddWithValue("$source_lookup", sourceLookup);
        object? value = command.ExecuteScalar();
        if (value is null || value is DBNull)
        {
            return null;
        }

        return new DateTimeOffset(Convert.ToInt64(value, null), TimeSpan.Zero);
    }

    private (string Account, string Source) Lookups(
        string canonicalUsername,
        string sourceAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAddress);
        return (
            _protector.LookupToken(canonicalUsername, AccountLookupPurpose),
            _protector.LookupToken(sourceAddress, SourceLookupPurpose));
    }

    private byte[] ProtectAudit(AuthenticationFailureAuditRecord audit)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(audit);
        try
        {
            return _protector.Protect(plaintext, AuditPurpose(audit.AuditId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static string AuditPurpose(Guid auditId) =>
        $"authentication-audit-payload-v1:{auditId:D}";

    private static void ValidateAudit(AuthenticationFailureAuditRecord audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        bool supportedReason = audit.ReasonCode is
            "identity.password.invalid" or
            "identity.account.locked" or
            "identity.account.disabled";
        if (audit.AuditId == Guid.Empty ||
            audit.CorrelationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(audit.CanonicalUsername) ||
            string.IsNullOrWhiteSpace(audit.SourceAddress) ||
            !Enum.IsDefined(audit.Context) ||
            audit.PrincipalId == Guid.Empty ||
            !supportedReason)
        {
            throw new ArgumentException("The authentication failure audit is invalid.", nameof(audit));
        }
    }
}
