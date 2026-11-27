// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Network;

internal sealed record ClassroomControlPrepared(Guid ChangeId, string PublicDiffSha256);
internal sealed record ClassroomControlScheduleReply(ClassroomScheduledControl? Change);

public sealed partial class ClassroomHost
{
    private readonly ClassroomControlAuthority _control;
    private bool _monitoringHeldInput;

    public void DesignateOperator(Guid? clientId)
    {
        lock (_gate)
        {
            EnsureControlOpen();
            if (clientId is { } id && _branches.ContainsKey(id)) { throw new InvalidOperationException("The endpoint is assigned to a private branch."); }
            _control.Designate(clientId, HostMonotonicNs);
        }
    }

    public void BindActionReducer(IClassroomActionReducer reducer)
    {
        lock (_gate) { EnsureControlOpen(); _control.BindReducer(reducer, HostMonotonicNs); }
    }

    public ClassroomControlSnapshot ReadControlSnapshot()
    {
        lock (_gate) { return _control.Snapshot(); }
    }

    public ClassroomActionResult SubmitTeacherAction(ClassroomActionIntent intent)
    {
        lock (_gate)
        {
            EnsureControlOpen();
            return CommitClassroomAction(_control.TeacherId, intent);
        }
    }

    public ClassroomScheduledControl ScheduleActionPolicy(Guid changeId, ulong basedOnConfigurationRevision, long effectiveSimTimeNs,
        IReadOnlyList<string> allowedActionIds, IReadOnlyList<Guid> requiredClients, int preparedQuorum)
    {
        lock (_gate)
        {
            EnsureControlOpen();
            var change = _control.Schedule(changeId, basedOnConfigurationRevision, effectiveSimTimeNs, allowedActionIds, requiredClients, preparedQuorum);
            PublishScheduledControl(change);
            return change;
        }
    }

    public bool CancelActionPolicy(Guid changeId)
    {
        lock (_gate)
        {
            EnsureControlOpen();
            var affected = _control.Configuration().Changes.Where(c => c.State is "Prepared" or "Scheduled").ToArray();
            if (!_control.Cancel(changeId)) { return false; }
            foreach (var prior in affected)
            {
                if (_control.FindConfiguration(prior.ChangeId) is { State: "Cancelled" } cancelled) { PublishScheduledControl(cancelled); }
            }
            return true;
        }
    }

    private void EnsureControlOpen()
    {
        ObjectDisposedException.ThrowIf(_disposal is not null, this);
        if (_admission.Ended) { throw new InvalidOperationException("Classroom session has ended."); }
    }

