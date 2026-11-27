// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monitor.Application.Measurements;
using Monitor.Network;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ClassroomBranchSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ForkRetainsWarmAcquisitionEstimates), ForkRetainsWarmAcquisitionEstimates),
        new(nameof(TeachingBranchClonesAtBoundaryAndRequiresExplicitRejoin),
            () => TeachingBranchClonesAtBoundaryAndRequiresExplicitRejoin().GetAwaiter().GetResult())
    ];

    private static void ForkRetainsWarmAcquisitionEstimates()
    {
        var shared = Guid.NewGuid();
        var instance = Guid.NewGuid();
        var source = PhysiologyIllustrationSource.CreateForSession(Guid.NewGuid(), shared);
        var measurements = LiveWaveformMeasurements.CreateIllustration();
        for (long time = WaveformBlockAssembler.BlockDurationNs; time <= 12_000_000_000; time += WaveformBlockAssembler.BlockDurationNs)
        {
            byte[] raw = source.AdvanceTo(time, 250, 1, 256, CancellationToken.None).Single();
            measurements.Consume(raw, out _, out _);
        }
        var detached = measurements.ForkForInstance(shared, instance);
        byte[] next = source.AdvanceTo(12_200_000_000, 250, 1, 256, CancellationToken.None).Single();
        var original = measurements.Consume(next, out var originalBeats, out _);
        byte[] released = WaveformEnvelopeCodec.EncodeRaw(WaveformEnvelopeCodec.Decode(next) with { InstanceId = instance });
        var forked = detached.Consume(released, out var privateBeats, out _);
        Check.That(original.HeartRate.Status == WaveformMeasurementStatus.Valid &&
            JsonElement.DeepEquals(JsonSerializer.SerializeToElement(original), JsonSerializer.SerializeToElement(forked)) &&
            originalBeats.SequenceEqual(privateBeats), "a fork retains the warmed ECG, pressure, respiration and optical estimates");
    }

    private static async Task TeachingBranchClonesAtBoundaryAndRequiresExplicitRejoin()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        CancellationToken ct = deadline.Token;
        await using var certificates = await ClassroomCertificateFixture.CreateAsync(ct).ConfigureAwait(false);
        using var leaf = ClassroomCertificate.Load(certificates.LeafPfxPath, null, null);
        using var identity = ClassroomNetworkSpecifications.AuthenticateTeacher(Path.GetDirectoryName(certificates.LeafPfxPath)!);
        var session = Guid.NewGuid();
        var shared = Guid.NewGuid();
        using var admission = new ClassroomAdmission(session, shared);
        var ring = WaveformBlockRing.Start(session, shared, 1, 1, 0, 0, WaveformBlockRing.HostRetentionBlockCount);
        await using var host = await ClassroomHost.StartAsync(IPAddress.Loopback, 0, "127.0.0.1", leaf,
            ring, admission, identity, certificates.Root, ct).ConfigureAwait(false);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task producer = host.RunIllustrationAsync(PhysiologyIllustrationConfiguration.Flutter(2), stop.Token);
        try
        {
            var studentId = Guid.NewGuid();
            using var student = await ClassroomStudentClient.JoinAsync(host.BaseAddress, admission.IssueInvitation(studentId),
                certificates.Root, ct).ConfigureAwait(false);
            using var observer = await ClassroomStudentClient.JoinAsync(host.BaseAddress, admission.IssueInvitation(Guid.NewGuid()),
                certificates.Root, ct).ConfigureAwait(false);
            host.StartClass();
            while ((await observer.GetSessionAsync(ct).ConfigureAwait(false)).NextBlockSequence < 2)
            { await Task.Delay(25, ct).ConfigureAwait(false); }

            foreach (ClassroomBranchDisposition disposition in new[] { ClassroomBranchDisposition.Archive, ClassroomBranchDisposition.Discard })
            {
                ClassroomBranchAssignment scheduled = host.SchedulePrivateBranch(studentId);
                await WaitForAsync(host, student, "Detached", ct).ConfigureAwait(false);
                await ExpectStatusAsync(() => student.GetControlSnapshotAsync(ct), HttpStatusCode.Conflict).ConfigureAwait(false);
                using var fork = await student.ConnectAssignedBranchAsync(ct).ConfigureAwait(false);
                ClassroomManifest a = await fork.GetSessionAsync(ct).ConfigureAwait(false);
                ClassroomManifest b = await observer.GetSessionAsync(ct).ConfigureAwait(false);
                Check.That(a.SessionId == session && a.InstanceId == scheduled.InstanceId && b.InstanceId == shared &&
                    a.StudentId == studentId && a.NextBlockStartSimTimeNs >= scheduled.EffectiveSimTimeNs,
                    "the branch must own a distinct instance at the scheduled verified boundary");

                ulong retained = (ulong)(scheduled.EffectiveSimTimeNs / WaveformBlockAssembler.BlockDurationNs) - 1;
                await using var privateStream = await fork.SubscribeAsync(a, retained, ct).ConfigureAwait(false);
                await using var sharedStream = await observer.SubscribeAsync(b, retained, ct).ConfigureAwait(false);
                WaveformEnvelope? privateBlock = await privateStream.ReceiveAsync(ct).ConfigureAwait(false);
                WaveformEnvelope? sharedBlock = await sharedStream.ReceiveAsync(ct).ConfigureAwait(false);
                Check.That(privateBlock is not null && sharedBlock is not null && privateBlock.InstanceId == a.InstanceId &&
                    WaveformEnvelopeCodec.EncodeRaw(privateBlock with { InstanceId = shared }).SequenceEqual(WaveformEnvelopeCodec.EncodeRaw(sharedBlock)),
                    "private preroll preserves the actual samples and frontier with a new scoped content hash");

                const string action = "therapy.defib.energy.set";
                var projection = await fork.GetActionProjectionAsync(action, ct).ConfigureAwait(false);
                var id = Guid.NewGuid();
                using var local = JsonDocument.Parse("""{"energy":{"unit_code":"JOULE","mantissa_i64":"200","exponent10":0}}""");
                var intent = new ClassroomActionIntent("LearnerActionIntent@1", id, $"action.{id:N}", a.SessionId, studentId,
                    a.InstanceId, action, projection.ProjectionRevision, 5, "classroom.energy", null, local.RootElement.Clone());
                var changed = await fork.SubmitActionAsync(intent, ct).ConfigureAwait(false);
                Check.That(changed.Accepted && (await fork.GetControlSnapshotAsync(ct).ConfigureAwait(false)).DeviceState
                    .GetProperty("defib_energy_joules").GetInt32() == 200 && (await observer.GetControlSnapshotAsync(ct).ConfigureAwait(false))
                    .DeviceState.GetProperty("defib_energy_joules").GetInt32() != 200,
                    "one student's private device actions never mutate the shared patient");

                if (disposition == ClassroomBranchDisposition.Archive)
                {
                    ClassroomControlSnapshot control = host.GetPrivateControlSnapshot(studentId);
                    long effective = (await fork.GetSessionAsync(ct).ConfigureAwait(false)).NextBlockStartSimTimeNs +
                        8L * WaveformBlockAssembler.BlockDurationNs;
                    host.SchedulePrivateConfiguration(studentId, Guid.NewGuid(), control.ConfigurationRevision, effective,
                        new ClassroomConfigurationContent(control.AllowedActionIds, 120, []), [], 0);
                    bool diverged = false;
                    do
                    {
                        privateBlock = await privateStream.ReceiveAsync(ct).ConfigureAwait(false);
                        sharedBlock = await sharedStream.ReceiveAsync(ct).ConfigureAwait(false);
                        Check.That(privateBlock is not null && sharedBlock is not null && privateBlock.BlockSequence == sharedBlock.BlockSequence,
                            "the two actual generators retain their respective acquisition clocks");
                        if (privateBlock!.StartSimTimeNs >= effective)
                        {
                            Check.That(privateBlock.ConfigurationRevision > sharedBlock!.ConfigurationRevision,
                                "the new private configuration revision does not affect the shared stream");
                            diverged = privateBlock.Planes.Any(p => !p.Samples.SequenceEqual(
                                sharedBlock.Planes.Single(q => q.ChannelId == p.ChannelId).Samples));
                        }
                    } while (!diverged && privateBlock.StartSimTimeNs < effective + 2_000_000_000);
                    Check.That(diverged, "the cloned patient's revised heart rate changes actual private samples");
                }
                host.ScheduleSharedReturn(studentId, disposition);
                await WaitForAsync(host, student, "Shared", ct).ConfigureAwait(false);
                await ExpectStatusAsync(() => fork.GetSessionAsync(ct), HttpStatusCode.NotFound).ConfigureAwait(false);
                await ExpectStatusAsync(() => student.SynchronizeClockAsync(ct), HttpStatusCode.PreconditionFailed).ConfigureAwait(false);
                ClassroomManifest returned = await student.GetSessionAsync(ct).ConfigureAwait(false);
                await student.NegotiateAsync(returned, null, ct).ConfigureAwait(false);
                await ExpectStatusAsync(async () =>
                {
                    await using var denied = await student.SubscribeAsync(returned, cancellationToken: ct).ConfigureAwait(false);
                }, HttpStatusCode.PreconditionFailed).ConfigureAwait(false);
                var rejoined = await student.JoinAtFutureBoundaryAsync(returned, WaveformRecoveryPlanner.MinimumJoinLeadBlockCount, ct).ConfigureAwait(false);
                await rejoined.Subscription.DisposeAsync().ConfigureAwait(false);
                Check.That(returned.InstanceId == shared && (await student.GetControlSnapshotAsync(ct).ConfigureAwait(false))
                    .DeviceState.GetProperty("defib_energy_joules").GetInt32() != 200, "a rejoining endpoint uses shared authority again");
                ClassroomBranchArchive? archive = host.ReadArchivedBranch(scheduled.InstanceId);
                if (disposition == ClassroomBranchDisposition.Archive)
                {
                    Check.That(archive is not null && archive.StudentId == studentId && archive.Patient.Assembler.InstanceId == a.InstanceId &&
                        archive.RetainedWaveforms.NextBlockStartSimTimeNs >= scheduled.EffectiveSimTimeNs,
                        "an archived private branch retains its encrypted patient checkpoint and verified evidence");
                    long next = archive!.Patient.Assembler.NextBlockStartSimTimeNs + WaveformBlockAssembler.BlockDurationNs;
                    var restored = PhysiologyWaveformGroup.Restore(archive.Patient);
                    WaveformEnvelope continued = WaveformEnvelopeCodec.Decode(restored.AdvanceTo(next, 250, 1, 256, ct).Single());
                    Check.That(continued.InstanceId == scheduled.InstanceId && archive.Measurements.SampleTimeNs ==
                        archive.Patient.Assembler.NextBlockStartSimTimeNs && archive.Patient.Channels.All(c =>
                        c.Generator.Timeline.Plan.ConductionPattern == AvConductionPattern.AtrialFlutterIllustration),
                        "the archived alternate rhythm resumes after its final measured waveform block");

                }
                else { Check.That(archive is null, "a discarded branch leaves no replayable patient archive"); }
            }
            await RemoveDetachedSeatAsync(host, student, observer, studentId, identity.DatabasePath, ct).ConfigureAwait(false);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            try { await producer.ConfigureAwait(false); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    private static async Task RemoveDetachedSeatAsync(ClassroomHost host, ClassroomStudentClient student,
        ClassroomStudentClient observer, Guid studentId, string databasePath, CancellationToken ct)
    {
        ClassroomBranchAssignment branch = host.SchedulePrivateBranch(studentId);
        await WaitForAsync(host, student, "Detached", ct).ConfigureAwait(false);
        using var fork = await student.ConnectAssignedBranchAsync(ct).ConfigureAwait(false);
        var manifest = await fork.GetSessionAsync(ct).ConfigureAwait(false);
        await using var stream = await fork.SubscribeAsync(manifest, cancellationToken: ct).ConfigureAwait(false);
        host.SchedulePlayback(running: false);
        while ((await observer.GetTelemetryAsync(ct).ConfigureAwait(false)).Timeline?.RunState != "Paused")
        { await Task.Delay(40, ct).ConfigureAwait(false); }

        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "CREATE TRIGGER simulate_archive_failure BEFORE INSERT ON classroom_branch_archives BEGIN SELECT RAISE(ABORT,'full'); END";
        command.ExecuteNonQuery();
        bool failedToSave = false;
        try { host.RemoveMember(studentId); }
        catch (InvalidOperationException error) when (error.InnerException is SqliteException) { failedToSave = true; }
        Check.That(failedToSave && host.GetBranchFailure(studentId) is not null && host.Members.All(m => m.ClientId != studentId),
            "removal must revoke the seat even if archiving fails");
        await ExpectStatusAsync(() => student.GetSessionAsync(ct), HttpStatusCode.Unauthorized).ConfigureAwait(false);
        await ExpectStatusAsync(() => fork.GetSessionAsync(ct), HttpStatusCode.NotFound).ConfigureAwait(false);
        try { while (await stream.ReceiveAsync(ct).ConfigureAwait(false) is not null) { } }
        catch (Exception error) when (error is WebSocketException or IOException) { }

        command.CommandText = "DROP TRIGGER simulate_archive_failure";
        command.ExecuteNonQuery();
        host.RemoveMember(studentId);
        Check.That(host.ReadArchivedBranch(branch.InstanceId)?.StudentId == studentId &&
            (await observer.GetSessionAsync(ct).ConfigureAwait(false)).InstanceId == branch.SharedInstanceId,
            "retry saves the frozen private archive while the shared classroom remains paused");
    }

    private static async Task WaitForAsync(ClassroomHost host, ClassroomStudentClient student, string expected, CancellationToken ct)
    {
        while (true)
        {
            ClassroomBranchAssignment assignment = await student.GetBranchAssignmentAsync(ct).ConfigureAwait(false);
            if (assignment.State == expected) { return; }
            if (assignment.Reason is not null) { throw new InvalidOperationException(assignment.Reason + ": " + host.GetBranchFailure(assignment.StudentId)); }
            await Task.Delay(30, ct).ConfigureAwait(false);
        }
    }

    private static async Task ExpectStatusAsync(Func<Task> operation, HttpStatusCode status)
    {
        try { await operation().ConfigureAwait(false); }
        catch (HttpRequestException error) when (error.StatusCode == status) { return; }
        throw new InvalidOperationException($"Expected branch route rejection {(int)status}.");
    }
}
