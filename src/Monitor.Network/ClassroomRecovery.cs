// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Monitor.Infrastructure.Continuity;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

public sealed record ClassroomWaveformRecovery(
    ClassroomManifest Manifest,
    ulong? MissedFromBlockSequence,
    ulong PrerollFromBlockSequence,
    ulong NextBlockSequence,
    WaveformEnvelope Checkpoint,
    ClassroomTelemetrySnapshot Telemetry,
    ClassroomWaveformSubscription Subscription,
    IReadOnlyList<WaveformRetainedBlock> RenderHistory,
    ClassroomRenderedResumeSnapshot? Rendered = null);

internal sealed record ClassroomRecoverySnapshot(
    ClassroomManifest Manifest,
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong RequestedSequence,
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong PrerollFromBlockSequence,
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong JoinBlockSequence,
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] long JoinSimTimeNs,
    byte[] Checkpoint,
    ClassroomTelemetrySnapshot Telemetry,
    ClassroomRenderedResumeSnapshot? Rendered);

public sealed partial class ClassroomHost
{
    private void MapRecoveryRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/recovery/waveforms", context => WaveformRecoveryAsync(context, lateJoin: false));
        app.MapGet("/v1/recovery/join", context => WaveformRecoveryAsync(context, lateJoin: true));
        MapAuthorityRecoveryRoutes(app);
        MapContinuityDelivery(app);
    }

    private async Task WaveformRecoveryAsync(HttpContext context, bool lateJoin)
    {
        if (!Authorize(context, out Guid studentId, out _)) { return; }
        if (!TryReadRecoveryNumber(context, "from", out ulong from) ||
            !TryReadRecoveryNumber(context, "timebase", out ulong timebase) ||
            !TryReadRecoveryNumber(context, "stream", out ulong stream) ||
            !TryReadRecoveryNumber(context, "history", out ulong history) ||
            history is < WaveformRecoveryPlanner.MinimumJoinLeadBlockCount or > WaveformBlockRing.HostRetentionBlockCount)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        ClassroomRecoverySnapshot snapshot;
        lock (_gate)
        {
            if (timebase != _ring.TimebaseEpoch || stream != _ring.StreamEpoch ||
                _ring.Count == 0 || (lateJoin ? from > _ring.NextBlockSequence : from >= _ring.OldestBlockSequence))
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                return;
            }
            var request = new WaveformRecoveryRequest(_ring.SessionId, _ring.InstanceId, timebase, stream, from,
                history * WaveformBlockAssembler.BlockDurationNs, 0);
            WaveformJoinBoundary boundary = lateJoin ? WaveformRecoveryPlanner.PlanLateJoin(_ring, request.RequiredRenderHistoryNs)
                : WaveformRecoveryPlanner.Plan(_ring, request).JoinBoundary!;
            WaveformRetainedBlock checkpoint = _ring.ReadFrom(_ring.NextBlockSequence - 1, 1).Blocks[0];
            var manifest = new ClassroomManifest(1, studentId, _ring.SessionId, _ring.InstanceId, timebase, stream,
                _ring.OldestBlockSequence, _ring.NextBlockSequence, _ring.NextBlockStartSimTimeNs);
            ClassroomTelemetrySnapshot telemetry = CurrentTelemetrySnapshot();
            snapshot = new ClassroomRecoverySnapshot(manifest, from, boundary.BlockSequence - history, boundary.BlockSequence,
                boundary.SimTimeNs, checkpoint.RawEnvelope, telemetry, CaptureRenderedResume(manifest, telemetry, (int)history, telemetry.Control.CommitSeq));
        }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonSerializerOptions.Web);
        if (bytes.Length > RecoveryWireCodec.MaximumMessageBytes)
        { context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity; return; }
        if (lateJoin)
        {
            ulong first = snapshot.PrerollFromBlockSequence;
            if (snapshot.Rendered is not null)
            {
                ulong available = ulong.Parse(snapshot.Rendered.Descriptor.GetProperty("available_render_history_ns").GetString()!,
                    CultureInfo.InvariantCulture) / WaveformBlockAssembler.BlockDurationNs;
                first = Math.Min(first, snapshot.Manifest.NextBlockSequence - available);
            }
            lock (_gate) { PrepareSharedRejoin(studentId, first, snapshot.JoinBlockSequence); }
        }
        await WriteRecoveryAsync(context, bytes).ConfigureAwait(false);
    }

    private static bool TryReadRecoveryNumber(HttpContext context, string key, out ulong number)
    {
        number = 0;
        var values = context.Request.Query[key];
        return values.Count == 1 && ulong.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}

public sealed partial class ClassroomStudentClient
{
    public Task<ClassroomWaveformRecovery> ResynchronizeWaveformsAsync(ClassroomManifest previous, ulong nextBlockSequence,
        CancellationToken cancellationToken = default) =>
        ResynchronizeWaveformsAsync(previous, nextBlockSequence, WaveformRecoveryPlanner.MinimumJoinLeadBlockCount, cancellationToken);

    public Task<ClassroomWaveformRecovery> ResynchronizeWaveformsAsync(ClassroomManifest previous, ulong nextBlockSequence,
        int renderHistoryBlocks, CancellationToken cancellationToken = default) =>
        JoinSnapshotAsync(previous, nextBlockSequence, renderHistoryBlocks, false, cancellationToken);

