// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Monitor.Network;

public sealed partial class ClassroomHost
{
    private const string AllowlistSchema = """{"type":"array","uniqueItems":true,"maxItems":30,"items":{"type":"string"}}""";
    private const string ContentSchema = """
        {"type":"object","additionalProperties":false,"required":["allowed_action_ids","heart_rate_bpm","named_projections"],"properties":{"allowed_action_ids":{"type":"array","uniqueItems":true,"maxItems":30,"items":{"type":"string"}},"heart_rate_bpm":{"type":["integer","null"],"minimum":40,"maximum":180},"named_projections":{"type":"object","maxProperties":8,"properties":{"display.settings":{"type":"object","additionalProperties":false,"required":["schema_id","layout_id","ecg12_theme_id"],"properties":{"schema_id":{"const":"ClassroomDisplaySettings@1"},"layout_id":{"enum":["ThreeRows","FourRows","FiveRows","SevenRows"]},"ecg12_theme_id":{"enum":["MonitorDarkGreen","PaperGridBlack"]}}}},"additionalProperties":{"$ref":"schemas/monitoring.schema.json#/$defs/DisplayRoleResolutionProjection"}}}}
        """;
    private static string ConfigurationSchemaHash(bool full) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(full ? ContentSchema : AllowlistSchema)));

    public ClassroomScheduledControl ScheduleConfiguration(Guid changeId, ulong basedOnConfigurationRevision, long effectiveSimTimeNs,
        ClassroomConfigurationContent content, IReadOnlyList<Guid> requiredClients, int preparedQuorum,
        ClassroomChangeConflict conflict = ClassroomChangeConflict.RejectOnConflict, bool requireTeacherConfirmation = false)
    {
        lock (_gate)
        {
            EnsureControlOpen();
            ArgumentNullException.ThrowIfNull(content);
            if (content.NamedProjections?.ContainsKey("display.settings") == true &&
                _control.Snapshot().DeviceProfileId != new ClassroomControlDevice().ProfileId)
            { throw new InvalidOperationException("This device profile does not support the classroom display configuration."); }
            if (content.HeartRateBpm != _control.Configuration().Active.HeartRateBpm)
            {
                if (_source is null) { throw new InvalidOperationException("A classroom-owned simulation source is required to schedule a heart-rate change."); }
                // Reject a definition that cannot continue this source before publishing or replacing a plan.
                _ = _source.WithConfigurationRevision(checked(_control.ConfigurationRevision + 1), DefinitionFor(content.HeartRateBpm));
            }
            var previous = _control.Configuration().Changes.Where(c => c.State is "Scheduled" or "Prepared").ToArray();
            var change = _control.ScheduleConfiguration(changeId, basedOnConfigurationRevision, effectiveSimTimeNs, content,
                requiredClients, preparedQuorum, conflict, requireTeacherConfirmation);
            if (conflict == ClassroomChangeConflict.ReplacePending)
            {
                foreach (var prior in previous)
                { PublishScheduledControl(prior with { State = "Cancelled" }); }
            }
            PublishScheduledControl(change);
            return change;
        }
    }

    public bool ConfirmConfiguration(Guid changeId)
    {
        lock (_gate) { EnsureControlOpen(); return _control.Confirm(changeId); }
    }

    public ClassroomConfigurationStatus ReadConfiguration()
    {
        lock (_gate) { return _control.Configuration(); }
    }

    public IReadOnlyDictionary<Guid, ClassroomConfigurationAck> ReadConfigurationAcks(Guid changeId)
    {
        lock (_gate) { return _control.Acks(changeId); }
    }

    private void MapConfigurationRoutes(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v1/configuration", (HttpContext ctx) =>
        {
            if (!Authorize(ctx, out Guid actor, out _)) { return Results.StatusCode(ctx.Response.StatusCode); }
            lock (_gate) { return Results.Json(_control.Configuration(actor), ClassroomControlWire.Json); }
        });
        routes.MapPost("/v1/configuration/ack", (Func<HttpContext, Task<IResult>>)AcceptConfigurationAckAsync)
            .WithMetadata(ClassroomEndpointAccess.ControlWrite);
    }

    private async Task<IResult> AcceptConfigurationAckAsync(HttpContext context)
    {
        if (!Authorize(context, out Guid actor, out _)) { return Results.StatusCode(context.Response.StatusCode); }
        if (!context.Request.HasJsonContentType()) { return Results.StatusCode(415); }
        var body = await ClassroomMessageJson.ReadAsync<JsonElement>(context.Request.Body, 2048, context.RequestAborted).ConfigureAwait(false);
        var ack = body.Deserialize<ClassroomConfigurationAck>(ClassroomControlWire.Json);
        if (ack is null) { return Results.BadRequest(); }
        lock (_gate)
        {
            if (!Authorize(context, out Guid current, out _)) { return Results.StatusCode(context.Response.StatusCode); }
            if (current != actor) { return Results.Unauthorized(); }
            var expected = _control.FindConfiguration(ack.ChangeId);
            if (ack.Stage == ClassroomAckStage.Applied && ack.FirstBlockSequence is { } first && first >= _ring.OldestBlockSequence)
            {
                var captured = _ring.ReadFrom(first, 1);
                if (expected is null || captured.Blocks.Count != 1 || captured.Blocks[0].ConfigurationRevision != expected.BaseConfigurationRevision + 1)
                { return Results.Conflict(); }
            }
            string? before = expected?.State;
            if (!_control.Acknowledge(actor, ack, _ring.NextBlockSequence, _ring.NextBlockStartSimTimeNs)) { return Results.Conflict(); }
            if (_control.FindConfiguration(ack.ChangeId) is { } change && change.State != before)
            { PublishScheduledControl(change); }
            return Results.NoContent();
        }
    }
}

