// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;

namespace Monitor.Network;

internal sealed record ExamReplicationRequest(Guid ClusterId, Guid OwnerId, long AuthorityEpoch, long FencingToken, ExamReplicaEntry Entry);

internal sealed partial class ExamClusterNode
{
    private const string PeerPath = "/v1/exam/peer";

    // The host maps these endpoints only on its peer-authenticated replication listener.
    internal void MapPeerEndpoints(IEndpointRouteBuilder routes)
    {
        routes.MapGet(PeerPath + "/status", () => Results.Json(GetStatus(), ExamClusterConfiguration.Json));
        routes.MapGet(PeerPath + "/entries/{sequence:long}", (long sequence, long epoch, long token) =>
        {
            lock (_gate)
            {
                if (!OwnsLiveLease() || epoch != _term.AuthorityEpoch || token != _term.FencingToken) { return Results.Conflict(); }
                return sequence >= 1 && _store.Read(sequence) is { } entry
                    ? Results.Json(entry, ExamClusterConfiguration.Json) : Results.NotFound();
            }
        });
        routes.MapPost(PeerPath + "/replicate", (Func<HttpContext, Task<IResult>>)ReceiveEntryAsync);
    }

    private async Task<IResult> ReceiveEntryAsync(HttpContext context)
    {
        if (!context.Request.HasJsonContentType()) { return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType); }
        const int maximumBody = 2 * ExamReplicaEntry.MaxPayloadBytes;
        if (context.Request.ContentLength > maximumBody) { return Results.StatusCode(StatusCodes.Status413PayloadTooLarge); }
        var feature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) { feature.MaxRequestBodySize = maximumBody; }
        using var deadline = Deadline(2400, context.RequestAborted);
        try
        {
            var request = await JsonSerializer.DeserializeAsync<ExamReplicationRequest>(context.Request.Body,
                ExamClusterConfiguration.Json, deadline.Token).ConfigureAwait(false);
            if (request is null || request.ClusterId != _cluster || request.OwnerId != _other || request.Entry is null)
            { return Results.BadRequest(); }
            request.Entry.Validate(_cluster);
            bool refresh;
            lock (_gate) { refresh = NowMs >= _peerUntilMs || request.AuthorityEpoch != _term.AuthorityEpoch; }
            if (refresh) { await ReadWitnessAsync(deadline.Token).ConfigureAwait(false); }
            lock (_gate)
            {
                RequireFollower(request.AuthorityEpoch, request.FencingToken);
                if (!_armed) { return Results.Conflict(); }
                if (request.Entry.Sequence > _store.Head.Sequence &&
                    (request.Entry.AuthorityEpoch != request.AuthorityEpoch || request.Entry.FencingToken != request.FencingToken))
                { return Results.Conflict(); }
                ExamReplicaCursor durable = _store.Apply(request.Entry);
                RequireFollower(request.AuthorityEpoch, request.FencingToken);
                return Results.Json(durable, ExamClusterConfiguration.Json);
            }
        }
        catch (JsonException) { return Results.BadRequest(); }
        catch (InvalidDataException) { return Results.Conflict(); }
        catch (InvalidOperationException) { return Results.Conflict(); }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or SqliteException or CryptographicException)
        { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
    }

    internal async Task<ExamReplicaCursor> WriteAsync(long authorityEpoch, long fencingToken, ExamReplicaCommand command,
        CancellationToken cancellationToken)
    {
        ExamReplicaEntry.ValidateCommand(command);
        command = command with { Payload = (byte[])command.Payload.Clone() };
        await _writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ExamReplicaEntry? previous;
            lock (_gate)
            {
                RequireWriter(authorityEpoch, fencingToken);
                previous = _store.Pending;
            }
            if (previous is not null && (previous.AuthorityEpoch != authorityEpoch || previous.FencingToken != fencingToken))
            {
                await ReconcileAsync(previous, authorityEpoch, fencingToken, cancellationToken).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_store.Pending is not null) { throw new InvalidOperationException("Both hosts must reconcile the preceding lease's transaction."); }
                }
                previous = null;
            }
            if (previous is not null && previous.Command.RequestId != command.RequestId)
            { await ReplicateAsync(previous, authorityEpoch, fencingToken, cancellationToken).ConfigureAwait(false); }
            ExamReplicaEntry entry;
            lock (_gate)
            {
                RequireWriter(authorityEpoch, fencingToken);
                entry = _store.Prepare(authorityEpoch, fencingToken, command);
            }
            return await ReplicateAsync(entry, authorityEpoch, fencingToken, cancellationToken).ConfigureAwait(false);
        }
        finally { _writes.Release(); }
    }

    private async Task<ExamReplicaCursor> ReplicateAsync(ExamReplicaEntry entry, long epoch, long token, CancellationToken cancellationToken)
    {
        var request = new ExamReplicationRequest(_cluster, _self, epoch, token, entry);
        using var limit = Deadline(2400, cancellationToken);
        using var response = await _peer.PostAsJsonAsync(PeerPath + "/replicate", request, ExamClusterConfiguration.Json, limit.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var durable = await ReadJsonAsync<ExamReplicaCursor>(response, limit.Token).ConfigureAwait(false);
        if (durable != entry.Cursor) { throw new InvalidDataException("The peer receipt does not match the proposed transaction."); }
        lock (_gate)
        {
            RequireWriter(epoch, token);
            var confirmed = _store.Confirm(durable);
            RequireWriter(epoch, token);
            return confirmed;
        }
    }

    private async Task ReconcileIdlePrimaryAsync(CancellationToken cancellationToken)
    {
        if (!await _writes.WaitAsync(0, cancellationToken).ConfigureAwait(false)) { return; }
        try
        {
            long epoch, token;
            ExamReplicaEntry? pending;
            lock (_gate)
            {
                epoch = _term.AuthorityEpoch;
                token = _term.FencingToken;
                RequireWriter(epoch, token);
                pending = _store.Pending;
            }
            if (pending is not null) { await ReconcileAsync(pending, epoch, token, cancellationToken).ConfigureAwait(false); }
        }
        finally { _writes.Release(); }
    }

    private async Task ReconcileAsync(ExamReplicaEntry pending, long epoch, long token, CancellationToken cancellationToken)
    {
        // A standby can durably apply before either side receives the reply. Its exact
        // committed hash proves the lost receipt even if it subsequently restarted.
        var peer = await PeerStatusAsync(cancellationToken).ConfigureAwait(false);
        if (peer.Role is not ("Recovering" or "Standby") || peer.AuthorityEpoch != epoch || peer.FencingToken != token) { return; }
        lock (_gate)
        {
            RequireWriter(epoch, token);
            if (_store.Pending?.Cursor != pending.Cursor) { return; }
            if (peer.Head == pending.Cursor)
            { _store.ConfirmReplicated(pending.Cursor, epoch, token); }
            else if (peer.Head == _store.Head && (pending.AuthorityEpoch != epoch || pending.FencingToken != token))
            {
                // This peer has advanced its fence. A delayed request in the old term can
                // no longer append, and neither committed frontier contains the pending row.
                _store.DiscardPending();
            }
        }
    }

    private async Task CatchUpAsync(string? operatorName, CancellationToken cancellationToken)
    {
        await _catchUp.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long epoch, token;
            ExamReplicaCursor beforeProbe;
            lock (_gate)
            {
                beforeProbe = _store.Head;
                if (_inOffice || _manual || _term.OwnerId != _other || NowMs >= _peerUntilMs)
                {
                    if (operatorName is not null) { throw new InvalidOperationException("A live peer primary is required for requalification."); }
                    return;
                }
                epoch = _term.AuthorityEpoch;
                token = _term.FencingToken;
            }
            var source = await PeerStatusAsync(cancellationToken).ConfigureAwait(false);
            if (source.Role != "Primary" || !source.LeaseValid || source.AuthorityEpoch != epoch || source.FencingToken != token)
            {
                if (operatorName is not null) { throw new InvalidOperationException("The peer primary is not ready."); }
                return;
            }
            ExamReplicaCursor cursor;
            lock (_gate)
            {
                RequireFollower(epoch, token);
                cursor = _store.Head;
                if (cursor != beforeProbe) { return; }
                if (source.Head.Sequence < cursor.Sequence)
                {
                    // The standby may have returned a receipt while the primary is still committing it.
                    if (cursor.Sequence == source.Head.Sequence + 1)
                    {
                        _reason = "AwaitingPrimaryReceiptConfirmation";
                        return;
                    }
                    throw new InvalidDataException("The peer's committed frontier is behind this replica.");
                }
                if (source.Head.Sequence > cursor.Sequence) { _armed = false; }
            }
            // Bound each pass so a large catch-up does not monopolize the peer or a local admin.
            long count = Math.Min(32, source.Head.Sequence - cursor.Sequence);
            for (long i = 0; i < count; i++)
            {
                string path = FormattableString.Invariant($"{PeerPath}/entries/{cursor.Sequence + 1}?epoch={epoch}&token={token}");
                using var timeout = Deadline(2000, cancellationToken);
                var entry = await GetJsonAsync<ExamReplicaEntry>(_peer, path, timeout.Token).ConfigureAwait(false);
                entry.Validate(_cluster);
                if (entry.Sequence != cursor.Sequence + 1 || entry.PreviousHash != cursor.Hash ||
                    entry.Sequence == source.Head.Sequence && entry.Cursor != source.Head)
                { throw new InvalidDataException("The peer returned a divergent or reordered history."); }
                lock (_gate)
                {
                    RequireFollower(epoch, token);
                    if (_store.Pending is { } pending && pending.Hash != entry.Hash) { _store.DiscardPending(); }
                    cursor = _store.Apply(entry);
                }
            }
            lock (_gate)
            {
                RequireFollower(epoch, token);
                if (cursor.Sequence < source.Head.Sequence) { _reason = "CatchingUp"; return; }
                if (cursor != source.Head) { throw new InvalidDataException("The two hosts disagree at the same committed sequence."); }
                if (_store.Pending is not null) { _store.DiscardPending(); }
                if (_armed && operatorName is null) { return; }
                if (!_canQualify && operatorName is null) { _reason = "ExplicitRequalificationRequired"; return; }
                _store.Verify();
                if (_store.Head != cursor) { throw new InvalidOperationException("The frontier advanced during verification."); }
                RequireFollower(epoch, token);
                SaveTerm(_term with { Qualified = true, OperatorName = operatorName, Evidence = "PeerFrontierVerified" });
                _canQualify = true;
                _armed = true;
                _reason = "PeerFrontierVerified";
            }
        }
        finally { _catchUp.Release(); }
    }
}
