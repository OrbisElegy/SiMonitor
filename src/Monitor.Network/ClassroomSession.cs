// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Network;

internal sealed record ClassroomHello(Guid ClientId, string ProtocolSha256, string DomainPackageSha256, string SampleFormat, bool AllowLz4, JsonElement? Presentation);
public sealed record ClassroomNegotiation(Guid SessionId, Guid InstanceId, Guid CohortId, Guid ClientId, ulong SessionEpoch, ulong AuthorityEpoch,
    string SampleFormat, bool AllowLz4, string DomainPackageSha256, DateTimeOffset TokenExpiresAt, int RemainingSeconds, int RefreshWindowSeconds, JsonElement? SessionManifest);
public sealed record ClassroomMember(Guid ClientId, bool Prepared, long RecommendedDelayNs, long LastSeenNs, JsonElement? Progress);
internal sealed record ClassroomReady(bool Prepared, long RecommendedDelayNs);
internal sealed record ClassroomAccepted(bool Accepted);
internal sealed record ClassroomRefreshRequest(Guid RequestId);
internal sealed record ClassroomRefreshedToken(string Token, DateTimeOffset ExpiresAt, int RemainingSeconds);

public sealed partial class ClassroomHost
{
    private readonly Dictionary<Guid, ClassroomMember> _members = [];
    private JsonElement? _presentationManifest;
    private string[] _presentationCapabilities = [];
    private Guid[] _startingSet = [];
    private bool _managedLesson;

    internal long CurrentWaveformFrontierNs { get { lock (_gate) { return _ring.NextBlockStartSimTimeNs; } } }
    public IReadOnlyList<ClassroomMember> Members { get { lock (_gate) { return _members.Values.ToArray(); } } }
    public IReadOnlyList<Guid> StartingSet { get { lock (_gate) { return _startingSet.ToArray(); } } }

    public void RemoveMember(Guid studentId)
    {
        lock (_gate)
        {
            _admission.RevokeStudent(studentId);
            _members.Remove(studentId);
            _assignments.Remove(studentId);
            _requiredRejoins.Remove(studentId);
            _branchFailures.Remove(studentId);
            if (_sharedRequests.TryGetValue(studentId, out var requests))
            {
                foreach (HttpContext request in requests.ToArray()) { request.Abort(); }
            }
            try { RevokePrivateBranch(studentId); }
            finally { _control.ReleaseDesignation(studentId, HostMonotonicNs); }
        }
    }

    public void ConfigurePresentation(JsonElement manifest, IReadOnlyList<string> requiredCapabilities)
    {
        ArgumentNullException.ThrowIfNull(requiredCapabilities);
        ClassroomContracts.Validate("SessionManifest@1", manifest);
        if (manifest.GetProperty("session_id").GetGuid() != _ring.SessionId || manifest.GetProperty("instance_id").GetGuid() != _ring.InstanceId ||
            manifest.GetProperty("assessment_mode").GetString() != "Teaching" ||
            manifest.GetProperty("cohort_id").ValueKind != JsonValueKind.Null && manifest.GetProperty("cohort_id").GetGuid() != _admission.CohortId ||
            manifest.GetProperty("domain_contract_ref").GetProperty("content_sha256").GetString() != ClassroomContracts.PackageHash ||
            requiredCapabilities.Count > 256 || requiredCapabilities.Any(string.IsNullOrWhiteSpace))
        { throw new InvalidDataException("The presentation manifest does not describe this teaching session."); }
        lock (_gate)
        {
            if (_members.Count != 0 || _timeline.Started) { throw new InvalidOperationException("Publish classroom requirements before enrollment."); }
            _presentationManifest = manifest.Clone();
            _presentationCapabilities = requiredCapabilities.Distinct(StringComparer.Ordinal).ToArray();
        }
    }

    public ClassroomTimebase StartClass(long? fixedPlayoutDelayNs = null)
    {
        lock (_gate)
        {
            if (_timeline.Started) { throw new InvalidOperationException("Classroom already started."); }
            long now = HostMonotonicNs;
            ClassroomMember[] ready = _members.Values.Where(m => m.Prepared && now - m.LastSeenNs <= 5_000_000_000).ToArray();
            long delay = fixedPlayoutDelayNs ?? ready.Select(m => m.RecommendedDelayNs).DefaultIfEmpty(500_000_000).Max();
            var change = _timeline.Schedule("Running", now, _ring.NextBlockStartSimTimeNs, 1, 1, delay);
            _startingSet = ready.Select(m => m.ClientId).ToArray();
            return change;
        }
    }

