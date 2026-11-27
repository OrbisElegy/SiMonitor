// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Monitor.Network;

public sealed record ExamClusterNodeStatus(Guid ClusterId, Guid NodeId, string Role, long AuthorityEpoch, long FencingToken,
    bool LeaseValid, bool Qualified, ExamReplicaCursor Head, string Reason)
{
    public ExamReplicaCursor? Pending { get; init; }
}

internal sealed partial class ExamClusterNode : IDisposable
{
    private const int ColdProbeTimeoutMs = 10000;
    private const int WitnessProbeTimeoutMs = 800;
    private const int PeerProbeTimeoutMs = 550;
    private readonly TimeProvider _clock;
    private readonly Guid _cluster;
    private readonly Guid _self;
    private readonly Guid _other;
    private readonly bool _initialPrimary;
    private readonly ExamReplicaStore _store;
    private readonly HttpClient _peer;
    private readonly HttpClient _witness;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _elections = new(1);
    private readonly SemaphoreSlim _writes = new(1);
    private readonly SemaphoreSlim _catchUp = new(1);
    private ExamReplicaElection _term;
    private bool _canQualify;
    private bool _armed;
    private bool _inOffice;
    private bool _manual;
    private bool _stopped;
    private bool _witnessReady;
    private bool _peerReady;
    private string? _witnessProblem;
    private string? _peerProblem;
    private long _witnessRevision = -1;
    private long _writeUntilMs;
    private long _peerUntilMs;
    private string _reason = "WaitingForWitness";
    private (long Epoch, long Token)? _requestedGrant;
    private bool _requestedGrantNeedsQualification;

    internal ExamClusterNode(Guid clusterId, Guid nodeId, Guid peerId, bool initialPrimary, ExamReplicaStore store,
        HttpClient peer, HttpClient witness, TimeProvider? clock = null)
    {
        if (clusterId == Guid.Empty || nodeId == Guid.Empty || peerId == Guid.Empty || nodeId == peerId)
        { throw new ArgumentException("Expected distinct provisioned exam hosts."); }
        _clock = clock ?? TimeProvider.System;
        _cluster = clusterId;
        _self = nodeId;
        _other = peerId;
        _initialPrimary = initialPrimary;
        _store = store;
        _peer = peer;
        _witness = witness;
        _term = store.Election;
        _canQualify = !initialPrimary && _term == ExamReplicaElection.Initial;
        // A restored database can have a valid chain and still be behind. A grant or
        // eligibility from another process lifetime never suffices for promotion.
        if (_term.Qualified) { SaveTerm(_term with { Qualified = false }); }
    }

    public void Dispose()
    {
        _catchUp.Dispose();
        _writes.Dispose();
        _elections.Dispose();
    }

    private void SaveTerm(ExamReplicaElection next)
    {
        _store.RecordElection(next);
        _term = next;
    }

    internal ExamClusterNodeStatus GetStatus()
    {
        lock (_gate)
        {
            bool leaseValid = OwnsLiveLease();
            string role = _manual ? "ManualPrimary" : _inOffice ? "Primary" : _armed ? "Standby" : "Recovering";
            string reason = _witnessProblem ?? _peerProblem ?? (_inOffice && !leaseValid ? "AwaitingWitnessLease" : _reason);
            return new(_cluster, _self, role, _term.AuthorityEpoch, _term.FencingToken, leaseValid, _armed, _store.Head, reason)
            { Pending = _store.Pending?.Cursor };
        }
    }

    private long NowMs => _clock.GetElapsedTime(0, _clock.GetTimestamp()).Ticks / TimeSpan.TicksPerMillisecond;

    private bool OwnsLiveLease() => !_stopped && _inOffice && NowMs < _writeUntilMs;

    private void RequireWriter(long epoch, long token)
    {
        if (!OwnsLiveLease() || _term.OwnerId != _self || _term.AuthorityEpoch != epoch || _term.FencingToken != token)
        { throw new InvalidOperationException("A current local witness lease and exact fencing token are required."); }
    }

