// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Monitor.Network;

public enum ClassroomChangeConflict { RejectOnConflict, Queue, ReplacePending }
public enum ClassroomAckStage { Received, Prepared, Applied, Rejected }
public sealed record ClassroomConfigurationAck(Guid ChangeId, string ContentSha256, ClassroomAckStage Stage, string? ReasonCode,
    long? ActualSimTimeNs, ulong? FirstBlockSequence)
{
    public long? DeviationNs { get; init; }
}
public sealed record ClassroomConfigurationContent(string[] AllowedActionIds, int? HeartRateBpm, Dictionary<string, JsonElement> NamedProjections)
{
    public string ComputeHash()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_id", "ClassroomConfigurationContent@1");
            writer.WritePropertyName("allowed_action_ids");
            writer.WriteStartArray();
            foreach (string id in AllowedActionIds.Order(StringComparer.Ordinal)) { writer.WriteStringValue(id); }
            writer.WriteEndArray();
            if (HeartRateBpm is { } rate) { writer.WriteNumber("heart_rate_bpm", rate); } else { writer.WriteNull("heart_rate_bpm"); }
            writer.WriteStartObject("named_projections");
            foreach (var entry in NamedProjections.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(entry.Key);
                WriteValue(writer, entry.Value);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (JsonProperty property in value.EnumerateObject().OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteValue(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (JsonElement element in value.EnumerateArray()) { WriteValue(writer, element); }
            writer.WriteEndArray();
        }
        else if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long integer)) { writer.WriteNumberValue(integer); }
        else { value.WriteTo(writer); }
    }

    internal ClassroomConfigurationContent Snapshot()
    {
        if (AllowedActionIds is null || AllowedActionIds.Length > ClassroomControlCatalog.Catalog.Count ||
            AllowedActionIds.Any(id => id is null || !ClassroomControlCatalog.Catalog.ContainsKey(id)) ||
            AllowedActionIds.Distinct().Count() != AllowedActionIds.Length || HeartRateBpm is < 40 or > 180 ||
            NamedProjections is null || NamedProjections.Count > 8)
        { throw new InvalidDataException("Invalid classroom configuration content."); }
        foreach (var entry in NamedProjections)
        {
            // Configuration may carry pre-resolved learner display bindings. Live medical facts, sealed
            // configuration graphs and assessment materials are never eligible as staged settings.
            if (!ClassroomControlCatalog.Stable(entry.Key) || entry.Value.ValueKind != JsonValueKind.Object ||
                !entry.Value.TryGetProperty("schema_id", out var schema) || schema.ValueKind != JsonValueKind.String)
            { throw new InvalidDataException("Unsupported learner configuration projection."); }
            if (entry.Key == "display.settings")
            {
                if (schema.GetString() != "ClassroomDisplaySettings@1" || entry.Value.EnumerateObject().Count() != 3 ||
                    !entry.Value.TryGetProperty("layout_id", out var layout) || layout.ValueKind != JsonValueKind.String ||
                    layout.GetString() is not ("ThreeRows" or "FourRows" or "FiveRows" or "SevenRows") ||
                    !entry.Value.TryGetProperty("ecg12_theme_id", out var theme) || theme.ValueKind != JsonValueKind.String ||
                    theme.GetString() is not ("MonitorDarkGreen" or "PaperGridBlack"))
                { throw new InvalidDataException("Invalid classroom display configuration."); }
            }
            else { ClassroomContracts.Validate("DisplayRoleResolutionProjection@1", entry.Value); }
        }
        if (JsonSerializer.SerializeToUtf8Bytes(this, ClassroomControlWire.Json).Length > 8192)
        { throw new InvalidDataException("Classroom configuration content exceeds 8 KiB."); }
        return this with
        {
            AllowedActionIds = AllowedActionIds.Order(StringComparer.Ordinal).ToArray(),
            NamedProjections = NamedProjections.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal)
        };
    }
}

public sealed record ClassroomConfigurationStatus(ulong ActiveRevision, ClassroomConfigurationContent Active,
    ClassroomScheduledControl[] Changes)
{
    public Dictionary<Guid, ClassroomConfigurationAck?> RequiredChanges { get; init; } = [];
}

internal sealed partial class ClassroomControlAuthority
{
    private static bool PendingChange(ClassroomControlPlan? plan) => plan?.Change.State is "Scheduled" or "Prepared";
    private static ClassroomScheduledControl CopyChange(ClassroomScheduledControl change) =>
        change with { AllowedActionIds = (string[])change.AllowedActionIds.Clone(), Content = change.Content?.Snapshot() };

