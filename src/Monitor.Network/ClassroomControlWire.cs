// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Monitor.Network;

public sealed record ClassroomActionIntent(string SchemaId, Guid IntentId, string IdempotencyKey, Guid SessionId,
    Guid ClientId, Guid InstanceId, string ActionId, ulong ProjectionRevision, long ClientMonotonicNs,
    string ControlId, string? SkinId, JsonElement Parameters);

public sealed record ClassroomActionProjection(string SchemaId, string ActionId, ulong ProjectionRevision,
    string Availability, string InteractionKind, string ParameterSchemaId, string? DisabledReasonCode, string LocalizationKey);

public sealed record ClassroomActionRejection(string SchemaId, string ReasonCode, string ObjectRef, string FieldPath,
    ulong? CurrentRevision, bool Retryable, string LocalizationKey, Guid CorrelationId);

public sealed record ClassroomActionResult(string SchemaId, Guid IntentId, string IdempotencyKey, bool Accepted,
    string ActionId, ulong ProjectionRevision, ulong? CommitSeq, long? SimTimeNs, ClassroomActionRejection? Rejection);

// A reducer is called inside the authority's SQLite transaction. Its only output is the next learner-visible
// device state. It must not perform I/O, mutate shared objects or publish effects before the transaction commits.
public interface IClassroomActionReducer
{
    public string ProfileId { get; }
    public JsonElement InitialState { get; }
    public bool Supports(string actionId);
    public JsonElement Reduce(JsonElement currentState, ClassroomActionIntent action, long simTimeNs, long safetyTimeNs);
}

// Momentary operations are offered only by a device that can explicitly unwind the held interaction.
public interface IClassroomMomentaryActionReducer : IClassroomActionReducer
{
    public JsonElement CancelInteraction(JsonElement currentState, Guid clientId, Guid interactionId, long safetyTimeNs);
}

public sealed class ClassroomActionDeniedException(string reasonCode) : Exception(reasonCode)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record ClassroomControlSnapshot(ulong ProjectionRevision, ulong CommitSeq, ulong ConfigurationRevision,
    string DeviceProfileId, JsonElement DeviceState, string[] AllowedActionIds);

internal static class ClassroomControlWire
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 16,
        Converters = { new U64(), new I64(), new JsonStringEnumConverter<ClassroomAckStage>(allowIntegerValues: false) }
    };

    internal sealed class U64 : JsonConverter<ulong>
    {
        public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong result) ||
                result.ToString(CultureInfo.InvariantCulture) != value) { throw new JsonException("Invalid unsigned decimal string."); }
            return result;
        }
        public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }

    internal sealed class I64 : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long result) ||
                result.ToString(CultureInfo.InvariantCulture) != value) { throw new JsonException("Invalid signed decimal string."); }
            return result;
        }
        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
