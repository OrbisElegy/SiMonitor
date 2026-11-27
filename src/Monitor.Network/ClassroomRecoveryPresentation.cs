// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

// The renderer supplies its already-normalized state images. Their exact bytes are content-addressed.
public sealed record ClassroomRenderedResumeSnapshot(JsonElement Descriptor, JsonElement ResolvedPlan,
    byte[] ProjectionImage, byte[] NumericImage);

public sealed partial class ClassroomHost
{
    private readonly Dictionary<Guid, ClassroomRenderMaterial> _learnerRenderMaterials = [];

    public void ConfigureRecoveryPresentation(Guid studentId, JsonElement resolvedPlan, ReadOnlySpan<byte> projectionImage,
        ReadOnlySpan<byte> numericImage)
    {
        if (studentId == Guid.Empty) { throw new ArgumentException("A student ID is required.", nameof(studentId)); }
        ClassroomContracts.Validate("ResolvedPresentationPlan@1", resolvedPlan);
        if (resolvedPlan.GetRawText().Length > 32_768 || projectionImage.Length is 0 or > 131_072 || numericImage.Length is 0 or > 131_072)
        { throw new ArgumentException("Learner presentation images exceed the snapshot budget."); }
        var material = new ClassroomRenderMaterial(resolvedPlan.Clone(), projectionImage.ToArray(), numericImage.ToArray(), AuthorityEpoch);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            if (!_learnerRenderMaterials.ContainsKey(studentId) && _learnerRenderMaterials.Count >= MaximumStudents)
            { throw new InvalidOperationException("Classroom render snapshot capacity reached."); }
            if (!ClassroomRenderMaterial.Matches(material.ProjectionImage, material.NumericImage, CurrentTelemetrySnapshot()))
            { throw new InvalidDataException("Render images must match current learner-visible projections and numerics."); }
            _learnerRenderMaterials[studentId] = material;
        }
    }

    public void ClearRecoveryPresentation(Guid studentId)
    {
        lock (_gate) { _learnerRenderMaterials.Remove(studentId); }
    }

    private ClassroomRenderedResumeSnapshot? CaptureRenderedResume(ClassroomManifest manifest, ClassroomTelemetrySnapshot telemetry,
        int requestedHistory, ulong commitSeq)
    {
        if (!_learnerRenderMaterials.TryGetValue(manifest.StudentId, out var material) || material.AuthorityEpoch != AuthorityEpoch ||
            !ClassroomRenderMaterial.Matches(material.ProjectionImage, material.NumericImage, telemetry))
        { return null; }
        // Reserve one second of ring space during the snapshot-to-subscription handoff.
        int count = Math.Min(requestedHistory, Math.Min(_ring.Count, WaveformBlockRing.HostRetentionBlockCount - WaveformRecoveryPlanner.MinimumJoinLeadBlockCount));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (WaveformRetainedBlock block in _ring.ReadFrom(_ring.NextBlockSequence - (ulong)count, count).Blocks)
        { hash.AppendData(block.RawEnvelope); }
        var descriptor = JsonSerializer.SerializeToElement(new
        {
            schema_id = "ClientResumeSnapshot@1",
            snapshot_id = Guid.NewGuid(),
            session_id = manifest.SessionId,
            instance_id = manifest.InstanceId,
            authority_epoch = Decimal(AuthorityEpoch),
            commit_seq = Decimal(commitSeq),
            sim_time_ns = Decimal(manifest.NextBlockStartSimTimeNs),
            resolved_plan_sha256 = material.Plan.GetProperty("content_sha256").GetString(),
            projection_bundle_sha256 = Convert.ToHexStringLower(SHA256.HashData(material.ProjectionImage)),
            numeric_snapshot_sha256 = Convert.ToHexStringLower(SHA256.HashData(material.NumericImage)),
            waveform_preroll_sha256 = Convert.ToHexStringLower(hash.GetHashAndReset()),
            required_render_history_ns = Decimal((long)requestedHistory * WaveformBlockAssembler.BlockDurationNs),
            available_render_history_ns = Decimal((long)count * WaveformBlockAssembler.BlockDurationNs),
            prepared = false
        });
        ClassroomContracts.Validate("ClientResumeSnapshot@1", descriptor);
        return new(descriptor, material.Plan, material.ProjectionImage, material.NumericImage);
    }
}

internal sealed record ClassroomRenderMaterial(JsonElement Plan, byte[] ProjectionImage, byte[] NumericImage, ulong AuthorityEpoch)
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 24, AllowDuplicateProperties = false };

    internal static bool Matches(byte[] projections, byte[] numerics, ClassroomTelemetrySnapshot telemetry)
    {
        using var projection = JsonDocument.Parse(projections, Options);
        using var numeric = JsonDocument.Parse(numerics, Options);
        JsonElement expectedProjection = JsonSerializer.SerializeToElement(new
        {
            projections = telemetry.Projections,
            control = telemetry.Control,
            activeConfiguration = telemetry.ActiveConfiguration
        }, JsonSerializerOptions.Web);
        return JsonElement.DeepEquals(projection.RootElement, expectedProjection) &&
            JsonElement.DeepEquals(numeric.RootElement, JsonSerializer.SerializeToElement(telemetry.Numerics));
    }

    internal static int Validate(ClassroomRenderedResumeSnapshot rendered, ClassroomManifest manifest, ClassroomTelemetrySnapshot telemetry)
    {
        ClassroomContracts.Validate("ClientResumeSnapshot@1", rendered.Descriptor);
        ClassroomContracts.Validate("ResolvedPresentationPlan@1", rendered.ResolvedPlan);
        JsonElement d = rendered.Descriptor;
        long available = long.Parse(d.GetProperty("available_render_history_ns").GetString()!, CultureInfo.InvariantCulture);
        long required = long.Parse(d.GetProperty("required_render_history_ns").GetString()!, CultureInfo.InvariantCulture);
        if (d.GetProperty("session_id").GetGuid() != manifest.SessionId || d.GetProperty("instance_id").GetGuid() != manifest.InstanceId ||
            d.GetProperty("sim_time_ns").GetString() != manifest.NextBlockStartSimTimeNs.ToString(CultureInfo.InvariantCulture) ||
            d.GetProperty("prepared").GetBoolean() || available < 0 || required < available ||
            required > (long)WaveformBlockRing.HostRetentionBlockCount * WaveformBlockAssembler.BlockDurationNs ||
            available % WaveformBlockAssembler.BlockDurationNs != 0 || required % WaveformBlockAssembler.BlockDurationNs != 0 ||
            available / WaveformBlockAssembler.BlockDurationNs > (long)(manifest.NextBlockSequence - manifest.OldestBlockSequence) ||
            rendered.ProjectionImage is not { Length: > 0 and <= 131_072 } || rendered.NumericImage is not { Length: > 0 and <= 131_072 } ||
            d.GetProperty("resolved_plan_sha256").GetString() != rendered.ResolvedPlan.GetProperty("content_sha256").GetString() ||
            d.GetProperty("projection_bundle_sha256").GetString() != Convert.ToHexStringLower(SHA256.HashData(rendered.ProjectionImage)) ||
            d.GetProperty("numeric_snapshot_sha256").GetString() != Convert.ToHexStringLower(SHA256.HashData(rendered.NumericImage)) ||
            !Matches(rendered.ProjectionImage, rendered.NumericImage, telemetry))
        { throw new InvalidDataException("Learner render snapshot material does not match its descriptor."); }
        return (int)(available / WaveformBlockAssembler.BlockDurationNs);
    }
}
