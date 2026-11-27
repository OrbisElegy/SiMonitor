// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Monitor.Infrastructure.Identity;

namespace Monitor.Network;

// Institution-provisioned ciphertexts. Each host unwraps its copy locally. The stable
// lookup key preserves request id indexes across rotations of payload encryption keys.
internal sealed class ExamDataKeyRing : IIdentityPayloadProtector, IDisposable
{
    private sealed record WrappedKey(Guid Id, byte[] Ciphertext);
    private sealed record KeyFile(string Certificate, Guid Active, byte[] LookupKey, WrappedKey[] Keys);
    private readonly Dictionary<Guid, AesGcmIdentityPayloadProtector> _keys = [];
    private readonly AesGcmIdentityPayloadProtector _lookup;
    private readonly Guid _active;

    private ExamDataKeyRing(Guid active, AesGcmIdentityPayloadProtector lookup)
    {
        _active = active;
        _lookup = lookup;
    }

    internal static ExamDataKeyRing Open(string path)
    {
        var file = ExamClusterConfiguration.ReadFile<KeyFile>(path);
        if (file.Active == Guid.Empty || string.IsNullOrWhiteSpace(file.Certificate) || file.Keys is not { Length: >= 1 and <= 16 })
        { throw new InvalidDataException("Invalid exam data key ring."); }
        string reference = ExamClusterConfiguration.ResolveCertificate(file.Certificate, Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var cert = ClassroomCertificate.Load(reference, null, Environment.GetEnvironmentVariable("SIMONITOR_EXAM_KEY_PASSWORD"));
        using RSA rsa = cert.GetRSAPrivateKey() ?? throw new CryptographicException("The exam data key ring requires its institution RSA decryption key.");
        if (rsa.KeySize < 2048) { throw new CryptographicException("Invalid exam wrapping key size."); }
        AesGcmIdentityPayloadProtector Unwrap(byte[] ciphertext)
        {
            if (ciphertext is null || ciphertext.Length != rsa.KeySize / 8) { throw new InvalidDataException("Invalid wrapped exam key."); }
            byte[] key = rsa.Decrypt(ciphertext, RSAEncryptionPadding.OaepSHA256);
            try { return new AesGcmIdentityPayloadProtector(key); }
            finally { CryptographicOperations.ZeroMemory(key); }
        }

        var ring = new ExamDataKeyRing(file.Active, Unwrap(file.LookupKey));
        try
        {
            foreach (WrappedKey key in file.Keys)
            {
                if (key is null || key.Id == Guid.Empty || ring._keys.ContainsKey(key.Id))
                { throw new InvalidDataException("Invalid exam payload key id."); }
                ring._keys.Add(key.Id, Unwrap(key.Ciphertext));
            }
            if (!ring._keys.ContainsKey(file.Active)) { throw new InvalidDataException("Missing active exam payload key."); }
            return ring;
        }
        catch { ring.Dispose(); throw; }
    }

    public byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose)
    {
        byte[] body = _keys[_active].Protect(plaintext, purpose + "/" + _active.ToString("N"));
        byte[] envelope = new byte[16 + body.Length];
        _active.TryWriteBytes(envelope);
        body.CopyTo(envelope, 16);
        return envelope;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedPayload, string purpose)
    {
        if (protectedPayload.Length < 16) { throw new CryptographicException("Invalid exam key envelope."); }
        var keyId = new Guid(protectedPayload[..16]);
        if (!_keys.TryGetValue(keyId, out var key))
        { throw new CryptographicException("Missing exam payload decryption key."); }
        return key.Unprotect(protectedPayload[16..], purpose + "/" + keyId.ToString("N"));
    }

    public string LookupToken(string value, string purpose) => _lookup.LookupToken(value, purpose);

    public void Dispose()
    {
        _lookup.Dispose();
        foreach (var key in _keys.Values) { key.Dispose(); }
    }
}