    private void PublishScheduledControl(ClassroomScheduledControl change)
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            schema_id = "ScheduledChange@1",
            change_id = change.ChangeId,
            base_configuration_revision = Decimal(change.BaseConfigurationRevision),
            commit_seq = Decimal(_control.CommitSequence),
            effective_sim_time_ns = Decimal(change.EffectiveSimTimeNs),
            public_diff_sha256 = change.PublicDiffSha256,
            resource_refs = Array.Empty<object>(),
            preparation_policy = change.PreparationPolicy,
            conflict_policy = "RejectOnRevisionMismatch",
            state = change.State
        });
        PublishDomainEvent("configuration.change.scheduled", payload, _ring.NextBlockStartSimTimeNs, change.ChangeId, _control.CommitSequence);
    }

    private void AdvanceControl(long simTimeNs, long safetyTimeNs)
    {
        ClassroomControlPlan? due = _control.Due(simTimeNs);
        PhysiologyWaveformGroup? replacement = null;
        if (_source is not null && due is { Confirmed: true, Change.State: "Prepared" } &&
            due.Change.BaseConfigurationRevision == _control.ConfigurationRevision)
        {
            PhysiologyWaveformGroup? definition = null;
            if (due.Change.Content is { } content && content.HeartRateBpm != _control.Configuration().Active.HeartRateBpm)
            { definition = DefinitionFor(content.HeartRateBpm); }
            replacement = _source.WithConfigurationRevision(checked(_control.ConfigurationRevision + 1), definition);
        }
        _control.Advance(simTimeNs, safetyTimeNs);
        if (due is null || _control.FindConfiguration(due.Change.ChangeId) is not { } change) { return; }
        if (change.State == "Committed" && replacement is not null) { _source = replacement; }
        PublishScheduledControl(change);
        if (change.State != "Committed") { return; }
        var snapshot = JsonSerializer.SerializeToElement(new
        {
            schema_id = "ActiveConfigurationSnapshot@1",
            configuration_revision = Decimal(_control.ConfigurationRevision),
            effective_sim_time_ns = Decimal(change.EffectiveSimTimeNs),
            learner_projection_schema_ref = new { id = change.Content is null ? "ClassroomActionAllowlist" : "ClassroomConfigurationContent", version = "1.0.0", content_sha256 = ConfigurationSchemaHash(change.Content is not null) },
            learner_projection_sha256 = change.PublicDiffSha256,
            configuration_graph_sha256 = change.PublicDiffSha256
        });
        PublishDomainEvent("configuration.active.changed", snapshot, simTimeNs, change.ChangeId, _control.CommitSequence);
        if (change.Content is { } activated)
        {
            foreach (JsonElement role in activated.NamedProjections.Where(p => p.Key != "display.settings").Select(p => p.Value))
            { PublishDomainEvent("display.role.resolved", role, simTimeNs, change.ChangeId, _control.CommitSequence); }
        }
    }

    private PhysiologyWaveformGroup DefinitionFor(int? heartRateBpm)
    {
        var settings = _illustrationConfiguration;
        if (heartRateBpm is { } rate)
        {
            string seed = _ring.SessionId.ToString("N") + _ring.InstanceId.ToString("N");
            settings = settings with { RateAdjustment = new CardiacRateAdjustment(rate, null, seed, 0) };
        }
        return PhysiologyIllustrationSource.CreateForSession(_ring.SessionId, _ring.InstanceId, settings);
    }

    private ClassroomActionResult CommitClassroomAction(Guid actor, ClassroomActionIntent intent)
    {
        if (_admission.Ended) { throw new InvalidOperationException("Classroom session has ended."); }
        ClassroomActionResult result = _control.Submit(actor, intent, HostMonotonicNs,
            actor == _control.TeacherId || _students.ContainsKey(actor), out bool committed, _timeline.Current.RunState == "Running");
        if (_control.HasHeldAction && !_monitoringHeldInput)
        {
            _monitoringHeldInput = true;
            _ = MonitorHeldInputAsync();
        }
        if (committed)
        {
            PublishDomainEvent("action.learner.committed", JsonSerializer.SerializeToElement(result, ClassroomControlWire.Json),
                result.SimTimeNs!.Value, result.IntentId, result.CommitSeq!.Value);
        }
        return result;
    }

    private async Task MonitorHeldInputAsync()
    {
        try
        {
            while (true)
            {
                long delay;
                lock (_gate)
                {
                    long now = HostMonotonicNs;
                    _control.Expire(now);
                    if (_control.HeldDeadline is not { } deadline) { _monitoringHeldInput = false; return; }
                    delay = Math.Max(1, (deadline - now + 99) / 100);
                }
                await Task.Delay(TimeSpan.FromTicks(delay), _stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (Exception)
        {
            // A failed device cancellation invalidates the class. Stop the listener before allowing more actions.
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    private void MapControlRoutes(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v1/actions/projection/{actionId}", (HttpContext ctx, string actionId) =>
        {
            if (!Authorize(ctx, out Guid actor, out _)) { return Results.StatusCode(ctx.Response.StatusCode); }
            if (!ClassroomControlCatalog.Catalog.ContainsKey(actionId)) { return Results.NotFound(); }
            lock (_gate) { return Results.Json(_control.Projection(actionId, actor), ClassroomControlWire.Json); }
        });
        routes.MapGet("/v1/actions/results/{intentId:guid}", (HttpContext ctx, Guid intentId) =>
        {
            if (!Authorize(ctx, out Guid actor, out _)) { return Results.StatusCode(ctx.Response.StatusCode); }
            lock (_gate)
            {
                ClassroomActionResult? receipt = _control.Lookup(actor, intentId);
                return receipt is null ? Results.NotFound() : Results.Json(receipt, ClassroomControlWire.Json);
            }
        });
        routes.MapGet("/v1/actions/state", (HttpContext ctx) =>
        {
            if (!Authorize(ctx, out _, out _)) { return Results.StatusCode(ctx.Response.StatusCode); }
            lock (_gate) { return Results.Json(_control.Snapshot(), ClassroomControlWire.Json); }
        });
        MapConfigurationRoutes(routes);
        routes.MapPost("/v1/actions", (Func<HttpContext, Task<IResult>>)ProcessActionAsync);
        routes.MapGet("/v1/configuration/actions", (HttpContext ctx) =>
        {
            if (!Authorize(ctx, out _, out _)) { return Results.StatusCode(ctx.Response.StatusCode); }
            lock (_gate) { return Results.Json(new ClassroomControlScheduleReply(_control.Pending()), ClassroomControlWire.Json); }
        });
        routes.MapPost("/v1/configuration/actions/prepared", (Func<HttpContext, Task<IResult>>)PrepareActionPolicyAsync);
    }

    private async Task<IResult> ProcessActionAsync(HttpContext context)
    {
        if (!Authorize(context, out Guid actor, out _)) { return Results.StatusCode(context.Response.StatusCode); }
        if (!context.Request.HasJsonContentType()) { return Results.StatusCode(415); }
        try
        {
            var bodyLimit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false }) { bodyLimit.MaxRequestBodySize = 8192; }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            ClassroomActionIntent? intent = await JsonSerializer.DeserializeAsync<ClassroomActionIntent>(context.Request.Body,
                ClassroomControlWire.Json, timeout.Token).ConfigureAwait(false);
            if (intent is null) { return Results.BadRequest(); }
            lock (_gate)
            {
                // Recheck expiry/revocation after reading the body and before the transaction starts.
                if (!Authorize(context, out Guid current, out _)) { return Results.StatusCode(context.Response.StatusCode); }
                if (current != actor) { return Results.Unauthorized(); }
                return Results.Json(CommitClassroomAction(actor, intent), ClassroomControlWire.Json);
            }
        }
        catch (JsonException) { return Results.BadRequest(); }
        catch (InvalidDataException) { return Results.BadRequest(); }
        catch (UnauthorizedAccessException) { return Results.StatusCode(403); }
        catch (OperationCanceledException) { return Results.StatusCode(408); }
    }

    private async Task<IResult> PrepareActionPolicyAsync(HttpContext context)
    {
        if (!Authorize(context, out Guid actor, out _)) { return Results.StatusCode(context.Response.StatusCode); }
        if (!context.Request.HasJsonContentType()) { return Results.StatusCode(415); }
        try
        {
            var bodyLimit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false }) { bodyLimit.MaxRequestBodySize = 8192; }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var ack = await JsonSerializer.DeserializeAsync<ClassroomControlPrepared>(context.Request.Body, ClassroomControlWire.Json,
                timeout.Token).ConfigureAwait(false);
            if (ack is null) { return Results.BadRequest(); }
            lock (_gate)
            {
                if (!Authorize(context, out Guid current, out _)) { return Results.StatusCode(context.Response.StatusCode); }
                if (current != actor) { return Results.Unauthorized(); }
                string? before = _control.PlanState;
                if (!_control.Prepare(actor, ack.ChangeId, ack.PublicDiffSha256)) { return Results.Conflict(); }
                if (before != _control.PlanState) { PublishScheduledControl(_control.Pending()!); }
                return Results.NoContent();
            }
        }
        catch (JsonException) { return Results.BadRequest(); }
        catch (OperationCanceledException) { return Results.StatusCode(408); }
    }
}
