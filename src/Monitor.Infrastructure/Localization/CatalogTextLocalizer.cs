// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Localization;

namespace Monitor.Infrastructure.Localization;

public sealed class CatalogTextLocalizer : ITextLocalizer
{
    private readonly TranslationCatalog _catalog;
    private readonly TranslationCatalog _fallback;

    public CatalogTextLocalizer(TranslationCatalog catalog, TranslationCatalog fallback)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(fallback);
        catalog.ValidateAgainst(fallback);
        _catalog = catalog;
        _fallback = fallback;
    }

    public string Locale => _catalog.Locale;

    public LocalizedText Resolve(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var entry = _catalog.Find(key);
        if (entry is not null) { return new(key, entry.Value, Locale, Locale); }
        entry = _fallback.Find(key);
        return entry is not null
            ? new(key, entry.Value, Locale, _fallback.Locale)
            : new(key, $"[[{key}]]", Locale, null);
    }

    public string GetString(string key) => Format(key);

    public string Format(string key, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(arguments);
        var entry = _catalog.Find(key) ?? _fallback.Find(key);
        if (entry is null) { return $"[[{key}]]"; }
        if (arguments.Length != entry.Format.MinimumArgumentCount)
        {
            throw new FormatException($"Translation {key} requires {entry.Format.MinimumArgumentCount} arguments; received {arguments.Length}.");
        }
        // CompositeFormat's zero-argument fast path returns the raw template, including escaped braces.
        return entry.Format.MinimumArgumentCount == 0
            ? string.Format(_catalog.Culture, entry.Value, arguments)
            : string.Format(_catalog.Culture, entry.Format, arguments);
    }
}
