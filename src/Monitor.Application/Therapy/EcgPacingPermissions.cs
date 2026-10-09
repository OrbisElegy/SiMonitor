// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.ObjectModel;
using System.Globalization;

namespace Monitor.Application.Therapy;

// Explicit teaching permissions, independent of clinical rhythm indications.
public static class EcgPacingPermissions
{
    public static IReadOnlyList<string> TemplateIds { get; } = Array.AsReadOnly(
        Enumerable.Range(0, 180).Select(index => string.Create(CultureInfo.InvariantCulture, $"ecgTemplate.t{index:D3}")).ToArray());

    public static IReadOnlyDictionary<string, bool> Snapshot(IReadOnlyDictionary<string, bool> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (permissions.Count > TemplateIds.Count || permissions.Keys.Any(key => !TemplateIds.Contains(key, StringComparer.Ordinal)))
        { throw new ArgumentException("Pacing.InvalidTemplatePermissions", nameof(permissions)); }
        return new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>(permissions, StringComparer.Ordinal));
    }
}
