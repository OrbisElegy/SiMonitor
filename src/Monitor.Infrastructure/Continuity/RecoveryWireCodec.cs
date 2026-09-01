// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Monitor.Domain.Continuity;

namespace Monitor.Infrastructure.Continuity;

public sealed class RecoveryWireCodecException : ArgumentException
{
    public RecoveryWireCodecException(
        string reasonCode,
        string parameterName,
        Exception? innerException = null)
        : base(reasonCode, parameterName, innerException)
    {
        ReasonCode = reasonCode;
    }

    public string ReasonCode { get; }
}

public static class RecoveryWireCodec
{
    public const int MaximumMessageBytes = 1_048_576;

    private const string HelloSchemaId = "RecoveryHello@1";
    private const string ReportSchemaId = "LocalContinuationReport@1";
    private const string PlanSchemaId = "ResyncPlan@1";

    private static readonly string[] HelloProperties =
    [
        "base_sha256",
        "client_id",
        "instance_id",
        "last_applied_commit_seq",
        "last_applied_event_seq",
        "last_delta_sha256",
        "local_continuation_report_id",
        "momentary_inputs_cleared",
        "observed_authority_epoch",
        "schema_id",
        "session_id",
    ];

    private static readonly string[] ReportProperties =
    [
        "base_sha256",
        "branch_id",
        "client_id",
        "end_sim_time_ns",
        "fallback_epoch",
        "instance_id",
        "integrity_state",
        "last_block_seq",
        "last_delta_sha256",
        "last_event_seq",
        "last_sample_index_by_channel",
        "offline_action_chain_sha256",
        "offline_action_event_ids",
        "report_id",
        "rolling_state_sha256",
        "schema_id",
        "session_id",
        "start_sim_time_ns",
    ];

    private static readonly string[] PlanProperties =
    [
        "accepted_through_commit_seq",
        "host_checkpoint_seq",
        "host_state_sha256",
        "local_prediction_decision",
        "new_authority_epoch",
        "new_stream_epoch",
        "plan_id",
        "preroll_from_sim_time_ns",
        "preroll_until_sim_time_ns",
        "reason_code",
        "relock_sim_time_ns",
        "resume_snapshot_sha256",
        "schema_id",
    ];

    private static readonly string[] SampleFrontierProperties =
    [
        "channel_id",
        "last_sample_index",
    ];

    public static byte[] EncodeHello(RecoveryHelloMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateHello(message);
        return Encode(writer =>
        {
            writer.WriteStartObject();
            WriteNullableString(writer, "base_sha256", message.BaseSha256);
            WriteUuid(writer, "client_id", message.ClientId);
            WriteUuid(writer, "instance_id", message.InstanceId);
            WriteU64(writer, "last_applied_commit_seq",
                message.LastAppliedCommitSequence);
            WriteU64(writer, "last_applied_event_seq",
                message.LastAppliedEventSequence);
            WriteNullableString(writer, "last_delta_sha256",
                message.LastDeltaSha256);
            WriteNullableUuid(writer, "local_continuation_report_id",
                message.LocalContinuationReportId);
            writer.WriteBoolean("momentary_inputs_cleared",
                message.MomentaryInputsCleared);
            WriteU64(writer, "observed_authority_epoch",
                message.ObservedAuthorityEpoch);
            writer.WriteString("schema_id", HelloSchemaId);
            WriteUuid(writer, "session_id", message.SessionId);
            writer.WriteEndObject();
        });
    }

    public static RecoveryHelloMessage DecodeHello(ReadOnlyMemory<byte> wire)
    {
        using JsonDocument document = Parse(wire);
        JsonElement root = document.RootElement;
        RequireObject(root, HelloProperties);
        RequireSchema(root, HelloSchemaId);
        RecoveryHelloMessage message = new(
            ReadUuid(root, "client_id"),
            ReadUuid(root, "session_id"),
            ReadUuid(root, "instance_id"),
            ReadU64(root, "observed_authority_epoch"),
            ReadU64(root, "last_applied_commit_seq"),
            ReadU64(root, "last_applied_event_seq"),
            ReadNullableHash(root, "base_sha256"),
            ReadNullableHash(root, "last_delta_sha256"),
            ReadNullableUuid(root, "local_continuation_report_id"),
            ReadRequiredTrue(root, "momentary_inputs_cleared"));
        ValidateHello(message);
        RequireCanonical(wire, EncodeHello(message));
        return message;
    }