    private void RequireFollower(long epoch, long token)
    {
        if (_stopped || _inOffice || _manual || _term.OwnerId != _other || _term.AuthorityEpoch != epoch ||
            _term.FencingToken != token || NowMs >= _peerUntilMs)
        { throw new InvalidOperationException("The sending primary has no verified current fencing token."); }
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        try { await Task.WhenAll(PollAsync(true, cancellationToken), PollAsync(false, cancellationToken)).ConfigureAwait(false); }
        finally
        {
            lock (_gate) { _stopped = true; _inOffice = false; _armed = false; _writeUntilMs = 0; }
        }
    }

    private async Task PollAsync(bool election, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ExamWitnessStore.RenewIntervalMs));
        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (election) { await PollElectionAsync(cancellationToken).ConfigureAwait(false); }
                    else if (GetStatus().LeaseValid)
                    {
                        // This read-only connection probe runs independently of lease renewal
                        // and never holds the write gate or participates in promotion.
                        bool warmPeer;
                        lock (_gate) { warmPeer = !_peerReady || _peerProblem is not null; }
                        if (warmPeer) { await PeerStatusAsync(true, cancellationToken).ConfigureAwait(false); }
                        await ReconcileIdlePrimaryAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else { await CatchUpAsync(null, cancellationToken).ConfigureAwait(false); }
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested && error is HttpRequestException or
                    OperationCanceledException or InvalidOperationException or IOException or InvalidDataException or JsonException or
                    CryptographicException or SqliteException)
                {
                    lock (_gate)
                    {
                        _reason = election ? "WitnessOrElectionUnavailable" : "PeerOrReplayUnavailable";
                        if (error is InvalidDataException or JsonException or CryptographicException or SqliteException)
                        {
                            _armed = false;
                            _canQualify = false;
                            if (election) { _writeUntilMs = 0; }
                        }
                    }
                }
            } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task PollElectionAsync(CancellationToken cancellationToken)
    {
        await _elections.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ExamWitnessLease view = await ReadWitnessAsync(true, cancellationToken).ConfigureAwait(false);
            bool renew, claim, checkFormerLeader;
            lock (_gate)
            {
                renew = view.OwnerId == _self && view.RemainingMs > 0 &&
                    (_inOffice || _requestedGrant == (view.AuthorityEpoch, view.FencingToken));
                checkFormerLeader = _armed && view.OwnerId == _other && view.AuthorityEpoch == _term.AuthorityEpoch &&
                    view.FencingToken == _term.FencingToken;
                bool initial = _initialPrimary && view.Revision == 0 && _term == ExamReplicaElection.Initial && _store.Head == ExamReplicaCursor.Empty;
                bool incumbent = _inOffice && view.OwnerId == _self && view.AuthorityEpoch == _term.AuthorityEpoch;
                claim = view.RemainingMs == 0 && (initial || incumbent || checkFormerLeader || _manual);
            }
            if (renew) { await ChangeLeaseAsync(view, false, null, null, cancellationToken).ConfigureAwait(false); }
            else if (claim)
            {
                if (checkFormerLeader)
                {
                    var peer = await ProbePeerAsync(cancellationToken).ConfigureAwait(false);
                    if (peer is { Role: "Primary", LeaseValid: true } && peer.AuthorityEpoch == view.AuthorityEpoch) { return; }
                    lock (_gate) { if (!_armed) { return; } }
                }
                await ChangeLeaseAsync(view, true, null, null, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { _elections.Release(); }
    }

    private async Task<ExamWitnessLease> ReadWitnessAsync(bool allowColdProbe, CancellationToken cancellationToken)
    {
        long started = NowMs;
        int timeout;
        lock (_gate) { timeout = allowColdProbe && !_witnessReady && !_armed && !_inOffice ? ColdProbeTimeoutMs : WitnessProbeTimeoutMs; }
        using var limit = Deadline(timeout, cancellationToken);
        var view = await GetJsonAsync<ExamWitnessLease>(_witness, ExamWitnessEndpoints.LeasePath, limit.Token).ConfigureAwait(false);
        lock (_gate)
        {
            ObserveWitness(view, started);
            _witnessReady = true;
            if (!_initialPrimary && view.OwnerId is null && _term.OwnerId is null) { _reason = "WaitingForPrimary"; }
        }
        return view;
    }

    private void ObserveWitness(ExamWitnessLease view, long started)
    {
        if (view.ClusterId != _cluster || view.RemainingMs is < 0 or > ExamWitnessStore.LeaseDurationMs ||
            view.Revision < 0 || view.AuthorityEpoch < 0 || view.FencingToken < 0 ||
            (view.OwnerId is null ? view.Revision != 0 || view.AuthorityEpoch != 0 || view.FencingToken != 0 || view.RemainingMs != 0 :
                (view.OwnerId != _other && view.OwnerId != _self) || view.AuthorityEpoch == 0 || view.FencingToken == 0 || view.Revision == 0))
        { throw new InvalidDataException("The witness returned an invalid lease."); }
        if (view.Revision < _witnessRevision) { throw new InvalidOperationException("A later witness reply has already been observed."); }
        _witnessRevision = view.Revision;
        if (_term.Manual && _term.OwnerId == _self && view.AuthorityEpoch < _term.AuthorityEpoch && view.FencingToken < _term.FencingToken) { return; }
        if (view.AuthorityEpoch < _term.AuthorityEpoch || view.FencingToken < _term.FencingToken ||
            (view.AuthorityEpoch == _term.AuthorityEpoch && (_term.OwnerId != view.OwnerId || _term.FencingToken != view.FencingToken)))
        {
            _writeUntilMs = 0;
            _peerUntilMs = 0;
            _armed = false;
            throw new InvalidDataException("The witness state conflicts with this host's durable fencing history.");
        }
        if (view.OwnerId == _other)
        {
            if (_term.AuthorityEpoch != view.AuthorityEpoch)
            {
                if (_term.OwnerId == _self) { _canQualify = false; }
                _armed = false;
                _inOffice = false;
                _manual = false;
                _writeUntilMs = 0;
                SaveTerm(new(view.AuthorityEpoch, view.FencingToken, _other, false, false, null, null));
            }
            _peerUntilMs = checked(started + view.RemainingMs);
        }
        else if (view.OwnerId == _self && view.RemainingMs == 0) { _writeUntilMs = 0; }
    }

    private async Task ChangeLeaseAsync(ExamWitnessLease view, bool acquire, string? operatorName, ExamReplicaCursor? recoveredReceipt,
        CancellationToken cancellationToken)
    {
        ExamWitnessRequest request;
        long expectedEpoch, expectedToken;
        bool needsQualifiedStandby;
        lock (_gate)
        {
            needsQualifiedStandby = acquire
                ? operatorName is null && _term.OwnerId == _other && !_manual
                : !_inOffice && _requestedGrantNeedsQualification;
            if (needsQualifiedStandby && !_armed) { throw new InvalidOperationException("The standby ceased to be qualified."); }
            if (view.Revision != _witnessRevision) { throw new InvalidOperationException("The witness revision changed."); }
            long epoch = Math.Max(_term.AuthorityEpoch, view.AuthorityEpoch);
            long token = Math.Max(_term.FencingToken, view.FencingToken);
            request = new(_cluster, view.Revision, epoch, token);
            expectedEpoch = acquire ? checked(epoch + 1) : epoch;
            expectedToken = acquire ? checked(token + 1) : token;
            if (acquire)
            {
                _requestedGrant = operatorName is null ? (expectedEpoch, expectedToken) : null;
                _requestedGrantNeedsQualification = needsQualifiedStandby;
            }
        }
        using var limit = Deadline(900, cancellationToken);
        long started = NowMs;
        ExamWitnessLease lease;
        bool rejected;
        try
        {
            using var response = await _witness.PostAsJsonAsync(ExamWitnessEndpoints.LeasePath + (acquire ? "/acquire" : "/renew"),
                request, ExamClusterConfiguration.Json, limit.Token).ConfigureAwait(false);
            rejected = response.StatusCode == HttpStatusCode.Conflict;
            if (!rejected) { response.EnsureSuccessStatusCode(); }
            lock (_gate) { _witnessProblem = null; }
            lease = await ReadJsonAsync<ExamWitnessLease>(response, limit.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            lock (_gate) { _witnessProblem = TransportProblem("Witness", error); }
            throw;
        }
        lock (_gate)
        {
            ObserveWitness(lease, started);
            if (rejected) { throw new InvalidOperationException("The witness rejected the lease comparison."); }
            if (lease.OwnerId != _self || lease.AuthorityEpoch != expectedEpoch || lease.FencingToken != expectedToken ||
                lease.Revision <= view.Revision || NowMs >= started + lease.RemainingMs)
            { throw new InvalidDataException("The witness did not return a usable grant."); }
            if (needsQualifiedStandby && !_armed)
            {
                _requestedGrant = null;
                throw new InvalidOperationException("The standby ceased to be qualified during promotion.");
            }
            if (!_inOffice || _term.AuthorityEpoch != lease.AuthorityEpoch)
            {
                SaveTerm(new(lease.AuthorityEpoch, lease.FencingToken, _self, false, false, operatorName, operatorName is null ? null : "VerifiedColdRecovery"));
            }
            if (recoveredReceipt is not null)
            {
                _store.ConfirmReplicated(recoveredReceipt, lease.AuthorityEpoch, lease.FencingToken);
                if (NowMs >= started + lease.RemainingMs) { throw new InvalidOperationException("The recovery grant expired before the frontier was durable."); }
            }
            _inOffice = true;
            _manual = false;
            _armed = false;
            _canQualify = false;
            _peerUntilMs = 0;
            _requestedGrant = null;
            _writeUntilMs = checked(started + lease.RemainingMs);
            _reason = "WitnessLease";
        }
    }

    private static CancellationTokenSource Deadline(int milliseconds, CancellationToken token)
    {
        var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(milliseconds);
        return limit;
    }

    private async Task<T> GetJsonAsync<T>(HttpClient client, string path, CancellationToken token)
    {
        bool witness = ReferenceEquals(client, _witness);
        try
        {
            using var response = await client.GetAsync(path, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            lock (_gate)
            {
                if (witness) { _witnessProblem = null; }
                else { _peerProblem = null; }
            }
            return await ReadJsonAsync<T>(response, token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            lock (_gate)
            {
                if (witness) { _witnessProblem = TransportProblem("Witness", error); }
                else { _peerProblem = TransportProblem("Peer", error); }
            }
            throw;
        }
    }

    private static string TransportProblem(string link, Exception error) => error switch
    {
        HttpRequestException { StatusCode: { } status } => FormattableString.Invariant($"{link}Http{(int)status}"),
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => link + "DnsFailure",
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } or
            HttpRequestException { InnerException: AuthenticationException } => link + "TlsFailure",
        HttpRequestException { InnerException: SocketException { SocketErrorCode: SocketError.TimedOut } } => link + "TcpTimeout",
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } => link + "TcpFailure",
        OperationCanceledException => link + "RequestTimeout",
        _ => link + "TransportFailure"
    };

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken token) =>
        await response.Content.ReadFromJsonAsync<T>(ExamClusterConfiguration.Json, token).ConfigureAwait(false)
        ?? throw new InvalidDataException("The exam cluster response was empty.");

    private async Task<ExamClusterNodeStatus> PeerStatusAsync(bool allowColdProbe, CancellationToken token)
    {
        int timeout;
        lock (_gate) { timeout = allowColdProbe && !_peerReady ? ColdProbeTimeoutMs : PeerProbeTimeoutMs; }
        using var limit = Deadline(timeout, token);
        var peer = await GetJsonAsync<ExamClusterNodeStatus>(_peer, PeerPath + "/status", limit.Token).ConfigureAwait(false);
        if (peer.ClusterId != _cluster || peer.NodeId != _other || peer.Head is null || peer.Head.Sequence < 0 ||
            !ClassroomArtifactDescriptor.IsContentHash(peer.Head.Hash))
        { throw new InvalidDataException("The peer returned a status for a different host or cluster."); }
        lock (_gate) { _peerReady = true; }
        return peer;
    }

    private async Task<ExamClusterNodeStatus?> ProbePeerAsync(CancellationToken token)
    {
        try { return await PeerStatusAsync(false, token).ConfigureAwait(false); }
        catch (HttpRequestException error) when (error.StatusCode is null && error.HttpRequestError != HttpRequestError.SecureConnectionError) { return null; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
    }
}
