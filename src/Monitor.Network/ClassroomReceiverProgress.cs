// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Monitor.Domain.Continuity;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

// A command-line receiver has a verified data frontier but no rendered pixels or sweep phase.
public sealed record ClassroomReceiverProgress(Guid ClientId, Guid SessionId, Guid InstanceId, ulong AuthorityEpoch,
    ulong StreamEpoch, ulong BlockSequence, long BlockStartSimTimeNs, string ContentSha256, ulong ConfigurationRevision,
    ulong NextEventSequence, long EstimatedClockOffsetNs, long ClockUncertaintyNs, string ConnectionState, string SampleHealth);

public sealed partial class ClassroomHost
{
    private async Task ReceiveHeadlessProgressAsync(HttpContext context)
    {
        if (!Authorize(context, out Guid student, out _)) { return; }
        var report = await ClassroomMessageJson.ReadAsync<ClassroomReceiverProgress>(context.Request.Body, 4096, context.RequestAborted).ConfigureAwait(false);
        if (report.ClientId != student || report.SessionId != _ring.SessionId || report.InstanceId != _ring.InstanceId ||
            report.AuthorityEpoch != AuthorityEpoch || report.StreamEpoch != _ring.StreamEpoch || report.ClockUncertaintyNs < 0 ||
            !Enum.GetNames<ConnectionState>().Contains(report.ConnectionState, StringComparer.Ordinal) ||
            !Enum.GetNames<SamplePlaneProgressHealth>().Contains(report.SampleHealth, StringComparer.Ordinal))
        { context.Response.StatusCode = 422; return; }
        lock (_gate)
        {
            var retained = _ring.ReadFrom(report.BlockSequence, 1);
            if (retained.Blocks.Count != 1 || retained.Blocks[0].ContentSha256 != report.ContentSha256 ||
                retained.Blocks[0].ConfigurationRevision != report.ConfigurationRevision || retained.Blocks[0].StartSimTimeNs != report.BlockStartSimTimeNs ||
                report.NextEventSequence > _nextEvent)
            { context.Response.StatusCode = 409; return; }
            if (!Authorize(context, out _, out _) || !_members.TryGetValue(student, out var previous)) { return; }
            if (previous.Progress is { } prior && prior.TryGetProperty("blockSequence", out var cursor) &&
                ulong.Parse(cursor.ToString(), CultureInfo.InvariantCulture) > report.BlockSequence)
            { context.Response.StatusCode = 409; return; }
            _members[student] = previous with { LastSeenNs = HostMonotonicNs, Progress = JsonSerializer.SerializeToElement(report, ClassroomMessageJson.Options) };
        }
        await context.Response.WriteAsJsonAsync(new ClassroomAccepted(true), context.RequestAborted).ConfigureAwait(false);
    }
}

public sealed partial class ClassroomStudentClient
{
    public async Task ReportReceiverProgressAsync(ClassroomReceiverProgress report, CancellationToken cancellationToken = default) =>
        _ = await ExchangeAsync<ClassroomAccepted>(HttpMethod.Post, "v1/progress/receiver", report, cancellationToken).ConfigureAwait(false);
}
