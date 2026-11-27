// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Network;

public enum ClassroomBranchDisposition { Archive, Discard }

public sealed record ClassroomBranchAssignment(Guid StudentId, Guid SessionId, Guid SharedInstanceId, Guid InstanceId,
    string State, long EffectiveSimTimeNs, string? Reason);
internal sealed record ClassroomBranchCredential(Guid SessionId, Guid InstanceId, string JoinCode);

public sealed partial class ClassroomHost
{
    internal const string InstanceHeader = "X-SiMonitor-Instance";
    private const int MaximumInstances = 40;
    private readonly ClassroomBootstrap _identity;
    private readonly ClassroomHost? _parent;
    private readonly Dictionary<Guid, PrivateBranch> _branches = [];
    private readonly List<Task> _retiringBranches = [];
    private sealed record SharedRejoin(ulong First, ulong After);
    private readonly Dictionary<Guid, SharedRejoin?> _requiredRejoins = [];
    private readonly Dictionary<Guid, ClassroomBranchAssignment> _assignments = [];
    private readonly Dictionary<Guid, string> _branchFailures = [];
    private readonly Dictionary<Guid, HashSet<HttpContext>> _sharedRequests = [];
    private sealed record PrivateBranch(ClassroomHost Host, RequestDelegate Router, long ForkSimTimeNs);

    public ClassroomBranchAssignment GetBranchAssignment(Guid studentId)
    {
        lock (_gate)
        {
            if (_parent is not null || !_members.ContainsKey(studentId)) { throw new ArgumentException("Unknown shared classroom member.", nameof(studentId)); }
            return _assignments.TryGetValue(studentId, out var assignment) ? assignment :
                new ClassroomBranchAssignment(studentId, _ring.SessionId, _ring.InstanceId, _ring.InstanceId, "Shared", _ring.NextBlockStartSimTimeNs, null);
        }
    }

    public string? GetBranchFailure(Guid studentId)
    {
        lock (_gate) { return _branchFailures.GetValueOrDefault(studentId); }
    }

    public ClassroomBranchAssignment SchedulePrivateBranch(Guid studentId, long? effectiveSimTimeNs = null)
    {
        lock (_gate)
        {
            ClassroomBranchAssignment current = GetBranchAssignment(studentId);
            if (_source is null || _admission.Ended || _timeline.Current.RunState != "Running" ||
                current.State is not ("Shared" or "ForkRejected") || _branches.ContainsKey(studentId) || _requiredRejoins.ContainsKey(studentId))
            { throw new InvalidOperationException("The student cannot fork the shared simulation now."); }
            if (1 + _branches.Count + _assignments.Values.Count(a => a.State == "ForkPending") >= MaximumInstances)
            { throw new InvalidOperationException("The classroom instance limit has been reached."); }
            var assignment = current with { InstanceId = Guid.NewGuid(), State = "ForkPending", EffectiveSimTimeNs = NextBranchBoundary(effectiveSimTimeNs), Reason = null };
            _assignments[studentId] = assignment;
            return assignment;
        }
    }

    public ClassroomBranchAssignment ScheduleSharedReturn(Guid studentId, ClassroomBranchDisposition disposition, long? effectiveSimTimeNs = null)
    {
        if (!Enum.IsDefined(disposition)) { throw new ArgumentOutOfRangeException(nameof(disposition)); }
        lock (_gate)
        {
            ClassroomBranchAssignment current = GetBranchAssignment(studentId);
            if (!_branches.ContainsKey(studentId) || current.State is not ("Detached" or "ReturnRejected") ||
                _admission.Ended || !_timeline.Started)
            { throw new InvalidOperationException("There is no active private branch to return."); }
            var assignment = current with
            {
                State = disposition == ClassroomBranchDisposition.Archive ? "ArchivePending" : "DiscardPending",
                EffectiveSimTimeNs = NextBranchBoundary(effectiveSimTimeNs),
                Reason = null
            };
            _assignments[studentId] = assignment;
            return assignment;
        }
    }

    public ClassroomTelemetrySnapshot GetPrivateTelemetry(Guid studentId)
    {
        lock (_gate) { return GetPrivateHost(studentId).CurrentTelemetrySnapshot(); }
    }

