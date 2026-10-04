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
        : this(null, category, sections) { }
    internal SettingsSections(DesktopLocalization? localization, string category, params (string Title, Control Content)[] sections)
    {
        var pages = sections.Select(section => SettingsScroll.Create(new Border
        {
            Background = DesktopFluentStyle.Surface,
            CornerRadius = new CornerRadius(8),
            BorderBrush = DesktopFluentStyle.Stroke,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20),
            Margin = new Thickness(0),
            Child = section.Content
        })).ToArray();
        StyleNavigation(Sections);
        Sections.ItemsSource = sections.Select(s => Item(s.Title, localization: localization)).ToArray();
        if (localization is null) { _compact.ItemsSource = sections.Select(s => s.Title).ToArray(); }
        else { localization.SetChoices(_compact, sections.Select(s => s.Title).ToArray()); }
        AutomationProperties.SetName(Sections, category + "参数组"); AutomationProperties.SetName(_compact, category + "参数组");
        if (localization is not null)
        {
            localization.Bind(Sections, AutomationProperties.NameProperty, text => text.Format("settings.sectionNavigation", text.GetString(category)));
            localization.Bind(_compact, AutomationProperties.NameProperty, text => text.Format("settings.sectionNavigation", text.GetString(category)));
        }
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
            localization?.Bind(heading, TextBlock.TextProperty, text => text.Format("settings.sectionHeading", text.GetString(category), text.GetString(sections[selected].Title)));
        };
        _compact.SelectionChanged += (_, args) => { if (ReferenceEquals(args.Source, _compact) && _compact.SelectedIndex >= 0) { Sections.SelectedIndex = _compact.SelectedIndex; } };
        _adaptNavigation = width =>
        {
            bool compact = width < 760; Sections.IsVisible = !compact; _compact.IsVisible = compact;
            _columns.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 170);
        };
        Sections.SelectedIndex = 0;
    }
    internal static void StyleNavigation(ListBox list)
    {
        list.Background = Brushes.Transparent;
        list.BorderThickness = new Thickness(0);
        list.Padding = new Thickness(4);
        list.VerticalAlignment = VerticalAlignment.Top;
    }
    internal static ListBoxItem Item(string title, bool showChevron = true, DesktopLocalization? localization = null)
    {
        var text = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        var indicator = new Border { Width = 3, Height = 18, CornerRadius = new CornerRadius(2), Background = DesktopFluentStyle.Accent, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid { ColumnDefinitions = new("3,*,16") };
        content.Children.Add(indicator); Grid.SetColumn(text, 1); content.Children.Add(text);
        if (showChevron)
        {
            var chevron = new TextBlock { Text = "›", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(chevron, 2); content.Children.Add(chevron);
        }
        var item = new ListBoxItem { MinHeight = 44, Padding = new Thickness(4, 10, 10, 10), Margin = new Thickness(0, 0, 0, 4), Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(item, title);
        localization?.Bind(text, TextBlock.TextProperty, title);
        localization?.Bind(item, AutomationProperties.NameProperty, title);
        item.PropertyChanged += (_, args) =>
        {
            if (args.Property != ListBoxItem.IsSelectedProperty) { return; }
            indicator.IsVisible = item.IsSelected;
            text.FontWeight = item.IsSelected ? FontWeight.SemiBold : FontWeight.Normal;
        };
        return item;
    }
    internal static SettingsSections Split(string category, StackPanel owner, params (string Title, Control Start)[] groups)
    {
        var children = owner.Children.ToArray();
        int[] indices = groups.Select(g => Array.IndexOf(children, g.Start)).ToArray();
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
