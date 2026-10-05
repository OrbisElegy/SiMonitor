// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Monitor.Application.Localization;
using Monitor.Infrastructure.Localization;

namespace Monitor.Desktop;

// Owned by one window. Bindings retain their value; this registry does not retain old controls.
internal sealed class DesktopLocalization(string? locale = null)
{
    private ITextLocalizer _text = BuiltInLocalizations.Create(locale);
    private readonly List<WeakReference<LocalizedValue>> _values = [];
    private readonly ConditionalWeakTable<AvaloniaObject, Dictionary<AvaloniaProperty, LocalizedValue>> _sources = new();
    internal string Locale => _text.Locale;
    // The localizer for the selected language, for text computed outside bindings.
    internal ITextLocalizer Current => _text;
    internal string Get(string key) => _text.GetString(key);
    internal string Format(string key, params object?[] arguments) => _text.Format(key, arguments);

    // Data items keep the closed selection presenter bound to Value. ComboBoxItem.Content
    // is copied into SelectionBoxItem by ComboBox and does not follow later content changes.
    internal void SetChoices(ComboBox control, params string[] keys) =>
        SetChoices(control, keys.Select(key => (Func<ITextLocalizer, string>)(text => text.GetString(key))));

    internal void SetChoices(ComboBox control, IEnumerable<Func<ITextLocalizer, string>> formats)
    {
        int selected = control.SelectedIndex;
        control.ItemTemplate = new FuncDataTemplate<LocalizedValue>((_, _) =>
        {
            var caption = new TextBlock();
            caption.Bind(TextBlock.TextProperty, new Binding(nameof(LocalizedValue.Value)));
            return caption;
        });
        control.ItemsSource = formats.Select(format =>
        {
            var value = new LocalizedValue(this, format);
            _values.Add(new(value));
            return value;
        }).ToArray();
        control.SelectedIndex = selected;
    }

    // Raised after bound values refresh, so owners can rebuild text computed outside bindings.
    internal event Action? LocaleChanged;

    internal void Select(string locale)
    {
        _text = BuiltInLocalizations.Create(locale);
        foreach (var reference in _values.ToArray())
        {
            if (reference.TryGetTarget(out var value)) { value.Refresh(); }
        }
        _values.RemoveAll(reference => !reference.TryGetTarget(out _));
        LocaleChanged?.Invoke();
    }

    internal void Bind(AvaloniaObject target, AvaloniaProperty property, string key, params object?[] arguments) =>
        Bind(target, property, text => text.Format(key, arguments));

    // Settings navigation mixes catalog keys with titles that are not translated yet.
    // Only dotted catalog keys are looked up; other titles are shown verbatim.
    internal static bool IsKey(string value) => KeyPattern.IsMatch(value);
    internal static string Label(ITextLocalizer text, string keyOrTitle) => IsKey(keyOrTitle) ? text.GetString(keyOrTitle) : keyOrTitle;
    internal void BindLabel(AvaloniaObject target, AvaloniaProperty property, string keyOrTitle) =>
        Bind(target, property, text => Label(text, keyOrTitle));
    private static readonly Regex KeyPattern = new(@"^[a-z][A-Za-z0-9]*(\.[A-Za-z0-9]+)+$", RegexOptions.CultureInvariant);

    internal void Bind(AvaloniaObject target, AvaloniaProperty property, Func<ITextLocalizer, string> format)
    {
        _values.RemoveAll(reference => !reference.TryGetTarget(out _));
        var value = new LocalizedValue(this, format);
        _sources.GetOrCreateValue(target)[property] = value;
        _values.Add(new(value));
        target.Bind(property, new Binding(nameof(LocalizedValue.Value)) { Source = value, Mode = BindingMode.OneWay });
    }

    private sealed class LocalizedValue(DesktopLocalization owner, Func<ITextLocalizer, string> format) : INotifyPropertyChanged
    {
        public string Value => format(owner._text);
        public override string ToString() => Value;
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
}
