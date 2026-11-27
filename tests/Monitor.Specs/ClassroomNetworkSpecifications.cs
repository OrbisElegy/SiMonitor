// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using Monitor.Network;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class ClassroomNetworkSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(TeacherAndStudentExchangeAuthenticatedWaveforms), TeacherAndStudentExchangeAuthenticatedWaveforms),
    ];

    private static void TeacherAndStudentExchangeAuthenticatedWaveforms() =>
        ExerciseTransportAsync().GetAwaiter().GetResult();

    private static async Task ExerciseTransportAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        CancellationToken ct = timeout.Token;
        Guid sessionId = ClassroomContinuitySpecifications.SessionId, instanceId = ClassroomContinuitySpecifications.InstanceId, channelId = Guid.NewGuid();
        await using var certificates = await ClassroomCertificateFixture.CreateAsync(ct).ConfigureAwait(false);
        var trustedRoot = certificates.Root;
        using var certificate = ClassroomCertificate.Load(certificates.LeafPfxPath, keyPath: null, password: null);
        using var identity = AuthenticateTeacher(Path.GetDirectoryName(certificates.LeafPfxPath)!);
        using var admission = new ClassroomAdmission(sessionId, instanceId);
        var ring = WaveformBlockRing.Start(sessionId, instanceId, 1, 2, 0, 0, WaveformBlockRing.HostRetentionBlockCount);
        try
        {
            await using var unexpected = await ClassroomHost.StartAsync(IPAddress.Loopback, 0, "wrong.invalid", certificate,
                ring, admission, identity, trustedRoot, ct).ConfigureAwait(false);
            Check.That(false, "the host cannot listen using a certificate for a different name");
        }
        catch (CryptographicException) { }
        await using var host = await ClassroomHost.StartAsync(IPAddress.Loopback, 0, IPAddress.Loopback.ToString(), certificate,
            ring, admission, identity, trustedRoot, ct).ConfigureAwait(false);
        Guid firstId = ClassroomContinuitySpecifications.StudentId, secondId = Guid.NewGuid();
        string firstInvitation = admission.IssueInvitation(firstId);
        bool untrusted = false;
        try
        {
            using var unexpected = await ClassroomStudentClient.JoinAsync(host.BaseAddress, firstInvitation, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException error) when (error.HttpRequestError == HttpRequestError.SecureConnectionError) { untrusted = true; }
        Check.That(untrusted, "the system trust store rejects an untrusted classroom certificate");

        using var first = await ClassroomStudentClient.JoinAsync(host.BaseAddress, firstInvitation, trustedRoot, ct).ConfigureAwait(false);
        try
        {
            using var unexpected = await ClassroomStudentClient.JoinAsync(host.BaseAddress, firstInvitation, trustedRoot, ct).ConfigureAwait(false);
            Check.That(false, "a used invitation cannot be redeemed again");
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.Unauthorized) { }
        using var second = await ClassroomStudentClient.JoinAsync(host.BaseAddress, admission.IssueInvitation(secondId), trustedRoot, ct)
            .ConfigureAwait(false);
        ClassroomClockSample clock = await first.SynchronizeClockAsync(ct).ConfigureAwait(false);
        Check.That(clock.ClientReceiveNs >= clock.ClientSendNs && clock.HostSendNs >= clock.HostReceiveNs && clock.RoundTripNs >= 0,
            "the classroom returns paired monotonic clock samples");

        void Publish(ulong sequence)
        {
            WaveformPlane plane = new(channelId, 5, 1, sequence, 1, 1, 0, 1, WaveformQualityEncoding.None, [(short)sequence], []);
            var block = new WaveformEnvelope(sessionId, instanceId, 1, 2, sequence, 1,
                (long)sequence * WaveformBlockAssembler.BlockDurationNs, WaveformBlockAssembler.BlockDurationNs, [plane]);
            host.Publish(WaveformEnvelopeCodec.EncodeRaw(block));
        }

        for (ulong sequence = 0; sequence < 64; sequence++) { Publish(sequence); }
        ClassroomManifest firstManifest = await first.GetSessionAsync(ct).ConfigureAwait(false);
        ClassroomManifest secondManifest = await second.GetSessionAsync(ct).ConfigureAwait(false);
        Check.That(firstManifest.StudentId == firstId && secondManifest.StudentId == secondId &&
            firstManifest is { OldestBlockSequence: 0, NextBlockSequence: 64, StreamEpoch: 2 } &&
            secondManifest.SessionId == sessionId && secondManifest.InstanceId == instanceId,
            "the manifest binds credentials to their assigned observer and stream");
        await ClassroomSessionSpecifications.VerifySessionAsync(host, first, second, firstManifest, ct).ConfigureAwait(false);
        await ClassroomArtifactSpecifications.ExerciseTransportAsync(host, first, second, firstManifest, ct).ConfigureAwait(false);
        await ClassroomDiscoverySpecifications.ExercisePairingAsync(host.BaseAddress, trustedRoot, certificate, admission, ct).ConfigureAwait(false);
        await ClassroomControlSpecifications.VerifyLoopbackAsync(host, first, second, firstManifest, ct).ConfigureAwait(false);
        await using var slow = await second.SubscribeAsync(secondManifest, cancellationToken: ct).ConfigureAwait(false);
        ulong resumeAt;
        await using (var history = await first.SubscribeAsync(firstManifest, 0, ct).ConfigureAwait(false))
        {
            bool duplicate = false;
            try { await using var unexpected = await first.SubscribeAsync(firstManifest, 0, ct).ConfigureAwait(false); }
            catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.TooManyRequests) { duplicate = true; }
            Check.That(duplicate, "a second concurrent subscription cannot impersonate a separate observer");

            Publish(64);
            for (ulong sequence = 0; sequence <= 64; sequence++)
            {
                WaveformEnvelope? block = await history.ReceiveAsync(ct).ConfigureAwait(false);
                Check.That(block?.BlockSequence == sequence && block.Planes[0].Samples[0] == (short)sequence,
                    "history extends beyond the live queue and joins new data without a duplicate or gap");
            }
            resumeAt = history.NextBlockSequence;
        }

        Publish(resumeAt);
        await using var resumed = await first.ResumeAsync(firstManifest, resumeAt, ct).ConfigureAwait(false);
        Check.That((await resumed.ReceiveAsync(ct).ConfigureAwait(false))?.BlockSequence == resumeAt,
            "reconnection restores the first undelivered block");
        for (ulong sequence = resumeAt + 1; sequence < 120; sequence++)
        {
            Publish(sequence);
            Check.That((await resumed.ReceiveAsync(ct).ConfigureAwait(false))?.BlockSequence == sequence,
                "the responsive observer continues while a different observer withholds acknowledgements");
        }
        bool isolated = false;
        try
        {
            // One already-sent block may be waiting in the client's socket buffer.
            _ = await slow.ReceiveAsync(ct).ConfigureAwait(false);
            _ = await slow.ReceiveAsync(ct).ConfigureAwait(false);
        }
        catch (IOException) { isolated = true; }
        catch (WebSocketException) { isolated = true; }
        Check.That(isolated, "a slow observer must explicitly resynchronize after its live outbox fills");

        await resumed.DisposeAsync().ConfigureAwait(false);
        for (ulong sequence = 120; sequence <= 300; sequence++) { Publish(sequence); }
        bool expired = false;
        try { await using var unexpected = await first.SubscribeAsync(firstManifest, 0, ct).ConfigureAwait(false); }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.Conflict) { expired = true; }
        Check.That(expired, "the server rejects a cursor evicted since the manifest was fetched");

        await ClassroomContinuitySpecifications.VerifyLoopbackAsync(host, first, second, ct).ConfigureAwait(false);
        await ClassroomRecoverySpecifications.VerifyLoopbackAsync(host, first, firstManifest, Publish, ct).ConfigureAwait(false);
        var fresh = await first.GetSessionAsync(ct).ConfigureAwait(false);
        await using var idle = await first.SubscribeAsync(fresh, cancellationToken: ct).ConfigureAwait(false);
        Task<WaveformEnvelope?> pending = idle.ReceiveAsync(ct).AsTask();
        await host.DisposeAsync().AsTask().WaitAsync(ct).ConfigureAwait(false);
        try { Check.That(await pending.ConfigureAwait(false) is null, "stopping the host releases the waiting reader"); }
        catch (IOException) { }
        catch (WebSocketException) { }
        Check.That(pending.IsCompleted, "an idle WebSocket must not hold the classroom host open");
    }

    internal static ClassroomBootstrap AuthenticateTeacher(string directory)
    {
        const string username = "network-admin";
        const string password = "test-only classroom passphrase";
        string database = Path.Combine(directory, "classroom.db");
        string blocklist = Path.Combine(directory, "blocked.sha256");
        byte[] key = RandomNumberGenerator.GetBytes(32);
        try
        {
            try
            {
                using var unexpected = ClassroomBootstrap.AuthenticateHost(database, key, username, password);
                Check.That(false, "host credentials are unavailable before bootstrap completes");
            }
            catch (InvalidOperationException error) when (error.Message == "identity.bootstrap.required") { }
            File.WriteAllText(blocklist, Convert.ToHexString(SHA256.HashData("known compromised passphrase"u8)));
            ClassroomBootstrap.Initialize(database, key, username, "test-only initial credential", password, blocklist);
            return ClassroomBootstrap.AuthenticateHost(database, key, username, password);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