    internal ClassroomConfigurationStatus Configuration(Guid? actor = null)
    {
        ClassroomConfigurationContent active = _state.ActiveContent?.Snapshot() ?? new((string[])_state.Allowed.Clone(), null, []);
        return new(_state.ConfigRevision, active, AllPlans().Select(p => CopyChange(p.Change)).ToArray())
        {
            RequiredChanges = actor is { } id ? AllPlans().Where(p => p.Required.Contains(id)).ToDictionary(
                p => p.Change.ChangeId, p => p.Acknowledgements.GetValueOrDefault(id)) : []
        };
    }

    internal ClassroomScheduledControl? FindConfiguration(Guid changeId) =>
        AllPlans().FirstOrDefault(p => p.Change.ChangeId == changeId) is { } plan ? CopyChange(plan.Change) : null;

    internal ClassroomControlPlan? Due(long boundary)
    {
        ClassroomControlPlan? next = PendingChange(_state.Plan) ? _state.Plan : _state.Queued.FirstOrDefault();
        return next is not null && next.Change.EffectiveSimTimeNs <= boundary ? next : null;
    }

    private IEnumerable<ClassroomControlPlan> AllPlans() =>
        _state.Finished.Concat(_state.Plan is { } head ? [head] : Array.Empty<ClassroomControlPlan>()).Concat(_state.Queued);

    private void PromoteQueued()
    {
        if (PendingChange(_state.Plan) || _state.Queued.Length == 0) { return; }
        Save(_state with
        {
            Finished = Archive(_state.Plan),
            Plan = _state.Queued[0],
            Queued = _state.Queued[1..]
        });
    }

    private ClassroomControlPlan[] Archive(ClassroomControlPlan? plan) =>
        plan is null ? _state.Finished : _state.Finished.Append(plan).TakeLast(8).ToArray();

    internal ClassroomScheduledControl ScheduleConfiguration(Guid changeId, ulong basedOn, long due, ClassroomConfigurationContent content,
        IReadOnlyList<Guid> required, int quorum, ClassroomChangeConflict conflict, bool requireTeacher)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(required);
        content = content.Snapshot();
        foreach (var entry in content.NamedProjections.Where(pair => pair.Key != "display.settings"))
        {
            JsonElement channel = entry.Value.GetProperty("resolved_measurement_channel_ref");
            if (channel.ValueKind != JsonValueKind.Null && channel.GetProperty("source_instance_id").GetGuid() != _instanceId)
            { throw new InvalidDataException("The staged display role references a different instance."); }
        }
        if (!Enum.IsDefined(conflict) || changeId == Guid.Empty || AllPlans().Any(p => p.Change.ChangeId == changeId) ||
            (Int128)due - _simTimeNs < 1_000_000_000 || due % 200_000_000 != 0 || required.Count > 36 || required.Contains(Guid.Empty) ||
            required.Distinct().Count() != required.Count || quorum < 0 || quorum > required.Count)
        { throw new ArgumentException("Invalid configuration revision, preparation window, or required set."); }

        PromoteQueued();
        bool pending = PendingChange(_state.Plan);
        int preceding = pending && conflict == ClassroomChangeConflict.Queue ? _state.Queued.Length + 1 : 0;
        if (pending && conflict == ClassroomChangeConflict.RejectOnConflict || preceding >= 8 ||
            basedOn != checked(_state.ConfigRevision + (ulong)preceding) ||
            preceding > 0 && due <= (_state.Queued.LastOrDefault() ?? _state.Plan!).Change.EffectiveSimTimeNs)
        { throw new InvalidOperationException("Configuration revision or queue conflict."); }