    public Task<ClassroomWaveformRecovery> JoinAtFutureBoundaryAsync(ClassroomManifest manifest,
        int renderHistoryBlocks, CancellationToken cancellationToken = default) =>
        JoinSnapshotAsync(manifest, manifest.NextBlockSequence, renderHistoryBlocks, true, cancellationToken);

    private async Task<ClassroomWaveformRecovery> JoinSnapshotAsync(ClassroomManifest previous, ulong nextBlockSequence,
        int renderHistoryBlocks, bool lateJoin, CancellationToken cancellationToken)
    {
        ValidateManifest(previous);
        ArgumentOutOfRangeException.ThrowIfLessThan(renderHistoryBlocks, WaveformRecoveryPlanner.MinimumJoinLeadBlockCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(renderHistoryBlocks, WaveformBlockRing.HostRetentionBlockCount);
        string route = lateJoin ? "join" : "waveforms";
        string path = FormattableString.Invariant($"v1/recovery/{route}?from={nextBlockSequence}&timebase={previous.TimebaseEpoch}&stream={previous.StreamEpoch}&history={renderHistoryBlocks}");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        byte[] wire = await SendRecoveryAsync(request, RecoveryWireCodec.MaximumMessageBytes, cancellationToken).ConfigureAwait(false);
        ClassroomRecoverySnapshot snapshot = JsonSerializer.Deserialize<ClassroomRecoverySnapshot>(wire, RecoveryJson)
            ?? throw new InvalidDataException("Missing learner recovery snapshot.");
        ClassroomManifest current = snapshot.Manifest;
        ValidateManifest(current);
        if (current.StudentId != previous.StudentId || current.SessionId != previous.SessionId ||
            current.InstanceId != previous.InstanceId || current.TimebaseEpoch != previous.TimebaseEpoch ||
            current.StreamEpoch != previous.StreamEpoch || current.NextBlockSequence < previous.NextBlockSequence ||
            (Int128)current.NextBlockStartSimTimeNs - previous.NextBlockStartSimTimeNs !=
                ((Int128)current.NextBlockSequence - previous.NextBlockSequence) * WaveformBlockAssembler.BlockDurationNs ||
            (!lateJoin && current.OldestBlockSequence <= nextBlockSequence) || snapshot.RequestedSequence != nextBlockSequence ||
            snapshot.PrerollFromBlockSequence < current.OldestBlockSequence || snapshot.PrerollFromBlockSequence > current.NextBlockSequence ||
            (Int128)snapshot.JoinBlockSequence - snapshot.PrerollFromBlockSequence != renderHistoryBlocks ||
            (Int128)snapshot.JoinBlockSequence - current.NextBlockSequence < WaveformRecoveryPlanner.MinimumJoinLeadBlockCount ||
            (Int128)snapshot.JoinSimTimeNs - current.NextBlockStartSimTimeNs !=
                ((Int128)snapshot.JoinBlockSequence - current.NextBlockSequence) * WaveformBlockAssembler.BlockDurationNs ||
            snapshot.Checkpoint is null || snapshot.Telemetry is null ||
            snapshot.Telemetry.SessionId != current.SessionId || snapshot.Telemetry.InstanceId != current.InstanceId)
        {
            throw new InvalidDataException("Recovery snapshot does not match the classroom or requested gap.");
        }
        ValidateLearnerTelemetry(current, snapshot.Telemetry);
        WaveformEnvelope checkpoint = VerifyRecoveryCheckpoint(current, snapshot.Checkpoint);

        int renderCount = snapshot.Rendered is { } rendered ? ClassroomRenderMaterial.Validate(rendered, current, snapshot.Telemetry) : 0;
        ulong renderFrom = current.NextBlockSequence - (ulong)renderCount;
        ulong start = Math.Min(renderFrom, snapshot.PrerollFromBlockSequence);
        ClassroomWaveformSubscription subscription = await SubscribeAsync(current, start, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            using var hash = snapshot.Rendered is not null ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
            List<WaveformRetainedBlock> renderHistory = new(renderHistoryBlocks);
            while (subscription.NextBlockSequence < snapshot.JoinBlockSequence)
            {
                WaveformEnvelope block = await subscription.ReceiveAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new EndOfStreamException("The host stopped during recovery preroll.");
                WaveformRetainedBlock retained = subscription.ReadBufferedBlocks(block.BlockSequence, 1).Blocks[0];
                if (block.BlockSequence >= snapshot.PrerollFromBlockSequence) { renderHistory.Add(retained); }
                if (hash is not null && block.BlockSequence >= renderFrom && block.BlockSequence < current.NextBlockSequence)
                { hash.AppendData(retained.RawEnvelope); }
            }
            if (hash is not null && Convert.ToHexStringLower(hash.GetHashAndReset()) !=
                snapshot.Rendered!.Descriptor.GetProperty("waveform_preroll_sha256").GetString())
            { throw new InvalidDataException("The learner render preroll digest does not match the verified waveform stream."); }
            return new ClassroomWaveformRecovery(current, lateJoin ? null : nextBlockSequence, snapshot.PrerollFromBlockSequence,
                subscription.NextBlockSequence, checkpoint, snapshot.Telemetry, subscription, renderHistory.AsReadOnly(), snapshot.Rendered);
        }
        catch
        {
            await subscription.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static readonly JsonSerializerOptions RecoveryJson = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 24,
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private async Task<byte[]> SendRecoveryAsync(HttpRequestMessage request, int maximumBytes, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) { return []; }
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Expected a recovery JSON response.");
        }
        await response.Content.LoadIntoBufferAsync(maximumBytes, deadline.Token).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(false);
    }
}
