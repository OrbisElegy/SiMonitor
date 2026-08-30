// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;

namespace Monitor.Infrastructure.Identity;

public sealed class AesGcmIdentityPayloadProtector : IIdentityPayloadProtector, IDisposable
{
    public const int MasterKeySize = 32;

    private const byte EnvelopeVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize + TagSize;

    private byte[]? _encryptionKey;
    private byte[]? _lookupKey;

    public AesGcmIdentityPayloadProtector(ReadOnlySpan<byte> masterKey)
    {
        if (masterKey.Length != MasterKeySize)
        {
            throw new ArgumentException("The identity master key must be exactly 256 bits.", nameof(masterKey));
        }

        _encryptionKey = DeriveKey(masterKey, "MONITOR-IDENTITY-ENCRYPTION-V1");
        _lookupKey = DeriveKey(masterKey, "MONITOR-IDENTITY-LOOKUP-V1");
    }

    public byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose)
    {
        ValidatePurpose(purpose);
        byte[] key = GetKey(_encryptionKey);
        byte[] envelope = new byte[HeaderSize + plaintext.Length];
        envelope[0] = EnvelopeVersion;
        Span<byte> nonce = envelope.AsSpan(1, NonceSize);
        Span<byte> tag = envelope.AsSpan(1 + NonceSize, TagSize);
        Span<byte> ciphertext = envelope.AsSpan(HeaderSize);
        RandomNumberGenerator.Fill(nonce);

        byte[] associatedData = Encoding.UTF8.GetBytes(purpose);
        try
        {
            using AesGcm aes = new(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            return envelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedPayload, string purpose)
    {
        ValidatePurpose(purpose);
        if (protectedPayload.Length < HeaderSize || protectedPayload[0] != EnvelopeVersion)
        {
            throw new CryptographicException("The protected identity payload is invalid.");
        }

        byte[] key = GetKey(_encryptionKey);
        ReadOnlySpan<byte> nonce = protectedPayload.Slice(1, NonceSize);
        ReadOnlySpan<byte> tag = protectedPayload.Slice(1 + NonceSize, TagSize);
        ReadOnlySpan<byte> ciphertext = protectedPayload[HeaderSize..];
        byte[] plaintext = new byte[ciphertext.Length];
        byte[] associatedData = Encoding.UTF8.GetBytes(purpose);
        try
        {
            using AesGcm aes = new(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    public string LookupToken(string value, string purpose)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidatePurpose(purpose);
        byte[] key = GetKey(_lookupKey);
        byte[] input = Encoding.UTF8.GetBytes(string.Concat(purpose, "\0", value));
        try
        {
            byte[] digest = HMACSHA256.HashData(key, input);
            return Convert.ToBase64String(digest)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public void Dispose()
    {
        byte[]? encryptionKey = Interlocked.Exchange(ref _encryptionKey, null);
        byte[]? lookupKey = Interlocked.Exchange(ref _lookupKey, null);
        if (encryptionKey is not null)
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
        }

        if (lookupKey is not null)
        {
            CryptographicOperations.ZeroMemory(lookupKey);
        }
    }

    private static byte[] DeriveKey(ReadOnlySpan<byte> masterKey, string domain) =>
        HMACSHA256.HashData(masterKey, Encoding.ASCII.GetBytes(domain));

    private static byte[] GetKey(byte[]? key) =>
        key ?? throw new ObjectDisposedException(nameof(AesGcmIdentityPayloadProtector));

    private static void ValidatePurpose(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            throw new ArgumentException("A non-empty protection purpose is required.", nameof(purpose));
        }
    }
}
