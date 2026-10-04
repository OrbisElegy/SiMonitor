// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    internal void Select(string locale)
    {
        _text = BuiltInLocalizations.Create(locale);
        foreach (var reference in _values.ToArray())
        {
            if (reference.TryGetTarget(out var value)) { value.Refresh(); }
        }
        _values.RemoveAll(reference => !reference.TryGetTarget(out _));
    }

    internal void Bind(AvaloniaObject target, AvaloniaProperty property, string key, params object?[] arguments) =>
        Bind(target, property, text => text.Format(key, arguments));

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