    public static byte[] EncodeReport(LocalContinuationReportMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateReport(message);
        return Encode(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("base_sha256", message.BaseSha256);
            writer.WriteString("branch_id", message.BranchId);
            WriteUuid(writer, "client_id", message.ClientId);
            WriteI64(writer, "end_sim_time_ns", message.EndSimTimeNs);
            WriteU64(writer, "fallback_epoch", message.FallbackEpoch);
            WriteUuid(writer, "instance_id", message.InstanceId);
            writer.WriteString("integrity_state", message.IntegrityState.ToString());
            WriteU64(writer, "last_block_seq", message.LastBlockSequence);
            WriteNullableString(writer, "last_delta_sha256",
                message.LastDeltaSha256);
            WriteU64(writer, "last_event_seq", message.LastEventSequence);
            writer.WritePropertyName("last_sample_index_by_channel");
            writer.WriteStartArray();
            foreach (LocalContinuationSampleFrontier frontier in
                message.LastSampleIndexByChannel)
            {
                writer.WriteStartObject();
                writer.WriteString("channel_id", frontier.ChannelId);
                WriteU64(writer, "last_sample_index", frontier.LastSampleIndex);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("offline_action_chain_sha256",
                message.OfflineActionChainSha256);
            writer.WritePropertyName("offline_action_event_ids");
            writer.WriteStartArray();
            foreach (Guid eventId in message.OfflineActionEventIds)
            {
                writer.WriteStringValue(FormatUuid(eventId));
            }

            writer.WriteEndArray();
            WriteUuid(writer, "report_id", message.ReportId);
            writer.WriteString("rolling_state_sha256", message.RollingStateSha256);
            writer.WriteString("schema_id", ReportSchemaId);
            WriteUuid(writer, "session_id", message.SessionId);
            WriteI64(writer, "start_sim_time_ns", message.StartSimTimeNs);
            writer.WriteEndObject();
        });
    }

    public static LocalContinuationReportMessage DecodeReport(
        ReadOnlyMemory<byte> wire)
    {
        using JsonDocument document = Parse(wire);
        JsonElement root = document.RootElement;
        RequireObject(root, ReportProperties);
        RequireSchema(root, ReportSchemaId);
        List<LocalContinuationSampleFrontier> frontiers =
            ReadSampleFrontiers(root);
        List<Guid> eventIds = ReadUuidArray(
            root,
            "offline_action_event_ids",
            RecoveryHandshakeCoordinator.MaximumOfflineActionEvents);
        LocalContinuationReportMessage message = new(
            ReadUuid(root, "report_id"),
            ReadUuid(root, "session_id"),
            ReadUuid(root, "client_id"),
            ReadUuid(root, "instance_id"),
            ReadStableId(root, "branch_id"),
            ReadHash(root, "base_sha256"),
            ReadNullableHash(root, "last_delta_sha256"),
            ReadU64(root, "fallback_epoch"),
            ReadI64(root, "start_sim_time_ns"),
            ReadI64(root, "end_sim_time_ns"),
            frontiers,
            ReadU64(root, "last_block_seq"),
            ReadU64(root, "last_event_seq"),
            ReadHash(root, "rolling_state_sha256"),
            ReadHash(root, "offline_action_chain_sha256"),
            eventIds,
            ReadEnum<ContinuityReportIntegrityState>(root, "integrity_state"));
        ValidateReport(message);
        RequireCanonical(wire, EncodeReport(message));
        return message;
    }

