// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Monitor.Domain.Continuity;
using Monitor.Infrastructure.Continuity;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

public sealed partial class ClassroomStudentClient
{
    public async Task<RecoveryResyncPlan?> BeginAuthorityRecoveryAsync(RecoveryHelloMessage hello,
        ulong previousStreamEpoch, ulong nextBlockSequence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hello);
        string path = FormattableString.Invariant($"v1/recovery/hello?stream={previousStreamEpoch}&from={nextBlockSequence}");
        using var request = RecoveryPost(path, RecoveryWireCodec.EncodeHello(hello));
        byte[] bytes = await SendRecoveryAsync(request, 4096, cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0 && hello.LocalContinuationReportId.HasValue) { return null; }
        RecoveryResyncPlan plan = RecoveryWireCodec.DecodePlan(bytes);
        if (plan.NewAuthorityEpoch <= hello.ObservedAuthorityEpoch || plan.NewStreamEpoch <= previousStreamEpoch ||
            plan.AcceptedThroughCommitSequence < hello.LastAppliedCommitSequence)
        {
            throw new InvalidDataException("The recovered host supplied a stale plan.");
        }
        return plan;
    }

    public async Task<RecoveryResyncPlan> SendContinuationReportAsync(LocalContinuationReportMessage report,
        CancellationToken cancellationToken = default)
    {
        using var request = RecoveryPost("v1/recovery/report", RecoveryWireCodec.EncodeReport(report));
        byte[] bytes = await SendRecoveryAsync(request, 4096, cancellationToken).ConfigureAwait(false);
        return RecoveryWireCodec.DecodePlan(bytes);
    }

    public async Task<ClassroomLearnerResumeSnapshot> ReadRecoverySnapshotAsync(ClassroomManifest manifest,
        RecoveryResyncPlan plan, CancellationToken cancellationToken = default)
    {
        ValidateManifest(manifest);
        ArgumentNullException.ThrowIfNull(plan);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"v1/recovery/snapshots/{plan.PlanId:D}");
        byte[] bytes = await SendRecoveryAsync(request, RecoveryWireCodec.MaximumMessageBytes, cancellationToken).ConfigureAwait(false);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != plan.ResumeSnapshotSha256)
        {
            throw new InvalidDataException("The resume snapshot digest does not match the recovery plan.");
        }
        ClassroomLearnerResumeSnapshot snapshot = JsonSerializer.Deserialize<ClassroomLearnerResumeSnapshot>(bytes, RecoveryJson)
            ?? throw new InvalidDataException("Missing resume snapshot.");
        ClassroomManifest captured = snapshot.Manifest;
        ValidateManifest(captured);
        if (captured.SessionId != manifest.SessionId || captured.InstanceId != manifest.InstanceId ||
            captured.StudentId != manifest.StudentId || captured.StreamEpoch != plan.NewStreamEpoch ||
            captured.NextBlockStartSimTimeNs > plan.RelockSimTimeNs || snapshot.LatestWaveformEnvelope is null ||
            snapshot.Telemetry is null || snapshot.Telemetry.SessionId != captured.SessionId ||
            snapshot.Telemetry.InstanceId != captured.InstanceId)
        {
            throw new InvalidDataException("Resume snapshot belongs to a different classroom.");
        }
        ValidateLearnerTelemetry(captured, snapshot.Telemetry);
        VerifyRecoveryCheckpoint(captured, snapshot.LatestWaveformEnvelope);
        if (snapshot.Rendered is { } rendered)
        {
            int history = ClassroomRenderMaterial.Validate(rendered, captured, snapshot.Telemetry);
            if (rendered.Descriptor.GetProperty("authority_epoch").GetString() != plan.NewAuthorityEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                ulong.Parse(rendered.Descriptor.GetProperty("commit_seq").GetString()!, System.Globalization.CultureInfo.InvariantCulture) < plan.AcceptedThroughCommitSequence)
            { throw new InvalidDataException("The rendered snapshot has an unexpected authority epoch or commit."); }
            await using var stream = await SubscribeAsync(captured, captured.NextBlockSequence - (ulong)history, cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (int index = 0; index < history; index++)
            {
                WaveformEnvelope block = await stream.ReceiveAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new EndOfStreamException("Host stopped during render snapshot verification.");
                hash.AppendData(stream.ReadBufferedBlocks(block.BlockSequence, 1).Blocks[0].RawEnvelope);
            }
            if (Convert.ToHexStringLower(hash.GetHashAndReset()) != rendered.Descriptor.GetProperty("waveform_preroll_sha256").GetString())
            { throw new InvalidDataException("The rendered snapshot history does not match the verified waveform stream."); }
        }
        return snapshot;
    }

    public async Task<RecoveryCommitCommand?> SendRecoveryPreparedAsync(RecoveryPreparedAcknowledgement acknowledgement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        using var request = RecoveryPost("v1/recovery/prepared",
            JsonSerializer.SerializeToUtf8Bytes(acknowledgement, ClassroomRecoveryJson.Options));
        byte[] bytes = await SendRecoveryAsync(request, 4096, cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0) { return null; }
        var receipt = JsonSerializer.Deserialize<RecoveryCommitCommand>(bytes, ClassroomRecoveryJson.Options);
        if (receipt is null || receipt.PlanId != acknowledgement.PlanId || receipt.AuthorityEpoch != acknowledgement.AuthorityEpoch ||
            receipt.StreamEpoch != acknowledgement.StreamEpoch || receipt.RelockSimTimeNs != acknowledgement.RelockSimTimeNs ||
            receipt.HostStateSha256 != acknowledgement.HostStateSha256 || receipt.FirstAuthoritativeBlockSha256 is not { Length: 64 } hash ||
            !hash.All(char.IsAsciiHexDigitLower))
        {
            throw new InvalidDataException("The commit receipt does not match the prepared recovery.");
        }
        return receipt;
    }

    private static HttpRequestMessage RecoveryPost(string path, byte[] bytes)
    {
        var body = new ByteArrayContent(bytes);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new HttpRequestMessage(HttpMethod.Post, path) { Content = body };
    }

    private static void ValidateLearnerTelemetry(ClassroomManifest manifest, ClassroomTelemetrySnapshot telemetry)
    {
        if (telemetry.Numerics is not { Count: <= 64 } || telemetry.Projections is not { Count: <= 128 } ||
            telemetry.SessionId != manifest.SessionId || telemetry.InstanceId != manifest.InstanceId ||
            telemetry.OldestEventSequence > telemetry.NextEventSequence || telemetry.Control is null || telemetry.ActiveConfiguration is null ||
            telemetry.Timeline is null || telemetry.Timeline.TimebaseEpoch != manifest.TimebaseEpoch)
        { throw new InvalidDataException("Invalid learner state or event frontier in the recovery snapshot."); }
        ClassroomContracts.Validate("TimebaseState@1", telemetry.Timeline.ToTimebaseState());
        if (telemetry.PendingTimeline is { } next) { ClassroomContracts.Validate("TimebaseState@1", next.ToTimebaseState()); }
        _ = telemetry.ActiveConfiguration.Snapshot();
        foreach (JsonElement numeric in telemetry.Numerics)
        {
            ClassroomContracts.Validate("NumericFrame@1", numeric);
            if (numeric.GetProperty("measurement_channel_ref").GetProperty("source_instance_id").GetGuid() != manifest.InstanceId)
            { throw new InvalidDataException("Numeric frame belongs to another learner instance."); }
        }
        foreach (JsonElement projection in telemetry.Projections)
        {
            if (!projection.TryGetProperty("schema_id", out var schema) || schema.GetString() is not { } type ||
                type is not ("DisplayRoleResolutionProjection@1" or "MeasurementGroupFrame@1" or "AlarmEpisodeProjection@1" or
                    "AlarmAudioPolicyProjection@1" or "TherapyStateProjection@1" or "ActiveConfigurationSnapshot@1" or "SweepStateProjection@1"))
            { throw new InvalidDataException("Unrecognized learner recovery projection."); }
            ClassroomContracts.Validate(type, projection);
            if (projection.TryGetProperty("instance_id", out var instance) && instance.GetGuid() != manifest.InstanceId)
            { throw new InvalidDataException("Recovery projection belongs to another learner instance."); }
        }
    }

    private static WaveformEnvelope VerifyRecoveryCheckpoint(ClassroomManifest manifest, byte[] bytes)
    {
        WaveformEnvelope checkpoint = WaveformEnvelopeCodec.Decode(bytes);
        if (checkpoint.SessionId != manifest.SessionId || checkpoint.InstanceId != manifest.InstanceId ||
            checkpoint.TimebaseEpoch != manifest.TimebaseEpoch || checkpoint.StreamEpoch != manifest.StreamEpoch ||
            (Int128)checkpoint.BlockSequence + 1 != manifest.NextBlockSequence ||
            (Int128)checkpoint.StartSimTimeNs + checkpoint.DurationNs != manifest.NextBlockStartSimTimeNs)
        {
            throw new InvalidDataException("Recovery checkpoint does not match the host snapshot.");
        }
        return checkpoint;
    }
}
