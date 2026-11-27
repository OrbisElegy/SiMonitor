// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

public sealed record ClassroomTelemetrySnapshot(Guid SessionId, Guid InstanceId, Guid CohortId,
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong SessionEpoch, ClassroomTimebase Timeline,
    ClassroomTimebase? PendingTimeline, [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong OldestEventSequence,
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong NextEventSequence, IReadOnlyList<JsonElement> Numerics,
    IReadOnlyList<JsonElement> Projections, ClassroomControlSnapshot Control, ClassroomConfigurationContent ActiveConfiguration);

public sealed record ClassroomEventPage(Guid SessionId, Guid InstanceId,
    [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong NextSequence, IReadOnlyList<JsonElement> Events);

public sealed partial class ClassroomHost
{
    private const int MaximumEventBytes = 64 * 1024;
    private const int MaximumHistoryBytes = 4 * 1024 * 1024;
    private readonly Queue<(ulong Sequence, int Size, JsonElement Envelope)> _events = [];
    private readonly Dictionary<string, JsonElement> _numerics = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _projections = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _polling = [];
    private readonly ClassroomTimeline _timeline;
    private int _eventBytes;
    private ulong _nextEvent = 1;
    private ulong _eventCommit;
    private ulong _physiologySequence;
    private TaskCompletionSource _eventSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private long HostMonotonicNs => _clock.Elapsed.Ticks * 100;
    private static string Decimal<T>(T number) where T : IFormattable => number.ToString(null, CultureInfo.InvariantCulture);
    public ClassroomTelemetrySnapshot CurrentTelemetrySnapshot()
    {
        lock (_gate)
        {
            return new ClassroomTelemetrySnapshot(_ring.SessionId, _ring.InstanceId, _admission.CohortId, _admission.SessionEpoch, _timeline.Current,
                _timeline.Pending, _events.TryPeek(out var first) ? first.Sequence : _nextEvent, _nextEvent,
                _numerics.Values.ToArray(), _projections.Values.ToArray(), _control.Snapshot(), _control.Configuration().Active);
        }
    }

    // Host adapters publish registered learner-visible events only. Errors precede changes to replay and snapshots.
    public JsonElement PublishDomainEvent(string eventType, JsonElement payload, long simTimeNs, Guid correlationId, ulong commitSequence)
    {
        if (simTimeNs < 0 || correlationId == Guid.Empty) { throw new ArgumentException("Invalid classroom event position."); }
        if (!ClassroomContracts.EventTypes.TryGetValue(eventType, out string? schemaId) || eventType.StartsWith("assessment.", StringComparison.Ordinal))
        { throw new ArgumentException("Unregistered classroom event type.", nameof(eventType)); }
        ClassroomContracts.Validate(schemaId, payload);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            if (_admission.Ended) { throw new InvalidOperationException("Classroom session has ended."); }
            if (payload.TryGetProperty("instance_id", out var instance) && instance.GetGuid() != _ring.InstanceId)
            { throw new InvalidDataException("Event belongs to another instance."); }
            var envelope = JsonSerializer.SerializeToElement(new
            {
                schema_id = "DomainEventEnvelope",
                schema_major = 1,
                schema_minor = 0,
                event_id = Guid.NewGuid(),
                event_type = eventType,
                session_id = _ring.SessionId,
                cohort_id = _admission.CohortId,
                instance_id = _ring.InstanceId,
                authority_epoch = Decimal(AuthorityEpoch),
                timebase_epoch = Decimal(_ring.TimebaseEpoch),
                stream_epoch = Decimal(_ring.StreamEpoch),
                event_seq = Decimal(_nextEvent),
                commit_seq = Decimal(commitSequence),
                sim_time_ns = Decimal(simTimeNs),
                authority_monotonic_ns = Decimal(HostMonotonicNs),
                correlation_id = correlationId,
                causation_id = (Guid?)null,
                idempotency_key = (string?)null,
                source_component = "ClassroomHost",
                payload_schema_id = schemaId,
                payload
            });
            int size = System.Text.Encoding.UTF8.GetByteCount(envelope.GetRawText());
            if (size > MaximumEventBytes) { throw new InvalidDataException("Classroom event exceeds the transport limit."); }
            if (eventType == "monitoring.numeric.published")
            {
                string channel = NumericChannelKey(payload);
                if (!_numerics.ContainsKey(channel) && _numerics.Count >= 64) { throw new InvalidOperationException("Numeric channel capacity reached."); }
                _numerics[channel] = envelope.GetProperty("payload");
            }
            else
            {
                string? key = eventType switch
                {
                    "display.role.resolved" => eventType + "/" + payload.GetProperty("display_role").GetString(),
                    "monitoring.group.published" => eventType + "/" + payload.GetProperty("group_id").GetString(),
                    "alarm.episode.changed" => eventType + "/" + payload.GetProperty("alarm_episode_id").GetGuid().ToString("D"),
                    "alarm.audio_policy.changed" or "therapy.state.changed" or "configuration.active.changed" or "presentation.sweep_state.changed" => eventType,
                    _ => null
                };
                if (key is not null)
                {
                    if (eventType == "alarm.episode.changed" && payload.GetProperty("released_sim_time_ns").ValueKind != JsonValueKind.Null)
                    { _projections.Remove(key); }
                    else
                    {
                        if (_projections.Count >= 128 && !_projections.ContainsKey(key)) { throw new InvalidOperationException("Projection capacity reached."); }
                        _projections[key] = envelope.GetProperty("payload");
                    }
                }
            }
            _eventCommit = Math.Max(commitSequence, _eventCommit);
            _events.Enqueue((_nextEvent++, size, envelope));
            _eventBytes += size;
            while (_events.Count > 1024 || _eventBytes > MaximumHistoryBytes) { _eventBytes -= _events.Dequeue().Size; }
            _eventSignal.TrySetResult();
            _eventSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return envelope;
        }
    }

    private static string NumericChannelKey(JsonElement payload)
    {
        var channel = payload.GetProperty("measurement_channel_ref");
        return channel.GetProperty("parameter_id").GetString() + "/" + channel.GetProperty("source_instance_id").GetGuid().ToString("D");
    }

    private async Task ReadEventsAsync(HttpContext context)
    {
        if (!Authorize(context, out var student, out _)) { return; }
        if (!ulong.TryParse(context.Request.Query["from"], NumberStyles.None, CultureInfo.InvariantCulture, out ulong from))
        { context.Response.StatusCode = 400; return; }
        lock (_gate) { if (!_polling.Add(student)) { context.Response.StatusCode = 429; return; } }
        try
        {
            Task wait;
            lock (_gate) { wait = from == _nextEvent ? _eventSignal.Task : Task.CompletedTask; }
            try { await wait.WaitAsync(TimeSpan.FromSeconds(1), context.RequestAborted).ConfigureAwait(false); }
            catch (TimeoutException) { }
            if (!Authorize(context, out _, out _)) { return; }
            ClassroomEventPage page;
            lock (_gate)
            {
                if (from > _nextEvent || from < (_events.TryPeek(out var first) ? first.Sequence : _nextEvent))
                { context.Response.StatusCode = 409; return; }
                JsonElement[] batch = _events.Where(entry => entry.Sequence >= from).Take(12).Select(entry => entry.Envelope).ToArray();
                page = new(_ring.SessionId, _ring.InstanceId, from + (ulong)batch.Length, batch);
            }
            await context.Response.WriteAsJsonAsync(page, context.RequestAborted).ConfigureAwait(false);
        }
        finally { lock (_gate) { _polling.Remove(student); } }
    }

    internal void PublishMeasurements(LiveMeasurementSnapshot view, IReadOnlyList<DetectedEcgBeat> beats,
        IReadOnlyList<DetectedPlethPulse> pulses, ulong sequence, ulong revision)
    {
        long sim = view.SampleTimeNs;
        var correlation = Guid.NewGuid();
        ulong publish = checked(sequence + 1);
        ulong commit = _control.CommitSequence;
        void Numeric(string id, string unit, int exponent, WaveformMeasurementStatus status, int? value, long? measured)
        {
            string quality = status switch
            {
                WaveformMeasurementStatus.Valid when value.HasValue => "Valid",
                WaveformMeasurementStatus.Stale when value.HasValue => "Stale",
                WaveformMeasurementStatus.NoData => "NotMeasured",
                WaveformMeasurementStatus.WarmingUp => "Acquiring",
                _ => "Invalid"
            };
            var quantity = quality is "Valid" or "Stale" ? new { mantissa_i64 = Decimal(value!.Value), exponent10 = exponent, unit_code = unit } : null;
            long since = sim;
            lock (_gate)
            {
                if (_numerics.TryGetValue(id + "/" + _ring.InstanceId.ToString("D"), out var previous) && previous.GetProperty("quality").GetProperty("state").GetString() == quality)
                { since = long.Parse(previous.GetProperty("quality").GetProperty("since_sim_time_ns").GetString()!, CultureInfo.InvariantCulture); }
            }
            var payload = JsonSerializer.SerializeToElement(new
            {
                schema_id = "NumericFrame@1",
                measurement_channel_ref = new { parameter_id = id, source_instance_id = _ring.InstanceId },
                publish_seq = Decimal(publish),
                sim_time_ns = Decimal(sim),
                measured_at_sim_time_ns = Decimal(measured ?? sim),
                estimated_at_sim_time_ns = Decimal(sim),
                published_at_sim_time_ns = Decimal(sim),
                source_kind = "DerivedEstimate",
                quantity,
                quality = new { state = quality, reason_codes = Array.Empty<string>(), since_sim_time_ns = Decimal(since) },
                configuration_revision = Decimal(revision)
            });
            PublishDomainEvent("monitoring.numeric.published", payload, sim, correlation, commit);
        }

        if (sequence % 5 == 0)
        {
            Numeric("HR.ECG", "BEATS_PER_MINUTE", -3, view.HeartRate.Status, view.HeartRate.MilliBeatsPerMinute, view.HeartRate.LastBeatTimeNs);
            Numeric("PR.PLETH", "BEATS_PER_MINUTE", -3, view.PulseRate.Status, view.PulseRate.MilliBeatsPerMinute, view.PulseRate.LastPulseTimeNs);
            Numeric("RR.IMP", "BREATHS_PER_MINUTE", -3, view.ImpedanceRespiration.Status, view.ImpedanceRespiration.MilliBreathsPerMinute, view.ImpedanceRespiration.LastBreathTimeNs);
            Numeric("SpO2", "PERCENT", -3, view.SpO2.Status, view.SpO2.SaturationMilliPercent, view.SpO2.MeasuredAtNs);
            Numeric("EtCO2", "MMHG", -2, view.Capnography.EndTidalCentiMmHg.Status, view.Capnography.EndTidalCentiMmHg.Value, view.Capnography.EndTidalCentiMmHg.MeasuredAtNs);
            Numeric("ABP.MAP", "MMHG", -2, view.AbpMean.Status, view.AbpMean.MeanCentiMmHg, view.AbpMean.MeasuredAtNs);
            Numeric("PA.MEAN", "MMHG", -2, view.PaMean.Status, view.PaMean.MeanCentiMmHg, view.PaMean.MeasuredAtNs);
            Numeric("CVP.MEAN", "MMHG", -2, view.CvpMean.Status, view.CvpMean.MeanCentiMmHg, view.CvpMean.MeasuredAtNs);
        }
        // Sort event properties in RFC 8785 order for this fixed, ASCII-only content image.
        var events = beats.Select(beat => (beat.PeakTimeNs, Event: JsonSerializer.SerializeToElement(new
        {
            event_id = Guid.NewGuid(),
            event_type = "QRS",
            qrs_class = "Unknown",
            quality = "Valid",
            sim_time_ns = Decimal(beat.PeakTimeNs),
            source_id = "ECG.Detector"
        }))).Concat(pulses.Select(pulse => (pulse.PeakTimeNs, Event: JsonSerializer.SerializeToElement(new
        {
            event_id = Guid.NewGuid(),
            event_type = "MechanicalBeat",
            pulse_origin = "Intrinsic",
            quality = "Valid",
            sim_time_ns = Decimal(pulse.PeakTimeNs),
            source_id = "PLETH.Detector"
        })))).OrderBy(entry => entry.PeakTimeNs).Select(entry => entry.Event).ToArray();
        if (events.Length == 0) { return; }
        lock (_gate)
        {
            ulong first = _physiologySequence + 1;
            ulong last = checked(_physiologySequence + (ulong)events.Length);
            var payload = JsonSerializer.SerializeToElement(new
            {
                schema_id = "PhysiologyEventBatch@1",
                batch_id = Guid.NewGuid(),
                instance_id = _ring.InstanceId,
                stream_epoch = Decimal(_ring.StreamEpoch),
                first_event_seq = Decimal(first),
                last_event_seq = Decimal(last),
                events,
                content_sha256 = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(events)))
            });
            PublishDomainEvent("physiology.batch.published", payload, sim, correlation, commit);
            _physiologySequence = last;
        }
    }
}

