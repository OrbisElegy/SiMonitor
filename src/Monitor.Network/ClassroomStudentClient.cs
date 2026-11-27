// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

public sealed class ClassroomStudentClient : IDisposable
{
    private static readonly JsonSerializerOptions ControlJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 4
    };

    private readonly Uri _endpoint;
    private readonly HttpClient _http;
    private readonly HttpMessageInvoker _webSocketTransport;
    private string? _token;

    private ClassroomStudentClient(Uri endpoint, X509Certificate2? trustedRoot)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            endpoint.AbsolutePath != "/")
        {
            throw new ArgumentException("A classroom endpoint must be an HTTPS origin.", nameof(endpoint));
        }

        _endpoint = endpoint;
        var tls = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls13,
            CertificateChainPolicy = ClassroomCertificate.ServerTrust(trustedRoot),
            RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                errors == SslPolicyErrors.None && certificate is X509Certificate2 leaf &&
                ClassroomCertificate.MatchesServer(leaf, endpoint.IdnHost)
        };

        var handler = new SocketsHttpHandler
        {
            SslOptions = tls,
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            MaxResponseHeadersLength = 16
        };
        _webSocketTransport = new HttpMessageInvoker(handler);
        _http = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = endpoint,
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 4096
        };
    }

    public static async Task<ClassroomStudentClient> JoinAsync(Uri endpoint, string joinCode,
        X509Certificate2? trustedRoot = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(joinCode);
        if (joinCode.Length > 128 || !joinCode.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("Invalid join code.", nameof(joinCode));
        }

        var client = new ClassroomStudentClient(endpoint, trustedRoot);
        try
        {
            using HttpResponseMessage response = await client._http.PostAsJsonAsync("v1/join",
                new { protocolVersion = 1, joinCode }, cancellationToken).ConfigureAwait(false);
            JoinResponse joined = await ReadControlAsync<JoinResponse>(response, cancellationToken).ConfigureAwait(false);
            if (joined.Token is not { Length: 64 } || !joined.Token.All(char.IsAsciiHexDigit))
            {
                throw new InvalidDataException("The host returned an invalid access token.");
            }
            client._token = joined.Token;
            client._http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", client._token);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<ClassroomManifest> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.GetAsync("v1/session", cancellationToken).ConfigureAwait(false);
        ClassroomManifest manifest = await ReadControlAsync<ClassroomManifest>(response, cancellationToken).ConfigureAwait(false);
        ValidateManifest(manifest);
        return manifest;
    }

    public async Task<ClassroomClockSample> SynchronizeClockAsync(CancellationToken cancellationToken = default)
    {
        long sentNs = MonotonicNs();
        using HttpResponseMessage response = await _http.GetAsync("v1/clock?t1=" + sentNs.ToString(CultureInfo.InvariantCulture),
            cancellationToken).ConfigureAwait(false);
        long receivedNs = MonotonicNs();
        ClassroomClockReply reply = await ReadControlAsync<ClassroomClockReply>(response, cancellationToken).ConfigureAwait(false);
        if (reply.T1 != sentNs || reply.T2 < 0 || reply.T3 < reply.T2 || reply.T3 - reply.T2 > receivedNs - sentNs)
        {
            throw new InvalidDataException("The host returned an invalid clock sample.");
        }
        return new ClassroomClockSample(sentNs, reply.T2, reply.T3, receivedNs);
    }

    public async Task<ClassroomWaveformSubscription> ResumeAsync(ClassroomManifest previousManifest, ulong nextBlockSequence,
        CancellationToken cancellationToken = default)
    {
        ValidateManifest(previousManifest);
        ClassroomManifest current = await GetSessionAsync(cancellationToken).ConfigureAwait(false);
        if (previousManifest.StudentId != current.StudentId || previousManifest.SessionId != current.SessionId ||
            previousManifest.InstanceId != current.InstanceId || previousManifest.TimebaseEpoch != current.TimebaseEpoch ||
            previousManifest.StreamEpoch != current.StreamEpoch || current.NextBlockSequence < previousManifest.NextBlockSequence ||
            (Int128)current.NextBlockStartSimTimeNs - previousManifest.NextBlockStartSimTimeNs !=
                (Int128)(current.NextBlockSequence - previousManifest.NextBlockSequence) * WaveformBlockAssembler.BlockDurationNs)
        {
            throw new InvalidDataException("The original classroom stream is no longer available for resume.");
        }
        return await SubscribeAsync(current, nextBlockSequence, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClassroomWaveformSubscription> SubscribeAsync(ClassroomManifest manifest, ulong? fromBlockSequence = null,
        CancellationToken cancellationToken = default)
    {
        ValidateManifest(manifest);
        ulong from = fromBlockSequence ?? manifest.NextBlockSequence;
        if (from < manifest.OldestBlockSequence || from > manifest.NextBlockSequence)
        {
            throw new HttpRequestException("The requested waveform cursor is outside the host retention window.", null, HttpStatusCode.Conflict);
        }
        long startSimTimeNs = manifest.NextBlockStartSimTimeNs - (long)(manifest.NextBlockSequence - from) * WaveformBlockAssembler.BlockDurationNs;
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.SetRequestHeader("Authorization", "Bearer " + _token);
            socket.Options.AddSubProtocol(ClassroomWire.WaveformSubprotocol);
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(1);
            socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(4);
            socket.Options.CollectHttpResponseDetails = true;
            var destination = new UriBuilder(_endpoint)
            {
                Scheme = "wss",
                Port = _endpoint.Port,
                Path = "/v1/waveforms",
                Query = "from=" + from.ToString(CultureInfo.InvariantCulture)
            };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            await socket.ConnectAsync(destination.Uri, _webSocketTransport, deadline.Token).ConfigureAwait(false);
            if (socket.SubProtocol != ClassroomWire.WaveformSubprotocol)
            {
                throw new InvalidDataException("The host did not negotiate the classroom waveform protocol.");
            }
            var history = WaveformBlockRing.Start(manifest.SessionId, manifest.InstanceId, manifest.TimebaseEpoch,
                manifest.StreamEpoch, from, startSimTimeNs, WaveformBlockRing.ClientHistoryBlockCount);
            return new ClassroomWaveformSubscription(socket, history);
        }
        catch (WebSocketException error) when ((int)socket.HttpStatusCode is > 0 and not 101)
        {
            HttpStatusCode status = socket.HttpStatusCode;
            socket.Dispose();
            throw new HttpRequestException($"The host rejected the waveform subscription (HTTP {(int)status}).", error, status);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _webSocketTransport.Dispose();
    }

    private static long MonotonicNs() => (long)((Int128)Stopwatch.GetTimestamp() * 1_000_000_000 / Stopwatch.Frequency);

    private static void ValidateManifest(ClassroomManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.ProtocolVersion != 1 || manifest.StudentId == Guid.Empty || manifest.SessionId == Guid.Empty ||
            manifest.InstanceId == Guid.Empty || manifest.OldestBlockSequence > manifest.NextBlockSequence ||
            manifest.NextBlockSequence - manifest.OldestBlockSequence > WaveformBlockRing.HostRetentionBlockCount ||
            manifest.NextBlockStartSimTimeNs < (long)(manifest.NextBlockSequence - manifest.OldestBlockSequence) * WaveformBlockAssembler.BlockDurationNs)
        {
            throw new InvalidDataException("The host returned an invalid session manifest.");
        }
    }

    private static async Task<T> ReadControlAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        response.EnsureSuccessStatusCode();
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Expected a classroom JSON response.");
        }
        return await response.Content.ReadFromJsonAsync<T>(ControlJson, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The host returned an empty response.");
    }

    private sealed record JoinResponse(string Token);
}
