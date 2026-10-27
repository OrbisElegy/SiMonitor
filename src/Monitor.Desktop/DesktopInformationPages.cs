// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Monitor.Desktop;

internal static class DesktopInformationPages
{
    internal sealed record Topic(string Id, string Category, string Title, string Text);
    // Chinese topics are the reference set; every other language lists the same ids in the same order.
    private static readonly Dictionary<string, string> TopicResources = new(StringComparer.Ordinal)
    {
        ["zh-CN"] = "Monitor.Help.Topics",
        ["zh-TW"] = "Monitor.Help.Topics.zh-TW",
        ["en"] = "Monitor.Help.Topics.en"
    };
    private static readonly Dictionary<string, Topic[]> LoadedTopics = new(StringComparer.Ordinal);
    internal static IReadOnlyCollection<string> TopicLocales => TopicResources.Keys;
    internal static Topic[] Topics(string locale)
    {
        if (!TopicResources.TryGetValue(locale, out string? resource)) { throw new ArgumentOutOfRangeException(nameof(locale)); }
        if (LoadedTopics.TryGetValue(locale, out var loaded)) { return loaded; }
        using var stream = typeof(DesktopInformationPages).Assembly.GetManifestResourceStream(resource)!;
        var topics = JsonSerializer.Deserialize<Topic[]>(stream)!.Where(t => ProductIdentity.DevelopmentFeatures || t.Id != "settings-detail-11").ToArray();
        LoadedTopics[locale] = topics;
        return topics;
    }
    internal static Control Help(string id)
    {
        if (!Topics("zh-CN").Any(t => t.Id == id)) { throw new InvalidOperationException("Unknown help topic: " + id); }
        // Keep existing section anchors without reserving explanatory space.
        return new Border { IsVisible = false };
    }
    internal static Control CreateHelp(DesktopLocalization localization) => new HelpPage(localization);

