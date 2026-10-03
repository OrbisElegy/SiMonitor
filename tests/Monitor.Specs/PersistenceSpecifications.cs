// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Monitor.Application.Identity;
using Monitor.Domain.Identity;
using Monitor.Infrastructure.Identity;

namespace Monitor.Specs;

internal static class PersistenceSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ProtectedPayloadBindsPurposeAndKey), ProtectedPayloadBindsPurposeAndKey),
        new(nameof(SqliteBootstrapSurvivesRestartAtomically), SqliteBootstrapSurvivesRestartAtomically),
        new(nameof(RevisionConflictLeavesNoFragments), RevisionConflictLeavesNoFragments),
        new(nameof(WrongDatabaseKeyFailsClosed), WrongDatabaseKeyFailsClosed),
        new(nameof(DatabaseContainsNoSensitivePlaintext), DatabaseContainsNoSensitivePlaintext),
        new(nameof(AuthenticationAuditSurvivesRestart), AuthenticationAuditSurvivesRestart),
        new(nameof(DuplicateAuditRollsBackFailureCount), DuplicateAuditRollsBackFailureCount),
        new(nameof(AuthenticationLockSurvivesRestart), AuthenticationLockSurvivesRestart),
    ];

    private static void ProtectedPayloadBindsPurposeAndKey()
    {
        byte[] key = RandomNumberGenerator.GetBytes(AesGcmIdentityPayloadProtector.MasterKeySize);
        byte[] otherKey = RandomNumberGenerator.GetBytes(AesGcmIdentityPayloadProtector.MasterKeySize);
        byte[] plaintext = Encoding.UTF8.GetBytes("identity-secret-vector");
        using AesGcmIdentityPayloadProtector protector = new(key);
        byte[] protectedPayload = protector.Protect(plaintext, "purpose-a");
        Check.That(protector.Unprotect(protectedPayload, "purpose-a").SequenceEqual(plaintext),
            "matching key and purpose must decrypt");
        Check.That(ThrowsCryptographic(() => protector.Unprotect(protectedPayload, "purpose-b")),
            "a payload must not be movable across purposes");

        using AesGcmIdentityPayloadProtector otherProtector = new(otherKey);
        Check.That(ThrowsCryptographic(() => otherProtector.Unprotect(protectedPayload, "purpose-a")),
            "a different platform key must fail closed");
    }

    private static void SqliteBootstrapSurvivesRestartAtomically()
    {
        using DatabaseFixture fixture = new();
        PasswordVerifier bootstrapVerifier = Verifier("bootstrap-salt", "bootstrap-subkey");
        InstitutionAccount account = Account("ExamAdmin", "EXAMADMIN");
        BootstrapAuditRecord audit = Audit(account);

        using (AesGcmIdentityPayloadProtector protector = new(fixture.MasterKey))
        {
            SqliteIdentityRepository repository = new(fixture.Path, protector);
            Check.That(repository.TryInitializeBootstrap(bootstrapVerifier),
                "first initialization must create bootstrap state");
            Check.That(!repository.TryInitializeBootstrap(bootstrapVerifier),
                "bootstrap state must not be replaceable");
            Check.That(repository.TryCompleteBootstrap(
                new BootstrapCompletion(1, account, audit, true)) == BootstrapCommitResult.Committed,
                "valid completion must commit");
        }

        using AesGcmIdentityPayloadProtector restartedProtector = new(fixture.MasterKey);
        SqliteIdentityRepository restarted = new(fixture.Path, restartedProtector);
        BootstrapIdentitySnapshot snapshot = restarted.LoadBootstrap();
        InstitutionAccount? loadedAccount = restarted.FindByCanonicalUsername("EXAMADMIN");
        Check.That(snapshot is
        {
            State: BootstrapLifecycleState.Completed,
            BootstrapVerifier: null,
            RootPermanentlyRetired: true,
            Revision: 2,
        }, "completed bootstrap state must survive process restart");
        Check.That(loadedAccount == account, "protected account must survive process restart");
        Check.That(restarted.AuditExists(audit.AuditId), "activation audit must commit with account");
        using SqliteConnection connection = new($"Data Source={fixture.Path};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Check.That(StringComparer.OrdinalIgnoreCase.Equals(Convert.ToString(command.ExecuteScalar(), null), "wal"),
            "identity database persists WAL journal mode after reopening");
    }

    private static void RevisionConflictLeavesNoFragments()
    {
        using DatabaseFixture fixture = new();
        using AesGcmIdentityPayloadProtector protector = new(fixture.MasterKey);
        SqliteIdentityRepository repository = new(fixture.Path, protector);
        repository.TryInitializeBootstrap(Verifier("conflict-salt", "conflict-subkey"));
        InstitutionAccount account = Account("ConflictAdmin", "CONFLICTADMIN");
        BootstrapAuditRecord audit = Audit(account);

        BootstrapCommitResult result = repository.TryCompleteBootstrap(
            new BootstrapCompletion(2, account, audit, true));
        Check.That(result == BootstrapCommitResult.ConcurrencyConflict,
            "stale revision must report a conflict");
        Check.That(repository.LoadBootstrap().State == BootstrapLifecycleState.BootstrapOnly,
            "conflict must retain bootstrap-only state");
        Check.That(repository.FindByCanonicalUsername("CONFLICTADMIN") is null,
            "conflict must not leave an account fragment");
        Check.That(!repository.AuditExists(audit.AuditId),
            "conflict must not leave an audit fragment");
    }

    private static void WrongDatabaseKeyFailsClosed()
    {
        using DatabaseFixture fixture = new();
        using (AesGcmIdentityPayloadProtector protector = new(fixture.MasterKey))
        {
            SqliteIdentityRepository repository = new(fixture.Path, protector);
            repository.TryInitializeBootstrap(Verifier("key-salt", "key-subkey"));
        }

        byte[] wrongKey = RandomNumberGenerator.GetBytes(AesGcmIdentityPayloadProtector.MasterKeySize);
        using AesGcmIdentityPayloadProtector wrongProtector = new(wrongKey);
        SqliteIdentityRepository reopened = new(fixture.Path, wrongProtector);
        Check.That(ThrowsCryptographic(() => reopened.LoadBootstrap()),
            "wrong recovery key must not expose or silently replace identity state");
    }

    private static void DatabaseContainsNoSensitivePlaintext()
    {
        using DatabaseFixture fixture = new();
        const string displayUsername = "PlaintextSentinelAdmin";
        const string canonicalUsername = "PLAINTEXTSENTINELADMIN";
        const string saltSentinel = "sensitive-salt-sentinel";
        const string subkeySentinel = "sensitive-subkey-sentinel";
        const string sourceSentinel = "198.51.100.239-sensitive-source";
        using AesGcmIdentityPayloadProtector protector = new(fixture.MasterKey);
        SqliteIdentityRepository repository = new(fixture.Path, protector);
        repository.TryInitializeBootstrap(Verifier(saltSentinel, subkeySentinel));
        InstitutionAccount account = Account(displayUsername, canonicalUsername);
        repository.TryCompleteBootstrap(new BootstrapCompletion(1, account, Audit(account), true));

        SqliteAuthenticationFailureTracker tracker = new(fixture.Path, protector);
        tracker.RecordFailure(FailureAudit(canonicalUsername, sourceSentinel));
        byte[] databaseBytes = fixture.ReadAllDatabaseBytes();
        foreach (string sentinel in new[]
                 {
                     displayUsername,
                     canonicalUsername,
                     Convert.ToBase64String(Encoding.UTF8.GetBytes(saltSentinel)),
                     Convert.ToBase64String(Encoding.UTF8.GetBytes(subkeySentinel)),
                     sourceSentinel,
                 })
        {
            Check.That(!Contains(databaseBytes, Encoding.UTF8.GetBytes(sentinel)),
                $"database files must not contain sensitive plaintext: {sentinel}");
        }
    }

    private static void AuthenticationAuditSurvivesRestart()
    {
        using DatabaseFixture fixture = new();
        AuthenticationFailureAuditRecord audit = FailureAudit(
            "AUDITEDADMIN",
            "203.0.113.71");
        AuthenticationFailureAuditRecord lockedAudit = FailureAudit(
            "AUDITEDADMIN",
            "203.0.113.71",
            "identity.account.locked");
        using (AesGcmIdentityPayloadProtector protector = new(fixture.MasterKey))
        {
            SqliteAuthenticationFailureTracker tracker = new(fixture.Path, protector);
            tracker.RecordFailure(audit);
            tracker.RecordRejection(lockedAudit);
        }

        using AesGcmIdentityPayloadProtector restartedProtector = new(fixture.MasterKey);
        SqliteAuthenticationFailureTracker restarted = new(fixture.Path, restartedProtector);
        Check.That(restarted.FindAudit(audit.AuditId) == audit,
            "protected authentication audit must survive process restart");
        Check.That(restarted.FindAudit(lockedAudit.AuditId) == lockedAudit,
            "a lock rejection audit must survive process restart");
    }

    private static void DuplicateAuditRollsBackFailureCount()
    {
        using DatabaseFixture fixture = new();
        using AesGcmIdentityPayloadProtector protector = new(fixture.MasterKey);
        SqliteAuthenticationFailureTracker tracker = new(fixture.Path, protector);
        AuthenticationFailureAuditRecord first = FailureAudit("ATOMICADMIN", "203.0.113.72");
        tracker.RecordFailure(first);
        bool duplicateRejected = false;
        try
        {
            tracker.RecordFailure(first);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            duplicateRejected = true;
        }

        Check.That(duplicateRejected, "duplicate audit identity must reject");
        for (int attempt = 0; attempt < IdentityPolicy.FailureLimit - 2; attempt++)
        {
            Check.That(!tracker.RecordFailure(FailureAudit("ATOMICADMIN", "203.0.113.72")).IsLocked,
                "a rolled-back audit insert must not increment the failure window");
        }

        Check.That(tracker.RecordFailure(FailureAudit("ATOMICADMIN", "203.0.113.72")).IsLocked,
            "the fifth committed audit/failure must establish the lock");
    }

    private static void AuthenticationLockSurvivesRestart()
    {
        using DatabaseFixture fixture = new();
        const string account = "DURABLEADMIN";
        const string source = "203.0.113.44";
        using (AesGcmIdentityPayloadProtector protector = new(fixture.MasterKey))
        {
            SqliteAuthenticationFailureTracker tracker = new(fixture.Path, protector);
            for (int attempt = 0; attempt < IdentityPolicy.FailureLimit - 1; attempt++)
            {
                Check.That(!tracker.RecordFailure(FailureAudit(account, source)).IsLocked,
                    "pre-threshold failures must remain unlocked");
            }
        }

        using (AesGcmIdentityPayloadProtector protector = new(fixture.MasterKey))
        {
            SqliteAuthenticationFailureTracker tracker = new(fixture.Path, protector);
            AuthenticationLockState fifth = tracker.RecordFailure(FailureAudit(account, source));
            Check.That(fifth.IsLocked && fifth.LockedUntilUtc == TestTime + IdentityPolicy.LockDuration,
                "failure count must survive restart and lock on the fifth attempt");
        }

        using AesGcmIdentityPayloadProtector finalProtector = new(fixture.MasterKey);
        SqliteAuthenticationFailureTracker restarted = new(fixture.Path, finalProtector);
        Check.That(restarted.GetLockState(account, source, TestTime).IsLocked,
            "active account/source lock must survive another restart");
    }

    private static PasswordVerifier Verifier(string salt, string subkey) =>
        new(
            "TEST-ONLY",
            1,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(salt)),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(subkey)));

    private static InstitutionAccount Account(string username, string canonicalUsername) =>
        new(
            Guid.Parse("51111111-1111-4111-8111-111111111111"),
            username,
            canonicalUsername,
            InstitutionRole.Admin,
            InstitutionAccountState.Active,
            Verifier("account-salt-sentinel", "account-subkey-sentinel"),
            1);

    private static BootstrapAuditRecord Audit(InstitutionAccount account) =>
        new(
            Guid.Parse("52222222-2222-4222-8222-222222222222"),
            Guid.Parse("53333333-3333-4333-8333-333333333333"),
            "BootstrapAdminActivated",
            account.PrincipalId,
            account.CanonicalUsername,
            TestTime);

    private static AuthenticationFailureAuditRecord FailureAudit(
        string canonicalUsername,
        string sourceAddress,
        string reasonCode = "identity.password.invalid") =>
        new(
            Guid.NewGuid(),
            Guid.Parse("54444444-4444-4444-8444-444444444444"),
            canonicalUsername,
            sourceAddress,
            InstitutionAuthenticationContext.Teaching,
            null,
            reasonCode,
            TestTime);

    private static bool ThrowsCryptographic(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (CryptographicException)
        {
            return true;
        }
    }

    private static bool Contains(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle) =>
        haystack.IndexOf(needle) >= 0;

    private static readonly DateTimeOffset TestTime =
        new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);

    private sealed class DatabaseFixture : IDisposable
    {
        public DatabaseFixture()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"monitor-identity-{Guid.NewGuid():N}.db");
            MasterKey = RandomNumberGenerator.GetBytes(
                AesGcmIdentityPayloadProtector.MasterKeySize);
        }

        public string Path { get; }

        public byte[] MasterKey { get; }

        public byte[] ReadAllDatabaseBytes()
        {
            List<byte> bytes = [];
            foreach (string path in new[] { Path, $"{Path}-wal", $"{Path}-shm" })
            {
                if (File.Exists(path))
                {
                    bytes.AddRange(File.ReadAllBytes(path));
                }
            }

            return [.. bytes];
        }

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(MasterKey);
            foreach (string path in new[] { Path, $"{Path}-wal", $"{Path}-shm" })
            {
                File.Delete(path);
            }
        }
    }
}
