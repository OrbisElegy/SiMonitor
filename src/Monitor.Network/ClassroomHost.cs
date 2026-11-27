// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Monitor.Application.Identity;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

public sealed class ClassroomHost : IAsyncDisposable
{
    private const int MaximumStudents = 36;
    private readonly WebApplication _app;
    private readonly ClassroomAdmission _admission;
    private readonly WaveformBlockRing _ring;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ClassroomSubscription> _students = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TaskCompletionSource _published = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _disposal;

    private ClassroomHost(WebApplication app, ClassroomAdmission admission, WaveformBlockRing ring)
    {
        _app = app;
        _admission = admission;
        _ring = ring;
    }

    public Uri BaseAddress => new(_app.Urls.Single());

    // On successful start, the host owns admission and exclusive access to the host ring.
    // The caller owns the certificate and keeps it available until the host stops.
    public static async Task<ClassroomHost> StartAsync(IPAddress listenAddress, int port, string hostName,
        X509Certificate2 certificate, WaveformBlockRing ring, ClassroomAdmission admission,
        ClassroomBootstrap identity, X509Certificate2? trustedRoot = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(listenAddress);
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(ring);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(identity);
        if (!BootstrapIdentityService.MayStartNetworkListener(identity.Repository))
        {
            throw new InvalidOperationException("Complete local identity bootstrap before starting the classroom host.");
        }

        if (admission.SessionId != ring.SessionId || admission.InstanceId != ring.InstanceId ||
            ring.Capacity != WaveformBlockRing.HostRetentionBlockCount)
        {
            throw new ArgumentException("The classroom needs its own matching host retention ring.", nameof(ring));
        }

        ClassroomCertificate.ValidateForHost(certificate, hostName, trustedRoot);

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxConcurrentConnections = 128;
            options.Limits.MaxConcurrentUpgradedConnections = MaximumStudents;
            options.Limits.MaxRequestBodySize = 4096;
            options.Limits.MaxRequestHeadersTotalSize = 16_384;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            options.Listen(listenAddress, port, endpoint =>
            {
                endpoint.Protocols = HttpProtocols.Http1;
                endpoint.UseHttps(https =>
                {
                    https.ServerCertificate = certificate;
                    https.SslProtocols = SslProtocols.Tls13;
                });
            });
        });
        builder.Services.AddRouting();
        var app = builder.Build();
        var host = new ClassroomHost(app, admission, ring);
        try
        {
            app.UseWebSockets(new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(1),
                KeepAliveTimeout = TimeSpan.FromSeconds(5),
            });
            app.Use(async (context, next) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                // Native clients don't send Origin. A web page is never a classroom principal.
                if (context.Request.Headers.ContainsKey("Origin"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                await next(context).ConfigureAwait(false);
            });
            app.MapPost("/v1/join", host.JoinAsync);
            app.MapGet("/v1/session", host.ManifestAsync);
            app.MapGet("/v1/clock", host.ClockAsync);
            app.MapGet("/v1/waveforms", host.StreamAsync);
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            return host;
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            host._stopping.Dispose();
            throw;
        }
    }

    public WaveformBlockAppendResult Publish(ReadOnlySpan<byte> rawEnvelope)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            WaveformBlockAppendResult result = _ring.Append(rawEnvelope);
            if (result.Status == WaveformBlockAppendStatus.Appended)
            {
                foreach (ClassroomSubscription subscriber in _students.Values)
                {
                    subscriber.Outbox.Enqueue(result.BlockSequence);
                }
                _published.TrySetResult();
                _published = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            return result;
        }
    }

    private async Task JoinAsync(HttpContext context)
    {
        if (!context.Request.HasJsonContentType())
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        string? code = null;
        int? version = null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            using var json = await JsonDocument.ParseAsync(context.Request.Body,
                new JsonDocumentOptions { MaxDepth = 2 }, deadline.Token).ConfigureAwait(false);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            foreach (JsonProperty field in json.RootElement.EnumerateObject())
            {
                if (field.NameEquals("protocolVersion") && version is null &&
                    field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt32(out int number))
                {
                    version = number;
                }
                else if (field.NameEquals("joinCode") && code is null && field.Value.ValueKind == JsonValueKind.String)
                {
                    code = field.Value.GetString();
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
            }
        }
        catch (JsonException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        catch (OperationCanceledException)
        {
            context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
            return;
        }

        if (version != 1 || code is not { Length: > 0 and <= 128 })
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (context.Connection.RemoteIpAddress is not { } source ||
            !_admission.TryRedeem(code, source.ToString(), out string? token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        await context.Response.WriteAsJsonAsync(new { token }, context.RequestAborted).ConfigureAwait(false);
    }

    private bool Authorize(HttpContext context, out Guid studentId, out DateTimeOffset expiresAt)
    {
        studentId = default;
        expiresAt = default;
        var values = context.Request.Headers.Authorization;
        if (values.Count == 1 && AuthenticationHeaderValue.TryParse(values[0], out var header) &&
            header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) && header.Parameter is { } token &&
            _admission.TryAuthorize(token, out studentId, out expiresAt))
        {
            return true;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return false;
    }

    private async Task ManifestAsync(HttpContext context)
    {
        if (!Authorize(context, out Guid studentId, out _)) { return; }
        ClassroomManifest manifest;
        lock (_gate)
        {
            manifest = new ClassroomManifest(1, studentId, _ring.SessionId, _ring.InstanceId, _ring.TimebaseEpoch,
                _ring.StreamEpoch, _ring.OldestBlockSequence, _ring.NextBlockSequence, _ring.NextBlockStartSimTimeNs);
        }
        await context.Response.WriteAsJsonAsync(manifest, context.RequestAborted).ConfigureAwait(false);
    }

    private async Task ClockAsync(HttpContext context)
    {
        long t2 = _clock.Elapsed.Ticks * 100;
        if (!Authorize(context, out _, out _)) { return; }
        var values = context.Request.Query["t1"];
        if (values.Count != 1 || !long.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out long t1))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        await context.Response.WriteAsJsonAsync(new ClassroomClockReply(t1, t2, _clock.Elapsed.Ticks * 100),
            context.RequestAborted).ConfigureAwait(false);
    }

    private async Task StreamAsync(HttpContext context)
    {
        if (!Authorize(context, out Guid studentId, out DateTimeOffset expiresAt)) { return; }
        if (!context.WebSockets.IsWebSocketRequest ||
            !context.WebSockets.WebSocketRequestedProtocols.Contains(ClassroomWire.WaveformSubprotocol, StringComparer.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        ulong? from = null;
        if (context.Request.Query.TryGetValue("from", out var values))
        {
            if (values.Count != 1 || !ulong.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong value))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            from = value;
        }

        ClassroomSubscription subscriber;
        lock (_gate)
        {
            if (_students.Count >= MaximumStudents || _students.ContainsKey(studentId))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return;
            }
            ulong cursor = from ?? _ring.NextBlockSequence;
            if (cursor < _ring.OldestBlockSequence || cursor > _ring.NextBlockSequence)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                return;
            }
            subscriber = new ClassroomSubscription(studentId, _ring, cursor);
            _students.Add(studentId, subscriber);
        }

        WebSocket? socket = null;
        try
        {
            try
            {
                TimeSpan remaining = expiresAt - _admission.Clock.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                using var expiry = new CancellationTokenSource(remaining, _admission.Clock);
                using var connection = CancellationTokenSource.CreateLinkedTokenSource(
                    context.RequestAborted, _stopping.Token, expiry.Token);
                socket = await context.WebSockets.AcceptWebSocketAsync(ClassroomWire.WaveformSubprotocol).ConfigureAwait(false);
                await subscriber.SendAsync(socket, _gate, () => _published.Task, connection.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate) { _students.Remove(studentId); }
            }
            // A client may reconnect as soon as its clean closing handshake completes.
            if (socket.State == WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (IOException) { }
        finally { socket?.Dispose(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) { return new ValueTask(_disposal ??= StopAsync()); }
    }

    private async Task StopAsync()
    {
        // DisposeAsync records the shared stop task under _gate before shutdown begins.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        try
        {
            await _stopping.CancelAsync().ConfigureAwait(false);
            await _app.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            try { await _app.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                _admission.Dispose();
                _stopping.Dispose();
            }
        }
    }
}
