// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    internal static Control CreateHelp(DesktopLocalization localization)
    {
        var root = new Grid { RowDefinitions = new("Auto,Auto,*"), Margin = new Thickness(24) };
        var filters = new Grid { ColumnDefinitions = new("200,*") };
        var category = new ComboBox { MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 12, 0) };
        var search = new TextBox { MinHeight = 44 };
        localization.Bind(search, TextBox.PlaceholderTextProperty, "help.searchPlaceholder");
        localization.Bind(category, AutomationProperties.NameProperty, "help.categoryName");
        localization.Bind(search, AutomationProperties.NameProperty, "help.searchName");
        filters.Children.Add(category); Grid.SetColumn(search, 1); filters.Children.Add(search); root.Children.Add(filters);
        var count = new TextBlock { Margin = new Thickness(0, 12) };
        localization.Bind(count, AutomationProperties.NameProperty, "help.resultsName");
        Grid.SetRow(count, 1); root.Children.Add(count);
        var panel = new StackPanel { Spacing = 16 };
        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        Topic[] topics = [];
        string[] categories = [];
        bool loading = false;
        void Load()
        {
            // Categories keep their order in every language, so the selected index carries over.
            loading = true;
            int selected = Math.Max(category.SelectedIndex, 0);
            topics = Topics(TopicResources.ContainsKey(localization.Locale) ? localization.Locale : "zh-CN");
            categories = topics.Select(t => t.Category).Distinct().ToArray();
            category.ItemsSource = categories.Prepend(localization.Get("help.allCategories")).ToArray();
            category.SelectedIndex = Math.Min(selected, categories.Length);
            loading = false;
            Refresh();
        }
        void Refresh()
        {
            if (loading) { return; }
            string[] words = (search.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var matches = topics.Where(t => (category.SelectedIndex <= 0 || t.Category == categories[category.SelectedIndex - 1]) &&
                words.All(word => (t.Category + " " + t.Title + " " + t.Text).Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray();
            panel.Children.Clear(); scroll.Offset = default;
            count.Text = matches.Length == 0 ? localization.Get("help.noMatches") : localization.Format("help.matches", matches.Length);
            foreach (var group in matches.GroupBy(t => t.Category))
            {
                panel.Children.Add(new TextBlock { Text = group.Key, FontSize = 20, FontWeight = FontWeight.SemiBold });
                foreach (var entry in group)
                {
                    panel.Children.Add(new TextBlock { Text = entry.Title, FontSize = 16, FontWeight = FontWeight.SemiBold });
                    panel.Children.Add(new SelectableTextBlock { Text = entry.Text, TextWrapping = TextWrapping.Wrap });
                }
            }
        }
        search.TextChanged += (_, _) => Refresh(); category.SelectionChanged += (_, _) => Refresh();
        root.AttachedToVisualTree += (_, _) => localization.LocaleChanged += Load;
        root.DetachedFromVisualTree += (_, _) => localization.LocaleChanged -= Load;
        Load();
        return root;
    }
    internal static Control CreateAbout(DesktopLocalization localization) =>
        new SettingsSections(localization, "navigation.about", (ProductIdentity.Name, CreateLegalPage(localization, false)), ("about.thirdParty", CreateLegalPage(localization, true)));
    private static Grid CreateLegalPage(DesktopLocalization localization, bool thirdParty)
    {
        var assembly = typeof(DesktopInformationPages).Assembly;
        var root = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,*"), Margin = new Thickness(24) };
        var title = new TextBlock { FontSize = 24, FontWeight = FontWeight.SemiBold };
        if (thirdParty) { localization.Bind(title, TextBlock.TextProperty, "about.thirdParty"); }
        else { localization.Bind(title, TextBlock.TextProperty, ProductIdentity.DevelopmentFeatures ? "about.titleDevelopment" : "about.title", ProductIdentity.Name, ProductIdentity.Version); }
        root.Children.Add(title);
        string? buildVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString(3);
        int revisionStart = (buildVersion?.IndexOf('+') ?? -1) + 1;
        const int shortRevisionLength = 7;
        if (buildVersion is not null && revisionStart > 0 && buildVersion.Length > revisionStart + shortRevisionLength) { buildVersion = buildVersion[..(revisionStart + shortRevisionLength)]; }
        var version = new SelectableTextBlock { Margin = new Thickness(0, 12) };
        localization.Bind(version, TextBlock.TextProperty, text => text.Format("about.buildVersion", buildVersion ?? text.GetString("about.unknownVersion")));
        Grid.SetRow(version, 1); root.Children.Add(version);
        var notice = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        localization.Bind(notice, TextBlock.TextProperty, "about.licenseNotice");
        var information = new StackPanel { Spacing = 12 };
        if (!thirdParty)
        {
            information.Children.Add(new SelectableTextBlock { Text = ProductIdentity.CopyrightNotice, TextWrapping = TextWrapping.Wrap });
            var repository = new HyperlinkButton { Content = "GitHub · OrbisElegy/SiMonitor", NavigateUri = new Uri(ProductIdentity.RepositoryUrl), HorizontalAlignment = HorizontalAlignment.Left };
            localization.Bind(repository, AutomationProperties.NameProperty, "about.repositoryName");
            information.Children.Add(repository);
        }
        else { information.Children.Add(notice); }
        Grid.SetRow(information, 2); root.Children.Add(information);
        string[] names = assembly.GetManifestResourceNames().Where(n => n.StartsWith("Monitor.Legal.", StringComparison.Ordinal) &&
            (thirdParty ? n is not ("Monitor.Legal.LICENSE" or "Monitor.Legal.license-scope.md") : n is "Monitor.Legal.LICENSE" or "Monitor.Legal.license-scope.md")).Order().ToArray();
        var selector = new ComboBox { ItemsSource = names.Select(n => n["Monitor.Legal.".Length..]).ToArray(), MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 12) };
        localization.Bind(selector, AutomationProperties.NameProperty, "about.documentsName"); Grid.SetRow(selector, 3); root.Children.Add(selector);
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        localization.Bind(text, AutomationProperties.NameProperty, "about.documentText"); Grid.SetRow(text, 4); root.Children.Add(text);
        selector.SelectionChanged += (_, _) =>
        {
            if (selector.SelectedIndex < 0) { return; }
            using var stream = assembly.GetManifestResourceStream(names[selector.SelectedIndex])!;
            using var reader = new StreamReader(stream); text.Text = reader.ReadToEnd();
        };
        selector.SelectedIndex = thirdParty ? 0 : Array.FindIndex(names, n => n == "Monitor.Legal.LICENSE");
        return root;
    }
}
