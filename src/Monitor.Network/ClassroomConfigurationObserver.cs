// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

// One observer per stream. The application calls ObserveDeliveredAsync only after presenting a block.
public sealed class ClassroomConfigurationObserver(ClassroomStudentClient client,
    Func<ClassroomScheduledControl, string?>? prepare = null) : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _polling = new(1);
    private readonly Dictionary<ulong, ClassroomScheduledControl> _prepared = [];
    private readonly Dictionary<ulong, (ulong Block, long SimTimeNs)> _firstDelivered = [];

    public async Task PollAsync(CancellationToken cancellationToken = default)
    {
        await _polling.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ClassroomConfigurationStatus status = await client.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            foreach (ClassroomScheduledControl change in status.Changes)
            {
                ulong revision = checked(change.BaseConfigurationRevision + 1);
                if (!status.RequiredChanges.TryGetValue(change.ChangeId, out var previous)) { continue; }
                if (previous?.Stage is ClassroomAckStage.Rejected or ClassroomAckStage.Applied || change.State is "Rejected" or "Cancelled")
                {
                    Forget(revision, change.ChangeId);
                    continue;
                }
                var ack = new ClassroomConfigurationAck(change.ChangeId, change.PublicDiffSha256, ClassroomAckStage.Received, null, null, null);
                if (change.State is "Scheduled" or "Prepared")
                {
                    if (previous is null && !await ReportAsync(ack, cancellationToken).ConfigureAwait(false)) { continue; }
                    if (previous?.Stage != ClassroomAckStage.Prepared)
                    {
                        if (prepare?.Invoke(change) is { } reason)
                        {
                            await ReportAsync(ack with { Stage = ClassroomAckStage.Rejected, ReasonCode = reason }, cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        if (!await ReportAsync(ack with { Stage = ClassroomAckStage.Prepared }, cancellationToken).ConfigureAwait(false)) { continue; }
                    }
                }
                else if (previous?.Stage != ClassroomAckStage.Prepared) { continue; }
                lock (_gate) { _prepared[revision] = change; }
                await ReportDeliveryAsync(revision, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { _polling.Release(); }
    }

    public async Task ObserveDeliveredAsync(WaveformEnvelope block, long actualSimTimeNs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentOutOfRangeException.ThrowIfLessThan(actualSimTimeNs, block.StartSimTimeNs);
        lock (_gate)
        {
            _firstDelivered.TryAdd(block.ConfigurationRevision, (block.BlockSequence, actualSimTimeNs));
            if (_firstDelivered.Count > 16) { _firstDelivered.Remove(_firstDelivered.Keys.Min()); }
        }
        await ReportDeliveryAsync(block.ConfigurationRevision, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReportDeliveryAsync(ulong revision, CancellationToken cancellationToken)
    {
        ClassroomConfigurationAck ack;
        lock (_gate)
        {
            if (!_prepared.TryGetValue(revision, out var change) || !_firstDelivered.TryGetValue(revision, out var first) ||
                first.SimTimeNs < change.EffectiveSimTimeNs) { return; }
            ack = new ClassroomConfigurationAck(change.ChangeId, change.PublicDiffSha256, ClassroomAckStage.Applied,
                null, first.SimTimeNs, first.Block);
        }
        if (await ReportAsync(ack, cancellationToken).ConfigureAwait(false)) { Forget(revision, ack.ChangeId); }
    }

    private void Forget(ulong revision, Guid changeId)
    {
        lock (_gate)
        {
            if (_prepared.TryGetValue(revision, out var saved) && saved.ChangeId == changeId) { _prepared.Remove(revision); }
        }
    }

    private async Task<bool> ReportAsync(ClassroomConfigurationAck ack, CancellationToken cancellationToken)
    {
        try { await client.ReportConfigurationAckAsync(ack, cancellationToken).ConfigureAwait(false); return true; }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.Conflict) { return false; }
    }

    public void Dispose() => _polling.Dispose();
}
