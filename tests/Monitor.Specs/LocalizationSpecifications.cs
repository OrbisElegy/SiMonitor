// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using Monitor.Application.Localization;
using Monitor.Infrastructure.Localization;

namespace Monitor.Specs;

internal static class LocalizationSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(BuiltInLocalesHaveCompleteMatchingResources), BuiltInLocalesHaveCompleteMatchingResources),
        new(nameof(LocaleSelectionIsExplicitAndLimitedToEnglishAndSimplifiedChinese), LocaleSelectionIsExplicitAndLimitedToEnglishAndSimplifiedChinese),
        new(nameof(MissingTranslationsHaveObservableFallbacks), MissingTranslationsHaveObservableFallbacks),
        new(nameof(TranslationValidationRejectsBrokenCollaborativeEdits), TranslationValidationRejectsBrokenCollaborativeEdits),
        new(nameof(FormatsAllowReorderingAndEscapedBraces), FormatsAllowReorderingAndEscapedBraces),
        new(nameof(LocalizersDoNotReadOrChangeAmbientCulture), LocalizersDoNotReadOrChangeAmbientCulture),
        new(nameof(FormattingRejectsIncorrectArguments), FormattingRejectsIncorrectArguments),
    ];

    private static void BuiltInLocalesHaveCompleteMatchingResources()
    {
        ITextLocalizer english = BuiltInLocalizations.Create("en");
        ITextLocalizer chinese = BuiltInLocalizations.Create("cn");
        Check.That(english.GetString("common.apply") == "Apply" && chinese.GetString("common.apply") == "应用",
            "embedded catalogs are available without filesystem paths");
        Check.That(!chinese.Resolve("common.apply").IsFallback && !english.Resolve("common.apply").IsMissing,
            "shipped translations resolve directly");
        Check.That(chinese.Format("validation.integerRange", 1, 3600) == "请输入 1–3600 的整数。" &&
            english.Format("validation.integerRange", 1, 3600) == "Enter an integer from 1 to 3600.",
            "both catalogs satisfy the same formatting contract");
        Check.That(english.Format("sound.volumePercent", 12.3m) == "Sound volume: 12%" &&
            chinese.Format("sound.volumePercent", 12.3m) == "声音音量：12%", "format specifiers survive translation");
        // Loading Chinese validates every key and argument signature against English, including unused keys.
        Check.That(BuiltInLocalizations.Supported.Select(locale => locale.Code).SequenceEqual(["en", "zh-CN"]),
            "only the requested two languages are advertised");
    }

    private static void LocaleSelectionIsExplicitAndLimitedToEnglishAndSimplifiedChinese()
    {
        foreach (string? locale in new string?[] { null, "", "  ", "cn", " CN ", "zh", "zh-cn", "zh-Hans", "zh-Hans-CN" })
        {
            Check.That(BuiltInLocalizations.Create(locale).Locale == "zh-CN", "Chinese aliases and default resolve canonically");
        }
        foreach (string locale in new[] { "en", "EN", "en-US", "en-GB", "fr", "zh-TW", "zh-Hant", "invalid" })
        {
            Check.That(BuiltInLocalizations.Create(locale).Locale == "en", "unavailable locales fall back to English");
        }
    }

    private static void MissingTranslationsHaveObservableFallbacks()
    {
        var english = TranslationCatalog.Parse("en", """{"common.apply":"Apply","range":"{0} to {1}"}""");
        var chinese = TranslationCatalog.Parse("zh-CN", """{"common.apply":"应用"}""");
        var localizer = new CatalogTextLocalizer(chinese, english);
        var fallback = localizer.Resolve("range");
        Check.That(fallback.IsFallback && !fallback.IsMissing && fallback.RequestedLocale == "zh-CN" &&
            fallback.ResolvedLocale == "en" && localizer.Format("range", 1, 5) == "1 to 5",
            "incomplete catalogs preserve fallback provenance and formatting");
        var missing = localizer.Resolve("unknown.key");
        Check.That(missing.IsMissing && !missing.IsFallback && missing.ResolvedLocale is null &&
            localizer.GetString("unknown.key") == "[[unknown.key]]", "unknown keys remain visible and detectable");
        Reject<FormatException>(() => chinese.ValidateAgainst(english, requireComplete: true));
    }

    private static void TranslationValidationRejectsBrokenCollaborativeEdits()
    {
        foreach (string json in new[] { "[]", "{\"a\":null}", "{\"a\":2}", "{\"a\":\" \"}",
            "{\" a\":\"text\"}", "{\"\":\"text\"}", "{\"a\":\"one\",\"a\":\"two\"}",
            "{\"a\":\"{0\"}", "{\"a\":\"{1}\"}", "{\"a\":\"{32}\"}" })
        {
            Reject<FormatException>(() => TranslationCatalog.Parse("en", json));
        }
        Reject<JsonException>(() => TranslationCatalog.Parse("en", "{"));
        Reject<ArgumentException>(() => TranslationCatalog.Parse("", "{}"));

        var english = TranslationCatalog.Parse("en", """{"range":"{0:0.0} to {1:0.0}"}""");
        foreach (string json in new[] { "{\"typo\":\"value\"}", "{\"range\":\"{0:0.0}\"}",
            "{\"range\":\"{0} to {1}\"}", "{\"range\":\"no arguments\"}" })
        {
            var invalid = TranslationCatalog.Parse("zh-CN", json);
            Reject<FormatException>(() => { _ = new CatalogTextLocalizer(invalid, english); });
        }
    }

    private static void FormatsAllowReorderingAndEscapedBraces()
    {
        var english = TranslationCatalog.Parse("en", """{"range":"{{range}} {0:0.0} to {1:0.0}","literal":"{{literal}}"}""");
        var chinese = TranslationCatalog.Parse("zh-CN", """{"range":"{1:0.0} ← {0:0.0} ({0:0.0})","literal":"{{字面量}}"}""");
        var localizer = new CatalogTextLocalizer(chinese, english);
        Check.That(localizer.Format("range", 1.25m, 5.25m) == "5.3 ← 1.3 (1.3)",
            "translation can reorder and repeat arguments");
        Check.That(localizer.GetString("literal") == "{字面量}" &&
            new CatalogTextLocalizer(english, english).Format("range", 1.25m, 5.25m) == "{range} 1.3 to 5.3",
            "standard composite-format brace escaping is preserved");
    }

    private static void LocalizersDoNotReadOrChangeAmbientCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var externalCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            CultureInfo.CurrentCulture = externalCulture;
            CultureInfo.CurrentUICulture = externalCulture;
            var english = TranslationCatalog.Parse("en", """{"number":"{0:0.0}"}""");
            var chinese = TranslationCatalog.Parse("zh-CN", """{"number":"数值 {0:0.0}"}""");
            var en = new CatalogTextLocalizer(english, english);
            var cn = new CatalogTextLocalizer(chinese, english);
            Parallel.For(0, 32, index =>
            {
                Check.That(en.Format("number", 1.25m) == "1.3" && cn.Format("number", 1.25m) == "数值 1.3",
                    "separate immutable localizers can serve concurrent views");
            });
            Check.That(CultureInfo.CurrentCulture == externalCulture && CultureInfo.CurrentUICulture == externalCulture,
                "creating and using localizers never changes input, simulation or persistence culture");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static void FormattingRejectsIncorrectArguments()
    {
        var localizer = BuiltInLocalizations.Create("en");
        Reject<FormatException>(() => localizer.GetString("validation.integerRange"));
        Reject<FormatException>(() => localizer.Format("validation.integerRange", 1));
        Reject<FormatException>(() => localizer.Format("validation.integerRange", 1, 2, 3));
        Reject<FormatException>(() => localizer.Format("common.apply", 1));
        Reject<ArgumentException>(() => localizer.GetString(" "));
        Reject<ArgumentNullException>(() => localizer.Format("common.apply", null!));
    }

    private static void Reject<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