    public static byte[] EncodePlan(RecoveryResyncPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidatePlan(plan);
        return Encode(writer =>
        {
            writer.WriteStartObject();
            WriteU64(writer, "accepted_through_commit_seq",
                plan.AcceptedThroughCommitSequence);
            WriteU64(writer, "host_checkpoint_seq", plan.HostCheckpointSequence);
            writer.WriteString("host_state_sha256", plan.HostStateSha256);
            writer.WriteString("local_prediction_decision",
                plan.LocalPredictionDecision.ToString());
            WriteU64(writer, "new_authority_epoch", plan.NewAuthorityEpoch);
            WriteU64(writer, "new_stream_epoch", plan.NewStreamEpoch);
            WriteUuid(writer, "plan_id", plan.PlanId);
            WriteI64(writer, "preroll_from_sim_time_ns",
                plan.PrerollFromSimTimeNs);
            WriteI64(writer, "preroll_until_sim_time_ns",
                plan.PrerollUntilSimTimeNs);
            writer.WriteString("reason_code", plan.ReasonCode);
            WriteI64(writer, "relock_sim_time_ns", plan.RelockSimTimeNs);
            WriteNullableString(writer, "resume_snapshot_sha256",
                plan.ResumeSnapshotSha256);
            writer.WriteString("schema_id", PlanSchemaId);
            writer.WriteEndObject();
        });
    }

    public static RecoveryResyncPlan DecodePlan(ReadOnlyMemory<byte> wire)
    {
        using JsonDocument document = Parse(wire);
        JsonElement root = document.RootElement;
        RequireObject(root, PlanProperties);
        RequireSchema(root, PlanSchemaId);
        RecoveryResyncPlan plan = new(
            ReadUuid(root, "plan_id"),
            ReadU64(root, "new_authority_epoch"),
            ReadU64(root, "host_checkpoint_seq"),
            ReadHash(root, "host_state_sha256"),
            ReadI64(root, "relock_sim_time_ns"),
            ReadU64(root, "new_stream_epoch"),
            ReadI64(root, "preroll_from_sim_time_ns"),
            ReadI64(root, "preroll_until_sim_time_ns"),
            ReadEnum<LocalPredictionDecision>(root, "local_prediction_decision"),
            ReadReasonCode(root, "reason_code"),
            ReadU64(root, "accepted_through_commit_seq"),
            ReadNullableHash(root, "resume_snapshot_sha256"));
        ValidatePlan(plan);
        RequireCanonical(wire, EncodePlan(plan));
        return plan;
    }

