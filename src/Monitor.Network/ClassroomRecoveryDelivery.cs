// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Monitor.Application.Continuity;

namespace Monitor.Network;

internal sealed record ClassroomContinuityDelivery(ulong Sequence, ClassroomSignedContinuity Packet);

public sealed partial class ClassroomHost
{
    private readonly Dictionary<Guid, ContinuityMailbox> _continuityMailboxes = [];

    public void ConfigureContinuity(Guid studentId, ClassroomContinuityReceiver? receiver)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            if (receiver is null) { _continuityMailboxes.Remove(studentId); return; }
            if (receiver.StudentId != studentId || receiver.SessionId != _ring.SessionId || receiver.InstanceId != _ring.InstanceId || receiver.AuthorityEpoch != AuthorityEpoch)
            { throw new ArgumentException("Continuity receiver does not belong to the assigned student.", nameof(receiver)); }
            if (_continuityMailboxes.Count >= MaximumStudents && !_continuityMailboxes.ContainsKey(studentId))
            { throw new InvalidOperationException("Classroom continuity capacity reached."); }
            _continuityMailboxes[studentId] = new ContinuityMailbox(receiver);
        }
    }

    public ulong PublishContinuity(Guid studentId, ClassroomSignedContinuity packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(packet.Payload);
        ArgumentNullException.ThrowIfNull(packet.Proof);
        if (packet.Payload.Length > ContinuitySignatureGate.MaximumBaseCapsuleBytes || packet.Proof.SignatureRs64?.Length != 64)
        { throw new ArgumentException("Invalid continuity packet size.", nameof(packet)); }
        ContinuityMailbox mailbox;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            mailbox = ActiveMailbox(studentId);
        }
        packet = packet with { Payload = [.. packet.Payload], Proof = packet.Proof with { SignatureRs64 = [.. packet.Proof.SignatureRs64] } };
        // Only this learner's issuer serializes SM2 verification. Other students continue to receive waveforms.
        lock (mailbox)
        {
            ulong sequence;
            long simulationNs;
            bool newBase = packet.Body.CapsuleType == "ContinuityCapsuleBase";
            lock (_gate)
            {
                if (ActiveMailbox(studentId) != mailbox) { throw new InvalidOperationException("Replaced continuity mailbox."); }
                sequence = checked(mailbox.NextSequence + 1);
                if (!newBase && mailbox.Entries.Count >= 34) { throw new InvalidOperationException("Publish a fresh continuity base."); }
                simulationNs = _ring.NextBlockStartSimTimeNs;
            }
            ClassroomVerifiedContinuity verified = mailbox.Receiver.Accept(sequence, packet, simulationNs, HostMonotonicNs);
            lock (_gate)
            {
                if (ActiveMailbox(studentId) != mailbox) { throw new InvalidOperationException("Replaced continuity mailbox."); }
                if (newBase) { mailbox.Entries.Clear(); }
                mailbox.Entries.Add(new ContinuityEntry(new ClassroomContinuityDelivery(sequence, packet),
                    packet.Body.CapsuleType == "OfflineCapabilityLease" ? verified.Lease!.ExpiresAtSafetyNs : long.MaxValue));
                mailbox.NextSequence = sequence;
                return sequence;
            }
        }
    }

    private ContinuityMailbox ActiveMailbox(Guid studentId)
    {
        if (!_continuityMailboxes.TryGetValue(studentId, out ContinuityMailbox? mailbox) || mailbox.Receiver.AuthorityEpoch != AuthorityEpoch)
        { throw new InvalidOperationException("Provision learner continuity trust and capabilities first."); }
        return mailbox;
    }

    private void MapContinuityDelivery(IEndpointRouteBuilder routes) => routes.MapGet("/v1/recovery/continuity", async context =>
    {
        if (!Authorize(context, out Guid student, out _)) { return; }
        if (!TryReadRecoveryNumber(context, "after", out ulong after)) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        ClassroomContinuityDelivery? item;
        lock (_gate)
        {
            if (!_continuityMailboxes.TryGetValue(student, out ContinuityMailbox? mailbox) || mailbox.Entries.Count == 0 || mailbox.Receiver.AuthorityEpoch != AuthorityEpoch)
            { context.Response.StatusCode = StatusCodes.Status204NoContent; return; }
            if (after > mailbox.NextSequence || after != 0 && after < mailbox.Entries[0].Delivery.Sequence - 1)
            { context.Response.StatusCode = StatusCodes.Status409Conflict; return; }
            ContinuityEntry? entry = mailbox.Entries.FirstOrDefault(e => e.Delivery.Sequence > after);
            if (entry is null) { context.Response.StatusCode = StatusCodes.Status204NoContent; return; }
            if (!mailbox.Receiver.CanDeliver(entry.Delivery.Packet, _ring.NextBlockStartSimTimeNs, HostMonotonicNs) ||
                HostMonotonicNs >= entry.ExpiresAtSafetyNs)
            { context.Response.StatusCode = StatusCodes.Status410Gone; return; }
            item = entry.Delivery;
        }
        await context.Response.WriteAsJsonAsync(item, ClassroomRecoveryJson.Options, context.RequestAborted).ConfigureAwait(false);
    });

    private sealed class ContinuityMailbox(ClassroomContinuityReceiver receiver)
    {
        internal ClassroomContinuityReceiver Receiver { get; } = receiver;
        internal List<ContinuityEntry> Entries { get; } = [];
        internal ulong NextSequence { get; set; }
    }

    private sealed record ContinuityEntry(ClassroomContinuityDelivery Delivery, long ExpiresAtSafetyNs);
}

public sealed partial class ClassroomStudentClient
{
    public async Task<ClassroomVerifiedContinuity?> ReadContinuityAsync(ClassroomContinuityReceiver receiver, ulong afterSequence,
        Func<(long SimTimeNs, long AuthorityMonotonicNs)> currentTime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        ArgumentNullException.ThrowIfNull(currentTime);
        using var request = new HttpRequestMessage(HttpMethod.Get, FormattableString.Invariant($"v1/recovery/continuity?after={afterSequence}"));
        byte[] bytes = await SendRecoveryAsync(request, 96 * 1024, cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0) { return null; }
        var delivery = JsonSerializer.Deserialize<ClassroomContinuityDelivery>(bytes, ClassroomRecoveryJson.Options);
        if (delivery?.Packet is null || delivery.Sequence == 0 ||
            (afterSequence == 0 ? delivery.Packet.Body?.CapsuleType != "ContinuityCapsuleBase" :
                afterSequence == ulong.MaxValue || delivery.Sequence != afterSequence + 1))
        { throw new InvalidDataException("Continuity delivery is not a complete ordered chain."); }
        var (simTime, safetyTime) = currentTime();
        return receiver.Accept(delivery.Sequence, delivery.Packet, simTime, safetyTime);
    }
}
