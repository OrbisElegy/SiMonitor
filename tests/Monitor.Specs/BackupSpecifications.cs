// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Monitor.Domain.Identity;
using Monitor.Infrastructure.Identity;
using Monitor.Infrastructure.Persistence;

namespace Monitor.Specs;

internal static class BackupSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(SeparateKeyBackupRestoresConsistentDatabase), SeparateKeyBackupRestoresConsistentDatabase),
        new(nameof(WrongBackupKeyLeavesNoRestore), WrongBackupKeyLeavesNoRestore),
        new(nameof(TamperedBackupLeavesNoRestore), TamperedBackupLeavesNoRestore),
        new(nameof(RestoreNeverOverwritesExistingDatabase), RestoreNeverOverwritesExistingDatabase),
    ];

    private static void SeparateKeyBackupRestoresConsistentDatabase()
    {
        using BackupFixture fixture = new();
        fixture.AddLargePayload();
        BootstrapIdentitySnapshot expected = fixture.Repository.LoadBootstrap();
        EncryptedSqliteBackupDescriptor created = fixture.CreateBackup();
        Check.That(created.PlaintextBytes > 2 * 1_048_576,
            "the round-trip vector must exercise multiple authenticated chunks");
        byte[] backupBytes = File.ReadAllBytes(fixture.BackupPath);
        Check.That(backupBytes.AsSpan().IndexOf("SQLite format 3"u8) < 0,
            "the encrypted backup must not expose a SQLite file header");

        EncryptedSqliteBackupDescriptor restored = EncryptedSqliteBackupService.RestoreBackup(
            fixture.BackupPath,
            fixture.RestorePath,
            fixture.KeyReference,
            fixture.BackupKey);
        SqliteIdentityRepository restoredRepository = new(fixture.RestorePath, fixture.PayloadProtector);
        Check.That(restoredRepository.LoadBootstrap() == expected,
            "restored SQLite content must preserve protected identity state");
        Check.That(restored == created, "restored descriptor must match the authenticated header");
        Check.That(Directory.GetFiles(
            System.IO.Path.GetDirectoryName(fixture.BackupPath)!,
            $"{System.IO.Path.GetFileName(fixture.BackupPath)}.plain-*").Length == 0,
            "plaintext SQLite staging must be removed after backup");
    }

    private static void WrongBackupKeyLeavesNoRestore()
    {
        using BackupFixture fixture = new();
        fixture.CreateBackup();
        byte[] wrongKey = RandomNumberGenerator.GetBytes(EncryptedSqliteBackupService.BackupKeySize);
        try
        {
            Check.That(Throws<CryptographicException>(() =>
                EncryptedSqliteBackupService.RestoreBackup(
                    fixture.BackupPath,
                    fixture.RestorePath,
                    fixture.KeyReference,
                    wrongKey)),
                "an incorrect independent backup key must fail authentication");
            Check.That(!File.Exists(fixture.RestorePath),
                "failed key authentication must not publish a partial database");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrongKey);
        }
    }

    private static void TamperedBackupLeavesNoRestore()
    {
        using BackupFixture fixture = new();
        fixture.CreateBackup();
        byte[] backup = File.ReadAllBytes(fixture.BackupPath);
        backup[^20] ^= 0x40;
        File.WriteAllBytes(fixture.BackupPath, backup);
        Check.That(Throws<CryptographicException>(() =>
            EncryptedSqliteBackupService.RestoreBackup(
                fixture.BackupPath,
                fixture.RestorePath,
                fixture.KeyReference,
                fixture.BackupKey)),
            "tampered encrypted backup chunks must fail authentication");
        Check.That(!File.Exists(fixture.RestorePath),
            "tampered backup must not publish a partial database");
    }

    private static void RestoreNeverOverwritesExistingDatabase()
    {
        using BackupFixture fixture = new();
        fixture.CreateBackup();
        byte[] sentinel = Encoding.UTF8.GetBytes("existing-database-sentinel");
        File.WriteAllBytes(fixture.RestorePath, sentinel);
        Check.That(Throws<IOException>(() =>
            EncryptedSqliteBackupService.RestoreBackup(
                fixture.BackupPath,
                fixture.RestorePath,
                fixture.KeyReference,
                fixture.BackupKey)),
            "restore must reject an existing target");
        Check.That(File.ReadAllBytes(fixture.RestorePath).SequenceEqual(sentinel),
            "restore rejection must preserve the existing target bytes");
    }

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private sealed class BackupFixture : IDisposable
    {
        private readonly byte[] _databaseMasterKey;

        public BackupFixture()
        {
            string stem = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"monitor-backup-{Guid.NewGuid():N}");
            DatabasePath = $"{stem}.db";
            BackupPath = $"{stem}.mbak";
            RestorePath = $"{stem}-restored.db";
            KeyReference = "InformationOfficeBackupKey@1";
            _databaseMasterKey = RandomNumberGenerator.GetBytes(32);
            BackupKey = RandomNumberGenerator.GetBytes(EncryptedSqliteBackupService.BackupKeySize);
            PayloadProtector = new AesGcmIdentityPayloadProtector(_databaseMasterKey);
            Repository = new SqliteIdentityRepository(DatabasePath, PayloadProtector);
            Repository.TryInitializeBootstrap(new PasswordVerifier(
                "TEST-ONLY",
                1,
                Convert.ToBase64String(Encoding.UTF8.GetBytes("backup-salt")),
                Convert.ToBase64String(Encoding.UTF8.GetBytes("backup-subkey"))));
        }

        public string DatabasePath { get; }

        public string BackupPath { get; }

        public string RestorePath { get; }

        public string KeyReference { get; }

        public byte[] BackupKey { get; }

        public AesGcmIdentityPayloadProtector PayloadProtector { get; }

        public SqliteIdentityRepository Repository { get; }

        public EncryptedSqliteBackupDescriptor CreateBackup() =>
            new EncryptedSqliteBackupService(DatabasePath).CreateBackup(
                BackupPath,
                Guid.Parse("61111111-1111-4111-8111-111111111111"),
                new DateTimeOffset(2026, 8, 30, 11, 0, 0, TimeSpan.Zero),
                KeyReference,
                BackupKey);

        public void AddLargePayload()
        {
            byte[] payload = RandomNumberGenerator.GetBytes(2_200_000);
            try
            {
                using SqliteConnection connection = new($"Data Source={DatabasePath};Pooling=False");
                connection.Open();
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE backup_chunk_probe(payload BLOB NOT NULL) STRICT;
                    INSERT INTO backup_chunk_probe(payload) VALUES ($payload);
                    """;
                command.Parameters.Add("$payload", SqliteType.Blob).Value = payload;
                command.ExecuteNonQuery();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }

        public void Dispose()
        {
            PayloadProtector.Dispose();
            CryptographicOperations.ZeroMemory(_databaseMasterKey);
            CryptographicOperations.ZeroMemory(BackupKey);
            foreach (string path in new[]
                     {
                         DatabasePath,
                         $"{DatabasePath}-wal",
                         $"{DatabasePath}-shm",
                         BackupPath,
                         RestorePath,
                         $"{RestorePath}-wal",
                         $"{RestorePath}-shm",
                     })
            {
                File.Delete(path);
            }
        }
    }
}
