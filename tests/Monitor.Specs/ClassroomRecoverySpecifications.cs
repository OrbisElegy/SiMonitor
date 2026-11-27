// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Monitor.Domain.Continuity;
using Monitor.Network;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class ClassroomRecoverySpecifications
{
    internal static async Task VerifyLoopbackAsync(ClassroomHost host, ClassroomStudentClient client,
        ClassroomManifest manifest, Action<ulong> publish, CancellationToken ct)
    {
        ClassroomManifest before = await client.GetSessionAsync(ct).ConfigureAwait(false);
        ConfigurePresentation(host, manifest.StudentId);
        ulong next = before.NextBlockSequence;
        Task<ClassroomWaveformRecovery> resynchronizing = client.ResynchronizeWaveformsAsync(manifest, 0, 64, ct);
        while (!resynchronizing.IsCompleted)
        {
            await Task.Delay(40, ct).ConfigureAwait(false);
            publish(next++);
        }
        ClassroomWaveformRecovery recovery = await resynchronizing.ConfigureAwait(false);
        await using (ClassroomWaveformSubscription subscription = recovery.Subscription)
        {
            Check.That(recovery.MissedFromBlockSequence == 0 && recovery.PrerollFromBlockSequence > 0 &&
                recovery.NextBlockSequence == recovery.PrerollFromBlockSequence + 64 &&
                recovery.Rendered?.Descriptor.GetProperty("schema_id").GetString() == "ClientResumeSnapshot@1" &&
                recovery.Checkpoint.BlockSequence + 1 == recovery.Manifest.NextBlockSequence &&
                recovery.RenderHistory.Count == 64 && recovery.RenderHistory.Select((block, index) =>
                    block.BlockSequence == recovery.PrerollFromBlockSequence + (ulong)index).All(match => match) &&
                subscription.ReadBufferedBlocks(recovery.NextBlockSequence - 50, 50).Blocks.Count == 50,
                "an evicted cursor returns a learner snapshot, an explicit gap and verified render preroll");
            while (next <= recovery.NextBlockSequence) { publish(next++); }
            Check.That((await subscription.ReceiveAsync(ct).ConfigureAwait(false))?.BlockSequence == recovery.NextBlockSequence,
                "the first delivered block after recovery is the agreed future boundary");
        }

        await ClassroomSynchronizationSpecifications.VerifyHealthAsync(client, recovery.Checkpoint, ct).ConfigureAwait(false);

        byte[] state = SHA256.HashData("checkpoint admitted by the test authority"u8);
        string stateHash = Convert.ToHexStringLower(state);
        string actionHash = Convert.ToHexStringLower(SHA256.HashData("verified offline actions"u8));
        var authority = new RecoveredAuthority(stateHash, actionHash);
        host.ConfigureRecoveryAuthority(authority);
        ConfigurePresentation(host, manifest.StudentId);
        try
        {
            var hello = new RecoveryHelloMessage(manifest.StudentId, manifest.SessionId, manifest.InstanceId,
                1, 7, 2, stateHash, null, Guid.NewGuid(), true);
            await ExpectStatusAsync(() => client.BeginAuthorityRecoveryAsync(hello with { ClientId = Guid.NewGuid() },
                1, 0, ct), HttpStatusCode.Forbidden).ConfigureAwait(false);
            Check.That(await client.BeginAuthorityRecoveryAsync(hello, 1, 0, ct).ConfigureAwait(false) is null,
                "the host awaits the referenced local continuation report");
            var report = new LocalContinuationReportMessage(hello.LocalContinuationReportId!.Value, manifest.SessionId,
                manifest.StudentId, manifest.InstanceId, "student.practice", stateHash, null, 1, 0,
                WaveformBlockAssembler.BlockDurationNs, [], 0, 2, stateHash, actionHash, [], ContinuityReportIntegrityState.PendingVerification);
            RecoveryResyncPlan plan = await client.SendContinuationReportAsync(report, ct).ConfigureAwait(false);
            Check.That(plan.LocalPredictionDecision == LocalPredictionDecision.CommitVerified &&
                plan.NewAuthorityEpoch == authority.AuthorityEpoch && plan.HostStateSha256 == stateHash &&
                (await client.SendContinuationReportAsync(report, ct).ConfigureAwait(false)) == plan &&
                authority.Replays == 1, "a retry returns the same canonical plan and does not repeat trusted replay");

            ClassroomLearnerResumeSnapshot snapshot = await client.ReadRecoverySnapshotAsync(manifest, plan, ct).ConfigureAwait(false);
            Check.That(snapshot.Rendered is not null && snapshot.Manifest.StudentId == manifest.StudentId && snapshot.Manifest.NextBlockSequence == next,
                "digest-bound resume data contains the student projection at its actual checkpoint");
            int preroll = (int)((plan.RelockSimTimeNs - plan.PrerollFromSimTimeNs) / WaveformBlockAssembler.BlockDurationNs);
            ulong first = snapshot.Manifest.NextBlockSequence;
            await using var stream = await client.SubscribeAsync(snapshot.Manifest, first, ct).ConfigureAwait(false);
            for (int i = 0; i < preroll; i++)
            {
                publish(next++);
                _ = await stream.ReceiveAsync(ct).ConfigureAwait(false);
            }
            WaveformBlockReplayResult evidence = stream.ReadBufferedBlocks(first, preroll + 1);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (int i = 0; i < preroll; i++) { digest.AppendData(evidence.Blocks[i].RawEnvelope); }
            var ack = new RecoveryPreparedAcknowledgement(plan.PlanId, plan.NewAuthorityEpoch, plan.NewStreamEpoch,
                plan.RelockSimTimeNs, plan.HostStateSha256, Convert.ToHexStringLower(digest.GetHashAndReset()),
                plan.ResumeSnapshotSha256, plan.PrerollFromSimTimeNs);
            await ExpectStatusAsync(() => client.SendRecoveryPreparedAsync(ack with { PrerollContentSha256 = stateHash }, ct),
                HttpStatusCode.Conflict).ConfigureAwait(false);
            Check.That(await client.SendRecoveryPreparedAsync(ack, ct).ConfigureAwait(false) is null,
                "the host registers a prepared acknowledgement before publishing the first authoritative recovery block");
            publish(next++);
            _ = await stream.ReceiveAsync(ct).ConfigureAwait(false);
            var firstAuthoritative = stream.ReadBufferedBlocks(first + (ulong)preroll, 1).Blocks[0];
            RecoveryCommitCommand? commit = await client.SendRecoveryPreparedAsync(ack, ct).ConfigureAwait(false);
            Check.That(commit?.FirstAuthoritativeBlockSha256 == firstAuthoritative.ContentSha256 &&
                await client.SendRecoveryPreparedAsync(ack, ct).ConfigureAwait(false) == commit,
                "the idempotent commit binds the exact authoritative block and checked preroll");
        }
        finally { host.ConfigureRecoveryAuthority(null); }
    }

    private static void ConfigurePresentation(ClassroomHost host, Guid student)
    {
        var reference = new { id = "Classroom.View", version = "1.0.0", content_sha256 = new string('a', 64) };
        var plan = JsonSerializer.SerializeToElement(new
        {
            schema_id = "ResolvedPresentationPlan@1",
            plan_revision = "1",
            surface_kind = "ECG12StudyView",
            skin_ref = (object?)null,
            layout_ref = reference,
            theme_ref = reference,
            interaction_profile_ref = reference,
            institution_brand_pack_ref = reference,
            viewport_breakpoint_id = "Desktop",
            sweep_group_plan_refs = new[] { reference },
            calibration_glyph_plan_refs = new[] { reference },
            action_projection_refs = Array.Empty<object>(),
            home_softkey_plan_ref = (object?)null,
            system_view_command_bar_plan_ref = reference,
            therapy_composition = (object?)null,
            therapy_presentation_ref = (object?)null,
            parameter_color_binding_plan_ref = reference,
            content_sha256 = new string('b', 64)
        });
        ClassroomTelemetrySnapshot snapshot = host.CurrentTelemetrySnapshot();
        byte[] projection = JsonSerializer.SerializeToUtf8Bytes(new
        {
            projections = snapshot.Projections,
            control = snapshot.Control,
            activeConfiguration = snapshot.ActiveConfiguration
        }, JsonSerializerOptions.Web);
        byte[] numerics = JsonSerializer.SerializeToUtf8Bytes(snapshot.Numerics);
        host.ConfigureRecoveryPresentation(student, plan, projection, numerics);
    }

    private static async Task ExpectStatusAsync(Func<Task> operation, HttpStatusCode expected)
    {
        try { await operation().ConfigureAwait(false); }
        catch (HttpRequestException error) when (error.StatusCode == expected) { return; }
        throw new InvalidOperationException($"Expected HTTP status {expected}.");
    }

    private sealed class RecoveredAuthority(string state, string actions) : IClassroomRecoveryAuthority
    {
        public ulong AuthorityEpoch => 2;
        internal int Replays { get; private set; }
        public ClassroomAuthorityCheckpoint ReadCheckpoint(Guid studentId) => new(4, 9, 2, state);
        public ContinuityReportVerification Replay(RecoveryHelloMessage hello, LocalContinuationReportMessage report, CancellationToken cancellationToken)
        {
            Replays++;
            return new ContinuityReportVerification(report.ReportId, state, null, state, actions, 9,
                ContinuityReportVerificationResult.Verified, null);
        }
    }
}