public sealed partial class ClassroomStudentClient
{
    public async Task<ClassroomConfigurationStatus> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.GetAsync("v1/configuration", HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        JsonElement document = await ClassroomMessageJson.ReadAsync<JsonElement>(response, 256 * 1024, cancellationToken).ConfigureAwait(false);
        var state = document.Deserialize<ClassroomConfigurationStatus>(ClassroomControlWire.Json);
        if (state is null || state.Active is null || state.Changes is null || state.Changes.Length > 17 ||
            state.RequiredChanges is null || state.RequiredChanges.Keys.Except(state.Changes.Select(c => c.ChangeId)).Any())
        { throw new InvalidDataException("Invalid classroom configuration queue."); }
        _ = state.Active.Snapshot();
        foreach (ClassroomScheduledControl change in state.Changes) { VerifyScheduledContent(change); }
        return state;
    }

    public async Task ReportConfigurationAckAsync(ClassroomConfigurationAck ack, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ack);
        using HttpResponseMessage response = await _http.PostAsJsonAsync("v1/configuration/ack", ack, ClassroomControlWire.Json, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static void VerifyScheduledContent(ClassroomScheduledControl change)
    {
        if (change.AllowedActionIds is null || change.AllowedActionIds.Length > RegisteredActionIds.Count ||
            change.AllowedActionIds.Any(id => id is null || !ClassroomControlCatalog.Catalog.ContainsKey(id)) ||
            !change.AllowedActionIds.SequenceEqual(change.AllowedActionIds.Distinct().Order(StringComparer.Ordinal)) ||
            change.Content is { } content && !content.Snapshot().AllowedActionIds.SequenceEqual(change.AllowedActionIds) ||
            (change.Content?.ComputeHash() ?? ClassroomControlAuthority.ComputePolicyHash(change.AllowedActionIds)) != change.PublicDiffSha256)
        { throw new InvalidDataException("Invalid staged classroom content."); }
    }
}
