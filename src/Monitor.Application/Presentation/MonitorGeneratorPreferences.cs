// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

// Explicit editor schema, not a simulation checkpoint or measured values.
public sealed record MonitorGeneratorPreferences(int Ecg, string EcgName, int Respiration, int Ejection,
    string Seed, IReadOnlyDictionary<string, decimal?> Numbers,
    IReadOnlyDictionary<string, bool> Flags, IReadOnlyDictionary<string, int> Choices)
{
    public decimal ApplyDelaySeconds { get; init; } = 3;
    public OxygenationEditorPreferences? Oxygenation { get; init; }
    public void Validate()
    {
        if (ApplyDelaySeconds < 0 || ApplyDelaySeconds > 60 || ApplyDelaySeconds * 10 != decimal.Truncate(ApplyDelaySeconds * 10) || Ecg < 0 || EcgName is null || EcgName.Length > 128 || Respiration is < 0 or > 3 ||
            Ejection is < 0 or > 3 || Seed is null || Seed.Length > 1024 ||
            Numbers is null || Flags is null || Choices is null ||
            Numbers.Count > 64 || Flags.Count > 32 || Choices.Count > 16)
        { throw new ArgumentException("GeneratorPreferences.Invalid"); }
        Oxygenation?.Validate();
    }
}
