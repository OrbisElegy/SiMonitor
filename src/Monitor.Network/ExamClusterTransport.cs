// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;

namespace Monitor.Network;

internal sealed class ExamClusterTransport : IDisposable
{
    private const string ClientAuthentication = "1.3.6.1.5.5.7.3.2";
    private readonly ExamClusterConfiguration _config;
    private readonly X509Certificate2 _certificate;
    private readonly X509Certificate2 _root;

    internal ExamClusterTransport(ExamClusterConfiguration config)
    {
        _config = config;
        _root = X509CertificateLoader.LoadCertificateFromFile(config.TrustAnchor);
        try
        {
            _certificate = ClassroomCertificate.Load(config.Certificate, Environment.GetEnvironmentVariable("SIMONITOR_TLS_PEM_KEY"),
                Environment.GetEnvironmentVariable("SIMONITOR_TLS_PASSWORD"));
            try
            {
                if (!_certificate.HasPrivateKey || !Matches(_certificate, config.Self))
                { throw new CryptographicException("The local exam certificate needs its private key, configured pin, DNS SAN and both authentication EKUs."); }
                if (!Trusted(_certificate, null, out string? errors))
                { throw new CryptographicException("Exam certificate chain or revocation validation failed: " + errors); }
            }
            catch { _certificate.Dispose(); throw; }
        }
        catch { _root.Dispose(); throw; }
    }

    internal DateTime CertificateExpiresUtc => _certificate.NotAfter.ToUniversalTime();

    private X509ChainPolicy TrustPolicy()
    {
        X509ChainPolicy policy = ClassroomCertificate.ServerTrust(_root);
        policy.ApplicationPolicy.Add(new Oid(ClientAuthentication));
        policy.RevocationMode = _config.Revocation == "Offline" ? X509RevocationMode.Offline : X509RevocationMode.Online;
        policy.UrlRetrievalTimeout = TimeSpan.FromSeconds(2);
        return policy;
    }

    private bool Trusted(X509Certificate2 certificate, X509Chain? presented, out string errors)
    {
        using var chain = new X509Chain { ChainPolicy = TrustPolicy() };
        if (presented is not null)
        {
            chain.ChainPolicy.ExtraStore.AddRange(presented.ChainPolicy.ExtraStore);
            foreach (var element in presented.ChainElements) { chain.ChainPolicy.ExtraStore.Add(element.Certificate); }
        }
        bool valid = chain.Build(certificate);
        errors = valid ? string.Empty : string.Join(", ", chain.ChainStatus.Select(status => status.Status));
        return valid;
    }

    private static bool Matches(X509Certificate2 certificate, ExamClusterPeer peer) =>
        peer.CertificateSha256.Contains(certificate.GetCertHashString(HashAlgorithmName.SHA256), StringComparer.OrdinalIgnoreCase) &&
        ClassroomCertificate.MatchesServer(certificate, peer.Origin.IdnHost) &&
        certificate.MatchesHostname(peer.Origin.IdnHost, allowWildcards: false, allowCommonName: false) &&
        certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(eku =>
            eku.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == ClientAuthentication));

    internal Guid? Identify(HttpContext context) => context.Connection.ClientCertificate is { } cert ? Identify(cert) : null;

    private Guid? Identify(X509Certificate2 certificate)
    {
        foreach (ExamClusterPeer peer in _config.Members)
        {
            if (peer.Id != _config.NodeId && peer.Id != _config.Witness.Id && Matches(certificate, peer)) { return peer.Id; }
        }
        return null;
    }

    internal HttpClient Connect(ExamClusterPeer remote)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            ConnectCallback = ConnectDnsAsync,
            PooledConnectionLifetime = TimeSpan.FromSeconds(20),
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls13,
                AllowTlsResume = false,
                CertificateChainPolicy = TrustPolicy(),
                ClientCertificates = new X509CertificateCollection { _certificate },
                LocalCertificateSelectionCallback = (_, _, _, _, _) => _certificate,
                RemoteCertificateValidationCallback = (sender, cert, chain, errors) => cert is X509Certificate2 leaf &&
                    (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) == 0 &&
                    Matches(leaf, remote) && Trusted(leaf, chain, out _)
            }
        };
        return new HttpClient(handler)
        {
            BaseAddress = remote.Origin,
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = remote.Id == _config.Witness.Id ? 16384 : 2 * ExamReplicaEntry.MaxPayloadBytes
        };
    }

    // A non-listening AAAA address can consume a whole lease before the OS tries an A record.
    // Race the two address families for TCP only. TLS still authenticates the configured DNS origin.
    private static async ValueTask<Stream> ConnectDnsAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        DnsEndPoint endpoint = context.DnsEndPoint;
        IPAddress[] resolved = await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);
        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var attempts = resolved.Where(address => address.AddressFamily == AddressFamily.InterNetwork && Socket.OSSupportsIPv4 ||
            address.AddressFamily == AddressFamily.InterNetworkV6 && Socket.OSSupportsIPv6).GroupBy(address => address.AddressFamily)
            .Select(family => ConnectAsync(family.Key, family.ToArray())).ToList();
        try
        {
            while (attempts.Count > 0)
            {
                Task<Socket> next = await Task.WhenAny(attempts).ConfigureAwait(false);
                attempts.Remove(next);
                try { return new NetworkStream(await next.ConfigureAwait(false), ownsSocket: true); }
                catch (SocketException) when (attempts.Count > 0) { }
            }
            throw new SocketException((int)SocketError.HostNotFound);
        }
        finally
        {
            await race.CancelAsync().ConfigureAwait(false);
            foreach (Task<Socket> attempt in attempts)
            {
                try { (await attempt.ConfigureAwait(false)).Dispose(); }
                catch (Exception error) when (error is SocketException or OperationCanceledException) { }
            }
        }

        async Task<Socket> ConnectAsync(AddressFamily family, IPAddress[] addresses)
        {
            var socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addresses, endpoint.Port, race.Token).ConfigureAwait(false);
                return socket;
            }
            catch { socket.Dispose(); throw; }
        }
    }

    internal WebApplication BuildListener()
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxConcurrentConnections = 64;
            options.Limits.MaxRequestBodySize = 4 * 1024 * 1024;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(4);
            options.Listen(IPAddress.Parse(_config.ListenAddress), _config.Self.Origin.Port, endpoint =>
            {
                endpoint.Protocols = HttpProtocols.Http1;
                endpoint.UseHttps(https =>
                {
                    https.ServerCertificate = _certificate;
                    https.SslProtocols = SslProtocols.Tls13;
                    https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    https.ClientCertificateValidation = (leaf, chain, errors) => Identify(leaf).HasValue && Trusted(leaf, chain, out _);
                    https.OnAuthenticate = (_, ssl) =>
                    {
                        ssl.AllowTlsResume = false;
                        ssl.CertificateChainPolicy = TrustPolicy();
                    };
                });
            });
        });
        builder.Services.AddRouting();
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Headers.ContainsKey("Origin") || Identify(context) is null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next(context).ConfigureAwait(false);
        });
        return app;
    }

    public void Dispose()
    {
        _certificate.Dispose();
        _root.Dispose();
    }
}
