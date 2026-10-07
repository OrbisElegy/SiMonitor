// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Localization;

namespace Monitor.Desktop;

// Browsing and editing share the existing condition editors, including invalid drafts.
internal sealed class EcgAlarmSettingsPage : UserControl
{
    private readonly AlarmNotificationSettingsPanel _settings;
    private readonly DesktopLocalization _localization;
    private readonly Dictionary<string, string> _searchLabels;
    private readonly StackPanel _browse = new() { Spacing = 12 };
    private readonly StackPanel _detail = new() { Spacing = 12 };
    private readonly ContentControl _editor = new();
    private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _count = new() { Foreground = DesktopFluentStyle.SecondaryText, VerticalAlignment = VerticalAlignment.Center };
    private readonly Avalonia.Controls.Shapes.Path _filterIcon = new()
    {
        Width = 20,
        Height = 20,
        Stretch = Stretch.Uniform,
        StrokeThickness = 1.6,
        Data = Geometry.Parse("M3,4 L21,4 L14,12 L14,20 L10,18 L10,12 Z")
    };
    private readonly TextBlock _empty = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<string, Button> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _summaries = new(StringComparer.Ordinal);
    private readonly List<(StackPanel Panel, string[] Ids)> _groups = [];

    private static readonly (string Key, string[] Ids)[] Groups =
    [
        ("alarm.ecgGroupCritical", ["ecg-asystole", "ecg-vf", "ecg-vt"]),
        ("alarm.ecgGroupPauses", ["ecg-pause", "ecg-missed"]),
        ("alarm.ecgGroupVentricular", ["ecg-nsvt", "ecg-ventricular", "ecg-pvc-run", "ecg-pvc-pair", "ecg-bigeminy", "ecg-trigeminy", "ecg-multiform", "ecg-pvc-rate", "ecg-ron-t"]),
        ("alarm.ecgGroupAtrial", ["ecg-svt", "ecg-af", "ecg-irregular", "ecg-af-end", "ecg-irregular-end"]),
        ("alarm.ecgGroupRepolarization", ["ecg-st-high", "ecg-st-low", "ecg-qtc", "ecg-delta-qtc"]),
        ("alarm.ecgGroupPacing", ["ecg-pacer-capture", "ecg-pacer-pacing"])
    ];
    internal Button Filter { get; } = new()
    {
        Width = 44,
        Height = 44,
        Padding = new Thickness(10),
        HorizontalAlignment = HorizontalAlignment.Right,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };
    internal Flyout FilterTools { get; } = new() { Placement = PlacementMode.BottomEdgeAlignedRight };
    internal ComboBox Category { get; } = new() { MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
    internal Button Done { get; } = new() { MinHeight = 44 };
    internal bool HasFilters => Category.SelectedIndex > 0 || !string.IsNullOrWhiteSpace(Search.Text);
    internal TextBox Search { get; } = new() { MinHeight = 44 };
    internal Button Clear { get; } = new() { MinHeight = 44 };
    internal Button Back { get; } = new() { MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left };
    internal IReadOnlyDictionary<string, Button> Rows => _rows;
    internal string? SelectedId { get; private set; }
    internal int VisibleCount => _rows.Values.Count(row => row.IsVisible);

    internal EcgAlarmSettingsPage(AlarmNotificationSettingsPanel settings, DesktopLocalization localization)
    {
        _settings = settings;
        _localization = localization;
        var english = BuiltInLocalizations.Create("en");
        _searchLabels = EcgAlarmNotices.Descriptors.ToDictionary(item => item.Id,
            item => item.Text + " " + item.Message.Render(english) + " " + item.Id + " " + SearchAliases(item), StringComparer.Ordinal);
        _localization.Bind(Search, TextBox.PlaceholderTextProperty, "alarm.ecgSearch");
        _localization.Bind(Search, AutomationProperties.NameProperty, "alarm.ecgSearch");
        _localization.Bind(Clear, ContentControl.ContentProperty, "alarm.ecgClearFilters");
        _localization.Bind(Back, ContentControl.ContentProperty, "alarm.ecgBack");
        _localization.Bind(_empty, TextBlock.TextProperty, "alarm.ecgNoResults");
        var toolbar = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        toolbar.Children.Add(_count);
        Filter.Content = _filterIcon;
        Grid.SetColumn(Filter, 1);
        toolbar.Children.Add(Filter);
        _browse.Children.Add(toolbar);
        var filters = new StackPanel { Width = 300, Spacing = 12 };
        var filterHeading = new TextBlock { FontWeight = FontWeight.SemiBold };
        _localization.Bind(filterHeading, TextBlock.TextProperty, "alarm.ecgFilter");
        filters.Children.Add(filterHeading);
        filters.Children.Add(Search);
        var categoryLabel = new TextBlock();
        _localization.Bind(categoryLabel, TextBlock.TextProperty, "alarm.ecgCategory");
        filters.Children.Add(categoryLabel);
        _localization.Bind(Category, AutomationProperties.NameProperty, "alarm.ecgCategory");
        _localization.SetChoices(Category, Groups.Select(group => group.Key).Prepend("alarm.ecgAll").ToArray());
        Category.SelectedIndex = 0;
        filters.Children.Add(Category);
        var actions = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
        actions.Children.Add(Clear);
        _localization.Bind(Done, ContentControl.ContentProperty, "alarm.ecgFilterDone");
        Grid.SetColumn(Done, 2);
        actions.Children.Add(Done);
        filters.Children.Add(actions);
        FilterTools.Content = filters;
        Filter.Click += (_, _) => FilterTools.ShowAt(Filter);
        Done.Click += (_, _) => FilterTools.Hide();
        FilterTools.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (FilterTools.IsOpen) { Search.Focus(NavigationMethod.Tab); }
        }, DispatcherPriority.Loaded);
        FilterTools.Closed += (_, _) =>
        {
            if (SelectedId is null) { Filter.Focus(NavigationMethod.Tab); }
        };
        DetachedFromVisualTree += (_, _) => FilterTools.Hide();
        foreach (var (key, ids) in Groups)
        {
            var group = new StackPanel { Spacing = 4 };
            var heading = new TextBlock { FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 4) };
            _localization.Bind(heading, TextBlock.TextProperty, key);
            group.Children.Add(heading);
            foreach (string id in ids)
            {
                var descriptor = EcgAlarmNotices.Descriptors.Single(item => item.Id == id);
                var name = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
                _localization.Bind(name, TextBlock.TextProperty, descriptor.Message.Render);
                var summary = new TextBlock { Foreground = DesktopFluentStyle.SecondaryText, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
                _summaries.Add(id, summary);
                var text = new StackPanel { Spacing = 3 };
                text.Children.Add(name);
                text.Children.Add(summary);
                var content = new Grid { ColumnDefinitions = new("*,16") };
                content.Children.Add(text);
                var chevron = new TextBlock { Text = "›", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(chevron, 1);
                content.Children.Add(chevron);
                var row = new Button
                {
                    Content = content,
                    MinHeight = 54,
                    Padding = new Thickness(12, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                row.Click += (_, _) => Open(id);
                _rows.Add(id, row);
                group.Children.Add(row);
            }
            _groups.Add((group, ids));
            _browse.Children.Add(group);
        }
        _browse.Children.Add(_empty);
        _detail.Children.Add(Back);
        _detail.Children.Add(_title);
        _detail.Children.Add(_editor);
        Back.Click += (_, _) => ShowList();
        Search.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty) { Refresh(); }
        };
        Category.SelectionChanged += (_, _) => Refresh();
        Clear.Click += (_, _) => { Category.SelectedIndex = 0; Search.Text = ""; Refresh(); Search.Focus(); };
        _localization.LocaleChanged += Refresh;
        Content = _browse;
        Refresh();
    }

