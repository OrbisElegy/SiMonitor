// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

public sealed record ClassroomArtifactDescriptor(string Sha256, int LengthBytes, string MediaType)
{
    public const string StudyRecordMediaType = "application/vnd.simonitor.waveform-record.v1";
    public const string ImageMediaType = "image/png";
    public const int MaximumLengthBytes = 16 * 1024 * 1024;
    internal const int MaximumEntries = 128;

    internal bool IsValid => IsContentHash(Sha256) && LengthBytes is > 0 and <= MaximumLengthBytes &&
        MediaType is StudyRecordMediaType or ImageMediaType;

    internal static bool IsContentHash(string? hash) =>
        hash is { Length: 64 } && hash.All(character => character is >= 'a' and <= 'f' or >= '0' and <= '9');
}

public sealed partial class ClassroomHost
{
    private sealed record PublishedArtifact(ClassroomArtifactDescriptor Descriptor, byte[] Bytes)
    {
        internal CancellationTokenSource Withdrawal { get; } = new();
        internal bool Shared { get; set; }
        internal HashSet<Guid> Recipients { get; } = [];
        internal bool VisibleTo(Guid studentId) => Shared || Recipients.Contains(studentId);
    }

    private const int MaximumArtifactStoreBytes = 64 * 1024 * 1024;
    private readonly Dictionary<string, PublishedArtifact> _artifacts = [];
    private readonly SemaphoreSlim _artifactTransfers = new(8, 8);
    private int _artifactStoreBytes;

    // Local teacher explicitly grants the released projection to this classroom or an individual endpoint.
    public ClassroomArtifactDescriptor PublishStudyRecord(WaveformRecordArchive record, Guid? studentId = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        WaveformRecordArchivePlan plan = record.CapturePlan();
        if (plan.SessionId != _admission.SessionId || plan.InstanceId != _admission.InstanceId)
        {
            throw new ArgumentException("The study record belongs to a different classroom instance.", nameof(record));
        }
        return PublishArtifact(ClassroomArtifactDescriptor.StudyRecordMediaType, ClassroomArtifactRecord.Encode(record), studentId);
    }

    public ClassroomArtifactDescriptor PublishLearnerImage(ReadOnlySpan<byte> png, Guid? studentId = null)
    {
        if (png.Length is < 33 or > ClassroomArtifactDescriptor.MaximumLengthBytes ||
            !png.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) || !png.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new ArgumentException("Expected a bounded learner-visible PNG image.", nameof(png));
        }
        return PublishArtifact(ClassroomArtifactDescriptor.ImageMediaType, png.ToArray(), studentId);
    }

    public bool RemoveArtifact(string sha256)
    {
        PublishedArtifact? removed;
        lock (_gate)
        {
            if (!_artifacts.Remove(sha256, out removed)) { return false; }
            _artifactStoreBytes -= removed.Bytes.Length;
        }
        removed.Withdrawal.Cancel();
        removed.Withdrawal.Dispose();
        return true;
    }

    private ClassroomArtifactDescriptor PublishArtifact(string type, byte[] bytes, Guid? studentId)
    {
        if (studentId == Guid.Empty) { throw new ArgumentException("Invalid student identity.", nameof(studentId)); }
        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            if (!_artifacts.TryGetValue(hash, out PublishedArtifact? artifact))
            {
                if (_artifacts.Count >= ClassroomArtifactDescriptor.MaximumEntries || _artifactStoreBytes + bytes.Length > MaximumArtifactStoreBytes)
                {
                    throw new InvalidOperationException("The classroom artifact store is full.");
                }
                artifact = new PublishedArtifact(new ClassroomArtifactDescriptor(hash, bytes.Length, type), bytes);
                _artifacts.Add(hash, artifact);
                _artifactStoreBytes += bytes.Length;
            }
            if (studentId is null)
            {
                artifact.Shared = true;
                artifact.Recipients.Clear();
            }
            else if (!artifact.Shared)
            {
                if (artifact.Recipients.Count >= 256 && !artifact.Recipients.Contains(studentId.Value))
                {
                    throw new InvalidOperationException("The classroom artifact recipient list is full.");
                }
                artifact.Recipients.Add(studentId.Value);
            }
            return artifact.Descriptor;
        }
    }

    private void MapArtifactEndpoints(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v1/artifacts", ListArtifactsAsync);
        routes.MapMethods("/v1/artifacts/{sha256}", ["GET", "HEAD"], ReadArtifactAsync);
    }

    private async Task ListArtifactsAsync(HttpContext context)
    {
        if (!Authorize(context, out Guid studentId, out _)) { return; }
        ClassroomArtifactDescriptor[] visible;
        lock (_gate)
        {
            visible = _artifacts.Values.Where(artifact => artifact.VisibleTo(studentId))
                .Select(artifact => artifact.Descriptor).OrderBy(item => item.Sha256, StringComparer.Ordinal).ToArray();
        }
        await context.Response.WriteAsJsonAsync(visible, context.RequestAborted).ConfigureAwait(false);
    }

    private async Task ReadArtifactAsync(HttpContext context)
    {
        if (!Authorize(context, out Guid studentId, out DateTimeOffset expiresAt)) { return; }
        string? hash = context.Request.RouteValues["sha256"] as string;
        PublishedArtifact? artifact;
        CancellationToken withdrawn;
        lock (_gate)
        {
            if (!ClassroomArtifactDescriptor.IsContentHash(hash) || !_artifacts.TryGetValue(hash!, out artifact) || !artifact.VisibleTo(studentId))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            withdrawn = artifact.Withdrawal.Token;
        }
        if (!_artifactTransfers.Wait(0))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }
        try
        {
            TimeSpan remaining = expiresAt - _admission.Clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            using var expiry = new CancellationTokenSource(remaining < TimeSpan.FromSeconds(30) ? remaining : TimeSpan.FromSeconds(30),
                _admission.Clock);
            using var stopping = CancellationTokenSource.CreateLinkedTokenSource(expiry.Token, _stopping.Token, withdrawn);
            using var stopTransfer = stopping.Token.Register(context.Abort);
            context.Response.Headers.ContentDisposition = "attachment";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            await Results.Bytes(artifact.Bytes, contentType: artifact.Descriptor.MediaType,
                entityTag: new EntityTagHeaderValue("\"" + hash + "\""), enableRangeProcessing: true)
                .ExecuteAsync(context).ConfigureAwait(false);
        }
        finally { _artifactTransfers.Release(); }
    }
}
