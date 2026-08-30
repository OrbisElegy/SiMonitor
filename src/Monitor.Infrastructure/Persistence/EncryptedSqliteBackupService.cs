// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Monitor.Infrastructure.Persistence;

public sealed record EncryptedSqliteBackupDescriptor(
    Guid BackupId,
    string KeyReference,
    DateTimeOffset CreatedAtUtc,
    long PlaintextBytes,
    string PlaintextSha256);

public sealed class EncryptedSqliteBackupService
{
    public const int BackupKeySize = 32;

    private const ushort FormatVersion = 1;
    private const int ChunkSize = 1_048_576;
    private const int TagSize = 16;
    private const int FixedHeaderSize = 90;
    private const int MaximumKeyReferenceBytes = 128;
    private const long MaximumPlaintextBytes = 1L << 40;

    private static ReadOnlySpan<byte> Magic => "MONBKP01"u8;

    private readonly string _databasePath;

    public EncryptedSqliteBackupService(string databasePath)
    {
        _databasePath = ValidateAbsolutePath(databasePath, nameof(databasePath));
    }

    public EncryptedSqliteBackupDescriptor CreateBackup(
        string backupPath,
        Guid backupId,
        DateTimeOffset createdAtUtc,
        string keyReference,
        ReadOnlySpan<byte> backupKey)
    {
        string destinationPath = ValidateNewDestination(backupPath, nameof(backupPath));
        ValidateInputs(backupId, keyReference, backupKey);
        if (!File.Exists(_databasePath))
        {
            throw new FileNotFoundException("The source SQLite database does not exist.", _databasePath);
        }

        string plaintextPath = string.Concat(destinationPath, ".plain-", Guid.NewGuid().ToString("N"));
        string encryptedPath = string.Concat(destinationPath, ".tmp-", Guid.NewGuid().ToString("N"));
        try
        {
            CreateSqliteSnapshot(plaintextPath);
            long plaintextLength = new FileInfo(plaintextPath).Length;
            if (plaintextLength is <= 0 or > MaximumPlaintextBytes)
            {
                throw new InvalidDataException("The SQLite snapshot length is outside the backup limit.");
            }

            byte[] plaintextHash;
            using (FileStream snapshot = File.OpenRead(plaintextPath))
            {
                plaintextHash = SHA256.HashData(snapshot);
            }

            byte[] noncePrefix = RandomNumberGenerator.GetBytes(8);
            byte[] header = EncodeHeader(
                backupId,
                createdAtUtc,
                keyReference,
                plaintextLength,
                plaintextHash,
                noncePrefix);
            EncryptSnapshot(plaintextPath, encryptedPath, header, noncePrefix, backupKey);
            File.Move(encryptedPath, destinationPath, overwrite: false);
            return new EncryptedSqliteBackupDescriptor(
                backupId,
                keyReference,
                createdAtUtc.ToUniversalTime(),
                plaintextLength,
                Convert.ToHexStringLower(plaintextHash));
        }
        finally
        {
            File.Delete(plaintextPath);
            File.Delete(encryptedPath);
        }
    }

