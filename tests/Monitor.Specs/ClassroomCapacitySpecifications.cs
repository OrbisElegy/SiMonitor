// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using Monitor.Network;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class ClassroomCapacitySpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(ThirtySixReceiversShareAnIdenticalRetainedBlock), () => ThirtySixReceiversShareAnIdenticalRetainedBlock().GetAwaiter().GetResult()),
    ];

    private static async Task ThirtySixReceiversShareAnIdenticalRetainedBlock()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        CancellationToken ct = timeout.Token;
        await using var pki = await ClassroomCertificateFixture.CreateAsync(ct).ConfigureAwait(false);
        using var key = ClassroomCertificate.Load(pki.LeafPfxPath, null, null);
        using var teacher = ClassroomNetworkSpecifications.AuthenticateTeacher(Path.GetDirectoryName(pki.LeafPfxPath)!);
        var session = Guid.NewGuid();
        var instance = Guid.NewGuid();
        using var admission = new ClassroomAdmission(session, instance);
        var ring = WaveformBlockRing.Start(session, instance, 1, 1, 0, 0, WaveformBlockRing.HostRetentionBlockCount);
        await using var host = await ClassroomHost.StartAsync(IPAddress.Loopback, 0, "localhost", key, ring, admission, teacher, pki.Root, ct).ConfigureAwait(false);
        // Four 1 kHz int16 planes in one 200 ms block exceed the optional compression threshold.
        var planes = Enumerable.Range(1, 4).Select(index => new WaveformPlane(new Guid(index, 0, 0, new byte[8]), 1000, 1, 0, 1, 1,
            0, 1, WaveformQualityEncoding.None, new short[200], [])).ToArray();
        WaveformEnvelope expected = new(session, instance, 1, 1, 0, 1, 0, WaveformBlockAssembler.BlockDurationNs, planes);
        byte[] raw = WaveformEnvelopeCodec.EncodeRaw(expected);
        string sharedHash = Convert.ToHexStringLower(raw.AsSpan(WaveformEnvelopeCodec.ContentSha256Offset, 32));
        host.Publish(raw);

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int subscribed = 0;
        string[] invitations = Enumerable.Range(0, 36).Select(_ => admission.IssueInvitation(Guid.NewGuid())).ToArray();
        async Task<string> ReceiveAsync(string invitation, int index)
        {
            using var client = await ClassroomStudentClient.JoinAsync(host.BaseAddress, invitation, pki.Root, ct).ConfigureAwait(false);
            var manifest = await client.GetSessionAsync(ct).ConfigureAwait(false);
            if (index == 0) { await client.NegotiateAsync(manifest, null, allowLz4: false, ct).ConfigureAwait(false); }
            await using var sub = await client.SubscribeAsync(manifest, 0, ct).ConfigureAwait(false);
            if (Interlocked.Increment(ref subscribed) == 36) { release.TrySetResult(); }
            var block = await sub.ReceiveAsync(ct).ConfigureAwait(false);
            Check.That(block is { BlockSequence: 0 } && block.Planes.Count == 4 && sub.LastBlockWasCompressed == (index != 0),
                "raw and negotiated compressed students receive the same content through their own connections");
            await release.Task.WaitAsync(ct).ConfigureAwait(false);
            return sub.ReadBufferedBlocks(0, 1).Blocks.Single().ContentSha256;
        }
        string[] hashes = await Task.WhenAll(invitations.Select(ReceiveAsync)).ConfigureAwait(false);
        Check.That(host.Members.Count == 36 && hashes.Distinct(StringComparer.Ordinal).Count() == 1 &&
            hashes[0] == sharedHash, "all authorized receivers retain identical canonical sample bytes");
        try
        {
            using var excess = await ClassroomStudentClient.JoinAsync(host.BaseAddress, admission.IssueInvitation(Guid.NewGuid()), pki.Root, ct).ConfigureAwait(false);
            throw new InvalidOperationException("Room enrollment exceeded its announced capacity.");
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.TooManyRequests) { }
    }
}
