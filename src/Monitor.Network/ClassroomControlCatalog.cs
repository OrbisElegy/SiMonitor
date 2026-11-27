// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;

namespace Monitor.Network;

internal sealed record ClassroomControlAction(string Id, string ParameterRef, string Interaction)
{
    internal string Parameters => ParameterRef[(ParameterRef.LastIndexOf('/') + 1)..];
}

internal static class ClassroomControlCatalog
{
    internal static readonly IReadOnlyDictionary<string, ClassroomControlAction> Catalog =
        ClassroomContracts.ActionCatalog.EnumerateArray().Select(entry => new ClassroomControlAction(
            entry.GetProperty("action_id").GetString()!, entry.GetProperty("parameter_schema_ref").GetString()!,
            entry.GetProperty("interaction_kind").GetString()!)).ToDictionary(action => action.Id, StringComparer.Ordinal);

    internal static bool Stable(string? value) => value is { Length: > 0 and <= 128 } && char.IsAsciiLetter(value[0]) &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '@' or '/' or '-');

    internal static bool ValidIntent(ClassroomActionIntent intent)
    {
        if (intent.IntentId == Guid.Empty || intent.ClientId == Guid.Empty || intent.SessionId == Guid.Empty ||
            intent.InstanceId == Guid.Empty || intent.ClientMonotonicNs < 0 || intent.ActionId is null ||
            intent.Parameters.ValueKind != JsonValueKind.Object || !Catalog.TryGetValue(intent.ActionId, out var action))
        { return false; }
        try
        {
            ClassroomContracts.Validate("LearnerActionIntent@1", JsonSerializer.SerializeToElement(intent, ClassroomControlWire.Json));
        }
        catch (InvalidDataException) { return false; }
        if (!ClassroomContracts.MatchesActionParameters(action.ParameterRef, intent.Parameters)) { return false; }
        return intent.ActionId switch
        {
            "therapy.defib.energy.set" => Unit(intent, "energy", "JOULE"),
            "therapy.pacer.rate.set" => Unit(intent, "rate", "BEATS_PER_MINUTE"),
            "therapy.pacer.output.set" => Unit(intent, "output", "MILLIAMPERE"),
            _ when action.Interaction == "PressHoldRelease" => intent.Parameters.GetProperty("interaction_id").GetGuid() != Guid.Empty,
            _ => true
        };
    }

    private static bool Unit(ClassroomActionIntent intent, string field, string expected) =>
        intent.Parameters.GetProperty(field).GetProperty("unit_code").GetString() == expected;
}
