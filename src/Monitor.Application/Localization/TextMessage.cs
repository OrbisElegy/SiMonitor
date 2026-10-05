// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Localization;

// A catalog message rendered in the reader's language. Arguments are literal
// text or nested messages; a nested message without arguments is a plain key.
// Producers keep their own default text, so messages never decide behavior.
// Equality is structural, because messages travel inside records compared by value.
public sealed class TextMessage : IEquatable<TextMessage>
{
    private readonly object[] _arguments;

    public TextMessage(string key, params object[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Any(argument => argument is not (string or TextMessage)))
        { throw new ArgumentException("TextMessage.UnsupportedArgument", nameof(arguments)); }
        Key = key;
        _arguments = [.. arguments];
    }

    public string Key { get; }
    public IReadOnlyList<object> Arguments => _arguments;

    public string Render(ITextLocalizer text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Format(Key, _arguments.Select(argument => argument is TextMessage message ? message.Render(text) : argument).ToArray());
    }

    public bool Equals(TextMessage? other) => other is not null && Key == other.Key &&
        _arguments.SequenceEqual(other._arguments);

    public override bool Equals(object? obj) => Equals(obj as TextMessage);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Key, StringComparer.Ordinal);
        foreach (object argument in _arguments) { hash.Add(argument); }
        return hash.ToHashCode();
    }
}
