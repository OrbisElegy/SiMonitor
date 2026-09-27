// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Monitor.Desktop;

internal static class DesktopInformationPages
{
    private static readonly Dictionary<string, HashSet<string>> Instructions = [];
    internal static Control Help(string category, string text)
    {
        if (!Instructions.TryGetValue(category, out var entries)) { Instructions[category] = entries = []; }
        entries.Add(text);
        // Keep section anchors intact without reserving explanatory space.
        return new Border { IsVisible = false };
    }
    internal static Control CreateHelp()
    {
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(24) };
        foreach (var (category, entries) in Instructions)
        {
            panel.Children.Add(new TextBlock { Text = category, FontSize = 20, FontWeight = FontWeight.SemiBold });
            foreach (string entry in entries) { panel.Children.Add(new SelectableTextBlock { Text = entry, TextWrapping = TextWrapping.Wrap }); }
        }
        return new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    internal static Control CreateAbout()
    {
        var assembly = typeof(DesktopInformationPages).Assembly;
        var root = new Grid { RowDefinitions = new("Auto,Auto,Auto,Auto,*"), Margin = new Thickness(24) };
        var title = new TextBlock { Text = "心电监护 · V0.5 开发版", FontSize = 24, FontWeight = FontWeight.SemiBold };
        root.Children.Add(title);
        var version = new SelectableTextBlock { Text = "构建版本：" + (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString()), Margin = new Thickness(0, 12) };
        Grid.SetRow(version, 1); root.Children.Add(version);
        var notice = new TextBlock { Text = "原创代码：AGPL-3.0-only。第三方组件保留各自许可。以下为当前依赖清单及已随附的许可正文；完整发布包许可审核尚未完成。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(notice, 2); root.Children.Add(notice);
        var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith("Monitor.Legal.", StringComparison.Ordinal)).Order().ToArray();
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
        selector.SelectedIndex = Array.FindIndex(names, n => n == "Monitor.Legal.LICENSE");
        return root;
    }
}