    public ClassroomTimebase SchedulePlayback(bool running, int rateNumerator = 1, int rateDenominator = 1, long? playoutDelayNs = null)
    {
        lock (_gate)
        {
            if (!_timeline.Started) { throw new InvalidOperationException("Start the class first."); }
            return _timeline.Schedule(running ? "Running" : "Paused", HostMonotonicNs, _ring.NextBlockStartSimTimeNs,
                rateNumerator, rateDenominator, playoutDelayNs ?? _timeline.Current.PlayoutDelayNs);
        }
    }

    public async Task EndSessionAsync()
    {
        lock (_gate)
        {
            if (!_admission.Ended)
            {
                _admission.EndSession();
                _timeline.End(HostMonotonicNs, _ring.NextBlockStartSimTimeNs);
                _eventSignal.TrySetResult();
            }
        }
        await DisposeAsync().ConfigureAwait(false);
    }

    public Task RunIllustrationAsync(CancellationToken cancellationToken) =>
        RunIllustrationAsync(PhysiologyIllustrationConfiguration.Default, cancellationToken);

    public async Task RunIllustrationAsync(PhysiologyIllustrationConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var source = PhysiologyIllustrationSource.CreateForSession(_ring.SessionId, _ring.InstanceId, configuration);
        lock (_gate)
        {
            if (_managedLesson || _ring.Count != 0) { throw new InvalidOperationException("The classroom already has a producer."); }
            _managedLesson = true;
            _illustrationConfiguration = configuration;
            _source = source;
            _measurements = LiveWaveformMeasurements.CreateIllustration();
        }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(40));
        while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false))
        {
            ClassroomHost[] children;
            lock (_gate)
            {
                AdvanceIllustration(stop.Token);
                children = _branches.Values.Select(branch => branch.Host).ToArray();
            }
            foreach (ClassroomHost child in children)
            {
                lock (child._gate) { if (!child._admission.Ended) { child.AdvanceIllustration(stop.Token); } }
            }
        }
    }

    private PhysiologyIllustrationConfiguration _illustrationConfiguration = PhysiologyIllustrationConfiguration.Default;
    private PhysiologyWaveformGroup? _source;
    private LiveWaveformMeasurements? _measurements;

    private void AdvanceIllustration(CancellationToken token)
    {
        bool previouslyRunning = _timeline.Current.RunState == "Running";
        long target = _timeline.Target(HostMonotonicNs);
        if (previouslyRunning && _timeline.Current.RunState != "Running") { _control.CancelInteractions(HostMonotonicNs); }
        long until = Math.Min(target, checked(_ring.NextBlockStartSimTimeNs + 5 * WaveformBlockAssembler.BlockDurationNs));
        while (_ring.NextBlockStartSimTimeNs + WaveformBlockAssembler.BlockDurationNs <= until)
        {
            long boundary = checked(_ring.NextBlockStartSimTimeNs + WaveformBlockAssembler.BlockDurationNs);
            foreach (byte[] wire in _source!.AdvanceTo(boundary, 250, 1, 256, token))
            {
                var measured = _measurements!.Consume(wire, out var beats, out var pulses);
                Publish(wire, out _, envelope => PublishMeasurements(measured, beats, pulses, envelope.BlockSequence, envelope.ConfigurationRevision));
            }
            if (_ring.NextBlockStartSimTimeNs < boundary) { throw new InvalidOperationException("Waveform source missed a classroom block boundary."); }
            if (_parent is null) { ApplyBranchTransitions(boundary); }
        }
    }

    private void MapClassroomRoutes(IEndpointRouteBuilder routes)
    {
        routes.MapPost("/v1/hello", NegotiateAsync).WithMetadata(ClassroomEndpointAccess.Enrollment);
        routes.MapPost("/v1/ready", AcceptReadyAsync);
        routes.MapPost("/v1/progress", AcceptProgressAsync);
        routes.MapPost("/v1/progress/receiver", ReceiveHeadlessProgressAsync);
        routes.MapPost("/v1/refresh", RefreshAsync).WithMetadata(ClassroomEndpointAccess.BranchAccess);
        routes.MapGet("/v1/events", ReadEventsAsync);
        routes.MapGet("/v1/telemetry", async context =>
        {
            if (Authorize(context, out _, out _))
            {
                await context.Response.WriteAsJsonAsync(CurrentTelemetrySnapshot(), context.RequestAborted).ConfigureAwait(false);
            }
        });
    }

    private bool MatchesPresentation(JsonElement hello)
    {
        if (_presentationManifest is not { } manifest) { return true; }
        var capabilities = hello.GetProperty("capability_ids").EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal);
        var resources = hello.GetProperty("resource_pack_refs").EnumerateArray().ToArray();
        if (!_presentationCapabilities.All(capabilities.Contains)) { return false; }
        foreach (string field in new[] { "institution_brand_pack_ref", "presentation_profile_ref", "action_catalog_ref", "skin_ref", "system_readability_floor_ref" })
        {
            JsonElement expected = manifest.GetProperty(field);
            if (expected.ValueKind != JsonValueKind.Null && !resources.Any(resource => JsonElement.DeepEquals(expected, resource))) { return false; }
        }
        var viewport = hello.GetProperty("viewport");
        var rect = viewport.GetProperty("available_client_rect_dip");
        var insets = viewport.GetProperty("safe_insets_px");
        double scale = viewport.GetProperty("device_pixel_ratio").GetDouble();
        return viewport.GetProperty("orientation").GetString() == "Landscape" &&
            rect.GetProperty("width").GetInt32() - (insets.GetProperty("left").GetInt32() + insets.GetProperty("right").GetInt32()) / scale >= 1366 &&
            rect.GetProperty("height").GetInt32() - (insets.GetProperty("top").GetInt32() + insets.GetProperty("bottom").GetInt32()) / scale >= 768 &&
            viewport.GetProperty("input_modalities").EnumerateArray().Any(modality => modality.GetString() is "Keyboard" or "Mouse");
    }

    private async Task NegotiateAsync(HttpContext context)
    {
        if (!Authorize(context, out var student, out var expires)) { return; }
        ClassroomHello hello = await ClassroomMessageJson.ReadAsync<ClassroomHello>(context.Request.Body, 64 * 1024, context.RequestAborted).ConfigureAwait(false);
        if (hello.ClientId != student || hello.ProtocolSha256 != ClassroomContracts.ProtocolHash || hello.DomainPackageSha256 != ClassroomContracts.PackageHash || hello.SampleFormat != "raw-int16")
        { context.Response.StatusCode = 422; return; }
        if (hello.Presentation is { } presentation)
        {
            ClassroomContracts.Validate("ClientHello@1", presentation);
            if (presentation.GetProperty("client_id").GetGuid() != student ||
                presentation.GetProperty("requested_role").GetString() is not ("Student" or "Observer") ||
                presentation.GetProperty("protocol_ref").GetProperty("id").GetString() != ClassroomWire.ProtocolId ||
                presentation.GetProperty("protocol_ref").GetProperty("version").GetString() != ClassroomWire.ProtocolVersion ||
                presentation.GetProperty("protocol_ref").GetProperty("content_sha256").GetString() != ClassroomContracts.ProtocolHash ||
                presentation.GetProperty("domain_contract_ref").GetProperty("content_sha256").GetString() != ClassroomContracts.PackageHash ||
                !presentation.GetProperty("capability_ids").EnumerateArray().Any(x => x.GetString() == "raw-int16"))
            { context.Response.StatusCode = 422; return; }
        }
        ClassroomNegotiation negotiation;
        lock (_gate)
        {
            if (!Authorize(context, out _, out _)) { return; }
            if (_presentationManifest is not null && (hello.Presentation is not { } gui || !MatchesPresentation(gui)))
            { context.Response.StatusCode = 422; return; }
            bool newMember = !_members.ContainsKey(student);
            if (newMember && _members.Count >= MaximumStudents) { context.Response.StatusCode = 429; return; }
            string token = ReadBearer(context);
            if (!_admission.CompleteNegotiation(token, hello.AllowLz4)) { context.Response.StatusCode = 401; return; }
            if (newMember) { _members.Add(student, new ClassroomMember(student, false, 500_000_000, HostMonotonicNs, null)); }
            negotiation = new(_ring.SessionId, _ring.InstanceId, _admission.CohortId, student, _admission.SessionEpoch, AuthorityEpoch, hello.SampleFormat, hello.AllowLz4, ClassroomContracts.PackageHash, expires, (int)Math.Clamp((expires - _admission.Clock.GetUtcNow()).TotalSeconds, 0, _admission.Policy.TokenLifetimeSeconds), _admission.Policy.RefreshWindowSeconds, _presentationManifest);
        }
        await context.Response.WriteAsJsonAsync(negotiation, context.RequestAborted).ConfigureAwait(false);
    }

    private async Task AcceptReadyAsync(HttpContext context)
    {
        if (!Authorize(context, out var student, out _)) { return; }
        var ready = await ClassroomMessageJson.ReadAsync<ClassroomReady>(context.Request.Body, 4096, context.RequestAborted).ConfigureAwait(false);
        if (ready.RecommendedDelayNs is < 100_000_000 or > 1_500_000_000) { context.Response.StatusCode = 422; return; }
        lock (_gate)
        {
            if (!Authorize(context, out _, out _) || !_members.TryGetValue(student, out var member)) { return; }
            _members[student] = member with { Prepared = ready.Prepared, LastSeenNs = HostMonotonicNs, RecommendedDelayNs = ready.RecommendedDelayNs };
        }
        await context.Response.WriteAsJsonAsync(new ClassroomAccepted(true), context.RequestAborted).ConfigureAwait(false);
    }

    private async Task AcceptProgressAsync(HttpContext context)
    {
        if (!Authorize(context, out var student, out _)) { return; }
        JsonElement progress = await ClassroomMessageJson.ReadAsync<JsonElement>(context.Request.Body, 16 * 1024, context.RequestAborted).ConfigureAwait(false);
        ClassroomContracts.Validate("ClientProgress@1", progress);
        lock (_gate)
        {
            bool InOrder(string field, JsonElement before) => UInt(progress, field) >= UInt(before, field);
            static ulong UInt(JsonElement source, string field) => ulong.Parse(source.GetProperty(field).GetString()!, CultureInfo.InvariantCulture);
            static long? Ns(JsonElement source, string field) => source.GetProperty(field).GetString() is string number
                ? long.Parse(number, CultureInfo.InvariantCulture) : null;
            long? from = Ns(progress, "buffered_from_sim_time_ns");
            long? until = Ns(progress, "buffered_until_sim_time_ns");
            if (progress.GetProperty("client_id").GetGuid() != student || progress.GetProperty("session_id").GetGuid() != _ring.SessionId ||
                progress.GetProperty("instance_id").GetGuid() != _ring.InstanceId || UInt(progress, "authority_epoch") != AuthorityEpoch ||
                UInt(progress, "last_applied_event_seq") >= _nextEvent || UInt(progress, "last_applied_commit_seq") > Math.Max(_eventCommit, _control.CommitSequence) ||
                UInt(progress, "last_applied_waveform_block_seq") > (_ring.NextBlockSequence == 0 ? 0 : _ring.NextBlockSequence - 1) ||
                (from.HasValue != until.HasValue) || from < 0 || from > until || until > _ring.NextBlockStartSimTimeNs ||
                Ns(progress, "playhead_sim_time_ns") < 0 || Ns(progress, "playhead_sim_time_ns") > _ring.NextBlockStartSimTimeNs)
            { context.Response.StatusCode = 422; return; }
            if (!Authorize(context, out _, out _) || !_members.TryGetValue(student, out var previous)) { return; }
            if (previous.Progress is { } prior && prior.TryGetProperty("reported_at_authority_monotonic_ns", out _) &&
                (Ns(progress, "reported_at_authority_monotonic_ns") < Ns(prior, "reported_at_authority_monotonic_ns") ||
                 !InOrder("last_applied_event_seq", prior) || !InOrder("last_applied_commit_seq", prior) ||
                 !InOrder("last_applied_plan_revision", prior) || !InOrder("last_applied_waveform_block_seq", prior)))
            { context.Response.StatusCode = 409; return; }
            _members[student] = previous with
            {
                Progress = progress.Clone(),
                LastSeenNs = HostMonotonicNs,
                Prepared = previous.Prepared && progress.GetProperty("sync_health").GetString() == "Healthy"
            };
        }
        await context.Response.WriteAsJsonAsync(new ClassroomAccepted(true), context.RequestAborted).ConfigureAwait(false);
    }

    private async Task RefreshAsync(HttpContext context)
    {
        if (!Authorize(context, out _, out _)) { return; }
        string old = ReadBearer(context);
        var request = await ClassroomMessageJson.ReadAsync<ClassroomRefreshRequest>(context.Request.Body, 4096, context.RequestAborted).ConfigureAwait(false);
        if (!_admission.TryRefresh(old, request.RequestId, out string? replacement)) { context.Response.StatusCode = 409; return; }
        if (!_admission.TryAuthorize(replacement!, out _, out var expires)) { context.Response.StatusCode = 401; return; }
        int remaining = (int)Math.Clamp((expires - _admission.Clock.GetUtcNow()).TotalSeconds, 0, _admission.Policy.TokenLifetimeSeconds);
        await context.Response.WriteAsJsonAsync(new ClassroomRefreshedToken(replacement!, expires, remaining), context.RequestAborted).ConfigureAwait(false);
    }
}