    private static string SearchAliases(EcgAlarmDescriptor descriptor) => descriptor.Id switch
    {
        "ecg-vt" or "ecg-nsvt" => "室速 VT",
        "ecg-vf" => "室颤 VF",
        "ecg-svt" => "室上速 SVT",
        "ecg-af" or "ecg-af-end" => "房颤 AF AFib",
        _ => descriptor.Text.Contains("室早", StringComparison.Ordinal) ? "室性早搏 PVC" : ""
    };

    internal void Open(string id)
    {
        var descriptor = EcgAlarmNotices.Descriptors.Single(item => item.Id == id);
        SelectedId = id;
        FilterTools.Hide();
        _title.Text = descriptor.Message.Render(_localization.Current);
        _editor.Content = _settings.Editors[id];
        Content = _detail;
        Dispatcher.UIThread.Post(() =>
        {
            if (SelectedId == id) { Back.Focus(NavigationMethod.Tab); }
        }, DispatcherPriority.Loaded);
    }

    private void ShowList()
    {
        string? previous = SelectedId;
        SelectedId = null;
        _editor.Content = null;
        Content = _browse;
        Refresh();
        Dispatcher.UIThread.Post(() =>
        {
            if (SelectedId is not null) { return; }
            if (previous is not null && _rows[previous].IsVisible) { _rows[previous].Focus(NavigationMethod.Tab); }
            else { Filter.Focus(); }
        }, DispatcherPriority.Loaded);
    }

    internal void Refresh()
    {
        string[] terms = (Search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var descriptor in EcgAlarmNotices.Descriptors)
        {
            string name = descriptor.Message.Render(_localization.Current);
            string searchable = name + " " + _searchLabels[descriptor.Id];
            var editor = _settings.Editors[descriptor.Id];
            string summary = _settings.SummaryFor(descriptor.Id);
            if (editor.IsValid && editor.LatchUntilAcknowledged.IsChecked == true)
            { summary += " · " + _localization.Get("alarm.ecgRetained"); }
            _summaries[descriptor.Id].Text = summary;
            AutomationProperties.SetName(_rows[descriptor.Id], _localization.Format("alarm.eventRowName", name, summary));
            _rows[descriptor.Id].IsVisible = (Category.SelectedIndex <= 0 || Groups[Category.SelectedIndex - 1].Ids.Contains(descriptor.Id)) &&
                terms.All(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var (panel, ids) in _groups) { panel.IsVisible = ids.Any(id => _rows[id].IsVisible); }
        _empty.IsVisible = VisibleCount == 0;
        _count.Text = _localization.Format("alarm.ecgCount", VisibleCount, _rows.Count);
        Clear.IsEnabled = HasFilters;
        _filterIcon.Stroke = HasFilters ? DesktopFluentStyle.Accent : DesktopFluentStyle.Text;
        Filter.BorderBrush = HasFilters ? DesktopFluentStyle.Accent : DesktopFluentStyle.Stroke;
        string filterLabel = _localization.Get(HasFilters ? "alarm.ecgFilterActive" : "alarm.ecgFilter");
        AutomationProperties.SetName(Filter, filterLabel);
        ToolTip.SetTip(Filter, filterLabel);
        if (SelectedId is { } selected)
        { _title.Text = EcgAlarmNotices.Descriptors.Single(item => item.Id == selected).Message.Render(_localization.Current); }
    }
}
