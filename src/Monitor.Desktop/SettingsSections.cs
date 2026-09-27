// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Monitor.Desktop;

// Persistent section views: navigation never rebuilds draft controls or applies them.
internal sealed class SettingsSections : UserControl
{
    internal ListBox Sections { get; } = new();
    private readonly ComboBox _compact = new() { MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Grid _columns = new() { ColumnDefinitions = new("170,*") };
    private readonly ContentControl _detail = new();
    private Action<double>? _adaptNavigation;
    protected override Size MeasureOverride(Size availableSize)
    {
        _adaptNavigation?.Invoke(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }
    internal bool Compact => _compact.IsVisible;
    internal SettingsSections(string category, params (string Title, Control Content)[] sections)
    {
        var pages = sections.Select(section => new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Border
            {
                Background = DesktopFluentStyle.Surface,
                CornerRadius = new CornerRadius(8),
                BorderBrush = DesktopFluentStyle.Stroke,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(20),
                Margin = new Thickness(12, 0, 0, 0),
                Child = section.Content
            }
        }).ToArray();
        Sections.ItemsSource = sections.Select(s => Item(s.Title)).ToArray();
        _compact.ItemsSource = sections.Select(s => s.Title).ToArray();
        AutomationProperties.SetName(Sections, category + "参数组"); AutomationProperties.SetName(_compact, category + "参数组");
        var heading = new TextBlock { FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) }; top.Children.Add(heading); top.Children.Add(_compact);
        var root = new Grid { RowDefinitions = new("Auto,*"), Margin = new Thickness(16) };
        root.Children.Add(top); Grid.SetRow(_columns, 1); root.Children.Add(_columns);
        _columns.Children.Add(Sections); Grid.SetColumn(_detail, 1); _columns.Children.Add(_detail); Content = root;
        Sections.SelectionChanged += (_, args) =>
        {
            if (!ReferenceEquals(args.Source, Sections) || Sections.SelectedIndex < 0) { return; }
            int selected = Sections.SelectedIndex; _compact.SelectedIndex = selected;
            heading.Text = category + " / " + sections[selected].Title; _detail.Content = pages[selected];
        };
        _compact.SelectionChanged += (_, args) => { if (ReferenceEquals(args.Source, _compact) && _compact.SelectedIndex >= 0) { Sections.SelectedIndex = _compact.SelectedIndex; } };
        _adaptNavigation = width =>
        {
            bool compact = width < 760; Sections.IsVisible = !compact; _compact.IsVisible = compact;
            _columns.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 170);
        };
        Sections.SelectedIndex = 0;
    }
    internal static ListBoxItem Item(string title)
    {
        var text = new TextBlock { Text = title + "  ›", TextWrapping = TextWrapping.Wrap };
        var item = new ListBoxItem { MinHeight = 44, Padding = new Thickness(12, 10), Margin = new Thickness(0, 0, 0, 4), Content = text };
        AutomationProperties.SetName(item, title);
        item.PropertyChanged += (_, args) =>
        {
            if (args.Property != ListBoxItem.IsSelectedProperty) { return; }
            text.Text = title + (item.IsSelected ? "  ✓" : "  ›");
            text.FontWeight = item.IsSelected ? FontWeight.SemiBold : FontWeight.Normal;
        };
        return item;
    }
    internal static SettingsSections Split(string category, StackPanel owner, params (string Title, Control Start)[] groups)
    {
        var children = owner.Children.ToArray();
        var indices = groups.Select(g => Array.IndexOf(children, g.Start)).ToArray();
        if (indices[0] != 0 || indices.Any(i => i < 0) || !indices.SequenceEqual(indices.Order())) { throw new InvalidOperationException("Settings.InvalidGroups"); }
        owner.Children.Clear();
        return new(category, groups.Select((g, i) =>
        {
            var panel = new StackPanel { Spacing = 14 };
            foreach (var child in children[indices[i]..(i + 1 < indices.Length ? indices[i + 1] : children.Length)]) { panel.Children.Add(child); }
            return (g.Title, (Control)panel);
        }).ToArray());
    }
}