public sealed partial class ClassroomStudentClient
{
    private long _refreshAfterNs;
    private JsonElement? _negotiatedHello;
    private readonly SemaphoreSlim _rotate = new(1, 1);
    private Guid _refreshId = Guid.NewGuid();
    internal bool RefreshDue => MonotonicNs() >= Volatile.Read(ref _refreshAfterNs);
    public ClassroomNegotiation? Negotiation { get; private set; }

    public static Task<ClassroomStudentClient> JoinWithPresentationAsync(Uri endpoint, string joinCode, JsonElement hello,
        System.Security.Cryptography.X509Certificates.X509Certificate2? trustedRoot = null, CancellationToken cancellationToken = default) =>
        JoinCoreAsync(endpoint, joinCode, trustedRoot, null, null, null, hello, cancellationToken);

    public Task<ClassroomNegotiation> NegotiateAsync(ClassroomManifest manifest, JsonElement? presentation, CancellationToken cancellationToken = default) =>
        NegotiateAsync(manifest, presentation, allowLz4: true, cancellationToken);

    public async Task<ClassroomNegotiation> NegotiateAsync(ClassroomManifest manifest, JsonElement? presentation, bool allowLz4, CancellationToken cancellationToken = default)
    {
        if (presentation is { } hello) { ClassroomContracts.Validate("ClientHello@1", hello); }
        var request = new ClassroomHello(manifest.StudentId, ClassroomContracts.ProtocolHash, ClassroomContracts.PackageHash, "raw-int16", allowLz4, presentation);
        var result = await ExchangeAsync<ClassroomNegotiation>(HttpMethod.Post, "v1/hello", request, cancellationToken).ConfigureAwait(false);
        if (result.SessionId != manifest.SessionId || result.InstanceId != manifest.InstanceId || result.CohortId == Guid.Empty || result.ClientId != manifest.StudentId ||
            result.DomainPackageSha256 != request.DomainPackageSha256 || result.SampleFormat != request.SampleFormat || result.AllowLz4 && !allowLz4 || result.SessionEpoch == 0)
        { throw new InvalidDataException("Host selected an incompatible classroom profile."); }
        if (result.SessionManifest is { } profile) { ClassroomContracts.Validate("SessionManifest@1", profile); }
        Volatile.Write(ref _refreshAfterNs, checked(MonotonicNs() + Math.Max(1, result.RemainingSeconds - result.RefreshWindowSeconds / 2) * 1_000_000_000L));
        _negotiatedHello = presentation?.Clone();
        Negotiation = result;
        return result;
    }

