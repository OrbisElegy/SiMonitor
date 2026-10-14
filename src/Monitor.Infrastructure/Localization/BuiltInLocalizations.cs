// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Monitor.Application.Localization;

namespace Monitor.Infrastructure.Localization;

public sealed record SupportedLocale(string Code, string NativeName);

public static class BuiltInLocalizations
{
    public const string SystemLocale = "system";
    public const string DefaultLocale = "zh-CN";
    public const string FallbackLocale = "en";
    public const string TraditionalChineseLocale = "zh-TW";

    private static readonly Lazy<TranslationCatalog> English = new(() => Load(FallbackLocale));
    private static readonly Lazy<TranslationCatalog> Chinese = new(() =>
    {
        var catalog = Load(DefaultLocale);
        catalog.ValidateAgainst(English.Value, requireComplete: true);
        return catalog;
    });
    private static readonly Lazy<TranslationCatalog> TraditionalChinese = new(() =>
    {
        var catalog = Load(TraditionalChineseLocale);
        catalog.ValidateAgainst(English.Value, requireComplete: true);
        return catalog;
    });

    public static IReadOnlyList<SupportedLocale> Supported { get; } = Array.AsReadOnly<SupportedLocale>(
        [new(FallbackLocale, "English"), new(DefaultLocale, "简体中文"), new(TraditionalChineseLocale, "繁體中文")]);

    public static string ResolveLocale(string? requestedLocale)
    {
        if (string.IsNullOrWhiteSpace(requestedLocale)) { return DefaultLocale; }
        string locale = requestedLocale.Trim();
        // Read the UI language only for the explicit system preference. Numeric input
        // and deterministic simulation cultures remain untouched.
        if (locale.Equals(SystemLocale, StringComparison.OrdinalIgnoreCase))
        {
            locale = CultureInfo.CurrentUICulture.Name;
        }
        if (locale.Equals("cn", StringComparison.OrdinalIgnoreCase) ||
            locale.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
            locale.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
            locale.Equals("zh-SG", StringComparison.OrdinalIgnoreCase) ||
            locale.Equals("zh-CHS", StringComparison.OrdinalIgnoreCase) ||
            locale.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase) ||
            locale.StartsWith("zh-Hans-", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultLocale;
        }
        if (locale.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ||
            locale.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) ||
            locale.Equals("zh-MO", StringComparison.OrdinalIgnoreCase) ||
            locale.Equals("zh-CHT", StringComparison.OrdinalIgnoreCase) ||
            locale.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
            locale.StartsWith("zh-Hant-", StringComparison.OrdinalIgnoreCase))
        {
            return TraditionalChineseLocale;
        }
        return FallbackLocale;
    }

    public static ITextLocalizer Create(string? requestedLocale = null) =>
        new CatalogTextLocalizer(ResolveLocale(requestedLocale) switch
        {
            DefaultLocale => Chinese.Value,
            TraditionalChineseLocale => TraditionalChinese.Value,
            _ => English.Value
        }, English.Value);

    private static TranslationCatalog Load(string locale)
    {
        string name = $"Monitor.Localization.{locale}.json";
        using var stream = typeof(BuiltInLocalizations).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded translation catalog: {name}.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return TranslationCatalog.Parse(locale, reader.ReadToEnd());
    }
}
