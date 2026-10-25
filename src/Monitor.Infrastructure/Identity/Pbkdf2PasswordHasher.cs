// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Monitor.Application.Identity;
using Monitor.Domain.Identity;

namespace Monitor.Infrastructure.Identity;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Algorithm = "PBKDF2-HMAC-SHA-256";

    public PasswordVerifier Hash(ReadOnlySpan<char> password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(IdentityPolicy.PasswordSaltBytes);
        byte[] subkey = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            IdentityPolicy.PasswordIterations,
            HashAlgorithmName.SHA256,
            IdentityPolicy.PasswordSubkeyBytes);
        PasswordVerifier verifier = new(
            Algorithm,
            IdentityPolicy.PasswordIterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(subkey));
        CryptographicOperations.ZeroMemory(subkey);
        CryptographicOperations.ZeroMemory(salt);
        return verifier;
    }

    public bool Verify(ReadOnlySpan<char> password, PasswordVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        Span<byte> salt = stackalloc byte[IdentityPolicy.PasswordSaltBytes];
        Span<byte> expected = stackalloc byte[IdentityPolicy.PasswordSubkeyBytes];
        Span<byte> actual = stackalloc byte[IdentityPolicy.PasswordSubkeyBytes];
        try
        {
            if (!TryReadVerifier(verifier, salt, expected))
            {
                return false;
            }

            Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                actual,
                verifier.Iterations,
                HashAlgorithmName.SHA256);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    public bool IsSupported(PasswordVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        Span<byte> salt = stackalloc byte[IdentityPolicy.PasswordSaltBytes];
        Span<byte> subkey = stackalloc byte[IdentityPolicy.PasswordSubkeyBytes];
        try
        {
            return TryReadVerifier(verifier, salt, subkey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(subkey);
        }
    }

    private static bool TryReadVerifier(PasswordVerifier verifier, Span<byte> salt, Span<byte> subkey) =>
        StringComparer.Ordinal.Equals(verifier.Algorithm, Algorithm) &&
        verifier.Iterations == IdentityPolicy.PasswordIterations &&
        verifier.SaltBase64 is not null && verifier.SubkeyBase64 is not null &&
        Convert.TryFromBase64String(verifier.SaltBase64, salt, out int saltBytes) &&
        saltBytes == IdentityPolicy.PasswordSaltBytes &&
        Convert.TryFromBase64String(verifier.SubkeyBase64, subkey, out int subkeyBytes) &&
        subkeyBytes == IdentityPolicy.PasswordSubkeyBytes;
}