    public async Task ReportPreparedAsync(bool prepared, long recommendedDelayNs, CancellationToken cancellationToken = default) =>
        _ = await ExchangeAsync<ClassroomAccepted>(HttpMethod.Post, "v1/ready", new ClassroomReady(prepared, recommendedDelayNs), cancellationToken).ConfigureAwait(false);

    public async Task ReportProgressAsync(JsonElement progress, CancellationToken cancellationToken = default)
    {
        ClassroomContracts.Validate("ClientProgress@1", progress);
        _ = await ExchangeAsync<ClassroomAccepted>(HttpMethod.Post, "v1/progress", progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshAccessAsync(CancellationToken cancellationToken = default)
    {
        string? issued = Volatile.Read(ref _token);
        await _rotate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (issued != Volatile.Read(ref _token)) { return; }
            var joined = await ExchangeAsync<ClassroomRefreshedToken>(HttpMethod.Post, "v1/refresh", new ClassroomRefreshRequest(_refreshId), cancellationToken).ConfigureAwait(false);
            if (joined.Token is not { Length: 64 } || !joined.Token.All(char.IsAsciiHexDigit) || joined.RemainingSeconds is < 1 or > 86400)
            { throw new InvalidDataException("Invalid refreshed token."); }
            Volatile.Write(ref _token, joined.Token);
            _refreshId = Guid.NewGuid();
            Volatile.Write(ref _refreshAfterNs, checked(MonotonicNs() + Math.Max(1, joined.RemainingSeconds - (Negotiation?.RefreshWindowSeconds ?? 600) / 2) * 1_000_000_000L));
            if (Negotiation is { } previous) { Negotiation = previous with { TokenExpiresAt = joined.ExpiresAt, RemainingSeconds = joined.RemainingSeconds }; }
        }
        finally { _rotate.Release(); }
    }
}