    private static byte[] Encode(Action<Utf8JsonWriter> write)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            write(writer);
        }

        if (buffer.WrittenCount > MaximumMessageBytes)
        {
            throw Error("RecoveryWire.MessageTooLarge", nameof(write));
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static JsonDocument Parse(ReadOnlyMemory<byte> wire)
    {
        if (wire.IsEmpty)
        {
            throw Error("RecoveryWire.InvalidJson", nameof(wire));
        }

        if (wire.Length > MaximumMessageBytes)
        {
            throw Error("RecoveryWire.MessageTooLarge", nameof(wire));
        }

        try
        {
            return JsonDocument.Parse(wire, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
        }
        catch (JsonException exception)
        {
            throw Error("RecoveryWire.InvalidJson", nameof(wire), exception);
        }
    }

    private static void RequireObject(JsonElement root, string[] expectedProperties)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw InvalidShape(nameof(root));
        }

        HashSet<string> remaining = new(expectedProperties, StringComparer.Ordinal);
        int propertyCount = 0;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            propertyCount++;
            if (!remaining.Remove(property.Name))
            {
                throw InvalidShape(nameof(root));
            }
        }

        if (propertyCount != expectedProperties.Length || remaining.Count != 0)
        {
            throw InvalidShape(nameof(root));
        }
    }

    private static void RequireSchema(JsonElement root, string schemaId)
    {
        JsonElement value = root.GetProperty("schema_id");
        if (value.ValueKind != JsonValueKind.String ||
            !StringComparer.Ordinal.Equals(value.GetString(), schemaId))
        {
            throw Error("RecoveryWire.UnsupportedSchema", nameof(root));
        }
    }

    private static void RequireCanonical(
        ReadOnlyMemory<byte> wire,
        ReadOnlySpan<byte> canonical)
    {
        if (!wire.Span.SequenceEqual(canonical))
        {
            throw Error("RecoveryWire.NonCanonical", nameof(wire));
        }
    }

    private static List<LocalContinuationSampleFrontier> ReadSampleFrontiers(
        JsonElement root)
    {
        JsonElement array = root.GetProperty("last_sample_index_by_channel");
        if (array.ValueKind != JsonValueKind.Array ||
            array.GetArrayLength() >
                RecoveryHandshakeCoordinator.MaximumChannelFrontiers)
        {
            throw InvalidShape(nameof(root));
        }

        List<LocalContinuationSampleFrontier> result =
            new(array.GetArrayLength());
        foreach (JsonElement item in array.EnumerateArray())
        {
            RequireObject(item, SampleFrontierProperties);
            result.Add(new LocalContinuationSampleFrontier(
                ReadStableId(item, "channel_id"),
                ReadU64(item, "last_sample_index")));
        }

        return result;
    }

    private static List<Guid> ReadUuidArray(
        JsonElement root,
        string propertyName,
        int maximumCount)
    {
        JsonElement array = root.GetProperty(propertyName);
        if (array.ValueKind != JsonValueKind.Array ||
            array.GetArrayLength() > maximumCount)
        {
            throw InvalidShape(propertyName);
        }

        List<Guid> result = new(array.GetArrayLength());
        foreach (JsonElement item in array.EnumerateArray())
        {
            result.Add(ParseUuidValue(item, propertyName));
        }

        return result;
    }

    private static Guid ReadUuid(JsonElement root, string propertyName) =>
        ParseUuidValue(root.GetProperty(propertyName), propertyName);

    private static Guid ParseUuidValue(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(value.GetString(), "D", out Guid result))
        {
            throw InvalidShape(propertyName);
        }

        return result;
    }

    private static Guid? ReadNullableUuid(JsonElement root, string propertyName)
    {
        JsonElement value = root.GetProperty(propertyName);
        return value.ValueKind == JsonValueKind.Null
            ? null
            : ParseUuidValue(value, propertyName);
    }

    private static ulong ReadU64(JsonElement root, string propertyName)
    {
        string value = ReadString(root, propertyName);
        if (!ulong.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out ulong result) ||
            !StringComparer.Ordinal.Equals(FormatU64(result), value))
        {
            throw InvalidShape(propertyName);
        }

        return result;
    }

    private static long ReadI64(JsonElement root, string propertyName)
    {
        string value = ReadString(root, propertyName);
        if (!long.TryParse(
                value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out long result) ||
            !StringComparer.Ordinal.Equals(FormatI64(result), value))
        {
            throw InvalidShape(propertyName);
        }

        return result;
    }

    private static string ReadHash(JsonElement root, string propertyName)
    {
        string value = ReadString(root, propertyName);
        if (!IsSha256(value))
        {
            throw InvalidShape(propertyName);
        }

        return value;
    }

    private static string? ReadNullableHash(
        JsonElement root,
        string propertyName)
    {
        JsonElement value = root.GetProperty(propertyName);
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadHash(root, propertyName);
    }

    private static string ReadStableId(JsonElement root, string propertyName)
    {
        string value = ReadString(root, propertyName);
        if (!IsStableId(value))
        {
            throw InvalidShape(propertyName);
        }

        return value;
    }

    private static string ReadReasonCode(JsonElement root, string propertyName)
    {
        string value = ReadString(root, propertyName);
        if (!IsReasonCode(value))
        {
            throw InvalidShape(propertyName);
        }

        return value;
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        JsonElement value = root.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw InvalidShape(propertyName);
        }

        return value.GetString()!;
    }

    private static T ReadEnum<T>(JsonElement root, string propertyName)
        where T : struct, Enum
    {
        string value = ReadString(root, propertyName);
        if (!Enum.TryParse(value, ignoreCase: false, out T result) ||
            !Enum.IsDefined(result))
        {
            throw InvalidShape(propertyName);
        }

        return result;
    }

    private static bool ReadRequiredTrue(JsonElement root, string propertyName)
    {
        if (root.GetProperty(propertyName).ValueKind != JsonValueKind.True)
        {
            throw InvalidShape(propertyName);
        }

        return true;
    }

    private static void ValidateHello(RecoveryHelloMessage message)
    {
        if ((message.BaseSha256 is not null && !IsSha256(message.BaseSha256)) ||
            (message.LastDeltaSha256 is not null &&
                !IsSha256(message.LastDeltaSha256)) ||
            !message.MomentaryInputsCleared)
        {
            throw InvalidShape(nameof(message));
        }
    }

    private static void ValidateReport(LocalContinuationReportMessage message)
    {
        if (!IsStableId(message.BranchId) ||
            !IsSha256(message.BaseSha256) ||
            (message.LastDeltaSha256 is not null &&
                !IsSha256(message.LastDeltaSha256)) ||
            !IsSha256(message.RollingStateSha256) ||
            !IsSha256(message.OfflineActionChainSha256) ||
            !Enum.IsDefined(message.IntegrityState) ||
            message.LastSampleIndexByChannel is null ||
            message.LastSampleIndexByChannel.Count >
                RecoveryHandshakeCoordinator.MaximumChannelFrontiers ||
            message.LastSampleIndexByChannel.Any(static frontier =>
                frontier is null || !IsStableId(frontier.ChannelId)) ||
            message.OfflineActionEventIds is null ||
            message.OfflineActionEventIds.Count >
                RecoveryHandshakeCoordinator.MaximumOfflineActionEvents)
        {
            throw InvalidShape(nameof(message));
        }
    }

    private static void ValidatePlan(RecoveryResyncPlan plan)
    {
        if (!IsSha256(plan.HostStateSha256) ||
            !Enum.IsDefined(plan.LocalPredictionDecision) ||
            !IsReasonCode(plan.ReasonCode) ||
            (plan.ResumeSnapshotSha256 is not null &&
                !IsSha256(plan.ResumeSnapshotSha256)))
        {
            throw InvalidShape(nameof(plan));
        }
    }

    private static void WriteUuid(
        Utf8JsonWriter writer,
        string propertyName,
        Guid value) => writer.WriteString(propertyName, FormatUuid(value));

    private static void WriteNullableUuid(
        Utf8JsonWriter writer,
        string propertyName,
        Guid? value)
    {
        if (value.HasValue)
        {
            WriteUuid(writer, propertyName, value.Value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    private static void WriteNullableString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteU64(
        Utf8JsonWriter writer,
        string propertyName,
        ulong value) => writer.WriteString(propertyName, FormatU64(value));

    private static void WriteI64(
        Utf8JsonWriter writer,
        string propertyName,
        long value) => writer.WriteString(propertyName, FormatI64(value));

    private static string FormatUuid(Guid value) => value.ToString("D");

    private static string FormatU64(ulong value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string FormatI64(long value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsStableId(string? value)
    {
        if (value is not { Length: >= 1 and <= 128 } ||
            !IsAsciiLetter(value[0]))
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if (!IsAsciiLetter(character) &&
                character is not (>= '0' and <= '9') &&
                character is not '.' and not '_' and not ':' and not '@' and
                    not '/' and not '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsReasonCode(string? value)
    {
        if (value is not { Length: >= 2 and <= 64 } ||
            value[0] is < 'A' or > 'Z')
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if (character is not (>= 'A' and <= 'Z') &&
                character is not (>= '0' and <= '9') &&
                character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static RecoveryWireCodecException InvalidShape(string parameterName) =>
        Error("RecoveryWire.InvalidShape", parameterName);

    private static RecoveryWireCodecException Error(
        string reasonCode,
        string parameterName,
        Exception? innerException = null) =>
        new(reasonCode, parameterName, innerException);
}
