// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Monitor.Specs;

internal sealed class ClassroomCertificateFixture : IAsyncDisposable
{
    private readonly WebApplication _crlServer;
    private readonly DirectoryInfo _directory;

    private ClassroomCertificateFixture(WebApplication crlServer, DirectoryInfo directory, X509Certificate2 root)
    {
        _crlServer = crlServer;
        _directory = directory;
        Root = root;
    }

    internal X509Certificate2 Root { get; }
    internal string LeafPfxPath => Path.Combine(_directory.FullName, "loopback.pfx");

    internal static async Task<ClassroomCertificateFixture> CreateAsync(CancellationToken cancellationToken,
        Action<string, X509Certificate2, Uri>? issueAdditionalLeaves = null)
    {
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest($"CN=Classroom test CA {Guid.NewGuid():N}",
            caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        caRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caRequest.PublicKey, false));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using var issuer = caRequest.CreateSelfSigned(now.AddMinutes(-5), now.AddHours(3));
        byte[] crl = new CertificateRevocationListBuilder().Build(issuer, 1, now.AddHours(2),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, now.AddMinutes(-2));
        // A unique distribution point also isolates OS revocation caches between runs.
        string crlPath = $"/{Guid.NewGuid():N}.crl";
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddRouting();
        var app = builder.Build();
        var directory = Directory.CreateTempSubdirectory("monitor-classroom-cert-");
        var fixture = new ClassroomCertificateFixture(app, directory, X509CertificateLoader.LoadCertificate(issuer.RawData));
        try
        {
            app.MapMethods(crlPath, ["GET", "HEAD"], () => Results.Bytes(crl, "application/pkix-crl"));
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            var crlUri = new Uri(new Uri(app.Urls.Single()), crlPath);
            fixture.WriteLeaf(issuer, now, crlUri);
            issueAdditionalLeaves?.Invoke(directory.FullName, issuer, crlUri);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _crlServer.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            Root.Dispose();
            _directory.Delete(recursive: true);
        }
    }

    private void WriteLeaf(X509Certificate2 issuer, DateTimeOffset now, Uri crlUri)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, true, false));
        request.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([crlUri.AbsoluteUri]));
        using var leaf = request.Create(issuer, now.AddMinutes(-1), now.AddHours(1), RandomNumberGenerator.GetBytes(16));
        using var withKey = leaf.CopyWithPrivateKey(key);
        byte[] pfx = withKey.Export(X509ContentType.Pfx);
        try { File.WriteAllBytes(LeafPfxPath, pfx); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }
}