    public ClassroomControlSnapshot GetPrivateControlSnapshot(Guid studentId)
    {
        lock (_gate) { return GetPrivateHost(studentId).ReadControlSnapshot(); }
    }

    public ClassroomScheduledControl SchedulePrivateConfiguration(Guid studentId, Guid changeId, ulong basedOnConfigurationRevision,
        long effectiveSimTimeNs, ClassroomConfigurationContent content, IReadOnlyList<Guid> requiredClients, int preparedQuorum,
        ClassroomChangeConflict conflict = ClassroomChangeConflict.RejectOnConflict, bool requireTeacherConfirmation = false)
    {
        lock (_gate)
        {
            return GetPrivateHost(studentId).ScheduleConfiguration(changeId, basedOnConfigurationRevision, effectiveSimTimeNs,
                content, requiredClients, preparedQuorum, conflict, requireTeacherConfirmation);
        }
    }

    internal ClassroomHost GetPrivateHost(Guid studentId)
    {
        lock (_gate)
        {
            return _branches.TryGetValue(studentId, out var branch) && !branch.Host._admission.Ended ? branch.Host :
                throw new ArgumentException("This student has no private classroom instance.", nameof(studentId));
        }
    }

    private long NextBranchBoundary(long? desired)
    {
        long now = _ring.NextBlockStartSimTimeNs;
        long result = desired ?? checked(now + WaveformBlockAssembler.BlockDurationNs);
        if (result <= now || (result - now) % WaveformBlockAssembler.BlockDurationNs != 0)
        { throw new ArgumentOutOfRangeException(nameof(desired), "Select a future 200 ms sample boundary."); }
        return result;
    }

