// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Monitor.Application.Continuity;
using Monitor.Domain.Continuity;
using Monitor.Infrastructure.Continuity;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

// The recovered application provides its durable frontier and independently replays offline inputs.
// The transport constructs plans and receipts from this authority plus actual scoped waveform bytes.
public sealed record ClassroomAuthorityCheckpoint(ulong CheckpointSequence, ulong DurableCommitSequence,
    ulong LastDurableEventSequence, string StateSha256);

public interface IClassroomRecoveryAuthority
{
    public ulong AuthorityEpoch { get; }
    public ClassroomAuthorityCheckpoint ReadCheckpoint(Guid studentId);
    public ContinuityReportVerification Replay(RecoveryHelloMessage hello, LocalContinuationReportMessage report,
        CancellationToken cancellationToken);
}

public sealed record ClassroomLearnerResumeSnapshot(ClassroomManifest Manifest, ClassroomTelemetrySnapshot Telemetry,
    byte[] LatestWaveformEnvelope, ClassroomRenderedResumeSnapshot? Rendered = null);

internal static class ClassroomRecoveryJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
        MaxDepth = 24
    };
}

public sealed partial class ClassroomHost
{
    private static readonly TimeSpan RecoveryPlanLifetime = TimeSpan.FromMinutes(2);
    private readonly object _recoveryGate = new();
    private readonly Dictionary<Guid, PendingRecovery> _pendingRecovery = [];
    private IClassroomRecoveryAuthority? _recoveryAuthority;
    private ulong _authorityEpoch = 1;
    internal ulong AuthorityEpoch => Volatile.Read(ref _authorityEpoch);

    public void ConfigureRecoveryAuthority(IClassroomRecoveryAuthority? authority)
    {
        if (authority?.AuthorityEpoch == 0) { throw new ArgumentException("A recovered authority needs a positive epoch.", nameof(authority)); }
        lock (_recoveryGate)
        {
            if (ReferenceEquals(_recoveryAuthority, authority)) { return; }
            if (authority is not null)
            {
                if (authority.AuthorityEpoch <= AuthorityEpoch) { throw new ArgumentException("Recovered authority epoch must advance.", nameof(authority)); }
                Volatile.Write(ref _authorityEpoch, authority.AuthorityEpoch);
            }
            _recoveryAuthority = authority;
            _pendingRecovery.Clear();
        }
    }

    private void MapAuthorityRecoveryRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/recovery/hello", context => RecoveryPostAsync(context, "hello"));
        app.MapPost("/v1/recovery/report", context => RecoveryPostAsync(context, "report"));
        app.MapPost("/v1/recovery/prepared", context => RecoveryPostAsync(context, "prepared"));
        app.MapGet("/v1/recovery/snapshots/{planId:guid}", async context =>
        {
            if (!Authorize(context, out Guid student, out _)) { return; }
            byte[]? snapshot;
            lock (_recoveryGate)
            {
                PendingRecovery? pending = FindPending(student);
                snapshot = pending?.Plan?.PlanId.ToString("D") == (string?)context.Request.RouteValues["planId"]
                    ? pending?.Snapshot : null;
            }
            if (snapshot is null) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
            await WriteRecoveryAsync(context, snapshot).ConfigureAwait(false);
        });
    }

    private async Task RecoveryPostAsync(HttpContext context, string stage)
    {
        if (!Authorize(context, out Guid student, out _)) { return; }
        if (!context.Request.HasJsonContentType()) { context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType; return; }
        if (!TryGetRecoveryAuthority(context, out IClassroomRecoveryAuthority? authority)) { return; }
        try
        {
            int maximum = stage == "report" ? RecoveryWireCodec.MaximumMessageBytes : 4096;
            byte[] body = await ReadRecoveryBodyAsync(context, maximum).ConfigureAwait(false);
            byte[]? reply = stage switch
            {
                "hello" => OpenRecovery(context, student, authority, RecoveryWireCodec.DecodeHello(body)),
                "report" => ProcessRecoveryReport(student, authority, RecoveryWireCodec.DecodeReport(body), context.RequestAborted),
                _ => ProcessPrepared(student, authority, JsonSerializer.Deserialize<RecoveryPreparedAcknowledgement>(body,
                    ClassroomRecoveryJson.Options) ?? throw new InvalidDataException("Missing acknowledgement."))
            };
            if (reply is null) { context.Response.StatusCode = StatusCodes.Status204NoContent; return; }
            await WriteRecoveryAsync(context, reply).ConfigureAwait(false);
        }
        catch (BadHttpRequestException error) { context.Response.StatusCode = error.StatusCode; }
        catch (RecoveryWireCodecException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
        catch (JsonException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
        catch (InvalidDataException) { context.Response.StatusCode = StatusCodes.Status400BadRequest; }
        catch (RecoveryHandshakeException) { context.Response.StatusCode = StatusCodes.Status409Conflict; }
        catch (RecoveryResyncPlanFactoryException) { context.Response.StatusCode = StatusCodes.Status409Conflict; }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
        }
    }

    private bool TryGetRecoveryAuthority(HttpContext context,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IClassroomRecoveryAuthority? authority)
    {
        lock (_recoveryGate) { authority = _recoveryAuthority; }
        if (authority is not null) { return true; }
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return false;
    }

    private static async Task<byte[]> ReadRecoveryBodyAsync(HttpContext context, int limit)
    {
        if (context.Request.ContentLength > limit) { throw new BadHttpRequestException("Recovery message too large.", 413); }
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
        {
            feature.MaxRequestBodySize = limit;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var body = new MemoryStream();
        await context.Request.Body.CopyToAsync(body, timeout.Token).ConfigureAwait(false);
        if (body.Length > limit) { throw new BadHttpRequestException("Recovery message too large.", 413); }
        return body.ToArray();
    }

    private static async Task WriteRecoveryAsync(HttpContext context, byte[] body)
    {
        context.Response.ContentType = "application/json";
        context.Response.ContentLength = body.Length;
        await context.Response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
    }

    private byte[]? OpenRecovery(HttpContext context, Guid student, IClassroomRecoveryAuthority authority, RecoveryHelloMessage hello)
    {
        if (hello.ClientId != student || hello.SessionId != _ring.SessionId || hello.InstanceId != _ring.InstanceId)
        {
            throw new BadHttpRequestException("Forbidden recovery identity.", 403);
        }
        if (!TryReadRecoveryNumber(context, "stream", out ulong oldStream) || oldStream >= _ring.StreamEpoch ||
            !TryReadRecoveryNumber(context, "from", out ulong cursor))
        {
            throw new BadHttpRequestException("Invalid previous waveform cursor.", 409);
        }
        var request = new WaveformRecoveryRequest(hello.SessionId, hello.InstanceId, _ring.TimebaseEpoch, oldStream, cursor,
            WaveformRecoveryPlanner.MinimumJoinLeadNs, 0);
        var handshake = RecoveryHandshakeCoordinator.Start(student, hello.SessionId, hello.InstanceId, AuthorityEpoch);
        handshake.AcceptHello(hello);
        ClassroomAuthorityCheckpoint checkpoint = authority.ReadCheckpoint(student);
        if (hello.LastAppliedEventSequence > checkpoint.LastDurableEventSequence || hello.LastAppliedCommitSequence > checkpoint.DurableCommitSequence)
        {
            throw new BadHttpRequestException("Unrecognized durable frontier.", 409);
        }

        lock (_recoveryGate)
        {
            EnsureCurrentAuthority(authority);
            if (FindPending(student) is { } previous && previous.Hello == hello && previous.Request == request)
            {
                return previous.Plan is null ? null : RecoveryWireCodec.EncodePlan(previous.Plan);
            }
            if (!_pendingRecovery.ContainsKey(student) && _pendingRecovery.Count >= MaximumStudents)
            {
                throw new BadHttpRequestException("Too many active recovery handshakes.", 429);
            }
            var pending = new PendingRecovery(hello, handshake, request);
            byte[]? wire = hello.LocalContinuationReportId is null ? CreatePlan(pending, checkpoint) : null;
            _pendingRecovery[student] = pending;
            return wire;
        }
    }

    private byte[] ProcessRecoveryReport(Guid student, IClassroomRecoveryAuthority authority,
        LocalContinuationReportMessage report, CancellationToken ct)
    {
        if (report.ClientId != student || report.SessionId != _ring.SessionId || report.InstanceId != _ring.InstanceId)
        {
            throw new BadHttpRequestException("Forbidden report identity.", 403);
        }
        PendingRecovery pending;
        lock (_recoveryGate)
        {
            EnsureCurrentAuthority(authority);
            pending = FindPending(student) ?? throw new BadHttpRequestException("Send a recovery hello first.", 409);
            pending.Handshake.AcceptReport(report);
            if (pending.Plan is not null) { return RecoveryWireCodec.EncodePlan(pending.Plan); }
            if (pending.Handshake.Phase != RecoveryHandshakePhase.VerifyingReport)
            {
                throw new BadHttpRequestException("Offline report failed validation.", 409);
            }
            if (pending.ReplayInProgress) { throw new BadHttpRequestException("Report replay is in progress.", 429); }
            pending.ReplayInProgress = true;
        }
        try
        {
            ContinuityReportVerification verification = authority.Replay(pending.Hello, report, ct);
            ClassroomAuthorityCheckpoint checkpoint = authority.ReadCheckpoint(student);
            lock (_recoveryGate)
            {
                EnsureCurrentAuthority(authority);
                if (pending != FindPending(student)) { throw new BadHttpRequestException("Recovery handshake replaced.", 409); }
                pending.Handshake.RecordVerification(verification);
                return CreatePlan(pending, checkpoint);
            }
        }
        finally { lock (_recoveryGate) { pending.ReplayInProgress = false; } }
    }

    private byte[] CreatePlan(PendingRecovery pending, ClassroomAuthorityCheckpoint checkpoint)
    {
        RecoveryPlanningInput input = pending.Handshake.CapturePlanningInput();
        lock (_gate)
        {
            if (_ring.Count == 0) { throw new BadHttpRequestException("No published waveform checkpoint.", 409); }
            var manifest = new ClassroomManifest(1, input.ClientId, _ring.SessionId, _ring.InstanceId, _ring.TimebaseEpoch,
                _ring.StreamEpoch, _ring.OldestBlockSequence, _ring.NextBlockSequence, _ring.NextBlockStartSimTimeNs);
            ClassroomTelemetrySnapshot telemetry = CurrentTelemetrySnapshot();
            byte[] snapshot = JsonSerializer.SerializeToUtf8Bytes(new ClassroomLearnerResumeSnapshot(manifest, telemetry,
                _ring.ReadFrom(_ring.NextBlockSequence - 1, 1).Blocks[0].RawEnvelope,
                CaptureRenderedResume(manifest, telemetry, WaveformRecoveryPlanner.MinimumJoinLeadBlockCount, checkpoint.DurableCommitSequence)),
                JsonSerializerOptions.Web);
            if (snapshot.Length > RecoveryWireCodec.MaximumMessageBytes) { throw new BadHttpRequestException("Learner snapshot exceeds its budget.", StatusCodes.Status422UnprocessableEntity); }
            string digest = Convert.ToHexStringLower(SHA256.HashData(snapshot));
            var context = new RecoveryHostPlanContext(Guid.NewGuid(), checkpoint.CheckpointSequence, checkpoint.DurableCommitSequence,
                checkpoint.StateSha256, _ring.NextBlockStartSimTimeNs, !input.LocalPredictionVerified, digest, "RECOVERED_AUTHORITY");
            RecoveryResyncPlan plan = RecoveryResyncPlanFactory.Create(input, _ring, pending.Request, context);
            pending.JoinSequence = checked(_ring.NextBlockSequence + (ulong)((plan.RelockSimTimeNs - _ring.NextBlockStartSimTimeNs) /
                WaveformBlockAssembler.BlockDurationNs));
            pending.Snapshot = snapshot;
            pending.Plan = plan;
            return RecoveryWireCodec.EncodePlan(plan);
        }
    }

    private byte[]? ProcessPrepared(Guid student, IClassroomRecoveryAuthority authority, RecoveryPreparedAcknowledgement ack)
    {
        ClassroomAuthorityCheckpoint checkpoint = authority.ReadCheckpoint(student);
        lock (_recoveryGate)
        {
            EnsureCurrentAuthority(authority);
            PendingRecovery pending = FindPending(student) ?? throw new BadHttpRequestException("Recovery plan expired.", 409);
            if (pending.Plan is not { } plan || ack.PlanId != plan.PlanId || ack.AuthorityEpoch != plan.NewAuthorityEpoch ||
                ack.StreamEpoch != plan.NewStreamEpoch || ack.RelockSimTimeNs != plan.RelockSimTimeNs ||
                ack.HostStateSha256 != plan.HostStateSha256 || ack.ResumeSnapshotSha256 != plan.ResumeSnapshotSha256 ||
                ack.PrerollContentSha256 is not { Length: 64 } prerollHash || !prerollHash.All(char.IsAsciiHexDigitLower) ||
                ack.PreparedAtSimTimeNs < 0 || ack.PreparedAtSimTimeNs >= plan.RelockSimTimeNs ||
                checkpoint.DurableCommitSequence < plan.AcceptedThroughCommitSequence)
            {
                throw new BadHttpRequestException("Prepared acknowledgement does not match the plan.", 409);
            }
            if (pending.Acknowledgement is not null && pending.Acknowledgement != ack)
            { throw new BadHttpRequestException("Changed acknowledgement.", 409); }
            if (pending.Receipt is not null) { return pending.Receipt; }
            lock (_gate)
            {
                if (_ring.NextBlockSequence > pending.JoinSequence && pending.Acknowledgement is null)
                { throw new BadHttpRequestException("Preparation missed the authoritative boundary.", 409); }
                if (_ring.NextBlockSequence < pending.JoinSequence)
                {
                    pending.Acknowledgement = ack;
                    return null;
                }
                int history = checked((int)((plan.RelockSimTimeNs - plan.PrerollFromSimTimeNs) / WaveformBlockAssembler.BlockDurationNs));
                WaveformBlockReplayResult preroll = _ring.ReadFrom(pending.JoinSequence - (ulong)history, history + 1);
                if (preroll.Blocks.Count < history) { throw new BadHttpRequestException("Recovery boundary expired.", 409); }
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                for (int index = 0; index < history; index++) { hash.AppendData(preroll.Blocks[index].RawEnvelope); }
                if (Convert.ToHexStringLower(hash.GetHashAndReset()) != ack.PrerollContentSha256)
                {
                    throw new BadHttpRequestException("Recovery preroll does not match published waveforms.", 409);
                }
                pending.Acknowledgement = ack;
                if (preroll.Blocks.Count == history) { return null; }
                var command = new RecoveryCommitCommand(plan.PlanId, plan.NewAuthorityEpoch, plan.NewStreamEpoch, plan.RelockSimTimeNs,
                    checkpoint.DurableCommitSequence, plan.HostStateSha256, preroll.Blocks[history].ContentSha256);
                pending.Receipt = JsonSerializer.SerializeToUtf8Bytes(command, ClassroomRecoveryJson.Options);
                return pending.Receipt;
            }
        }
    }

    private void EnsureCurrentAuthority(IClassroomRecoveryAuthority authority)
    {
        if (!ReferenceEquals(_recoveryAuthority, authority)) { throw new BadHttpRequestException("Recovery authority changed.", 409); }
    }

    private PendingRecovery? FindPending(Guid student)
    {
        foreach (var (id, recovery) in _pendingRecovery)
        {
            if (Stopwatch.GetElapsedTime(recovery.Started) >= RecoveryPlanLifetime) { _pendingRecovery.Remove(id); }
        }
        return _pendingRecovery.GetValueOrDefault(student);
    }

    private sealed class PendingRecovery(RecoveryHelloMessage hello, RecoveryHandshakeCoordinator handshake, WaveformRecoveryRequest request)
    {
        internal long Started { get; } = Stopwatch.GetTimestamp();
        internal RecoveryHelloMessage Hello { get; } = hello;
        internal RecoveryHandshakeCoordinator Handshake { get; } = handshake;
        internal WaveformRecoveryRequest Request { get; } = request;
        internal bool ReplayInProgress { get; set; }
        internal RecoveryResyncPlan? Plan { get; set; }
        internal ulong JoinSequence { get; set; }
        internal byte[]? Snapshot { get; set; }
        internal RecoveryPreparedAcknowledgement? Acknowledgement { get; set; }
        internal byte[]? Receipt { get; set; }
    }
}
