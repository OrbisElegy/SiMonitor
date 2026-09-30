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
    internal sealed record Topic(string Id, string Category, string Text);
    private static readonly Lazy<Topic[]> Topics = new(() =>
    {
        using var stream = typeof(DesktopInformationPages).Assembly.GetManifestResourceStream("Monitor.Help.Topics")!;
        return JsonSerializer.Deserialize<Topic[]>(stream)!;
    });
    internal static Control Help(string id)
    {
        if (!Topics.Value.Any(t => t.Id == id)) { throw new InvalidOperationException("Unknown help topic: " + id); }
        // Keep existing section anchors without reserving explanatory space.
        return new Border { IsVisible = false };
    }
    internal static Control CreateHelp()
    {
        var root = new Grid { RowDefinitions = new("Auto,Auto,*"), Margin = new Thickness(24) };
        var filters = new Grid { ColumnDefinitions = new("200,*") };
        var categories = Topics.Value.Select(t => t.Category).Distinct().Prepend("全部分类").ToArray();
        var category = new ComboBox { ItemsSource = categories, SelectedIndex = 0, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 12, 0) };
        var search = new TextBox { PlaceholderText = "搜索说明，例如：心率、量程、声音", MinHeight = 44 };
        AutomationProperties.SetName(category, "帮助分类"); AutomationProperties.SetName(search, "搜索帮助");
        filters.Children.Add(category); Grid.SetColumn(search, 1); filters.Children.Add(search); root.Children.Add(filters);
        var count = new TextBlock { Margin = new Thickness(0, 12) }; AutomationProperties.SetName(count, "帮助搜索结果"); Grid.SetRow(count, 1); root.Children.Add(count);
        var panel = new StackPanel { Spacing = 16 };
        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        void Refresh()
        {
            string[] words = (search.Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var matches = Topics.Value.Where(t => (category.SelectedIndex <= 0 || t.Category == categories[category.SelectedIndex]) &&
                words.All(word => (t.Category + " " + t.Text).Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray();
            panel.Children.Clear(); scroll.Offset = default;
            count.Text = matches.Length == 0 ? "没有匹配说明，请更换关键词或分类。" : $"找到 {matches.Length} 条说明";
            foreach (var group in matches.GroupBy(t => t.Category))
            {
                panel.Children.Add(new TextBlock { Text = group.Key, FontSize = 20, FontWeight = FontWeight.SemiBold });
                foreach (var entry in group) { panel.Children.Add(new SelectableTextBlock { Text = entry.Text, TextWrapping = TextWrapping.Wrap }); }
            }
        }
        search.TextChanged += (_, _) => Refresh(); category.SelectionChanged += (_, _) => Refresh(); Refresh();
        return root;
    }
    internal static Control CreateAbout() => new SettingsSections("关于", ("Seele's SiMonitor", CreateLegalPage(false)), ("开源组件", CreateLegalPage(true)));
    private static Grid CreateLegalPage(bool thirdParty)
    {
        var assembly = typeof(DesktopInformationPages).Assembly;
        var root = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,*"), Margin = new Thickness(24) };
        var title = new TextBlock { Text = thirdParty ? "开源组件" : ProductIdentity.Name + " · V0.5 开发版", FontSize = 24, FontWeight = FontWeight.SemiBold };
        root.Children.Add(title);
        var version = new SelectableTextBlock { Text = "构建版本：" + (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString()), Margin = new Thickness(0, 12) };
        Grid.SetRow(version, 1); root.Children.Add(version);
        var notice = new TextBlock { Text = "原创代码：AGPL-3.0-only。第三方组件保留各自许可。以下为当前依赖清单及已随附的许可正文；完整发布包许可审核尚未完成。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(notice, 2); root.Children.Add(notice);
        var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith("Monitor.Legal.", StringComparison.Ordinal) &&
            (thirdParty ? n is not ("Monitor.Legal.LICENSE" or "Monitor.Legal.license-scope.md") : n is "Monitor.Legal.LICENSE" or "Monitor.Legal.license-scope.md")).Order().ToArray();
        var selector = new ComboBox { ItemsSource = names.Select(n => n["Monitor.Legal.".Length..]).ToArray(), MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(selector, "开源许可与依赖文档"); Grid.SetRow(selector, 3); root.Children.Add(selector);
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(text, "许可文档正文"); Grid.SetRow(text, 4); root.Children.Add(text);
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
