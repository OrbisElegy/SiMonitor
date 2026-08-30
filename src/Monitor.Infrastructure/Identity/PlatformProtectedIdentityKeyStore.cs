// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Monitor.Infrastructure.Identity;

public sealed class PlatformProtectedIdentityKeyStore
{
    private const ushort FormatVersion = 1;
    private const int FixedHeaderSize = 16;
    private const int DigestSize = 32;
    private const int MaximumProtectorIdBytes = 128;
    private const int MaximumWrappedKeyBytes = 65_536;

    private static ReadOnlySpan<byte> Magic => "MONKEY01"u8;

    private readonly string _keyFilePath;
    private readonly IPlatformKeyProtector _platformProtector;

    public PlatformProtectedIdentityKeyStore(
        string keyFilePath,
        IPlatformKeyProtector platformProtector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyFilePath);
        if (!Path.IsPathRooted(keyFilePath))
        {
            throw new ArgumentException("The protected key file path must be absolute.", nameof(keyFilePath));
        }

        _keyFilePath = Path.GetFullPath(keyFilePath);
        string? directory = Path.GetDirectoryName(_keyFilePath);
        if (directory is null || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The protected key file directory does not exist.");
        }

        _platformProtector = platformProtector ??
            throw new ArgumentNullException(nameof(platformProtector));
        ValidateStableId(_platformProtector.ProtectorId, nameof(platformProtector));
    }

    public AesGcmIdentityPayloadProtector OpenOrCreatePayloadProtector()
    {
        byte[] masterKey = File.Exists(_keyFilePath)
            ? LoadMasterKey()
            : CreateOrLoadMasterKey();
        try
        {
            return new AesGcmIdentityPayloadProtector(masterKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    private byte[] CreateOrLoadMasterKey()
    {
        byte[] masterKey = RandomNumberGenerator.GetBytes(
            AesGcmIdentityPayloadProtector.MasterKeySize);
        string temporaryPath = string.Concat(_keyFilePath, ".tmp-", Guid.NewGuid().ToString("N"));
        try
        {
            byte[] wrappedKey = _platformProtector.Protect(masterKey);
            byte[] envelope = EncodeEnvelope(_platformProtector.ProtectorId, wrappedKey);
            try
            {
                WriteRestrictedFile(temporaryPath, envelope);
                try
                {
                    File.Move(temporaryPath, _keyFilePath, overwrite: false);
                    return masterKey;
                }
                catch (IOException) when (File.Exists(_keyFilePath))
                {
                    CryptographicOperations.ZeroMemory(masterKey);
                    return LoadMasterKey();
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(envelope);
            }
        }
        catch
        {
            CryptographicOperations.ZeroMemory(masterKey);
            throw;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private byte[] LoadMasterKey()
    {
        ValidateRestrictedPermissions(_keyFilePath);
        byte[] envelope = File.ReadAllBytes(_keyFilePath);
        try
        {
            (string protectorId, byte[] wrappedKey) = DecodeEnvelope(envelope);
            if (!StringComparer.Ordinal.Equals(protectorId, _platformProtector.ProtectorId))
            {
                throw new CryptographicException("The configured platform key protector does not match.");
            }

            byte[] masterKey = _platformProtector.Unprotect(wrappedKey);
            if (masterKey.Length != AesGcmIdentityPayloadProtector.MasterKeySize)
            {
                CryptographicOperations.ZeroMemory(masterKey);
                throw new CryptographicException("The unwrapped identity key has an invalid length.");
            }

            return masterKey;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(envelope);
        }
    }

    private static byte[] EncodeEnvelope(string protectorId, byte[] wrappedKey)
    {
        byte[] protectorIdBytes = Encoding.ASCII.GetBytes(protectorId);
        if (wrappedKey.Length is < 1 or > MaximumWrappedKeyBytes)
        {
            throw new CryptographicException("The wrapped identity key has an invalid length.");
        }

        int payloadLength = checked(FixedHeaderSize + protectorIdBytes.Length + wrappedKey.Length);
        byte[] envelope = new byte[checked(payloadLength + DigestSize)];
        Magic.CopyTo(envelope);
        BinaryPrimitives.WriteUInt16LittleEndian(envelope.AsSpan(8), FormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(
            envelope.AsSpan(10),
            checked((ushort)protectorIdBytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(envelope.AsSpan(12), checked((uint)wrappedKey.Length));
        protectorIdBytes.CopyTo(envelope.AsSpan(FixedHeaderSize));
        wrappedKey.CopyTo(envelope.AsSpan(FixedHeaderSize + protectorIdBytes.Length));
        SHA256.HashData(envelope.AsSpan(0, payloadLength), envelope.AsSpan(payloadLength, DigestSize));
        return envelope;
    }

    private static (string ProtectorId, byte[] WrappedKey) DecodeEnvelope(ReadOnlySpan<byte> envelope)
    {
        if (envelope.Length < FixedHeaderSize + DigestSize ||
            !envelope[..Magic.Length].SequenceEqual(Magic) ||
            BinaryPrimitives.ReadUInt16LittleEndian(envelope[8..]) != FormatVersion)
        {
            throw new InvalidDataException("The protected identity key envelope is invalid.");
        }

        int protectorIdLength = BinaryPrimitives.ReadUInt16LittleEndian(envelope[10..]);
        uint wrappedLengthValue = BinaryPrimitives.ReadUInt32LittleEndian(envelope[12..]);
        if (protectorIdLength is < 1 or > MaximumProtectorIdBytes ||
            wrappedLengthValue is 0 or > MaximumWrappedKeyBytes)
        {
            throw new InvalidDataException("The protected identity key envelope has invalid lengths.");
        }

        int wrappedLength = checked((int)wrappedLengthValue);
        int payloadLength = checked(FixedHeaderSize + protectorIdLength + wrappedLength);
        if (envelope.Length != payloadLength + DigestSize)
        {
            throw new InvalidDataException("The protected identity key envelope length is inconsistent.");
        }

        Span<byte> computedDigest = stackalloc byte[DigestSize];
        SHA256.HashData(envelope[..payloadLength], computedDigest);
        if (!CryptographicOperations.FixedTimeEquals(computedDigest, envelope[payloadLength..]))
        {
            throw new InvalidDataException("The protected identity key envelope is corrupt.");
        }

        string protectorId = Encoding.ASCII.GetString(
            envelope.Slice(FixedHeaderSize, protectorIdLength));
        ValidateStableId(protectorId, nameof(envelope));
        byte[] wrappedKey = envelope.Slice(
            FixedHeaderSize + protectorIdLength,
            wrappedLength).ToArray();
        return (protectorId, wrappedKey);
    }

    private static void WriteRestrictedFile(string path, ReadOnlySpan<byte> contents)
    {
        FileStreamOptions options = new()
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.WriteThrough,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using FileStream stream = new(path, options);
        stream.Write(contents);
        stream.Flush(flushToDisk: true);
    }

    private static void ValidateRestrictedPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        UnixFileMode mode = File.GetUnixFileMode(path);
        UnixFileMode forbidden = UnixFileMode.GroupRead |
            UnixFileMode.GroupWrite |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherWrite |
            UnixFileMode.OtherExecute;
        if ((mode & forbidden) != 0)
        {
            throw new UnauthorizedAccessException(
                "The protected identity key file grants group or other access.");
        }
    }

    private static void ValidateStableId(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumProtectorIdBytes ||
            !IsAsciiLetter(value[0]))
        {
            throw new ArgumentException("The platform protector ID is invalid.", parameterName);
        }

        foreach (char character in value)
        {
            if (!IsAsciiLetter(character) && !char.IsAsciiDigit(character) &&
                character is not ('.' or '_' or ':' or '@' or '/' or '-'))
            {
                throw new ArgumentException("The platform protector ID is invalid.", parameterName);
            }
        }
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
