// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Monitor.Network;

namespace Monitor.Specs;

internal sealed class ExamClusterProcessFixture(ClassroomCertificateFixture certificates) : IAsyncDisposable
{
    internal static readonly string[] Names = ["primary", "standby", "witness"];
    internal readonly string DirectoryPath = Path.GetDirectoryName(certificates.LeafPfxPath)!;
    private readonly byte[] _identityKey = RandomNumberGenerator.GetBytes(32);
    private readonly string _password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private readonly List<Child> _children = [];
    private string IdentityDatabase => Path.Combine(DirectoryPath, "operator.db");

    internal static void IssueTestCertificates(string directory, X509Certificate2 issuer, Uri crl)
    {
        foreach (string name in Names.Append("primary-next"))
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
                { new("1.3.6.1.5.5.7.3.1"), new("1.3.6.1.5.5.7.3.2") }, false));
            request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, true, false));
            request.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([crl.AbsoluteUri]));
            using var leaf = request.Create(issuer, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1), RandomNumberGenerator.GetBytes(16));
            using var pfx = leaf.CopyWithPrivateKey(key);
            File.WriteAllBytes(Path.Combine(directory, name + ".pfx"), pfx.Export(X509ContentType.Pkcs12));
        }
    }

    internal async Task PrepareAsync(CancellationToken ct)
    {
        File.WriteAllBytes(Path.Combine(DirectoryPath, "root.cer"), certificates.Root.RawData);
        string blocked = Path.Combine(DirectoryPath, "blocked.txt");
        File.WriteAllText(blocked, Convert.ToHexString(SHA256.HashData("blocked test password"u8)));
        ClassroomBootstrap.Initialize(IdentityDatabase, _identityKey, "exam-admin", "test initial passphrase", _password, blocked);
        var cluster = Guid.NewGuid();
        var keyId = Guid.NewGuid();
        byte[] lookup = RandomNumberGenerator.GetBytes(32);
        byte[] encryption = RandomNumberGenerator.GetBytes(32);
        List<TcpListener> reservations = [];
        List<ExamClusterPeer> members = [];
        try
        {
            for (int i = 0; i < Names.Length; i++)
            {
                string name = Names[i];
                var port = new TcpListener(IPAddress.Loopback, 0);
                port.Start();
                reservations.Add(port);
                using var cert = ClassroomCertificate.Load(Path.Combine(DirectoryPath, name + ".pfx"), null, null);
                string[] pins = [cert.GetCertHashString(HashAlgorithmName.SHA256)];
                if (i == 0)
                {
                    using var replacement = ClassroomCertificate.Load(Path.Combine(DirectoryPath, "primary-next.pfx"), null, null);
                    pins = [pins[0], replacement.GetCertHashString(HashAlgorithmName.SHA256)];
                }
                members.Add(new(Guid.NewGuid(), new Uri($"https://localhost:{((IPEndPoint)port.LocalEndpoint).Port}/"), pins));
                if (i < 2)
                {
                    using RSA rsa = cert.GetRSAPublicKey()!;
                    WriteJson(name + "-keys.json", new
                    {
                        certificate = name + ".pfx",
                        active = keyId,
                        lookupKey = rsa.Encrypt(lookup, RSAEncryptionPadding.OaepSHA256),
                        keys = new[] { new { id = keyId, ciphertext = rsa.Encrypt(encryption, RSAEncryptionPadding.OaepSHA256) } }
                    });
                }
            }
            for (int i = 0; i < Names.Length; i++)
            {
                string name = Names[i];
                WriteJson(name + ".json", new ExamClusterConfiguration(cluster, members[i].Id, "127.0.0.1", name + ".db",
                    name + ".pfx", "root.cer", members[0], members[1], members[2], i < 2 ? name + "-keys.json" : null));
                using var provision = new Child(Start(name, "provision"), name);
                await provision.WaitForSuccessAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var listener in reservations) { listener.Stop(); }
            CryptographicOperations.ZeroMemory(lookup);
            CryptographicOperations.ZeroMemory(encryption);
        }
    }

    private void WriteJson(string file, object value) => File.WriteAllText(Path.Combine(DirectoryPath, file),
        JsonSerializer.Serialize(value, ExamClusterConfiguration.Json));

    internal void RotatePrimaryKeys()
    {
        string configFile = Path.Combine(DirectoryPath, "primary.json");
        var config = ExamClusterConfiguration.ReadFile<ExamClusterConfiguration>(configFile);
        WriteJson("primary.json", config with { Certificate = "primary-next.pfx" });
        using var oldCert = ClassroomCertificate.Load(Path.Combine(DirectoryPath, "primary.pfx"), null, null);
        using var newCert = ClassroomCertificate.Load(Path.Combine(DirectoryPath, "primary-next.pfx"), null, null);
        using RSA oldKey = oldCert.GetRSAPrivateKey()!;
        using RSA newKey = newCert.GetRSAPublicKey()!;
        using var ring = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DirectoryPath, "primary-keys.json")));
        byte[] Rewrap(JsonElement wrapped)
        {
            byte[] plain = oldKey.Decrypt(wrapped.GetBytesFromBase64(), RSAEncryptionPadding.OaepSHA256);
            try { return newKey.Encrypt(plain, RSAEncryptionPadding.OaepSHA256); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        var prior = ring.RootElement.GetProperty("keys").EnumerateArray().Single();
        var active = Guid.NewGuid();
        byte[] rotated = RandomNumberGenerator.GetBytes(32);
        try
        {
            WriteJson("primary-keys.json", new
            {
                certificate = "primary-next.pfx",
                active,
                lookupKey = Rewrap(ring.RootElement.GetProperty("lookupKey")),
                keys = new[] { new { id = prior.GetProperty("id").GetGuid(), ciphertext = Rewrap(prior.GetProperty("ciphertext")) },
                    new { id = active, ciphertext = newKey.Encrypt(rotated, RSAEncryptionPadding.OaepSHA256) } }
            });
        }
        finally { CryptographicOperations.ZeroMemory(rotated); }
    }

    private Process Start(string name, string verb)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            WorkingDirectory = DirectoryPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(typeof(ClassroomHost).Assembly.Location);
        start.ArgumentList.Add("exam");
        start.ArgumentList.Add(verb);
        start.ArgumentList.Add(name + ".json");
        foreach (string variable in start.Environment.Keys.Where(k => k.StartsWith("SIMONITOR_", StringComparison.Ordinal)).ToArray())
        { start.Environment.Remove(variable); }
        if (verb == "serve" && name != "witness")
        {
            start.ArgumentList.Add(IdentityDatabase);
            start.ArgumentList.Add("exam-admin");
            start.Environment["SIMONITOR_IDENTITY_KEY"] = Convert.ToBase64String(_identityKey);
            start.Environment["SIMONITOR_HOST_PASSWORD"] = _password;
        }
        return Process.Start(start) ?? throw new IOException("Cannot start an exam node.");
    }

    internal async Task<Child> ServeAsync(string name, CancellationToken ct)
    {
        var child = new Child(Start(name, "serve"), name);
        _children.Add(child);
        await child.WaitForListenerAsync(ct).ConfigureAwait(false);
        return child;
    }

    internal sealed class Child(Process process, string name) : IDisposable
    {
        private readonly Task<string> _errors = process.StandardError.ReadToEndAsync();

        internal async Task WaitForSuccessAsync(CancellationToken ct)
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            Check.That(process.ExitCode == 0, name + " provisioning: " + await _errors.ConfigureAwait(false));
        }

        internal async Task WaitForListenerAsync(CancellationToken ct)
        {
            while (await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            { if (line.Contains(" listening at ", StringComparison.Ordinal)) { return; } }
            throw new InvalidOperationException(name + " listener exited: " + await _errors.ConfigureAwait(false));
        }

        internal async Task<JsonElement> CommandAsync(string command, CancellationToken ct)
        {
            await process.StandardInput.WriteLineAsync(command.AsMemory(), ct).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
            while (await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (line.StartsWith('{'))
                {
                    using var json = JsonDocument.Parse(line);
                    return json.RootElement.Clone();
                }
            }
            throw new InvalidOperationException(name + " exited: " + await _errors.ConfigureAwait(false));
        }

        internal async Task<JsonElement> RoleAsync(string role, CancellationToken ct)
        {
            JsonElement status;
            var timer = Stopwatch.StartNew();
            do
            {
                status = (await CommandAsync("status", ct).ConfigureAwait(false)).GetProperty("result");
                if (status.GetProperty("role").GetString() == role &&
                    (role != "Primary" || status.GetProperty("leaseValid").GetBoolean())) { return status; }
                await Task.Delay(40, ct).ConfigureAwait(false);
            } while (timer.Elapsed < TimeSpan.FromSeconds(14));
            throw new InvalidOperationException($"{name} waiting for {role}: {status}");
        }

        internal void Kill()
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }
            process.WaitForExit();
        }

        public void Dispose() { Kill(); process.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var child in _children) { child.Dispose(); }
        CryptographicOperations.ZeroMemory(_identityKey);
        await certificates.DisposeAsync().ConfigureAwait(false);
    }
}
