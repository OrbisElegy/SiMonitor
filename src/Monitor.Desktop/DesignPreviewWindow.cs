// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Monitor.Simulation.Acquisition;

namespace Monitor.Desktop;

// Explicit first-round design preview. Existing development entry points remain separate.
internal sealed class DesignPreviewWindow : Window
{
    internal static FontFamily PreviewFont { get; } = new("Segoe UI, Microsoft YaHei UI, WenQuanYi Zen Hei, sans-serif");
    private readonly ContentControl _workspace = new();
    private readonly Button _advanced = new() { Content = "更多参数与现有示例…", MinHeight = 36 };
    private readonly TextBlock _snapshot = Text("", 13);
    private readonly TextBlock _title = Text("十二导联", 27, true);
    private readonly TextBlock _subtitle = Text("固定纸格 · 三行四列与 II 导联节律条", 14);
    private readonly Button[] _navigation = new Button[3];
    private WaveformEnvelope[] _ecg = [];
    private WaveformEnvelope[] _physiology = [];
    internal ComboBox Preset { get; } = new() { ItemsSource = new[] { "窦性参考", "窦性停搏（无逸搏）", "单形室早" }, SelectedIndex = 0, MinWidth = 180 };
    internal Button Generate { get; } = new() { Content = "生成快照", MinHeight = 36 };
    internal int Page { get; private set; } = 1;
    internal DesignPreviewTrace? CurrentTrace => (_workspace.Content as ScrollViewer)?.Content as DesignPreviewTrace;
    internal DesignPreviewWindow()
    {
        Title = "心电监护 · V0.5 设计预览";
        Width = 1440; Height = 940; MinWidth = 960; MinHeight = 640;
        RequestedThemeVariant = ThemeVariant.Light;
        Background = Brush.Parse("#F5F6F8");
        Foreground = Brush.Parse("#202C39");
        FontSize = 14;
        FontFamily = PreviewFont;
        Generate.Background = Brush.Parse("#2464BA");
        Generate.Foreground = Brushes.White;
        Generate.CornerRadius = new CornerRadius(6);
        _advanced.Background = Brushes.White;
        _advanced.BorderBrush = Brush.Parse("#C9D1DB");
        _advanced.BorderThickness = new Thickness(1);
        _advanced.CornerRadius = new CornerRadius(6);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new Grid { ColumnDefinitions = new("184,*"), Background = Background };
        var sidebar = new DockPanel { Margin = new Thickness(16, 24) };
        var brand = new StackPanel { Spacing = 5, Margin = new Thickness(12, 0, 0, 32) };
        brand.Children.Add(Text("心电监护", 21, true));
        brand.Children.Add(Text("V0.5  ·  Standalone", 12));
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var footer = Text("离线教学模拟\n不得用于临床决策", 12); footer.Margin = new Thickness(12, 20);
        DockPanel.SetDock(footer, Dock.Bottom); sidebar.Children.Add(footer);
        var nav = new StackPanel { Spacing = 8 };
        string[] labels = ["监护波形", "十二导联", "声音与报警"];
        for (int i = 0; i < labels.Length; i++)
        {
            int page = i;
            var button = new Button
            {
                Content = labels[i],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 12),
                CornerRadius = new CornerRadius(8)
            };
            button.Click += (_, _) => SelectPage(page);
            AutomationProperties.SetName(button, labels[i]);
            _navigation[i] = button; nav.Children.Add(button);
        }
        sidebar.Children.Add(nav);
        root.Children.Add(new Border { Background = Brush.Parse("#EBEEF2"), BorderBrush = Brush.Parse("#DCE1E7"), BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebar });
        var main = new Grid { RowDefinitions = new("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(28, 24) };
        Grid.SetColumn(main, 1); root.Children.Add(main);
        var heading = new StackPanel { Spacing = 6 }; heading.Children.Add(_title); heading.Children.Add(_subtitle); main.Children.Add(heading);
        var tools = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 16) };
        var presetLabel = Text("生成示例", 14); presetLabel.VerticalAlignment = VerticalAlignment.Center;
        tools.Children.Add(presetLabel); tools.Children.Add(Preset); tools.Children.Add(Generate);
        AutomationProperties.SetName(Preset, "快照示例");
        _advanced.Click += (_, _) => new WaveformDemoWindow(physiology: Page == 0, projected: Page != 0).Show(this);
        tools.Children.Add(_advanced);
        foreach (Control child in tools.Children) { child.Margin = new Thickness(0, 0, 12, 6); }
        Grid.SetRow(tools, 1); main.Children.Add(tools);
        var state = new Border
        {
            Background = Brush.Parse("#E9F0FA"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 0, 0, 16),
            Child = _snapshot
        };
        Grid.SetRow(state, 2); main.Children.Add(state);
        var card = new Border { Background = Brushes.White, BorderBrush = Brush.Parse("#DCE1E7"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = _workspace };
        Grid.SetRow(card, 3); main.Children.Add(card);
        var note = Text("冻结快照  ·  调整窗口不会拉伸波形；内容较宽时可横向滚动。", 12); note.Margin = new Thickness(0, 14, 0, 0);
        Grid.SetRow(note, 4); main.Children.Add(note);
        Generate.Click += (_, _) => Regenerate();
        Content = root;
        Regenerate();
    }
    internal void Regenerate()
    {
        var config = Preset.SelectedIndex switch { 1 => PhysiologyDemoConfiguration.SinusArrestPreset, 2 => PhysiologyDemoConfiguration.PrematureVentricular, _ => PhysiologyDemoConfiguration.Default };
        var ecgConfig = Preset.SelectedIndex switch { 1 => ProjectedEcgDemoConfiguration.SinusArrestPreset, 2 => ProjectedEcgDemoConfiguration.PrematureVentricular, _ => ProjectedEcgDemoConfiguration.Default };
        var source = PhysiologyDemoSource.Create(config);
        var projected = ProjectedEcgDemoSource.Create(ecgConfig);
        List<WaveformEnvelope> physiology = [], ecg = [];
        for (int step = 1; step <= 51; step++)
        {
            foreach (var bytes in projected.AdvanceTo(step * 200_000_000L, 50, 1, 100)) { ecg.Add(WaveformEnvelopeCodec.Decode(bytes)); }
            if (step <= 40)
            { foreach (var bytes in source.AdvanceTo(step * 200_000_000L, 50, 1, 100)) { physiology.Add(WaveformEnvelopeCodec.Decode(bytes)); } }
        }
        _physiology = physiology.ToArray(); _ecg = ecg.Take(50).ToArray();
        _snapshot.Text = $"当前快照：{Preset.SelectedItem}  ·  首轮设计预览，未启动实时监护或报警。";
        SelectPage(Page);
    }
    internal void SelectPage(int page)
    {
        Page = page;
        for (int i = 0; i < _navigation.Length; i++)
        {
            _navigation[i].Background = Brush.Parse(i == page ? "#D3E3FA" : "#EBEEF2");
            _navigation[i].FontWeight = i == page ? FontWeight.SemiBold : FontWeight.Normal;
            AutomationProperties.SetName(_navigation[i], $"{_navigation[i].Content}{(i == page ? "，当前页面" : "")}");
        }
        Generate.IsEnabled = Preset.IsEnabled = _advanced.IsEnabled = page != 2;
        _title.Text = page switch { 0 => "监护波形", 1 => "十二导联", _ => "声音与报警" };
        _subtitle.Text = page switch { 0 => "七通道 · 6 秒合成波形快照", 1 => "固定纸格 · 三行四列与 II 导联节律条", _ => "声音状态与报警操作将在这里集中呈现" };
        if (page == 2)
        {
            var empty = new StackPanel { Spacing = 14, Margin = new Thickness(40), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 520 };
            empty.Children.Add(Text("声音与报警尚未启用", 23, true));
            empty.Children.Add(Text("此预览不会发出心搏提示音或报警，也不表示当前没有报警条件。", 15));
            empty.Children.Add(Text("完成接入后，这里将显示提示音来源、报警优先级、确认与限时声音暂停。", 14));
            _workspace.Content = empty;
        }
        else
        {
            _workspace.Content = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new DesignPreviewTrace(page == 1 ? _ecg : _physiology, page == 1)
            };
        }
    }
    private static TextBlock Text(string value, double size, bool strong = false) => new()
    { Text = value, FontSize = size, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
}
