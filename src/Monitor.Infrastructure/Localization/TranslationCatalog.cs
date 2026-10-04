// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Monitor.Infrastructure.Localization;

public sealed class TranslationCatalog
{
    private readonly FrozenDictionary<string, Entry> _entries;

    private TranslationCatalog(string locale, Dictionary<string, Entry> entries)
    {
        Culture = CultureInfo.GetCultureInfo(locale);
        Locale = Culture.Name;
        _entries = entries.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public string Locale { get; }
    public IReadOnlyCollection<string> Keys => _entries.Keys;
    internal CultureInfo Culture { get; }

    public static TranslationCatalog Parse(string locale, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("A translation catalog must be a JSON object.");
        }

        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name) || property.Name != property.Name.Trim() ||
                property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                throw new FormatException($"Invalid translation entry: {property.Name}.");
            }

            string value = property.Value.GetString()!;
            CompositeFormat format;
            try
            {
                format = CompositeFormat.Parse(value);
            }
            catch (FormatException exception)
            {
                throw new FormatException($"Invalid translation format: {property.Name}.", exception);
            }
            if (format.MinimumArgumentCount > 32)
            {
                throw new FormatException($"Too many translation arguments: {property.Name}.");
            }
            var signature = new ArgumentSignature();
            object?[] probes = Enumerable.Range(0, format.MinimumArgumentCount).Select(index => (object?)index).ToArray();
            _ = string.Format(signature, format, probes);
            if (signature.Items.Select(item => item.Index).Distinct().Count() != format.MinimumArgumentCount)
            {
                throw new FormatException($"Translation argument indices must be contiguous from zero: {property.Name}.");
            }
            if (!entries.TryAdd(property.Name, new Entry(value, format, signature.Items)))
            {
                throw new FormatException($"Duplicate translation key: {property.Name}.");
            }
        }
        return new TranslationCatalog(locale, entries);
    }

    public void ValidateAgainst(TranslationCatalog fallback, bool requireComplete = false)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        foreach (var (key, entry) in _entries)
        {
            if (!fallback._entries.TryGetValue(key, out var reference))
            {
                throw new FormatException($"Unknown translation key in {Locale}: {key}.");
            }
            if (!entry.Signature.SetEquals(reference.Signature))
            {
                throw new FormatException($"Translation arguments differ in {Locale}: {key}.");
            }
        }
        if (requireComplete && _entries.Count != fallback._entries.Count)
        {
            throw new FormatException($"Translation catalog is incomplete: {Locale}.");
        }
    }

    internal Entry? Find(string key) => _entries.GetValueOrDefault(key);

    internal sealed record Entry(string Value, CompositeFormat Format, HashSet<(int Index, string? Format)> Signature);

    private sealed class ArgumentSignature : IFormatProvider, ICustomFormatter
    {
        internal HashSet<(int Index, string? Format)> Items { get; } = [];
        public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : null;
        public string Format(string? format, object? arg, IFormatProvider? formatProvider)
        {
            Items.Add(((int)arg!, format));
            return string.Empty;
        }
    }
}
