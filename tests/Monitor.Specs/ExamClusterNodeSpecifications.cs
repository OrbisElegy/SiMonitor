// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Monitor.Infrastructure.Identity;
using Monitor.Network;

namespace Monitor.Specs;

internal static class ExamClusterNodeSpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(LostExamReceiptsReconcileAgainstTheNewFence), () => LostExamReceiptsReconcileAgainstTheNewFence().GetAwaiter().GetResult()),
        new(nameof(StandbyCanFenceAReachablePrimaryThatLostItsLease), () => StandbyCanFenceAReachablePrimaryThatLostItsLease().GetAwaiter().GetResult()),
        new(nameof(ColdRecoveryReconcilesASingleLostDurableReceipt), () => ColdRecoveryReconcilesASingleLostDurableReceipt().GetAwaiter().GetResult()),
        new(nameof(RestoredExamQualificationAndManualFencesNeedIndependentProof), () => RestoredExamQualificationAndManualFencesNeedIndependentProof().GetAwaiter().GetResult())
    ];

    private static async Task LostExamReceiptsReconcileAgainstTheNewFence()
    {
        foreach (bool reachedDisk in new[] { true, false })
        {
            using var f = new Fixture();
            using var node = f.StartNode(true);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task running = node.RunAsync(stop.Token);
            try
            {
                await UntilAsync(() => node.GetStatus().LeaseValid, stop.Token);
                f.OnReplicate = entry =>
                {
                    f.WitnessOffline = true;
                    f.PeerOffline = true;
                    // Deliver either a delayed durable ACK or a transport failure after
                    // the old lease's last instant; neither may acknowledge the command.
                    f.Clock.Advance(3001);
                    if (!reachedDisk) { throw new HttpRequestException("Disconnected before durable replication."); }
                    return f.OtherStore.Apply(entry);
                };
                var answer = new ExamReplicaCommand(Guid.NewGuid(), "Answer", [1, 3, 5]);
                try
                {
                    await node.WriteAsync(1, 1, answer, stop.Token);
                    throw new InvalidDataException("The old lease acknowledged a write.");
                }
                catch (Exception error) when (error is InvalidOperationException or HttpRequestException) { }
                var original = f.Local.Pending!;
                Check.That(f.Local.Head == ExamReplicaCursor.Empty && original.Command.RequestId == answer.RequestId,
                    "late network outcomes leave the original row pending on the primary");
                f.PeerRole = "Recovering"; // The standby restarted and lost its runtime qualification.
                f.PeerOffline = false;
                f.WitnessOffline = false;
                await UntilAsync(() => node.GetStatus() is { AuthorityEpoch: 2, LeaseValid: true } && f.Local.Pending is null, stop.Token);
                Check.That(reachedDisk ? f.Local.Head == original.Cursor : f.Local.Head == ExamReplicaCursor.Empty,
                    "exact peer evidence recovers its durable ACK and an empty peer authorizes dropping only an old prepare");
                f.OnReplicate = f.OtherStore.Apply;
                f.PeerRole = "Standby";
                var retried = await node.WriteAsync(2, 2, answer, stop.Token);
                Check.That(retried.Sequence == 1 && retried == f.OtherStore.Head && (retried == original.Cursor) == reachedDisk,
                    "a retried request keeps the exact replicated old row, or is committed once under the new fence");
            }
            finally { await stop.CancelAsync(); await running; }
        }
    }

    private static async Task StandbyCanFenceAReachablePrimaryThatLostItsLease()
    {
        using var f = new Fixture();
        Check.That(f.Witness.TryAcquire(f.Other, new(f.Cluster, 0, 0, 0), out _), "old primary has the first lease");
        f.PeerRole = "Primary";
        using var node = f.StartNode(false);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        Task running = node.RunAsync(stop.Token);
        try
        {
            await UntilAsync(() => node.GetStatus().Qualified, stop.Token);
            f.Clock.Advance(3001);
            await UntilAsync(() => node.GetStatus() is { AuthorityEpoch: 2, LeaseValid: true }, stop.Token);
            Check.That(f.Witness.Read().OwnerId == f.Self, "a reachable primary without a witness lease cannot veto a witnessed promotion");
        }
        finally { await stop.CancelAsync(); await running; }
    }

    private static async Task ColdRecoveryReconcilesASingleLostDurableReceipt()
    {
        foreach (bool chooseFormerPrimary in new[] { true, false })
        {
            using var f = new Fixture();
            Guid previousOwner = chooseFormerPrimary ? f.Self : f.Other;
            var primary = chooseFormerPrimary ? f.Local : f.OtherStore;
            var standby = chooseFormerPrimary ? f.OtherStore : f.Local;
            Check.That(f.Witness.TryAcquire(previousOwner, new(f.Cluster, 0, 0, 0), out _), "initial witness term");
            var firstTerm = new ExamReplicaElection(1, 1, previousOwner, false, false, null, null);
            primary.RecordElection(firstTerm);
            standby.RecordElection(firstTerm);
            var request = new ExamReplicaCommand(Guid.NewGuid(), "Answer", [12]);
            var entry = primary.Prepare(1, 1, request);
            standby.Apply(entry);
            f.Clock.Advance(3001);
            using var node = f.StartNode(chooseFormerPrimary);
            var restored = await node.RecoverAsync("operator", ExamReplicaCursor.Empty, CancellationToken.None);
            Check.That(restored.LeaseValid && restored.Head == entry.Cursor && f.Local.Find(request.RequestId)?.Cursor == entry.Cursor,
                "both physically durable copies establish the single unconfirmed receipt above the certified frontier");
        }
    }

    private static async Task RestoredExamQualificationAndManualFencesNeedIndependentProof()
    {
        using var f = new Fixture();
        Check.That(f.Witness.TryAcquire(f.Other, new(f.Cluster, 0, 0, 0), out _), "issued the preceding witness lease");
        f.Clock.Advance(3001);
        f.Local.RecordElection(new(1, 1, f.Other, true, false, null, null));
        using (var restored = f.StartNode(false))
        {
            Check.That(!restored.GetStatus().Qualified && !f.Local.Election.Qualified, "a restored eligibility flag cannot promote a stale backup");
            await ExpectRefusalAsync(() => restored.RecoverAsync("instructor", new(1, new string('1', 64)), CancellationToken.None));
            Check.That(f.Witness.Read().Revision == 1, "a mismatch with the supplied independent head cannot claim the witness");
        }
        f.Local.RecordElection(new(2, 2, f.Self, false, true, "instructor", "NetworkIsolated:R70"));
        using var fenced = f.StartNode(false);
        var active = await fenced.RecoverAsync("recovery operator", ExamReplicaCursor.Empty, CancellationToken.None);
        Check.That(active.LeaseValid && active.AuthorityEpoch == 3 && active.FencingToken == 3,
            "a restarted manual authority must obtain a new witness grant above its durable fence after verifying both heads");
    }

    private static async Task ExpectRefusalAsync(Func<Task> run)
    {
        try { await run(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected this exam operation to be refused.");
    }

    private static async Task UntilAsync(Func<bool> predicate, CancellationToken token)
    {
        while (!predicate()) { await Task.Delay(10, token); }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("exam-authority-").FullName;
        private readonly AesGcmIdentityPayloadProtector _key = new(RandomNumberGenerator.GetBytes(32));
        private readonly HttpClient _witnessClient;
        private readonly HttpClient _peerClient;
        internal readonly Guid Cluster = Guid.NewGuid();
        internal readonly Guid Self = Guid.NewGuid();
        internal readonly Guid Other = Guid.NewGuid();
        internal readonly LocalClock Clock = new();
        internal readonly ExamReplicaStore Local;
        internal readonly ExamReplicaStore OtherStore;
        internal readonly ExamWitnessStore Witness;
        internal volatile bool WitnessOffline;
        internal volatile bool PeerOffline;
        internal volatile string PeerRole = "Recovering";
        internal Func<ExamReplicaEntry, ExamReplicaCursor>? OnReplicate;

        internal Fixture()
        {
            string ownPath = Path.Combine(_directory, "own.db");
            string otherPath = Path.Combine(_directory, "other.db");
            string witnessPath = Path.Combine(_directory, "witness.db");
            ExamReplicaStore.Provision(ownPath, Cluster, Self, _key);
            ExamReplicaStore.Provision(otherPath, Cluster, Other, _key);
            ExamWitnessStore.Initialize(witnessPath, Cluster);
            Local = new(ownPath, Cluster, Self, _key);
            OtherStore = new(otherPath, Cluster, Other, _key);
            Witness = new(witnessPath, Cluster, Clock);
            _witnessClient = new(new Handler(WitnessAsync)) { BaseAddress = new("https://witness.invalid/") };
            _peerClient = new(new Handler(PeerAsync)) { BaseAddress = new("https://other.invalid/") };
        }

        internal ExamClusterNode StartNode(bool primary) => new(Cluster, Self, Other, primary, Local, _peerClient, _witnessClient, Clock);

        private async Task<HttpResponseMessage> WitnessAsync(HttpRequestMessage message, CancellationToken token)
        {
            if (WitnessOffline) { throw new HttpRequestException("Witness unavailable."); }
            if (message.Method == HttpMethod.Get) { return Reply(Witness.Read()); }
            var request = await message.Content!.ReadFromJsonAsync<ExamWitnessRequest>(ExamClusterConfiguration.Json, token);
            bool granted = message.RequestUri!.AbsolutePath.EndsWith("/acquire", StringComparison.Ordinal)
                ? Witness.TryAcquire(Self, request!, out var lease) : Witness.TryRenew(Self, request!, out lease);
            return Reply(lease, granted ? HttpStatusCode.OK : HttpStatusCode.Conflict);
        }

        private async Task<HttpResponseMessage> PeerAsync(HttpRequestMessage message, CancellationToken token)
        {
            if (PeerOffline) { throw new HttpRequestException("Peer unavailable."); }
            if (message.Method == HttpMethod.Get)
            {
                var lease = Witness.Read();
                return Reply(new ExamClusterNodeStatus(Cluster, Other, PeerRole, lease.AuthorityEpoch, lease.FencingToken,
                    PeerRole == "Primary" && lease.OwnerId == Other && lease.RemainingMs > 0, PeerRole == "Standby", OtherStore.Head, "test")
                { Pending = OtherStore.Pending?.Cursor });
            }
            var request = await message.Content!.ReadFromJsonAsync<ExamReplicationRequest>(ExamClusterConfiguration.Json, token);
            return Reply(OnReplicate!(request!.Entry));
        }

        private static HttpResponseMessage Reply<T>(T body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = JsonContent.Create(body, options: ExamClusterConfiguration.Json) };

        public void Dispose()
        {
            _peerClient.Dispose();
            _witnessClient.Dispose();
            OtherStore.Dispose();
            Local.Dispose();
            Witness.Dispose();
            _key.Dispose();
            Directory.Delete(_directory, true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handle(request, cancellationToken);
    }

    private sealed class LocalClock : TimeProvider
    {
        private long _milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref _milliseconds);
        internal void Advance(int ms) => Interlocked.Add(ref _milliseconds, ms);
    }
}
