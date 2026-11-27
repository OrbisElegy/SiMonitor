// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Monitor.Network;

internal static class ClassroomCertificate
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";

    internal static X509ChainPolicy ServerTrust(X509Certificate2? trustedRoot)
    {
        var policy = new X509ChainPolicy
        {
            ApplicationPolicy = { new Oid(ServerAuthentication) },
            RevocationMode = X509RevocationMode.Online,
            RevocationFlag = X509RevocationFlag.ExcludeRoot
        };
        if (trustedRoot is not null)
        {
            if (!IsCertificateAuthority(trustedRoot))
            {
                throw new CryptographicException("The classroom trust anchor must be a CA certificate.");
            }
            policy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            policy.CustomTrustStore.Add(trustedRoot);
        }
        return policy;
    }

    private static bool IsCertificateAuthority(X509Certificate2 certificate) =>
        certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(extension => extension.CertificateAuthority);

    internal static bool MatchesServer(X509Certificate2 certificate, string hostName) =>
        !IsCertificateAuthority(certificate) && certificate.MatchesHostname(hostName, allowCommonName: false) &&
        certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(extension =>
            extension.EnhancedKeyUsages.Cast<Oid>().Any(usage => usage.Value == ServerAuthentication));

    internal static X509Certificate2 Load(string reference, string? keyPath, string? password)
    {
        if (reference.StartsWith("store:", StringComparison.Ordinal))
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            return store.Certificates.Find(X509FindType.FindByThumbprint, reference[6..], validOnly: false)
                .SingleOrDefault() ?? throw new CryptographicException("Host certificate not found in CurrentUser/My.");
        }
        if (!string.IsNullOrEmpty(keyPath)) { return LoadPem(reference, keyPath, password); }
        return X509CertificateLoader.LoadPkcs12FromFile(reference, password, OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
    }

    internal static void ValidateForHost(X509Certificate2 certificate, string hostName, X509Certificate2? trustedRoot)
    {
        if (!certificate.HasPrivateKey || !MatchesServer(certificate, hostName))
        {
            throw new CryptographicException("The teacher certificate needs a private key, a matching SAN and server authentication usage.");
        }
        using var chain = new X509Chain { ChainPolicy = ServerTrust(trustedRoot) };
        if (!chain.Build(certificate))
        {
            throw new CryptographicException("Teacher certificate chain validation failed: " +
                string.Join(", ", chain.ChainStatus.Select(status => status.Status)));
        }
    }

    private static X509Certificate2 LoadPem(string path, string keyPath, string? password)
    {
        X509Certificate2 pem = password is null
            ? X509Certificate2.CreateFromPemFile(path, keyPath)
            : X509Certificate2.CreateFromEncryptedPemFile(path, password, keyPath);
        if (!OperatingSystem.IsWindows()) { return pem; }

        // Schannel needs to open the private key through its provider during the handshake.
        using (pem)
        {
            byte[] pfx = pem.Export(X509ContentType.Pfx);
            try { return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.DefaultKeySet); }
            finally { CryptographicOperations.ZeroMemory(pfx); }
        }
    }
}
