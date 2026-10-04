// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Localization;

public sealed record LocalizedText(string Key, string Value, string RequestedLocale, string? ResolvedLocale)
{
    public bool IsMissing => ResolvedLocale is null;
    public bool IsFallback => ResolvedLocale is not null && ResolvedLocale != RequestedLocale;
}

public interface ITextLocalizer
{
    public string Locale { get; }
    public LocalizedText Resolve(string key);
    public string GetString(string key);
    public string Format(string key, params object?[] arguments);
}