    public static EncryptedSqliteBackupDescriptor RestoreBackup(
        string backupPath,
        string restoredDatabasePath,
        string keyReference,
        ReadOnlySpan<byte> backupKey)
    {
        string sourcePath = ValidateAbsolutePath(backupPath, nameof(backupPath));
        string destinationPath = ValidateNewDestination(
            restoredDatabasePath,
            nameof(restoredDatabasePath));
        ValidateKey(keyReference, backupKey);
        string temporaryPath = string.Concat(destinationPath, ".restore-", Guid.NewGuid().ToString("N"));
        try
        {
            EncryptedSqliteBackupDescriptor descriptor = DecryptBackup(
                sourcePath,
                temporaryPath,
                keyReference,
                backupKey);
            ValidateMonitorDatabase(temporaryPath);
            File.Move(temporaryPath, destinationPath, overwrite: false);
            return descriptor;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private void CreateSqliteSnapshot(string snapshotPath)
    {
        using (FileStream empty = CreateRestrictedFile(snapshotPath))
        {
            empty.Flush(flushToDisk: true);
        }

        string sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();
        string destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();
        using SqliteConnection source = new(sourceConnectionString);
        using SqliteConnection destination = new(destinationConnectionString);
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private static void EncryptSnapshot(
        string plaintextPath,
        string encryptedPath,
        byte[] header,
        byte[] noncePrefix,
        ReadOnlySpan<byte> backupKey)
    {
        byte[] plaintext = new byte[ChunkSize];
        byte[] ciphertext = new byte[ChunkSize];
        byte[] tag = new byte[TagSize];
        byte[] nonce = new byte[12];
        byte[] associatedData = new byte[header.Length + sizeof(uint)];
        header.CopyTo(associatedData, 0);
        noncePrefix.CopyTo(nonce, 0);
        try
        {
            using FileStream input = File.OpenRead(plaintextPath);
            using FileStream output = CreateRestrictedFile(encryptedPath);
            output.Write(header);
            using AesGcm aes = new(backupKey, TagSize);
            uint chunkIndex = 0;
            int bytesRead;
            while ((bytesRead = ReadChunk(input, plaintext)) > 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), chunkIndex);
                BinaryPrimitives.WriteUInt32BigEndian(associatedData.AsSpan(header.Length), chunkIndex);
                aes.Encrypt(
                    nonce,
                    plaintext.AsSpan(0, bytesRead),
                    ciphertext.AsSpan(0, bytesRead),
                    tag,
                    associatedData);
                output.Write(ciphertext, 0, bytesRead);
                output.Write(tag);
                chunkIndex = checked(chunkIndex + 1);
            }

            output.Flush(flushToDisk: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    private static EncryptedSqliteBackupDescriptor DecryptBackup(
        string backupPath,
        string plaintextPath,
        string expectedKeyReference,
        ReadOnlySpan<byte> backupKey)
    {
        using FileStream input = File.OpenRead(backupPath);
        byte[] fixedHeader = new byte[FixedHeaderSize];
        input.ReadExactly(fixedHeader);
        ushort headerLength = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.AsSpan(10));
        if (headerLength is < FixedHeaderSize or > FixedHeaderSize + MaximumKeyReferenceBytes)
        {
            throw new InvalidDataException("The encrypted backup header length is invalid.");
        }

        byte[] header = new byte[headerLength];
        fixedHeader.CopyTo(header, 0);
        input.ReadExactly(header.AsSpan(FixedHeaderSize));
        BackupHeader parsed = DecodeHeader(header);
        if (!StringComparer.Ordinal.Equals(parsed.KeyReference, expectedKeyReference))
        {
            throw new CryptographicException("The supplied backup key reference does not match.");
        }

        long chunkCount = checked((parsed.PlaintextLength + ChunkSize - 1) / ChunkSize);
        long expectedFileLength = checked(headerLength + parsed.PlaintextLength + chunkCount * TagSize);
        if (input.Length != expectedFileLength)
        {
            throw new InvalidDataException("The encrypted backup length is inconsistent.");
        }

        byte[] ciphertext = new byte[ChunkSize];
        byte[] plaintext = new byte[ChunkSize];
        byte[] tag = new byte[TagSize];
        byte[] nonce = new byte[12];
        byte[] associatedData = new byte[header.Length + sizeof(uint)];
        header.CopyTo(associatedData, 0);
        parsed.NoncePrefix.CopyTo(nonce, 0);
        try
        {
            using FileStream output = CreateRestrictedFile(plaintextPath);
            using AesGcm aes = new(backupKey, TagSize);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long remaining = parsed.PlaintextLength;
            uint chunkIndex = 0;
            while (remaining > 0)
            {
                int count = checked((int)Math.Min(ChunkSize, remaining));
                input.ReadExactly(ciphertext.AsSpan(0, count));
                input.ReadExactly(tag);
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), chunkIndex);
                BinaryPrimitives.WriteUInt32BigEndian(associatedData.AsSpan(header.Length), chunkIndex);
                aes.Decrypt(
                    nonce,
                    ciphertext.AsSpan(0, count),
                    tag,
                    plaintext.AsSpan(0, count),
                    associatedData);
                output.Write(plaintext, 0, count);
                hash.AppendData(plaintext, 0, count);
                remaining -= count;
                chunkIndex = checked(chunkIndex + 1);
            }

            byte[] actualHash = hash.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(actualHash, parsed.PlaintextHash))
            {
                throw new CryptographicException("The restored SQLite content hash does not match.");
            }

            output.Flush(flushToDisk: true);
            return new EncryptedSqliteBackupDescriptor(
                parsed.BackupId,
                parsed.KeyReference,
                parsed.CreatedAtUtc,
                parsed.PlaintextLength,
                Convert.ToHexStringLower(actualHash));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    private static byte[] EncodeHeader(
        Guid backupId,
        DateTimeOffset createdAtUtc,
        string keyReference,
        long plaintextLength,
        byte[] plaintextHash,
        byte[] noncePrefix)
    {
        byte[] keyReferenceBytes = Encoding.ASCII.GetBytes(keyReference);
        byte[] header = new byte[FixedHeaderSize + keyReferenceBytes.Length];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), FormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), checked((ushort)header.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), ChunkSize);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(16), plaintextLength);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(24), createdAtUtc.UtcTicks);
        backupId.TryWriteBytes(header.AsSpan(32), bigEndian: true, out _);
        plaintextHash.CopyTo(header, 48);
        noncePrefix.CopyTo(header, 80);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(88), checked((ushort)keyReferenceBytes.Length));
        keyReferenceBytes.CopyTo(header, FixedHeaderSize);
        return header;
    }

    private static BackupHeader DecodeHeader(byte[] header)
    {
        if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic) ||
            BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8)) != FormatVersion ||
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12)) != ChunkSize)
        {
            throw new InvalidDataException("The encrypted backup format is unsupported.");
        }

        long plaintextLength = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(16));
        int keyReferenceLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(88));
        if (plaintextLength is <= 0 or > MaximumPlaintextBytes ||
            keyReferenceLength is < 1 or > MaximumKeyReferenceBytes ||
            header.Length != FixedHeaderSize + keyReferenceLength)
        {
            throw new InvalidDataException("The encrypted backup header is inconsistent.");
        }

        string keyReference = Encoding.ASCII.GetString(header, FixedHeaderSize, keyReferenceLength);
        ValidateStableId(keyReference, nameof(header));
        return new BackupHeader(
            new Guid(header.AsSpan(32, 16), bigEndian: true),
            new DateTimeOffset(BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(24)), TimeSpan.Zero),
            keyReference,
            plaintextLength,
            header.AsSpan(48, 32).ToArray(),
            header.AsSpan(80, 8).ToArray());
    }

    private static void ValidateMonitorDatabase(string path)
    {
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        using SqliteConnection connection = new(connectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        if (!StringComparer.Ordinal.Equals(Convert.ToString(command.ExecuteScalar(), null), "ok"))
        {
            throw new InvalidDataException("The restored SQLite database failed its integrity check.");
        }

        command.CommandText = "SELECT schema_version FROM schema_info WHERE singleton = 1;";
        if (Convert.ToInt64(command.ExecuteScalar(), null) != 1)
        {
            throw new InvalidDataException("The restored database schema is unsupported.");
        }
    }

    private static int ReadChunk(Stream input, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = input.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static FileStream CreateRestrictedFile(string path)
    {
        FileStreamOptions options = new()
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.WriteThrough,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(path, options);
    }

    private static void ValidateInputs(
        Guid backupId,
        string keyReference,
        ReadOnlySpan<byte> backupKey)
    {
        if (backupId == Guid.Empty)
        {
            throw new ArgumentException("The backup ID must not be empty.", nameof(backupId));
        }

        ValidateKey(keyReference, backupKey);
    }

    private static void ValidateKey(string keyReference, ReadOnlySpan<byte> backupKey)
    {
        ValidateStableId(keyReference, nameof(keyReference));
        if (backupKey.Length != BackupKeySize)
        {
            throw new ArgumentException("The backup key must be exactly 256 bits.", nameof(backupKey));
        }
    }

    private static void ValidateStableId(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumKeyReferenceBytes ||
            !IsAsciiLetter(value[0]) || value.Any(character =>
                !IsAsciiLetter(character) && !char.IsAsciiDigit(character) &&
                character is not ('.' or '_' or ':' or '@' or '/' or '-')))
        {
            throw new ArgumentException("The backup key reference is invalid.", parameterName);
        }
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static string ValidateNewDestination(string path, string parameterName)
    {
        string fullPath = ValidateAbsolutePath(path, parameterName);
        if (File.Exists(fullPath))
        {
            throw new IOException("The destination already exists and will not be overwritten.");
        }

        return fullPath;
    }

    private static string ValidateAbsolutePath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (!Path.IsPathRooted(path))
        {
            throw new ArgumentException("The path must be absolute.", parameterName);
        }

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is null || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The containing directory does not exist.");
        }

        return fullPath;
    }

    private sealed record BackupHeader(
        Guid BackupId,
        DateTimeOffset CreatedAtUtc,
        string KeyReference,
        long PlaintextLength,
        byte[] PlaintextHash,
        byte[] NoncePrefix);
}