        ulong commit = checked(_state.Commit + 1);
        var change = new ClassroomScheduledControl(changeId, basedOn, commit, due, content.ComputeHash(),
            quorum == 0 ? "BestEffortTeachingOnly" : quorum == required.Count ? "RequirePreparedAll" : "RequirePreparedQuorum",
            quorum == 0 ? "Prepared" : "Scheduled", content.AllowedActionIds)
        { Content = content, RequiresTeacherConfirmation = requireTeacher };
        var plan = new ClassroomControlPlan(change, required.ToArray(), [], quorum) { Confirmed = !requireTeacher };
        if (preceding != 0) { Save(_state with { Commit = commit, Queued = [.. _state.Queued, plan] }); }
        else
        {
            ClassroomControlPlan[] completed = pending
                ? AllPlans().Select(p => PendingChange(p) ? p with { Change = p.Change with { State = "Cancelled" } } : p).TakeLast(8).ToArray()
                : Archive(_state.Plan);
            Save(_state with { Commit = commit, Plan = plan, Queued = [], Finished = completed });
        }
        return CopyChange(change);
    }

    internal bool Confirm(Guid changeId)
    {
        var plan = AllPlans().SingleOrDefault(p => p.Change.ChangeId == changeId);
        if (!PendingChange(plan)) { return false; }
        if (!plan!.Confirmed) { Replace(plan with { Confirmed = true }); }
        return true;
    }

    internal bool Acknowledge(Guid endpoint, ClassroomConfigurationAck ack, ulong nextSequence, long latestSimTimeNs)
    {
        var plan = AllPlans().SingleOrDefault(p => p.Change.ChangeId == ack.ChangeId);
        if (plan is null || !Enum.IsDefined(ack.Stage) || plan.Change.PublicDiffSha256 != ack.ContentSha256 ||
            !plan.Required.Contains(endpoint)) { return false; }
        if (ack.Stage == ClassroomAckStage.Applied && ack.ActualSimTimeNs is { } applied && applied >= plan.Change.EffectiveSimTimeNs)
        {
            long deviation = applied - plan.Change.EffectiveSimTimeNs;
            if (ack.DeviationNs is { } claimed && claimed != deviation) { return false; }
            ack = ack with { DeviationNs = deviation };
        }
        else if (ack.DeviationNs.HasValue) { return false; }
        plan.Acknowledgements.TryGetValue(endpoint, out ClassroomConfigurationAck? previous);
        if (previous == ack) { return true; }
        if (ack.Stage == ClassroomAckStage.Applied)
        {
            if (plan.Change.State != "Committed" || previous?.Stage != ClassroomAckStage.Prepared || ack.ReasonCode is not null ||
                ack.ActualSimTimeNs is not { } time || time < plan.Change.EffectiveSimTimeNs || (Int128)time - latestSimTimeNs > 10_000_000_000 ||
                ack.FirstBlockSequence is not { } first || first >= nextSequence) { return false; }
            Int128 blockStart = (Int128)latestSimTimeNs - (Int128)(nextSequence - first) * 200_000_000;
            if (blockStart < plan.Change.EffectiveSimTimeNs || blockStart > time || AllPlans().Any(other =>
                other.Change.State == "Committed" && other.Change.BaseConfigurationRevision > plan.Change.BaseConfigurationRevision &&
                other.Change.EffectiveSimTimeNs <= blockStart))
            { return false; }
        }
        else if (!PendingChange(plan) || ack.ActualSimTimeNs.HasValue || ack.FirstBlockSequence.HasValue ||
            (ack.Stage == ClassroomAckStage.Rejected ? !ClassroomControlCatalog.Stable(ack.ReasonCode) : ack.ReasonCode != null) ||
            previous?.Stage is ClassroomAckStage.Rejected or ClassroomAckStage.Applied ||
            previous?.Stage == ClassroomAckStage.Prepared && ack.Stage == ClassroomAckStage.Received)
        { return false; }

        var responses = new Dictionary<Guid, ClassroomConfigurationAck>(plan.Acknowledgements) { [endpoint] = ack };
        Guid[] prepared = responses.Where(pair => pair.Value.Stage is ClassroomAckStage.Prepared or ClassroomAckStage.Applied)
            .Select(pair => pair.Key).ToArray();
        string state = PendingChange(plan) ? (prepared.Length >= plan.Quorum ? "Prepared" : "Scheduled") : plan.Change.State;
        Replace(plan with { Acknowledgements = responses, Prepared = prepared, Change = plan.Change with { State = state } });
        return true;
    }

    internal IReadOnlyDictionary<Guid, ClassroomConfigurationAck> Acks(Guid changeId)
    {
        var plan = AllPlans().SingleOrDefault(p => p.Change.ChangeId == changeId) ?? throw new KeyNotFoundException();
        return new Dictionary<Guid, ClassroomConfigurationAck>(plan.Acknowledgements);
    }

    private void Replace(ClassroomControlPlan plan)
    {
        Guid id = plan.Change.ChangeId;
        Save(_state with
        {
            Commit = checked(_state.Commit + 1),
            Plan = _state.Plan?.Change.ChangeId == id ? plan : _state.Plan,
            Finished = _state.Finished.Select(p => p.Change.ChangeId == id ? plan : p).ToArray(),
            Queued = _state.Queued.Select(p => p.Change.ChangeId == id ? plan : p).ToArray()
        });
    }
}
