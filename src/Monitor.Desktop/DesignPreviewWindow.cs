// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Monitor.Application.Presentation;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Local preview integration. No numeric estimator, alarm engine or diagnostic export.
internal sealed class DesignPreviewWindow : Window
{
    internal static FontFamily PreviewFont { get; } = new("Segoe UI, Microsoft YaHei UI, WenQuanYi Zen Hei, sans-serif");
    private readonly ContentControl _workspace = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock _title = Text("监护波形", 27, true);
    private readonly TextBlock _subtitle = Text("", 13);
    private readonly TextBlock _state = Text("", 12);
    private readonly Button[] _navigation = new Button[3];
    private WaveformEnvelope[] _ecg;
    private LiveMonitorTrace _monitor;
    private LocalMonitorPreviewSession _session;
    private LocalMonitorPreviewSession _thumbnailSource;
    private DispatcherTimer? _timer;
    private long _lastTick;
    private bool _closed;
    internal DesignPreviewSettings Settings { get; }
    internal int Page { get; private set; }
    internal LocalMonitorPreviewSession Session => _session;
    internal DispatcherTimer? ActiveTimer => _timer;
    internal DesignPreviewTrace? CurrentPaper => (_workspace.Content as Viewbox)?.Child as DesignPreviewTrace;
    internal LiveMonitorTrace MonitorTrace => _monitor;
    internal DesignPreviewWindow()
    {
        Title = "心电监护 · V0.5 设计预览";
        Width = 1440; Height = 940; MinWidth = 960; MinHeight = 640;
        RequestedThemeVariant = ThemeVariant.Light;
        Background = Brush.Parse("#F5F6F8"); Foreground = Brush.Parse("#202C39");
        FontSize = 14; FontFamily = PreviewFont; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _session = new(PhysiologyDemoConfiguration.Default, MonitorDisplayConfiguration.Default());
        _thumbnailSource = CreateThumbnailSource(PhysiologyDemoConfiguration.Default);
        _monitor = new(_session);
        _ecg = CapturePaper(ProjectedEcgDemoConfiguration.Default);
        Settings = new(() => _thumbnailSource, ApplySettings, () => { if (_timer is null) { Start(); } else { Pause(); } },
            () => new WaveformDemoWindow(projected: true).Show(this));
        Settings.Apply.Background = Brush.Parse("#2464BA"); Settings.Apply.Foreground = Brushes.White;
        var root = new Grid { ColumnDefinitions = new("184,*"), Background = Background };
        var sidebar = new DockPanel { Margin = new Thickness(16, 24) };
        var brand = new StackPanel { Spacing = 5, Margin = new Thickness(12, 0, 0, 32) };
        brand.Children.Add(Text("心电监护", 21, true)); brand.Children.Add(Text("V0.5 · Standalone", 12));
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var footer = new StackPanel { Spacing = 12, Margin = new Thickness(12, 20) };
        footer.Children.Add(_state); footer.Children.Add(Text("离线教学模拟\n不得用于临床决策", 12));
        DockPanel.SetDock(footer, Dock.Bottom); sidebar.Children.Add(footer);
        var nav = new StackPanel { Spacing = 8 };
        string[] labels = ["监护波形", "十二导联", "设置"];
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
            button.Click += (_, _) => SelectPage(page); _navigation[i] = button; nav.Children.Add(button);
        }
        sidebar.Children.Add(nav);
        root.Children.Add(new Border { Background = Brush.Parse("#EBEEF2"), Child = sidebar });
        var main = new Grid { RowDefinitions = new("Auto,*"), Margin = new Thickness(24, 20) };
        Grid.SetColumn(main, 1); root.Children.Add(main);
        var heading = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 14) };
        heading.Children.Add(_title); heading.Children.Add(_subtitle); main.Children.Add(heading);
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#DCE1E7"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = _workspace
        };
        Grid.SetRow(card, 1); main.Children.Add(card); Content = root;
        Opened += (_, _) => Start(); Closed += (_, _) => { _closed = true; Pause(); };
        SelectPage(0); UpdateState();
    }
    internal void SelectPage(int page)
    {
        if (page is < 0 or > 2) { throw new ArgumentOutOfRangeException(nameof(page)); }
        Page = page;
        for (int i = 0; i < _navigation.Length; i++)
        {
            _navigation[i].Background = Brush.Parse(i == page ? "#D3E3FA" : "#EBEEF2");
            _navigation[i].FontWeight = i == page ? FontWeight.SemiBold : FontWeight.Normal;
            AutomationProperties.SetName(_navigation[i], $"{_navigation[i].Content}{(i == page ? "，当前页面" : "")}");
        }
        _title.Text = page switch { 0 => "监护波形", 1 => "十二导联", _ => "设置" };
        _subtitle.Text = page switch
        {
            0 => $"{_session.Display.Slots.Count}个固定槽位 · 10秒扫屏 · 测量与报警未启用",
            1 => "监护采样快照",
            _ => "波形生成、显示、声音与报警"
        };
        _workspace.Content = page switch
        {
            0 => _monitor,
            1 => new Viewbox { Stretch = Stretch.Uniform, Child = new DesignPreviewTrace(_ecg) },
            _ => Settings
        };
    }
    internal void ApplySettings()
    {
        try
        {
            var config = Settings.EcgSelection switch
            { 1 => PhysiologyDemoConfiguration.SinusArrestPreset, 2 => PhysiologyDemoConfiguration.PrematureVentricular, _ => PhysiologyDemoConfiguration.Default };
            var ecgConfig = Settings.EcgSelection switch
            { 1 => ProjectedEcgDemoConfiguration.SinusArrestPreset, 2 => ProjectedEcgDemoConfiguration.PrematureVentricular, _ => ProjectedEcgDemoConfiguration.Default };
            config = config with
            {
                RespiratoryPattern = Settings.RespirationSelection switch { 1 => RespiratoryPattern.CheyneStokesIllustration, 2 => RespiratoryPattern.IntermittentIllustration, _ => RespiratoryPattern.Regular },
                RespiratoryActivity = Settings.RespirationSelection == 3 ? RespiratoryActivity.Absent : RespiratoryActivity.Breathing
            };
            if (Settings.EjectionSelection == 1 && Settings.EcgSelection != 2) { throw new ArgumentException("早搏弱射血需选择单形室早。"); }
            if (Settings.EjectionSelection == 2)
            {
                if (Settings.EcgSelection != 0) { throw new ArgumentException("2:1漏搏需选择窦性参考。"); }
                config = config with { VentricularConductionRatio = 2 };
                ecgConfig = ecgConfig with { VentricularConductionRatio = 2 };
            }
            if (Settings.EjectionSelection == 3) { config = config with { VentricularMechanicalEnabled = false }; }
            var next = new LocalMonitorPreviewSession(config, Settings.ReadDisplay());
            var ecg = CapturePaper(ecgConfig);
            var thumbnails = CreateThumbnailSource(config);
            Pause(); _session = next; _monitor = new(next); _ecg = ecg;
            _thumbnailSource = thumbnails; Settings.RefreshThumbnails();
            SelectPage(Page); Settings.Status.Text = "已应用；监护从头开始，十二导联快照已更新。"; Start();
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        { Settings.Status.Text = "未应用：检查样式组合、通道和量程上下限。原运行与画面保持不变。"; }
    }
    internal void Start()
    {
        if (_closed || _timer is not null) { return; }
        _lastTick = Stopwatch.GetTimestamp();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick; _timer.Start(); UpdateState();
    }
    internal void Pause()
    {
        var old = _timer; _timer = null;
        if (old is not null) { old.Stop(); old.Tick -= OnTick; }
        UpdateState();
    }
    private void OnTick(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _timer) || _timer is null || _closed) { return; }
        long now = Stopwatch.GetTimestamp();
        long elapsed = DemoFrameTiming.ResolveElapsed(Stopwatch.GetElapsedTime(_lastTick, now));
        _lastTick = now; Pulse(sender, elapsed);
    }
    internal void Pulse(object? timer, long deltaNs)
    {
        if (_closed || _timer is null || !ReferenceEquals(timer, _timer)) { return; }
        try
        {
            _session.Advance(deltaNs); _monitor.InvalidateVisual();
            UpdateState();
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        { Pause(); Settings.Status.Text = "生成失败，已暂停。可检查设置后重新开始。"; }
    }
    private void UpdateState()
    {
        _state.Text = $"{(_timer is null ? "已暂停" : "运行中")} · {_session.SimulationTimeNs / 1_000_000_000}s";
        if (Settings is not null) { Settings.Run.Content = _timer is null ? "继续生成" : "暂停生成"; }
    }
    private static LocalMonitorPreviewSession CreateThumbnailSource(PhysiologyDemoConfiguration configuration)
    {
        var preview = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default());
        for (int i = 0; i < 108; i++) { preview.Advance(50_000_000); }
        return preview;
    }
    private static WaveformEnvelope[] CapturePaper(ProjectedEcgDemoConfiguration configuration)
    {
        var source = ProjectedEcgDemoSource.Create(configuration); List<WaveformEnvelope> output = [];
        for (int step = 1; step <= 51; step++)
        { foreach (var bytes in source.AdvanceTo(step * 200_000_000L, 50, 1, 100)) { output.Add(WaveformEnvelopeCodec.Decode(bytes)); } }
        return output.Take(50).ToArray();
    }
    private static TextBlock Text(string value, double size, bool strong = false) => new()
    { Text = value, FontSize = size, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
}
