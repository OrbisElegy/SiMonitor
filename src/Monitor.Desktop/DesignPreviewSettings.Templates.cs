// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Monitor.Desktop;

// Waveform template selection: one tab per signal. Large catalogues are browsed by
// group or searched by name; previews are built only for visible cards, and choosing
// a card changes the draft only.
internal sealed partial class DesignPreviewSettings
{
    internal const int TemplateSearchLimit = 12;
    internal TabControl TemplateSignals { get; } = new() { Padding = new Thickness(0) };
    internal IReadOnlyList<TemplatePage> TemplatePages => _templatePages;
    private readonly List<TemplatePage> _templatePages = [];

    private TabControl BuildTemplatePages()
    {
        _templatePages.Add(new(this, "心电图", 0, EcgChoices, EcgChooserGroups.For, EcgChooserGroups.Ordered,
            () => EcgSelection, x => EcgSelection = x));
        _templatePages.Add(new(this, "呼吸", 1, RespirationChoices, i => i == 0 ? "规则呼吸" : "异常呼吸示意", null,
            () => RespirationSelection, x => RespirationSelection = x));
        _templatePages.Add(new(this, "射血", 3, EjectionChoices, i => i == 0 ? "节律相关" : "异常射血示意", null,
            () => EjectionSelection, x => EjectionSelection = x));
        TemplateSignals.ItemsSource = _templatePages.Select(page => new TabItem
        {
            Header = page.Title,
            Content = page,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 20, 0),
            FontSize = 14,
            MinHeight = 44
        }).ToArray();
        TemplateSignals.SelectionChanged += (_, args) =>
        {
            if (ReferenceEquals(args.Source, TemplateSignals) && TemplateSignals.SelectedIndex >= 0)
            { _templatePages[TemplateSignals.SelectedIndex].Refresh(); }
        };
        TemplateSignals.SelectedIndex = 0;
        TemplateSignals.Margin = new Thickness(20, 12, 20, 0);
        AutomationProperties.SetName(TemplateSignals, "波形生成信号");
        foreach (var page in _templatePages) { _refreshSignalRows.Add(page.Reset); }
        return TemplateSignals;
    }

    internal sealed class TemplatePage : UserControl
    {
        private readonly DesignPreviewSettings _owner;
        private readonly int _channel;
        private readonly string[] _choices;
        private readonly Func<int, string> _group;
        private readonly IReadOnlyList<string> _groups;
        private readonly Func<int> _read;
        private readonly Action<int> _write;
        private readonly TextBlock[] _groupMarks;
        private readonly Grid _body = new() { ColumnDefinitions = new("200,16,*"), RowDefinitions = new("Auto,*") };
        private readonly Grid _tools = new() { ColumnDefinitions = new("0,0,*"), Margin = new Thickness(0, 0, 0, 8) };
        private ScrollViewer? _groupScroll;
        private readonly StackPanel _cards = new() { Spacing = 12 };
        private readonly ScrollViewer _scroll;
        private string _activeGroup;
        private bool _syncing;
        internal string Title { get; }
        internal bool Browsable { get; }
        internal TextBlock Selection { get; } = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        internal Button Advanced { get; } = new() { Content = "当前波形高级参数", MinHeight = 44, Padding = new Thickness(12, 0) };
        internal TextBox Search { get; } = new() { MinHeight = 44 };
        internal ListBox Groups { get; } = new();
        internal ComboBox CompactGroups { get; } = new() { MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = false };
        internal TextBlock Results { get; } = new() { Foreground = DesktopFluentStyle.SecondaryText, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        internal string ActiveGroup => _activeGroup;
        internal bool Compact => CompactGroups.IsVisible;
        internal IEnumerable<Button> CardButtons => _cards.Children.OfType<WrapPanel>().SelectMany(panel => panel.Children.OfType<Button>());

        internal TemplatePage(DesignPreviewSettings owner, string title, int channel, string[] choices, Func<int, string> group,
            IReadOnlyList<string>? groups, Func<int> read, Action<int> write)
        {
            _owner = owner;
            Title = title;
            _channel = channel;
            _choices = choices;
            _group = group;
            _read = read;
            _write = write;
            Browsable = groups is not null;
            _groups = groups ?? Enumerable.Range(0, choices.Length).Select(group).Distinct().ToArray();
            _activeGroup = group(read());
            _groupMarks = new TextBlock[_groups.Count];
            Advanced.Click += (_, _) => owner.OpenAdvanced(channel);
            var header = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 12, 0, 12) };
            header.Children.Add(Selection); Grid.SetColumn(Advanced, 1); header.Children.Add(Advanced);
            var root = new Grid { RowDefinitions = new("Auto,*") };
            root.Children.Add(header);
            var cardArea = new StackPanel { Spacing = 12 };
            cardArea.Children.Add(Results); cardArea.Children.Add(_cards);
            _scroll = SettingsScroll.Create(cardArea);
            if (Browsable)
            {
                Search.PlaceholderText = "搜索" + title + "模板名称";
                AutomationProperties.SetName(Search, "搜索" + title + "模板");
                // Wide pages keep search above the group column so cards use the full height;
                // compact pages put the group selector and search on one row.
                _tools.Children.Add(CompactGroups); Grid.SetColumn(Search, 2); _tools.Children.Add(Search);
                _body.Children.Add(_tools);
                SettingsSections.StyleNavigation(Groups);
                Groups.ItemsSource = _groups.Select((name, index) =>
                {
                    _groupMarks[index] = new TextBlock { Text = "●", FontSize = 10, Foreground = DesktopFluentStyle.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
                    var item = SettingsSections.Item(name, showChevron: false, detail: _groupMarks[index]);
                    item.MinHeight = 36;
                    item.Padding = new Thickness(4, 6, 10, 6);
                    item.Margin = new Thickness(0, 0, 0, 2);
                    return item;
                }).ToArray();
                CompactGroups.ItemsSource = _groups.ToArray();
                AutomationProperties.SetName(Groups, title + "模板分组");
                AutomationProperties.SetName(CompactGroups, title + "模板分组");
                _groupScroll = SettingsScroll.Create(Groups);
                Grid.SetRow(_groupScroll, 1); _body.Children.Add(_groupScroll);
                Grid.SetColumn(_scroll, 2); Grid.SetRowSpan(_scroll, 2);
                Groups.SelectionChanged += (_, args) =>
                {
                    if (_syncing || !ReferenceEquals(args.Source, Groups) || Groups.SelectedIndex < 0) { return; }
                    ShowGroup(_groups[Groups.SelectedIndex]);
                };
                CompactGroups.SelectionChanged += (_, args) =>
                {
                    if (_syncing || !ReferenceEquals(args.Source, CompactGroups) || CompactGroups.SelectedIndex < 0) { return; }
                    ShowGroup(_groups[CompactGroups.SelectedIndex]);
                };
                Search.TextChanged += (_, _) => Refresh();
            }
            else
            {
                Grid.SetColumnSpan(_scroll, 3); Grid.SetRowSpan(_scroll, 2);
                _body.ColumnDefinitions[0].Width = new GridLength(0);
                _body.ColumnDefinitions[1].Width = new GridLength(0);
            }
            _body.Children.Add(_scroll);
            Grid.SetRow(_body, 1); root.Children.Add(_body);
            Content = root;
            Reset();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            // Narrow pages trade the group column for a selector so cards keep their size.
            bool compact = Browsable && availableSize.Width < 760;
            if (compact != CompactGroups.IsVisible)
            {
                CompactGroups.IsVisible = compact;
                _groupScroll!.IsVisible = !compact;
                _tools.ColumnDefinitions[0].Width = compact ? GridLength.Star : new GridLength(0);
                _tools.ColumnDefinitions[1].Width = new GridLength(compact ? 12 : 0);
                Grid.SetColumnSpan(_tools, compact ? 3 : 1);
                Grid.SetColumn(_scroll, compact ? 0 : 2);
                Grid.SetColumnSpan(_scroll, compact ? 3 : 1);
                Grid.SetRow(_scroll, compact ? 1 : 0);
                Grid.SetRowSpan(_scroll, compact ? 1 : 2);
            }
            return base.MeasureOverride(availableSize);
        }

        // Discards browsing state and shows the group that holds the current draft.
        internal void Reset()
        {
            _activeGroup = _group(_read());
            _syncing = true;
            Search.Text = "";
            _syncing = false;
            Refresh();
        }

        internal void ShowGroup(string group)
        {
            _activeGroup = group;
            _syncing = true;
            Search.Text = "";
            _syncing = false;
            Refresh();
        }

        internal void Refresh()
        {
            if (_syncing) { return; }
            int selected = _read();
            Selection.Text = "当前选择：" + _choices[selected] + " · 应用后生效";
            string current = _group(selected);
            for (int index = 0; index < _groupMarks.Length; index++)
            {
                if (_groupMarks[index] is not { } mark) { continue; }
                mark.IsVisible = _groups[index] == current;
                var item = (ListBoxItem)Groups.Items[index]!;
                AutomationProperties.SetName(item, _groups[index] + (mark.IsVisible ? "，含当前选择" : ""));
            }
            string[] words = (Search.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            int[] shown;
            if (words.Length > 0)
            {
                int[] matches = Enumerable.Range(0, _choices.Length).Where(index => words.All(word =>
                    (_choices[index] + " " + _group(index)).Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray();
                shown = matches.Take(TemplateSearchLimit).ToArray();
                Results.Text = matches.Length == 0 ? "没有匹配的模板，请更换关键词。"
                    : matches.Length > TemplateSearchLimit ? $"找到 {matches.Length} 个模板，显示前 {TemplateSearchLimit} 个；请输入更具体的名称。"
                    : $"找到 {matches.Length} 个模板";
                Results.IsVisible = true;
            }
            else
            {
                shown = Enumerable.Range(0, _choices.Length).Where(index => !Browsable || _group(index) == _activeGroup).ToArray();
                Results.IsVisible = false;
            }
            _syncing = true;
            int groupIndex = words.Length > 0 ? -1 : _groups.ToList().IndexOf(_activeGroup);
            if (Browsable) { Groups.SelectedIndex = groupIndex; CompactGroups.SelectedIndex = groupIndex; }
            _syncing = false;
            _cards.Children.Clear();
            bool headings = !Browsable || words.Length > 0;
            foreach (var section in shown.GroupBy(_group))
            {
                if (headings)
                {
                    _cards.Children.Add(new TextBlock { Text = section.Key, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = DesktopFluentStyle.SecondaryText });
                }
                var panel = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (int index in section) { panel.Children.Add(Card(index)); }
                _cards.Children.Add(panel);
            }
        }

        private Button Card(int index)
        {
            bool selected = _read() == index;
            var heading = new Grid { ColumnDefinitions = new("*,Auto"), Height = 40 };
            heading.Children.Add(new TextBlock { Text = _choices[index], Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
            var check = new TextBlock { Text = "✓", Foreground = Brush.Parse("#60CDFF"), FontWeight = FontWeight.SemiBold, IsVisible = selected, Margin = new Thickness(6, 0, 0, 0) };
            Grid.SetColumn(check, 1); heading.Children.Add(check);
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(heading);
            var candidate = new Button
            {
                Content = panel,
                Width = 238,
                Height = 162,
                CornerRadius = new CornerRadius(8),
                VerticalContentAlignment = VerticalAlignment.Top,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 10, 10),
                Background = Brushes.Black,
                BorderBrush = selected ? Brush.Parse("#60CDFF") : Brushes.Black,
                BorderThickness = new Thickness(selected ? 3 : 1)
            };
            AutomationProperties.SetName(candidate, _choices[index] + (selected ? "，已选择" : "，选择此模板"));
            try
            {
                var owner = _owner;
                var source = owner.Preview(_channel == 0 ? index : owner.EcgSelection, _channel == 1 ? index : owner.RespirationSelection,
                    _channel == 3 ? index : owner.EjectionSelection);
                panel.Children.Add(owner.Thumbnail(() => source, _channel, _channel == 1 ? index : null));
                candidate.Click += (_, _) =>
                {
                    _write(index);
                    owner.Localization.Bind(owner.Status, TextBlock.TextProperty, "settings.templateSelected", _choices[index]);
                    Refresh();
                    if (CardButtons.FirstOrDefault(button => AutomationProperties.GetName(button) == _choices[index] + "，已选择") is { } chosen)
                    { RestoreFocus(chosen); }
                };
            }
            catch (ArgumentException)
            {
                candidate.IsEnabled = false;
                panel.Children.Add(new TextBlock { Text = "与当前其他参数不兼容", Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
                AutomationProperties.SetHelpText(candidate, "请先调整其他波形设置，此组合当前不可用。");
            }
            return candidate;
        }
    }
}