    private void ApplyBranchTransitions(long boundary)
    {
        foreach (var (studentId, change) in _assignments.ToArray())
        {
            if (change.EffectiveSimTimeNs != boundary || change.State is not ("ForkPending" or "ArchivePending" or "DiscardPending")) { continue; }
            try
            {
                _branchFailures.Remove(studentId);
                if (change.State == "ForkPending") { ActivateBranch(studentId, change); }
                else { ReturnBranch(studentId, change); }
            }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException or ArgumentException or
                IOException or SqliteException or CryptographicException or NotSupportedException or JsonException or OverflowException)
            {
                _branchFailures[studentId] = error.Message;
                _assignments[studentId] = change with
                {
                    State = change.State == "ForkPending" ? "ForkRejected" : "ReturnRejected",
                    InstanceId = _branches.TryGetValue(studentId, out var branch) ? branch.Host._ring.InstanceId : _ring.InstanceId,
                    Reason = change.State == "ForkPending" ? "BRANCH_FORK_REJECTED" : "BRANCH_RETURN_REJECTED"
                };
            }
        }
    }

    private void ActivateBranch(Guid studentId, ClassroomBranchAssignment change)
    {
        Guid instance = change.InstanceId;
        PhysiologyWaveformGroupState state = _source!.CaptureState();
        var source = PhysiologyWaveformGroup.Restore(state with { Assembler = state.Assembler with { InstanceId = instance } });
        var measurements = _measurements!.ForkForInstance(_ring.InstanceId, instance);
        WaveformBlockRingState history = _ring.CaptureState();
        byte[][] released = history.RetainedRawBlocks.Select(bytes => WaveformEnvelopeCodec.EncodeRaw(
            WaveformEnvelopeCodec.Decode(bytes) with { InstanceId = instance })).ToArray();
        var ring = WaveformBlockRing.Restore(history with { InstanceId = instance, RetainedRawBlocks = released });
        var admission = new ClassroomAdmission(_ring.SessionId, instance, _admission.Clock, _admission.Policy, _admission.CohortId);
        ClassroomHost? child = null;
        bool controlForked = false;
        try
        {
            child = new ClassroomHost(_app, admission, ring, _identity, this)
            {
                _managedLesson = true,
                _source = source,
                _illustrationConfiguration = _illustrationConfiguration,
                _measurements = measurements,
                _physiologySequence = _physiologySequence
            };
            child._control.ForkFrom(_control, studentId, change.EffectiveSimTimeNs);
            controlForked = true;
            if (_presentationManifest is { } presentation)
            {
                child._presentationManifest = BindProjection(presentation, _ring.InstanceId, instance);
                child._presentationCapabilities = (string[])_presentationCapabilities.Clone();
            }
            foreach (JsonElement value in _numerics.Values)
            {
                var rebound = BindProjection(value, _ring.InstanceId, instance);
                child._numerics.Add(NumericChannelKey(rebound), rebound);
            }
            foreach (var (key, value) in _projections)
            {
                JsonElement bound = BindProjection(value, _ring.InstanceId, instance);
                if (key == "configuration.active.changed" &&
                    bound.GetProperty("learner_projection_schema_ref").GetProperty("id").GetString() == "ClassroomConfigurationContent")
                {
                    var snapshot = JsonNode.Parse(bound.GetRawText())!.AsObject();
                    string hash = child._control.Configuration().Active.ComputeHash();
                    snapshot["learner_projection_sha256"] = hash;
                    snapshot["configuration_graph_sha256"] = hash;
                    bound = JsonSerializer.SerializeToElement(snapshot);
                }
                child._projections.Add(key, bound);
            }
            var router = new ApplicationBuilder(_app.Services);
            router.UseRouting();
            router.UseEndpoints(child.MapNetworkEndpoints);
            RequestDelegate pipeline = router.Build();

            _control.ReleaseDesignation(studentId, HostMonotonicNs);
            _branches.Add(studentId, new PrivateBranch(child, pipeline, change.EffectiveSimTimeNs));
            _assignments[studentId] = change with { State = "Detached" };
            if (_sharedRequests.TryGetValue(studentId, out var contexts))
            {
                foreach (HttpContext context in contexts.ToArray()) { context.Abort(); }
            }
        }
        catch
        {
            if (child is null) { admission.Dispose(); }
            else
            {
                try { if (controlForked) { child._control.RetireBranch(archive: false); } }
                finally { RetireContext(child); }
            }
            throw;
        }
    }

    private void RetireContext(ClassroomHost child)
    {
        _retiringBranches.RemoveAll(static task => task.IsCompletedSuccessfully);
        _retiringBranches.Add(child.DisposeAsync().AsTask());
    }

    internal static JsonElement BindProjection(JsonElement source, Guid previous, Guid current)
    {
        JsonNode node = JsonNode.Parse(source.GetRawText())!;
        void Bind(JsonNode? item)
        {
            if (item is JsonObject obj)
            {
                foreach (var pair in obj.ToArray())
                {
                    if (pair.Key is "instance_id" or "source_instance_id" && pair.Value is JsonValue scalar &&
                        scalar.TryGetValue<Guid>(out var value) && value == previous) { obj[pair.Key] = current; }
                    else { Bind(pair.Value); }
                }
            }
            else if (item is JsonArray items) { foreach (JsonNode? member in items) { Bind(member); } }
        }
        Bind(node);
        return JsonSerializer.SerializeToElement(node);
    }

    private void ReturnBranch(Guid studentId, ClassroomBranchAssignment change)
    {
        PrivateBranch entry = _branches[studentId];
        ClassroomHost child = entry.Host;
        lock (child._gate)
        {
            // Catch up the private clock through the return barrier before sealing it. Its
            // timeline and source are independent; no private state is applied to the shared host.
            child.AdvanceIllustration(_stopping.Token);
            SealPrivateBranch(entry, studentId, archive: change.State == "ArchivePending");
            child._admission.EndSession();
            child._eventSignal.TrySetResult();
            _branches.Remove(studentId);
            _assignments[studentId] = change with { InstanceId = _ring.InstanceId, State = "Shared" };
            _requiredRejoins[studentId] = null;
            _admission.RequireNewNegotiation(studentId);
            RetireContext(child);
        }
    }

    private void SealPrivateBranch(PrivateBranch entry, Guid studentId, bool archive)
    {
        ClassroomHost child = entry.Host;
        child._control.CancelInteractions(child.HostMonotonicNs);
        ClassroomBranchArchive? snapshot = archive ? CaptureBranchArchive(child, studentId, entry.ForkSimTimeNs) : null;
        child._control.RetireBranch(archive, snapshot is null ? null :
            (database, transaction) => _identity.WriteBranchArchive(database, transaction, snapshot));
    }

    private void RevokePrivateBranch(Guid studentId)
    {
        if (!_branches.TryGetValue(studentId, out var entry)) { return; }
        ClassroomHost child = entry.Host;
        lock (child._gate)
        {
            child._admission.EndSession();
            child._stopping.Cancel();
            child._eventSignal.TrySetResult();
            try { SealPrivateBranch(entry, studentId, archive: true); }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException or ArgumentException or
                IOException or SqliteException or CryptographicException or NotSupportedException or JsonException or OverflowException)
            {
                // Keep the final frozen frontier for a teacher retry. No instance routes or credentials remain active.
                _branchFailures[studentId] = error.Message;
                throw new InvalidOperationException("Student access was revoked. Retry removal to save the private archive.", error);
            }
            _branches.Remove(studentId);
            RetireContext(child);
        }
    }

    // Called under the host publication lock. Only ACKs on the specific subscription
    // opened from this recovery plan may unlock the student's shared writes.
    private void PrepareSharedRejoin(Guid studentId, ulong first, ulong after)
    {
        if (_requiredRejoins.ContainsKey(studentId)) { _requiredRejoins[studentId] = new SharedRejoin(first, after); }
    }

    private void VerifiedSharedRejoin(Guid studentId, SharedRejoin plan, ulong next)
    {
        if (next >= plan.After && _requiredRejoins.TryGetValue(studentId, out var current) && ReferenceEquals(current, plan))
        { _requiredRejoins.Remove(studentId); }
    }

    private void MapBranchRoutes(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v1/branch/assignment", async context =>
        {
            if (!Authorize(context, out Guid student, out _)) { return; }
            ClassroomBranchAssignment assignment;
            lock (_gate)
            {
                if (!_members.ContainsKey(student)) { context.Response.StatusCode = 412; return; }
                assignment = GetBranchAssignment(student);
            }
            await context.Response.WriteAsJsonAsync(assignment, context.RequestAborted).ConfigureAwait(false);
        });
        routes.MapPost("/v1/branch/enter/{id:guid}", async context =>
        {
            if (!Authorize(context, out Guid student, out _)) { return; }
            ClassroomBranchCredential grant;
            lock (_gate)
            {
                if (!_branches.TryGetValue(student, out var branch) ||
                    !Guid.TryParse(context.Request.RouteValues["id"] as string, out var requested) || requested != branch.Host._ring.InstanceId)
                { context.Response.StatusCode = 409; return; }
                string code = branch.Host._admission.ReplaceInvitation(student);
                grant = new ClassroomBranchCredential(_ring.SessionId, requested, code);
            }
            await context.Response.WriteAsJsonAsync(grant, context.RequestAborted).ConfigureAwait(false);
        });
    }

    private async Task DispatchInstanceAsync(HttpContext context, RequestDelegate next)
    {
        var selected = context.Request.Headers[InstanceHeader];
        if (selected.Count > 0)
        {
            PrivateBranch? branch;
            CancellationToken lifetime;
            lock (_gate)
            {
                if (selected.Count != 1 || !Guid.TryParseExact(selected[0], "D", out Guid instance) ||
                    (branch = _branches.Values.FirstOrDefault(entry => entry.Host._ring.InstanceId == instance)) is null || branch.Host._admission.Ended)
                { context.Response.StatusCode = 404; return; }
                lifetime = branch.Host._stopping.Token;
            }
            using var stopRequest = lifetime.Register(context.Abort);
            context.SetEndpoint(null);
            context.Request.RouteValues = new RouteValueDictionary();
            await branch.Router(context).ConfigureAwait(false);
            return;
        }

        Guid studentId = default;
        bool tracked = false;
        lock (_gate)
        {
            if (!context.Request.Path.StartsWithSegments("/v1/branch") && context.Request.Path != "/v1/refresh" &&
                AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var header) &&
                header.Parameter is { } token && _admission.TryAuthorize(token, out studentId, out _))
            {
                if (_branches.ContainsKey(studentId)) { context.Response.StatusCode = 409; return; }
                if (!_sharedRequests.TryGetValue(studentId, out var requests)) { _sharedRequests.Add(studentId, requests = []); }
                requests.Add(context);
                tracked = true;
            }
        }
        try { await next(context).ConfigureAwait(false); }
        finally
        {
            if (tracked)
            {
                lock (_gate)
                {
                    var outstanding = _sharedRequests[studentId];
                    outstanding.Remove(context);
                    if (outstanding.Count == 0) { _sharedRequests.Remove(studentId); }
                }
            }
        }
    }
}
