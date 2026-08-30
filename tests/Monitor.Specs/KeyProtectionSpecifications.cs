// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using Monitor.Infrastructure.Identity;

namespace Monitor.Specs;

internal static class KeyProtectionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PlatformWrappedKeyReopensIdentityPayloads), PlatformWrappedKeyReopensIdentityPayloads),
        new(nameof(CorruptWrappedKeyDoesNotRegenerate), CorruptWrappedKeyDoesNotRegenerate),
        new(nameof(ProtectorMismatchFailsClosed), ProtectorMismatchFailsClosed),
        new(nameof(OverbroadKeyPermissionsAreRejected), OverbroadKeyPermissionsAreRejected),
    ];

    private static void PlatformWrappedKeyReopensIdentityPayloads()
    {
        using KeyFileFixture fixture = new();
        using TestPlatformKeyProtector platform = new("TestPlatformProtector@1");
        PlatformProtectedIdentityKeyStore store = new(fixture.Path, platform);
        byte[] plaintext = Encoding.UTF8.GetBytes("key-store-round-trip");
        byte[] protectedPayload;
        using (AesGcmIdentityPayloadProtector payloadProtector =
               store.OpenOrCreatePayloadProtector())
        {
            protectedPayload = payloadProtector.Protect(plaintext, "key-store-spec");
        }

        byte[] keyFile = File.ReadAllBytes(fixture.Path);
        Check.That(platform.LastProtectedPlaintext is { Length: 32 } generatedMasterKey &&
            keyFile.AsSpan().IndexOf(generatedMasterKey) < 0,
            "the key file must not contain the generated database master key");

        PlatformProtectedIdentityKeyStore restarted = new(fixture.Path, platform);
        using AesGcmIdentityPayloadProtector restartedProtector =
            restarted.OpenOrCreatePayloadProtector();
        Check.That(restartedProtector.Unprotect(protectedPayload, "key-store-spec")
            .SequenceEqual(plaintext),
            "the same platform protector must recover the database payload key");
    }

    private static void CorruptWrappedKeyDoesNotRegenerate()
    {
        using KeyFileFixture fixture = new();
        using TestPlatformKeyProtector platform = new("TestPlatformProtector@1");
        PlatformProtectedIdentityKeyStore store = new(fixture.Path, platform);
        using (store.OpenOrCreatePayloadProtector())
        {
        }

        byte[] corrupted = File.ReadAllBytes(fixture.Path);
        corrupted[^1] ^= 0x80;
        File.WriteAllBytes(fixture.Path, corrupted);
        Check.That(Throws<InvalidDataException>(() => store.OpenOrCreatePayloadProtector()),
            "a corrupt wrapped key must fail instead of replacing the database key");
        Check.That(File.ReadAllBytes(fixture.Path).SequenceEqual(corrupted),
            "failed recovery must leave the original evidence untouched");
    }

    private static void ProtectorMismatchFailsClosed()
    {
        using KeyFileFixture fixture = new();
        byte[] wrappingKey = RandomNumberGenerator.GetBytes(32);
        using (TestPlatformKeyProtector original = new("ApprovedProtector@1", wrappingKey))
        {
            PlatformProtectedIdentityKeyStore store = new(fixture.Path, original);
            using (store.OpenOrCreatePayloadProtector())
            {
            }
        }

        using TestPlatformKeyProtector mismatched = new("DifferentProtector@1", wrappingKey);
        PlatformProtectedIdentityKeyStore reopened = new(fixture.Path, mismatched);
        Check.That(Throws<CryptographicException>(() => reopened.OpenOrCreatePayloadProtector()),
            "protector identity mismatch must reject before unwrapping");
        CryptographicOperations.ZeroMemory(wrappingKey);
    }

    private static void OverbroadKeyPermissionsAreRejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using KeyFileFixture fixture = new();
        using TestPlatformKeyProtector platform = new("TestPlatformProtector@1");
        PlatformProtectedIdentityKeyStore store = new(fixture.Path, platform);
        using (store.OpenOrCreatePayloadProtector())
        {
        }

        File.SetUnixFileMode(
            fixture.Path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        Check.That(Throws<UnauthorizedAccessException>(() => store.OpenOrCreatePayloadProtector()),
            "group-readable protected key files must fail closed");
    }

    private static bool Throws<TException>(Func<IDisposable> action)
        where TException : Exception
    {
        try
        {
            using IDisposable result = action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private sealed class KeyFileFixture : IDisposable
    {
        public KeyFileFixture()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"monitor-key-{Guid.NewGuid():N}.bin");
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }

    private sealed class TestPlatformKeyProtector : IPlatformKeyProtector, IDisposable
    {
        private readonly byte[] _wrappingKey;

        public TestPlatformKeyProtector(string protectorId, byte[]? wrappingKey = null)
        {
            ProtectorId = protectorId;
            _wrappingKey = wrappingKey is null ? RandomNumberGenerator.GetBytes(32) : [.. wrappingKey];
        }

        public string ProtectorId { get; }

        public byte[]? LastProtectedPlaintext { get; private set; }

        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            if (LastProtectedPlaintext is not null)
            {
                CryptographicOperations.ZeroMemory(LastProtectedPlaintext);
            }

            LastProtectedPlaintext = plaintext.ToArray();
            byte[] envelope = new byte[12 + 16 + plaintext.Length];
            Span<byte> nonce = envelope.AsSpan(0, 12);
            Span<byte> tag = envelope.AsSpan(12, 16);
            RandomNumberGenerator.Fill(nonce);
            using AesGcm aes = new(_wrappingKey, 16);
            aes.Encrypt(nonce, plaintext, envelope.AsSpan(28), tag);
            return envelope;
        }

        public byte[] Unprotect(ReadOnlySpan<byte> protectedPayload)
        {
            if (protectedPayload.Length < 28)
            {
                throw new CryptographicException("Test envelope is invalid.");
            }

            byte[] plaintext = new byte[protectedPayload.Length - 28];
            using AesGcm aes = new(_wrappingKey, 16);
            aes.Decrypt(
                protectedPayload[..12],
                protectedPayload[28..],
                protectedPayload.Slice(12, 16),
                plaintext);
            return plaintext;
        }

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(_wrappingKey);
            if (LastProtectedPlaintext is not null)
            {
                CryptographicOperations.ZeroMemory(LastProtectedPlaintext);
            }
        }
    }
}