    private sealed class HelpPage : Grid
    {
        private readonly DesktopLocalization _localization;
        private readonly Grid _filters = new() { ColumnDefinitions = new("220,*"), RowDefinitions = new("Auto,Auto") };
        private readonly ComboBox _category = new() { MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _search = new() { MinHeight = 44 };
        private readonly Button _clearSearch = new() { Content = "×", MinWidth = 44, MinHeight = 44, Margin = new Thickness(8, 0, 0, 0) };
        private readonly TextBlock _count = new() { Margin = new Thickness(0, 12), TextWrapping = TextWrapping.Wrap, Foreground = DesktopFluentStyle.SecondaryText };
        private readonly StackPanel _topicsPanel = new() { Spacing = 20 };
        private readonly ScrollViewer _scroll;
        private readonly StackPanel _categoryField;
        private readonly StackPanel _searchField;
        private readonly Button _clearFilters = new() { MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        private Topic[] _topics = [];
        private string[] _categories = [];
        private string? _loadedLocale;
        private bool _loading;
        private TopLevel? _keyboardRoot;

        internal HelpPage(DesktopLocalization localization)
        {
            _localization = localization;
            RowDefinitions = new("Auto,Auto,Auto,*");
            Margin = new Thickness(24);
            MaxWidth = 1080;
            var title = Heading(null, 26, 1);
            title.Margin = new Thickness(0, 0, 0, 20);
            localization.Bind(title, TextBlock.TextProperty, "navigation.help");
            Children.Add(title);

            _category.ItemTemplate = new FuncDataTemplate<string>((value, _) => new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });
            localization.Bind(_category, AutomationProperties.NameProperty, "help.categoryName");
            localization.Bind(_search, AutomationProperties.NameProperty, "help.searchName");
            localization.Bind(_search, TextBox.PlaceholderTextProperty, "help.searchPlaceholder");
            localization.Bind(_clearSearch, AutomationProperties.NameProperty, "help.clearSearch");
            localization.Bind(_clearSearch, ToolTip.TipProperty, "help.clearSearch");
            _categoryField = Field("help.categoryName", _category);
            var searchRow = new Grid { ColumnDefinitions = new("*,Auto") };
            searchRow.Children.Add(_search);
            Grid.SetColumn(_clearSearch, 1);
            searchRow.Children.Add(_clearSearch);
            _searchField = Field("help.searchName", searchRow);
            _filters.Children.Add(_categoryField);
            _filters.Children.Add(_searchField);
            Grid.SetRow(_filters, 1);
            Children.Add(_filters);

            localization.Bind(_count, AutomationProperties.NameProperty, "help.resultsName");
            AutomationProperties.SetLiveSetting(_count, AutomationLiveSetting.Polite);
            Grid.SetRow(_count, 2);
            Children.Add(_count);
            _scroll = new ScrollViewer
            {
                Content = _topicsPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(_scroll, 3);
            Children.Add(_scroll);
            localization.Bind(_clearFilters, ContentControl.ContentProperty, "help.clearFilters");
            _clearFilters.Click += (_, _) =>
            {
                _loading = true;
                _category.SelectedIndex = 0;
                _search.Text = "";
                _loading = false;
                Refresh();
                _search.Focus();
            };
            _clearSearch.Click += (_, _) => ClearSearch();
            _search.TextChanged += (_, _) => Refresh();
            _category.SelectionChanged += (_, _) => Refresh();
            AttachedToVisualTree += (_, _) =>
            {
                localization.LocaleChanged += Load;
                _keyboardRoot = TopLevel.GetTopLevel(this);
                _keyboardRoot?.AddHandler(KeyDownEvent, SearchKeyDown, RoutingStrategies.Tunnel);
                // A retained page can miss language changes while another page is visible.
                if (_loadedLocale != localization.Locale) { Load(); }
            };
            DetachedFromVisualTree += (_, _) =>
            {
                localization.LocaleChanged -= Load;
                _keyboardRoot?.RemoveHandler(KeyDownEvent, SearchKeyDown);
                _keyboardRoot = null;
            };
            Load();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            bool compact = availableSize.Width < 620;
            _filters.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(220);
            _filters.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(_searchField, compact ? 0 : 1);
            Grid.SetRow(_searchField, compact ? 1 : 0);
            _categoryField.Margin = compact ? new Thickness(0, 0, 0, 12) : new Thickness(0, 0, 12, 0);
            return base.MeasureOverride(availableSize);
        }

        private StackPanel Field(string labelKey, Control content)
        {
            var label = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
            _localization.Bind(label, TextBlock.TextProperty, labelKey);
            var field = new StackPanel { Spacing = 6 };
            field.Children.Add(label);
            field.Children.Add(content);
            return field;
        }

        private void SearchKeyDown(object? sender, KeyEventArgs args)
        {
            if (args.Key == Key.F && args.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
            {
                _search.Focus();
                _search.SelectAll();
                args.Handled = true;
            }
            else if (args.Key == Key.Escape && _search.IsKeyboardFocusWithin && !string.IsNullOrEmpty(_search.Text))
            {
                ClearSearch();
                args.Handled = true;
            }
        }

        private void ClearSearch()
        {
            _search.Text = "";
            _search.Focus();
        }

        private void Load()
        {
            // Categories keep their order in every language, so the selected index carries over.
            _loading = true;
            int selected = Math.Max(_category.SelectedIndex, 0);
            _loadedLocale = _localization.Locale;
            _topics = Topics(TopicResources.ContainsKey(_loadedLocale) ? _loadedLocale : "zh-CN");
            _categories = _topics.Select(topic => topic.Category).Distinct().ToArray();
            _category.ItemsSource = _categories.Prepend(_localization.Get("help.allCategories")).ToArray();
            _category.SelectedIndex = Math.Min(selected, _categories.Length);
            _loading = false;
            Refresh();
        }

        private void Refresh()
        {
            if (_loading) { return; }
            string[] words = (_search.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var matches = _topics.Where(topic => (_category.SelectedIndex <= 0 || topic.Category == _categories[_category.SelectedIndex - 1]) &&
                words.All(word => (topic.Category + " " + topic.Title + " " + topic.Text).Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray();
            _topicsPanel.Children.Clear();
            _scroll.Offset = default;
            _clearSearch.IsEnabled = !string.IsNullOrEmpty(_search.Text);
            _count.Text = matches.Length == 0 ? _localization.Get("help.noMatches") : _localization.Format("help.matches", matches.Length);
            if (matches.Length == 0)
            {
                _topicsPanel.Children.Add(_clearFilters);
                return;
            }
            foreach (var group in matches.GroupBy(topic => topic.Category))
            {
                var section = new StackPanel { Spacing = 12 };
                section.Children.Add(Heading(group.Key, 20, 2));
                foreach (var entry in group)
                {
                    var article = new StackPanel { Spacing = 8 };
                    article.Children.Add(Heading(entry.Title, 16, 3));
                    article.Children.Add(new SelectableTextBlock { Text = entry.Text, TextWrapping = TextWrapping.Wrap });
                    section.Children.Add(new Border
                    {
                        Background = DesktopFluentStyle.Surface,
                        BorderBrush = DesktopFluentStyle.Stroke,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(16),
                        Child = article
                    });
                }
                _topicsPanel.Children.Add(section);
            }
        }
    }

    private static TextBlock Heading(string? text, double size, int level)
    {
        var heading = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetHeadingLevel(heading, level);
        return heading;
    }

    internal static Control CreateAbout(DesktopLocalization localization) =>
        new SettingsSections(localization, "navigation.about", (ProductIdentity.Name, CreateLegalPage(localization, false)), ("about.thirdParty", CreateLegalPage(localization, true)));
    private static Grid CreateLegalPage(DesktopLocalization localization, bool thirdParty)
    {
        var assembly = typeof(DesktopInformationPages).Assembly;
        var root = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,*") };
        var title = Heading(null, 24, 2);
        if (thirdParty) { localization.Bind(title, TextBlock.TextProperty, "about.thirdParty"); }
        else { localization.Bind(title, TextBlock.TextProperty, ProductIdentity.DevelopmentFeatures ? "about.titleDevelopment" : "about.title", ProductIdentity.Name, ProductIdentity.Version); }
        root.Children.Add(title);
        string? buildVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString(3);
        int revisionStart = (buildVersion?.IndexOf('+') ?? -1) + 1;
        const int shortRevisionLength = 7;
        if (buildVersion is not null && revisionStart > 0 && buildVersion.Length > revisionStart + shortRevisionLength) { buildVersion = buildVersion[..(revisionStart + shortRevisionLength)]; }
        var version = new SelectableTextBlock { Margin = new Thickness(0, 12), TextWrapping = TextWrapping.Wrap, Foreground = DesktopFluentStyle.SecondaryText };
        localization.Bind(version, TextBlock.TextProperty, text => text.Format("about.buildVersion", buildVersion ?? text.GetString("about.unknownVersion")));
        Grid.SetRow(version, 1); root.Children.Add(version);
        var notice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        localization.Bind(notice, TextBlock.TextProperty, "about.licenseNotice");
        var information = new StackPanel { Spacing = 12 };
        if (!thirdParty)
        {
            string copyrightSummary = string.Join(Environment.NewLine, ProductIdentity.CopyrightNotice.Split('\n').Take(2));
            information.Children.Add(new SelectableTextBlock { Text = copyrightSummary, TextWrapping = TextWrapping.Wrap });
            var copyright = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new SelectableTextBlock { Text = ProductIdentity.CopyrightNotice, TextWrapping = TextWrapping.Wrap }
            };
            localization.Bind(copyright, HeaderedContentControl.HeaderProperty, "about.copyrightNotice");
            information.Children.Add(copyright);
            var repository = new HyperlinkButton { Content = "GitHub · OrbisElegy/SiMonitor", NavigateUri = new Uri(ProductIdentity.RepositoryUrl), HorizontalAlignment = HorizontalAlignment.Left };
            localization.Bind(repository, AutomationProperties.NameProperty, "about.repositoryName");
            information.Children.Add(repository);
        }
        else { information.Children.Add(notice); }
        Grid.SetRow(information, 2); root.Children.Add(information);
        string[] names = assembly.GetManifestResourceNames().Where(n => n.StartsWith("Monitor.Legal.", StringComparison.Ordinal) &&
            (thirdParty ? n is not ("Monitor.Legal.LICENSE" or "Monitor.Legal.license-scope.md") : n is "Monitor.Legal.LICENSE" or "Monitor.Legal.license-scope.md")).Order().ToArray();
        var selector = new ComboBox { ItemsSource = names.Select(n => n["Monitor.Legal.".Length..]).ToArray(), MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 12) };
        localization.Bind(selector, AutomationProperties.NameProperty, "about.documentsName");
        var documentLabel = new TextBlock { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        localization.Bind(documentLabel, TextBlock.TextProperty, "about.documentsName");
        var documentField = new StackPanel { Spacing = 6, Margin = new Thickness(0, 12, 0, 0) };
        documentField.Children.Add(documentLabel);
        documentField.Children.Add(selector);
        Grid.SetRow(documentField, 3);
        root.Children.Add(documentField);
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(16), MinHeight = 240, MaxHeight = 560 };
        ScrollViewer.SetHorizontalScrollBarVisibility(text, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        localization.Bind(text, AutomationProperties.NameProperty, "about.documentText"); Grid.SetRow(text, 4); root.Children.Add(text);
        selector.SelectionChanged += (_, _) =>
        {
            if (selector.SelectedIndex < 0) { return; }
            using var stream = assembly.GetManifestResourceStream(names[selector.SelectedIndex])!;
            using var reader = new StreamReader(stream);
            text.Text = reader.ReadToEnd();
            text.CaretIndex = 0;
        };
        selector.SelectedIndex = thirdParty ? 0 : Array.FindIndex(names, n => n == "Monitor.Legal.LICENSE");
        return root;
    }
}
