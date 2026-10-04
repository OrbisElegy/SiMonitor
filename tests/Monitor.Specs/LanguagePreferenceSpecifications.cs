// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Infrastructure.Localization;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Specs;

internal static class LanguagePreferenceSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(LanguagePreferencesRoundTripWithoutChangingOtherFiles), LanguagePreferencesRoundTripWithoutChangingOtherFiles),
        new(nameof(InvalidLanguagePreferencesRemainAvailableForRecovery), InvalidLanguagePreferencesRemainAvailableForRecovery),
        new(nameof(LanguageSaveFailuresAreReportedWithoutTemporaryFiles), LanguageSaveFailuresAreReportedWithoutTemporaryFiles),
    ];

    private static void LanguagePreferencesRoundTripWithoutChangingOtherFiles() => WithDirectory(directory =>
    {
        string path = Path.Combine(directory, "display.language.json");
        string display = Path.Combine(directory, "display.json");
        File.WriteAllText(display, "existing display preferences", System.Text.Encoding.UTF8);
        var store = new LanguagePreferenceStore(path);
        Check.That(store.Load(out bool rejected) == "zh-CN" && !rejected, "missing language file quietly defaults to Chinese");
        foreach (var locale in BuiltInLocalizations.Supported)
        {
            Check.That(store.Save(locale.Code) && store.Load(out rejected) == locale.Code && !rejected,
                "canonical supported language survives restart");
        }
        string before = File.ReadAllText(path, System.Text.Encoding.UTF8);
        try { store.Save("cn"); throw new InvalidOperationException("Expected canonical locale validation."); }
        catch (ArgumentException) { }
        Check.That(File.ReadAllText(path, System.Text.Encoding.UTF8) == before &&
            File.ReadAllText(display, System.Text.Encoding.UTF8) == "existing display preferences",
            "invalid save and independent language persistence preserve other preferences");
    });

    private static void InvalidLanguagePreferencesRemainAvailableForRecovery() => WithDirectory(directory =>
    {
        string path = Path.Combine(directory, "language.json");
        var store = new LanguagePreferenceStore(path);
        foreach (string json in new[] { "{", "null", "{}", "{\"Version\":1}", "{\"Locale\":\"en\"}",
            "{\"Version\":2,\"Locale\":\"en\"}", "{\"Version\":1,\"Locale\":null}",
            "{\"Version\":1,\"Locale\":\"fr\"}", "{\"Version\":1,\"Locale\":\"en\",\"Extra\":1}", new string(' ', 1025) })
        {
            File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
            Check.That(store.Load(out bool rejected) == "zh-CN" && rejected &&
                File.ReadAllText(path, System.Text.Encoding.UTF8) == json,
                "invalid language file uses a visible default and preserves file evidence");
        }
    });

    private static void LanguageSaveFailuresAreReportedWithoutTemporaryFiles() => WithDirectory(directory =>
    {
        string path = Path.Combine(directory, "blocked.json");
        Directory.CreateDirectory(path);
        var store = new LanguagePreferenceStore(path);
        Check.That(!store.Save("en") && !Directory.EnumerateFiles(directory).Any(),
            "failed atomic replacement returns false and removes temporary output");
    });

    private static void WithDirectory(Action<string> action)
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-language-specs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
