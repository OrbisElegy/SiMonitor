// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
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
    private readonly int[] _sectionOfItem;
    private readonly int[] _itemOfSection;
    private readonly TextBlock[] _details;
    private readonly string[] _titles;
    private readonly CompactEntry[] _entries;
    private int _selected;
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
        : this(localization, category, new Dictionary<int, string>(), sections) { }
    // Headers are inert list rows; section indices stay independent of item indices.
    internal SettingsSections(DesktopLocalization? localization, string category, IReadOnlyDictionary<int, string> headers, (string Title, Control Content)[] sections)
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
        var items = new List<ListBoxItem>();
        var sectionOfItem = new List<int>();
        _details = new TextBlock[sections.Length];
        _titles = sections.Select(section => section.Title).ToArray();
        for (int index = 0; index < sections.Length; index++)
        {
            if (headers.TryGetValue(index, out string? header))
            {
                items.Add(Header(header, localization));
                sectionOfItem.Add(-1);
            }
            _details[index] = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = DesktopFluentStyle.SecondaryText, Margin = new Thickness(0, 0, 4, 0) };
            var item = Item(sections[index].Title, localization: localization, detail: _details[index]);
            if (headers.Count > 0)
            {
                // Grouped lists are long; use compact Fluent row density so every group stays visible.
                item.MinHeight = 36;
                item.Padding = new Thickness(4, 6, 10, 6);
                item.Margin = new Thickness(0, 0, 0, 2);
            }
            items.Add(item);
            sectionOfItem.Add(index);
        }
        _sectionOfItem = sectionOfItem.ToArray();
        _itemOfSection = Enumerable.Range(0, sections.Length).Select(section => Array.IndexOf(_sectionOfItem, section)).ToArray();
        Sections.ItemsSource = items;
        // The compact selector mirrors list rows, including inert headers and section state.
        _entries = _sectionOfItem.Select((section, item) => section < 0
            ? new CompactEntry(headers[_sectionOfItem[item + 1]], true)
            : new CompactEntry(_titles[section], false)).ToArray();
        _compact.ItemTemplate = new FuncDataTemplate<CompactEntry>((entry, _) => CompactRow(entry, localization));
        _compact.ContainerPrepared += (_, args) =>
        {
            if (args.Container is not ComboBoxItem container) { return; }
            var entry = _entries[args.Index];
            container.IsEnabled = !entry.IsHeader;
            container.Focusable = !entry.IsHeader;
            if (localization is not null && DesktopLocalization.IsKey(entry.Title)) { localization.Bind(container, AutomationProperties.NameProperty, entry.Title); }
            else { container.Bind(AutomationProperties.NameProperty, new Binding(nameof(CompactEntry.AccessibleName)) { Source = entry }); }
        };
        _compact.ItemsSource = _entries;
        AutomationProperties.SetName(Sections, category + "参数组"); AutomationProperties.SetName(_compact, category + "参数组");
        if (localization is not null)
        {
            localization.Bind(Sections, AutomationProperties.NameProperty, text => text.Format("settings.sectionNavigation", DesktopLocalization.Label(text, category)));
            localization.Bind(_compact, AutomationProperties.NameProperty, text => text.Format("settings.sectionNavigation", DesktopLocalization.Label(text, category)));
        }
        var heading = new TextBlock { FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) }; top.Children.Add(heading); top.Children.Add(_compact);
        var root = new Grid { RowDefinitions = new("Auto,*"), Margin = new Thickness(16) };
        root.Children.Add(top); Grid.SetRow(_columns, 1); root.Children.Add(_columns);
        _columns.Children.Add(Sections); Grid.SetColumn(_detail, 1); _columns.Children.Add(_detail); Content = root;
        Sections.SelectionChanged += (_, args) =>
        {
            if (!ReferenceEquals(args.Source, Sections) || Sections.SelectedIndex < 0) { return; }
            int selected = _sectionOfItem[Sections.SelectedIndex];
            if (selected < 0)
            {
                Sections.SelectedIndex = _itemOfSection[_selected];
                return;
            }
            _selected = selected;
            _compact.SelectedIndex = Sections.SelectedIndex;
            heading.Text = category + " / " + sections[selected].Title; _detail.Content = pages[selected];
            localization?.Bind(heading, TextBlock.TextProperty, text => text.Format("settings.sectionHeading",
                DesktopLocalization.Label(text, category), DesktopLocalization.Label(text, sections[selected].Title)));
        };
        _compact.SelectionChanged += (_, args) =>
        {
            if (!ReferenceEquals(args.Source, _compact) || _compact.SelectedIndex < 0) { return; }
            if (_sectionOfItem[_compact.SelectedIndex] < 0)
            {
                _compact.SelectedIndex = _itemOfSection[_selected];
                return;
            }
            Sections.SelectedIndex = _compact.SelectedIndex;
        };
        _adaptNavigation = width =>
        {
            bool compact = width < 760; Sections.IsVisible = !compact; _compact.IsVisible = compact;
            _columns.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 170);
        };
        SelectedSection = 0;
    }
    internal int SelectedSection
    {
        get => Sections.SelectedIndex < 0 ? -1 : _sectionOfItem[Sections.SelectedIndex];
        set => Sections.SelectedIndex = _itemOfSection[value];
    }
    internal string? DetailFor(int section) => _details[section].Text;
    internal void SetDetail(int section, string? text, string? accessibleText = null)
    {
        _details[section].Text = text;
        var item = (ListBoxItem)Sections.Items[_itemOfSection[section]]!;
        string name = accessibleText is null ? _titles[section] : _titles[section] + "，" + accessibleText;
        AutomationProperties.SetName(item, name);
        _entries[_itemOfSection[section]].Update(text, name);
    }
    private static Grid CompactRow(CompactEntry entry, DesktopLocalization? localization)
    {
        var title = new TextBlock { Text = entry.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (entry.IsHeader)
        {
            title.FontSize = 12;
            title.FontWeight = FontWeight.SemiBold;
            title.Foreground = DesktopFluentStyle.SecondaryText;
        }
        localization?.BindLabel(title, TextBlock.TextProperty, entry.Title);
        var detail = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = DesktopFluentStyle.SecondaryText, Margin = new Thickness(12, 0, 0, 0) };
        detail.Bind(TextBlock.TextProperty, new Binding(nameof(CompactEntry.Detail)) { Source = entry });
        var row = new Grid { ColumnDefinitions = new("*,Auto") };
        row.Children.Add(title); Grid.SetColumn(detail, 1); row.Children.Add(detail);
        return row;
    }
    private sealed class CompactEntry(string title, bool isHeader) : INotifyPropertyChanged
    {
        public string Title { get; } = title;
        public bool IsHeader { get; } = isHeader;
        public string? Detail { get; private set; }
        public string AccessibleName { get; private set; } = title;
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Update(string? detail, string accessibleName)
        {
            Detail = detail;
            AccessibleName = accessibleName;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleName)));
        }
    }
    private static ListBoxItem Header(string title, DesktopLocalization? localization)
    {
        var text = new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = DesktopFluentStyle.SecondaryText };
        localization?.BindLabel(text, TextBlock.TextProperty, title);
        return new ListBoxItem
        {
            Content = text,
            IsEnabled = false,
            Focusable = false,
            IsHitTestVisible = false,
            MinHeight = 0,
            Padding = new Thickness(14, 10, 10, 2)
        };
    }
    internal static void StyleNavigation(ListBox list)
    {
        list.Background = Brushes.Transparent;
        list.BorderThickness = new Thickness(0);
        list.Padding = new Thickness(4);
        list.VerticalAlignment = VerticalAlignment.Top;
    }
    internal static ListBoxItem Item(string title, bool showChevron = true, DesktopLocalization? localization = null, TextBlock? detail = null)
    {
        var text = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        var indicator = new Border { Width = 3, Height = 18, CornerRadius = new CornerRadius(2), Background = DesktopFluentStyle.Accent, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid { ColumnDefinitions = new("3,*,Auto,16") };
        content.Children.Add(indicator); Grid.SetColumn(text, 1); content.Children.Add(text);
        if (detail is not null) { Grid.SetColumn(detail, 2); content.Children.Add(detail); }
        if (showChevron)
        {
            var chevron = new TextBlock { Text = "›", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(chevron, 3); content.Children.Add(chevron);
        }
        var item = new ListBoxItem { MinHeight = 44, Padding = new Thickness(4, 10, 10, 10), Margin = new Thickness(0, 0, 0, 4), Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(item, title);
        localization?.BindLabel(text, TextBlock.TextProperty, title);
        localization?.BindLabel(item, AutomationProperties.NameProperty, title);
        item.PropertyChanged += (_, args) =>
        {
            if (args.Property != ListBoxItem.IsSelectedProperty) { return; }
            indicator.IsVisible = item.IsSelected;
            text.FontWeight = item.IsSelected ? FontWeight.SemiBold : FontWeight.Normal;
        };
        return item;
    }
    internal static SettingsSections Split(DesktopLocalization? localization, string category, StackPanel owner, params (string Title, Control Start)[] groups)
        => Split(localization, category, owner, new Dictionary<int, string>(), groups);
    internal static SettingsSections Split(DesktopLocalization? localization, string category, StackPanel owner, IReadOnlyDictionary<int, string> headers, (string Title, Control Start)[] groups)
    {
        var children = owner.Children.ToArray();
        int[] indices = groups.Select(g => Array.IndexOf(children, g.Start)).ToArray();
        if (indices[0] != 0 || indices.Any(i => i < 0) || !indices.SequenceEqual(indices.Order())) { throw new InvalidOperationException("Settings.InvalidGroups"); }
        owner.Children.Clear();
        return new(localization, category, headers, groups.Select((g, i) =>
        {
            var panel = new StackPanel { Spacing = 14 };
            foreach (var child in children[indices[i]..(i + 1 < indices.Length ? indices[i + 1] : children.Length)]) { panel.Children.Add(child); }
            return (g.Title, (Control)panel);
        }).ToArray());
    }
}
