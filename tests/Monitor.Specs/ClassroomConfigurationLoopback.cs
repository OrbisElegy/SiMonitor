// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Text.Json;
using Monitor.Network;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static partial class ClassroomControlSpecifications
{
    private static async Task TeachingConfigurationChangesTheTransmittedWaveform()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        CancellationToken ct = deadline.Token;
        await using var certificates = await ClassroomCertificateFixture.CreateAsync(ct).ConfigureAwait(false);
        using var leaf = ClassroomCertificate.Load(certificates.LeafPfxPath, null, null);
        using var identity = ClassroomNetworkSpecifications.AuthenticateTeacher(Path.GetDirectoryName(certificates.LeafPfxPath)!);
        var session = Guid.NewGuid();
        var instance = Guid.NewGuid();
        using var admission = new ClassroomAdmission(session, instance);
        var ring = WaveformBlockRing.Start(session, instance, 1, 1, 0, 0, WaveformBlockRing.HostRetentionBlockCount);
        await using var host = await ClassroomHost.StartAsync(IPAddress.Loopback, 0, "127.0.0.1", leaf, ring, admission, identity,
            certificates.Root, ct).ConfigureAwait(false);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task producer = host.RunIllustrationAsync(stop.Token);
        try
        {
            var learnerId = Guid.NewGuid();
            using var student = await ClassroomStudentClient.JoinAsync(host.BaseAddress, admission.IssueInvitation(learnerId), certificates.Root, ct)
                .ConfigureAwait(false);
            var manifest = await student.GetSessionAsync(ct).ConfigureAwait(false);
            await using var wave = await student.SubscribeAsync(manifest, cancellationToken: ct).ConfigureAwait(false);
            using var settings = JsonDocument.Parse("""{"schema_id":"ClassroomDisplaySettings@1","layout_id":"FourRows","ecg12_theme_id":"PaperGridBlack"}""");
            var content = new ClassroomConfigurationContent(["nibp.measurement.start"], 120, new() { ["display.settings"] = settings.RootElement });
            using var observer = new ClassroomConfigurationObserver(student);
            const long effective = 1_400_000_000;
            var offer = host.ScheduleConfiguration(Guid.NewGuid(), 1, effective, content, [learnerId], 1,
                requireTeacherConfirmation: true);
            var config = await student.GetConfigurationAsync(ct).ConfigureAwait(false);
            Check.That(config.Changes.Single().Content!.ComputeHash() == offer.PublicDiffSha256, "the learner received the exact candidate");
            await observer.PollAsync(ct).ConfigureAwait(false);
            Check.That(host.ReadConfigurationAcks(offer.ChangeId)[learnerId].Stage == ClassroomAckStage.Prepared,
                "the headless observer confirms verified content for its own required seat");
            host.ConfirmConfiguration(offer.ChangeId);
            host.StartClass();

            bool samplesChanged = false;
            var original = PhysiologyIllustrationSource.CreateForSession(session, instance);
            for (ulong sequence = 0; sequence < 18; sequence++)
            {
                WaveformEnvelope block = (await wave.ReceiveAsync(ct).ConfigureAwait(false))!;
                long start = (long)sequence * WaveformBlockAssembler.BlockDurationNs;
                var baseline = WaveformEnvelopeCodec.Decode(original.AdvanceTo(start + WaveformBlockAssembler.BlockDurationNs,
                    250, 1, 256, ct).Single());
                bool same = block.Planes[0].Samples.SequenceEqual(baseline.Planes[0].Samples);
                Check.That(block.BlockSequence == sequence && block.StartSimTimeNs == start &&
                    block.ConfigurationRevision == (start < effective ? 1UL : 2UL), "MWS1 switches revision at the agreed block");
                if (start < effective) { Check.That(same, "the schedule cannot alter previously produced samples"); }
                else { samplesChanged |= !same; }
                await observer.ObserveDeliveredAsync(block, start, ct).ConfigureAwait(false);
            }
            Check.That(samplesChanged, "the accepted heart-rate change reaches the physiological generator");
            var changedDevice = await student.GetControlSnapshotAsync(ct).ConfigureAwait(false);
            Check.That(changedDevice.DeviceState.GetProperty("layout_id").GetString() == "FourRows" &&
                changedDevice.DeviceState.GetProperty("ecg12_theme_id").GetString() == "PaperGridBlack", "display settings agree with the new physiology revision");
            Check.That(host.ReadConfigurationAcks(offer.ChangeId)[learnerId].Stage == ClassroomAckStage.Applied,
                "teacher receives the acknowledged boundary and exact first block");
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            try { await producer.ConfigureAwait(false); } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }
}
