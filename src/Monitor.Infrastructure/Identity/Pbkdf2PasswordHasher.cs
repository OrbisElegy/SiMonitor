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
        if (!StringComparer.Ordinal.Equals(verifier.Algorithm, Algorithm) ||
            verifier.Iterations != IdentityPolicy.PasswordIterations)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(verifier.SaltBase64);
            expected = Convert.FromBase64String(verifier.SubkeyBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length != IdentityPolicy.PasswordSaltBytes ||
            expected.Length != IdentityPolicy.PasswordSubkeyBytes)
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(expected);
            return false;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            verifier.Iterations,
            HashAlgorithmName.SHA256,
            expected.Length);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(salt);
        }
    }
}
