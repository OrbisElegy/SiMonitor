// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

public sealed record EcgAdvancedPreferences(IReadOnlyDictionary<string, decimal?> Numbers,
    IReadOnlyDictionary<string, bool> Flags, IReadOnlyDictionary<string, int> Choices)
{
    public void Validate()
    {
        if (Numbers is null || Flags is null || Choices is null || Numbers.Count > 48 || Flags.Count > 16 || Choices.Count > 4)
        { throw new ArgumentException("EcgPreferences.Invalid"); }
    }
}
