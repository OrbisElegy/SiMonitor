// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Network;

internal sealed partial class ExamClusterNode
{
    internal async Task<ExamClusterNodeStatus> RequalifyAsync(string operatorName, CancellationToken cancellationToken)
    {
        ValidateOperator(operatorName);
        await ReadWitnessAsync(cancellationToken).ConfigureAwait(false);
        await CatchUpAsync(operatorName, cancellationToken).ConfigureAwait(false);
        var result = GetStatus();
        if (!result.Qualified) { throw new InvalidOperationException("The replica must finish verified catch-up before requalification."); }
        return result;
    }

    internal async Task<ExamClusterNodeStatus> ManualPromoteAsync(string operatorName, string isolationEvidence,
        CancellationToken cancellationToken)
    {
        ValidateOperator(operatorName);
        string[] evidence = (isolationEvidence ?? "").Split(':', 2);
        if (evidence.Length != 2 || evidence[0] is not ("PoweredOff" or "NetworkIsolated") ||
            string.IsNullOrWhiteSpace(evidence[1]) || isolationEvidence!.Length > 512 || isolationEvidence.Any(char.IsControl))
        { throw new ArgumentException("Record PoweredOff:<reference> or NetworkIsolated:<reference>.", nameof(isolationEvidence)); }
        await _elections.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                await ReadWitnessAsync(cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("Manual fencing requires an unavailable witness.");
            }
            catch (HttpRequestException error) when (error.StatusCode is null && error.HttpRequestError != HttpRequestError.SecureConnectionError) { }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            if (await ProbePeerAsync(cancellationToken).ConfigureAwait(false) is not null)
            { throw new InvalidOperationException("The old primary remains reachable."); }
            lock (_gate)
            {
                if (!_armed || _inOffice || _term.OwnerId != _other || NowMs < _peerUntilMs)
                { throw new InvalidOperationException("Manual fencing requires a qualified standby and expiration of the observed lease."); }
                _store.Verify();
                SaveTerm(new(checked(_term.AuthorityEpoch + 1), checked(_term.FencingToken + 1), _self, false, true, operatorName, isolationEvidence));
                _manual = true;
                _armed = false;
                _canQualify = false;
                _reason = "ManualFenceRecorded";
                return GetStatus();
            }
        }
        finally { _elections.Release(); }
    }

    // Both processes may have restarted, leaving neither qualified in this lifetime.
    // An operator supplies the institution's independent acknowledged frontier; matching
    // two local images alone would also accept two copies of the same stale backup.
    internal async Task<ExamClusterNodeStatus> RecoverAsync(string operatorName, ExamReplicaCursor trustedFrontier,
        CancellationToken cancellationToken)
    {
        ValidateOperator(operatorName);
        if (trustedFrontier is null || trustedFrontier.Sequence < 0 || !ClassroomArtifactDescriptor.IsContentHash(trustedFrontier.Hash))
        { throw new ArgumentException("Supply an independently verified exam frontier.", nameof(trustedFrontier)); }
        await _elections.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var lease = await ReadWitnessAsync(cancellationToken).ConfigureAwait(false);
            if (lease.RemainingMs > 0 || lease.Revision == 0) { throw new InvalidOperationException("Cold recovery requires an expired, previously issued lease."); }
            var peer = await PeerStatusAsync(cancellationToken).ConfigureAwait(false);
            long epochFloor, tokenFloor;
            lock (_gate)
            {
                epochFloor = Math.Max(lease.AuthorityEpoch, _term.AuthorityEpoch);
                tokenFloor = Math.Max(lease.FencingToken, _term.FencingToken);
            }
            if (peer.Role != "Recovering" || peer.Qualified || peer.LeaseValid ||
                peer.AuthorityEpoch > epochFloor || peer.FencingToken > tokenFloor)
            { throw new InvalidOperationException("Peer cold recovery state does not match the trusted frontier."); }
            ExamReplicaCursor? recoveredReceipt = null;
            lock (_gate)
            {
                _store.Verify();
                if (_armed || _manual || _inOffice) { throw new InvalidOperationException("Both hosts must be in read-only recovery."); }
                var local = _store.Head;
                var pending = _store.Pending;
                if (local == peer.Head && local == trustedFrontier)
                {
                    if (pending is not null) { _store.DiscardPending(); }
                }
                else if (pending is not null && pending.Cursor == peer.Head && (trustedFrontier == local || trustedFrontier == peer.Head))
                {
                    // Both WALs have the same transaction, but this side never learned that
                    // its standby made it durable. Publish it only under the new grant.
                    recoveredReceipt = pending.Cursor;
                }
                else if (peer.Pending != local || local.Sequence - peer.Head.Sequence != 1 ||
                    _store.Read(local.Sequence)?.PreviousHash != peer.Head.Hash ||
                    (trustedFrontier != local && trustedFrontier != peer.Head))
                { throw new InvalidOperationException("The two durable histories do not establish the trusted recovery frontier."); }
            }
            await ChangeLeaseAsync(lease, true, operatorName, recoveredReceipt, cancellationToken).ConfigureAwait(false);
            return GetStatus();
        }
        finally { _elections.Release(); }
    }

    private static void ValidateOperator(string operatorName)
    {
        if (string.IsNullOrWhiteSpace(operatorName) || operatorName.Length > 256 || operatorName.Any(char.IsControl))
        { throw new ArgumentException("A named institutional operator is required.", nameof(operatorName)); }
    }
}
