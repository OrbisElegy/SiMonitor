// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Monitor.Network;

// Only checked-in Domain.Contracts resources are resolved. Received JSON cannot select a file or network URI.
internal static class ClassroomContracts
{
    internal const string PackageHash = "5ca5ef507870ea63d8c16532d955ad7b894a19ffac07be4b66a1fc2545192964";
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
        MaxDepth = 32
    };
    private const string ResourcePrefix = "Classroom.Contracts.";
    private static readonly Dictionary<string, JsonElement> Files = LoadFiles();
    private static readonly Dictionary<string, string> Definitions = Index("contracts", "id", "schema_ref");
    internal static readonly IReadOnlyDictionary<string, string> EventTypes = Index("event_payload_registry", "event_type", "payload_schema_id");

    internal static JsonElement ActionCatalog => Files["contract-manifest.json"].GetProperty("action_catalog");
    internal static bool MatchesActionParameters(string reference, JsonElement parameters) =>
        DistinctFields(parameters) && MatchesReference("actions.schema.json", reference, parameters);

    internal static readonly string ProtocolHash = HashProtocol();
    private static string HashProtocol()
    {
        using Stream stream = typeof(ClassroomContracts).Assembly.GetManifestResourceStream(ResourcePrefix + "classroom-protocol.json")!;
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static Dictionary<string, JsonElement> LoadFiles()
    {
        Assembly assembly = typeof(ClassroomContracts).Assembly;
        Dictionary<string, JsonElement> files = new(StringComparer.Ordinal);
        foreach (string name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using var document = JsonDocument.Parse(stream);
            files.Add(name[ResourcePrefix.Length..], document.RootElement.Clone());
        }
        return files;
    }

    private static Dictionary<string, string> Index(string table, string key, string value) =>
        Files["contract-manifest.json"].GetProperty(table).EnumerateArray().ToDictionary(
            item => item.GetProperty(key).GetString()!, item => item.GetProperty(value).GetString()!, StringComparer.Ordinal);

    internal static void Validate(string schemaId, JsonElement instance)
    {
        if (!Definitions.TryGetValue(schemaId, out string? reference) || !DistinctFields(instance) ||
            !MatchesReference("common.schema.json", reference, instance))
        {
            throw new InvalidDataException($"Invalid classroom contract {schemaId}.");
        }
        ValidateSemantics(schemaId, instance);
        if (schemaId == "DomainEventEnvelope@1")
        {
            string type = instance.GetProperty("event_type").GetString()!;
            if (!EventTypes.TryGetValue(type, out string? payloadId) || instance.GetProperty("payload_schema_id").GetString() != payloadId)
            {
                throw new InvalidDataException("The classroom event discriminator does not match its registered payload.");
            }
            Validate(payloadId, instance.GetProperty("payload"));
        }
    }

    private static void ValidateSemantics(string schemaId, JsonElement value)
    {
        static long Time(JsonElement source, string field) => long.Parse(source.GetProperty(field).GetString()!, CultureInfo.InvariantCulture);
        switch (schemaId)
        {
            case "NumericFrame@1":
                ValidateMeasurement(value);
                break;
            case "MeasurementGroupFrame@1":
                HashSet<(string?, Guid)> channels = [];
                foreach (var member in value.GetProperty("members").EnumerateArray())
                {
                    ValidateMeasurement(member);
                    var channel = member.GetProperty("measurement_channel_ref");
                    if (!channels.Add((channel.GetProperty("parameter_id").GetString(), channel.GetProperty("source_instance_id").GetGuid())))
                    { throw new InvalidDataException("Duplicate measurement group channel."); }
                }
                break;
            case "MomentaryTherapyLease@1":
                long pressed = Time(value, "pressed_at_safety_ns");
                long refresh = Time(value, "last_refresh_safety_ns");
                if (Time(value, "issued_at_safety_ns") > pressed || pressed > refresh || refresh >= Time(value, "expires_at_safety_ns"))
                { throw new InvalidDataException("Invalid momentary input lease interval."); }
                break;
            case "AEDAnalysisEpisode@1":
                var window = value.GetProperty("input_window");
                if (ulong.Parse(window.GetProperty("sample_start_index").GetString()!, CultureInfo.InvariantCulture) >=
                    ulong.Parse(window.GetProperty("sample_end_exclusive_index").GetString()!, CultureInfo.InvariantCulture) ||
                    value.GetProperty("ended_sim_time_ns").ValueKind != JsonValueKind.Null &&
                    Time(value, "ended_sim_time_ns") < Time(value, "started_sim_time_ns"))
                { throw new InvalidDataException("Invalid analysis interval."); }
                break;
            case "ContinuityCapsuleBase@1":
            case "ContinuityCapsuleDelta@1":
            case "OfflineCapabilityLease@1":
                long until = Time(value, "continuation_valid_until");
                if (until > Time(value, "secret_boundary_sim_time") || schemaId == "ContinuityCapsuleBase@1" && Time(value, "sim_time_ns") > until)
                { throw new InvalidDataException("Continuation exceeds its authorized boundary."); }
                break;
        }
    }

    private static void ValidateMeasurement(JsonElement measurement)
    {
        var quantity = measurement.GetProperty("quantity");
        if (quantity.ValueKind == JsonValueKind.Null) { return; }
        string parameter = measurement.GetProperty("measurement_channel_ref").GetProperty("parameter_id").GetString()!;
        string? unit = parameter.Split('.')[0].ToUpperInvariant() switch
        {
            "HR" or "PR" => "BEATS_PER_MINUTE",
            "RR" => "BREATHS_PER_MINUTE",
            "SPO2" => "PERCENT",
            "NIBP" or "ABP" or "CVP" or "PA" or "ETCO2" => "MMHG",
            "TEMP" => "CELSIUS",
            "PI" => "DEVICE_RELATIVE",
            _ => null
        };
        if (unit is not null && quantity.GetProperty("unit_code").GetString() != unit)
        { throw new InvalidDataException("Unexpected unit for the measurement channel."); }
    }

    private static bool DistinctFields(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) { return value.EnumerateArray().All(DistinctFields); }
        if (value.ValueKind != JsonValueKind.Object) { return true; }
        HashSet<string> seen = new(StringComparer.Ordinal);
        return value.EnumerateObject().All(field => seen.Add(field.Name) && DistinctFields(field.Value));
    }

    private static bool MatchesReference(string file, string reference, JsonElement value)
    {
        string[] parts = reference.Split("#/$defs/", StringSplitOptions.None);
        string name = parts[1];
        if (parts[0].Length > 0) { file = Path.GetFileName(parts[0]); }
        if (file == "common.schema.json" && name is "I64" or "U64")
        {
            if (value.ValueKind != JsonValueKind.String) { return false; }
            string number = value.GetString()!;
            return name == "U64"
                ? ulong.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out ulong u) && number == u.ToString(CultureInfo.InvariantCulture)
                : long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long i) && number == i.ToString(CultureInfo.InvariantCulture);
        }
        return Matches(file, Files[file].GetProperty("$defs").GetProperty(name), value);
    }

    private static bool Matches(string file, JsonElement schema, JsonElement value)
    {
        bool Sub(JsonElement child) => Matches(file, child, value);
        if (schema.TryGetProperty("$ref", out var reference) && !MatchesReference(file, reference.GetString()!, value)) { return false; }
        if (schema.TryGetProperty("type", out var type) && !IsType(type.GetString(), value)) { return false; }
        if (schema.TryGetProperty("const", out var constant) && !JsonElement.DeepEquals(constant, value)) { return false; }
        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(choice => JsonElement.DeepEquals(choice, value))) { return false; }
        if (schema.TryGetProperty("oneOf", out var one) && one.EnumerateArray().Count(Sub) != 1) { return false; }
        if (schema.TryGetProperty("allOf", out var all) && !all.EnumerateArray().All(Sub)) { return false; }
        if (schema.TryGetProperty("not", out var disallowed) && Sub(disallowed)) { return false; }
        if (schema.TryGetProperty("if", out var condition) && schema.TryGetProperty(Sub(condition) ? "then" : "else", out var branch) && !Sub(branch)) { return false; }
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                if (schema.TryGetProperty("required", out var fields) && !fields.EnumerateArray().All(key => value.TryGetProperty(key.GetString()!, out _))) { return false; }
                bool hasProperties = schema.TryGetProperty("properties", out var properties);
                bool hasExtra = schema.TryGetProperty("additionalProperties", out var extra);
                foreach (JsonProperty field in value.EnumerateObject())
                {
                    if (hasProperties && properties.TryGetProperty(field.Name, out var property))
                    {
                        if (!Matches(file, property, field.Value)) { return false; }
                    }
                    else if (hasExtra && (extra.ValueKind == JsonValueKind.False || extra.ValueKind == JsonValueKind.Object && !Matches(file, extra, field.Value))) { return false; }
                }
                return InBounds(schema, value.GetPropertyCount(), "minProperties", "maxProperties");
            case JsonValueKind.Array:
                if (!InBounds(schema, value.GetArrayLength(), "minItems", "maxItems")) { return false; }
                if (schema.TryGetProperty("uniqueItems", out var unique) && unique.GetBoolean())
                {
                    // Most lists contain identifiers. Profile energy lists contain bounded decimal quantities.
                    HashSet<string> items = new(StringComparer.Ordinal);
                    List<JsonElement> structured = [];
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            if (!items.Add(item.GetString()!)) { return false; }
                        }
                        else
                        {
                            if (structured.Any(previous => JsonElement.DeepEquals(previous, item))) { return false; }
                            structured.Add(item);
                        }
                    }
                }
                return !schema.TryGetProperty("items", out var itemSchema) || value.EnumerateArray().All(item => Matches(file, itemSchema, item));
            case JsonValueKind.Number:
                return value.TryGetDecimal(out decimal number) && InBounds(schema, number, "minimum", "maximum") &&
                    (!schema.TryGetProperty("exclusiveMinimum", out var min) || number > min.GetDecimal());
            case JsonValueKind.String:
                string text = value.GetString()!;
                if (!InBounds(schema, text.EnumerateRunes().Count(), "minLength", "maxLength")) { return false; }
                if (schema.TryGetProperty("pattern", out var pattern) && !Regex.IsMatch(text, pattern.GetString()!.Replace("$", "\\z", StringComparison.Ordinal), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) { return false; }
                if (schema.TryGetProperty("format", out var format))
                {
                    if (format.GetString() == "uuid") { return Guid.TryParseExact(text, "D", out _); }
                    return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) && text.Contains('T') && text.EndsWith('Z');
                }
                return !schema.TryGetProperty("contentEncoding", out _) || System.Buffers.Text.Base64.IsValid(text);
            default:
                return true;
        }
    }

    private static bool InBounds(JsonElement schema, decimal number, string minimum, string maximum) =>
        (!schema.TryGetProperty(minimum, out var min) || number >= min.GetDecimal()) &&
        (!schema.TryGetProperty(maximum, out var max) || number <= max.GetDecimal());

    private static bool IsType(string? type, JsonElement value) => type switch
    {
        "array" => value.ValueKind == JsonValueKind.Array,
        "object" => value.ValueKind == JsonValueKind.Object,
        "string" => value.ValueKind == JsonValueKind.String,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number) && decimal.Truncate(number) == number,
        "number" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false
    };
}
