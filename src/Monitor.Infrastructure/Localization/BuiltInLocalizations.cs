// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Localization;

namespace Monitor.Infrastructure.Localization;

public sealed record SupportedLocale(string Code, string NativeName);

public static class BuiltInLocalizations
{
    public const string DefaultLocale = "zh-CN";
    public const string FallbackLocale = "en";

    private static readonly Lazy<TranslationCatalog> English = new(() => Load(FallbackLocale));
    private static readonly Lazy<TranslationCatalog> Chinese = new(() =>
    {
        var catalog = Load(DefaultLocale);
        catalog.ValidateAgainst(English.Value, requireComplete: true);
        return catalog;
    });

    public static IReadOnlyList<SupportedLocale> Supported { get; } = Array.AsReadOnly<SupportedLocale>(
        [new(FallbackLocale, "English"), new(DefaultLocale, "简体中文")]);

    public static string ResolveLocale(string? requestedLocale)
    {
        if (string.IsNullOrWhiteSpace(requestedLocale)) { return DefaultLocale; }
        string locale = requestedLocale.Trim();
        if (locale.Equals("cn", StringComparison.OrdinalIgnoreCase) ||
            locale.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
            locale.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
            locale.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase) ||
            locale.StartsWith("zh-Hans-", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultLocale;
        }
        return FallbackLocale;
    }

    public static ITextLocalizer Create(string? requestedLocale = null) =>
        new CatalogTextLocalizer(ResolveLocale(requestedLocale) == DefaultLocale ? Chinese.Value : English.Value, English.Value);

    private static TranslationCatalog Load(string locale)
    {
        string name = $"Monitor.Localization.{locale}.json";
        using var stream = typeof(BuiltInLocalizations).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded translation catalog: {name}.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return TranslationCatalog.Parse(locale, reader.ReadToEnd());
    }
}