public sealed partial class ClassroomStudentClient
{
    public async Task<ClassroomTelemetrySnapshot> GetTelemetryAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await ExchangeAsync<ClassroomTelemetrySnapshot>(HttpMethod.Get, "v1/telemetry", null, cancellationToken).ConfigureAwait(false);
        if (Negotiation is not { } session || snapshot.SessionId != session.SessionId || snapshot.CohortId != session.CohortId ||
            snapshot.InstanceId != session.InstanceId || snapshot.SessionEpoch != session.SessionEpoch || snapshot.Timeline is null ||
            snapshot.Numerics is null || snapshot.Numerics.Count > 64 || snapshot.Projections is null || snapshot.Projections.Count > 128 ||
            snapshot.OldestEventSequence > snapshot.NextEventSequence)
        { throw new InvalidDataException("Invalid classroom telemetry snapshot."); }
        ClassroomContracts.Validate("TimebaseState@1", snapshot.Timeline.ToTimebaseState());
        foreach (JsonElement frame in snapshot.Numerics) { ClassroomContracts.Validate("NumericFrame@1", frame); }
        foreach (JsonElement projection in snapshot.Projections)
        {
            if (!projection.TryGetProperty("schema_id", out var id) || id.GetString() is not ("DisplayRoleResolutionProjection@1" or
                "MeasurementGroupFrame@1" or "AlarmEpisodeProjection@1" or "AlarmAudioPolicyProjection@1" or "TherapyStateProjection@1" or
                "ActiveConfigurationSnapshot@1" or "SweepStateProjection@1"))
            { throw new InvalidDataException("Unsupported learner projection."); }
            ClassroomContracts.Validate(id.GetString()!, projection);
        }
        return snapshot;
    }

    public async Task<ClassroomEventPage> ReadEventsAsync(ClassroomManifest manifest, ulong fromSequence, CancellationToken cancellationToken = default)
    {
        ClassroomEventPage page = await ExchangeAsync<ClassroomEventPage>(HttpMethod.Get,
            "v1/events?from=" + fromSequence.ToString(CultureInfo.InvariantCulture), null, cancellationToken).ConfigureAwait(false);
        if (page.SessionId != manifest.SessionId || page.InstanceId != manifest.InstanceId || page.Events is null ||
            page.Events.Count > 12 || page.NextSequence != checked(fromSequence + (ulong)page.Events.Count))
        { throw new InvalidDataException("Invalid classroom event cursor."); }
        foreach (JsonElement envelope in page.Events)
        {
            ClassroomContracts.Validate("DomainEventEnvelope@1", envelope);
            if (envelope.GetProperty("cohort_id").GetGuid() != Negotiation?.CohortId || envelope.GetProperty("session_id").GetGuid() != manifest.SessionId || envelope.GetProperty("instance_id").GetGuid() != manifest.InstanceId ||
                envelope.GetProperty("event_seq").GetString() != (fromSequence++).ToString(CultureInfo.InvariantCulture))
            { throw new InvalidDataException("Misbound classroom event."); }
        }
        return page;
    }
}
