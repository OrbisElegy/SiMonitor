// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class DesignPreviewSmokeChecks
{
    internal static void Verify()
    {
        VerifyStableSlowContours();
        VerifyRespirationOverview();
        var window = new DesignPreviewWindow(); window.Show();
        try
        {
            var timer = window.ActiveTimer;
            for (int i = 0; i < 150; i++) { window.Pulse(timer, 50_000_000); }
            Require(window.Page == 0 && window.Session.FrontierNs > 0 && window.Settings.Parent is null, "live monitor without settings controls");
            VerifyGapAndCalibration(window.MonitorTrace);
            Capture(window, "ui-preview-monitor.png");
            double wideMonitor = window.MonitorTrace.Bounds.Width;
            window.Width = 1000; window.Height = 720;
            Capture(window, "ui-preview-monitor-compact.png");
            Require(window.MonitorTrace.Bounds.Width < wideMonitor && window.Session.Display.Slots.Count == 5, "monitor resizes but skin row count stays fixed");
            window.Width = 1440; window.Height = 940;
            for (int i = 0; i < 450; i++) { window.Pulse(timer, 50_000_000); }
            Capture(window, "ui-preview-monitor-auto.png");
            window.SelectPage(1); Capture(window, "ui-preview-paper.png");
            VerifyPaperEnd(window.CurrentPaper!);
            var root = (Control)window.Content!;
            double wideScale = window.CurrentPaper!.TransformToVisual(root)!.Value.M11;
            Require(window.CurrentPaper!.BlockCount == 55 && window.Settings.Parent is null, "complete paper snapshot with no settings controls");
            window.Width = 1000; window.Height = 720;
            Capture(window, "ui-preview-compact.png");
            double narrowScale = window.CurrentPaper!.TransformToVisual(root)!.Value.M11;
            Require(narrowScale < wideScale, "paper including waves and calibration scales with the viewport");
            window.Width = 1440; window.Height = 940; window.SelectPage(2);
            Capture(window, "ui-preview-settings.png");
            var homeCard = window.Settings.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b)?.StartsWith("心电图样式，", StringComparison.Ordinal) == true);
            var homeOrigin = homeCard.TranslatePoint(default, root)!.Value;
            var homeSize = homeCard.Bounds.Size;
            window.Settings.OpenEcgChooser(); Capture(window, "ui-preview-chooser.png");
            var chosenCard = window.Settings.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "SelectedStyleCard");
            Require(chosenCard.Bounds.Size == homeSize && chosenCard.TranslatePoint(default, root)!.Value == homeOrigin,
                "selected card preserves home size and top-left position independently of back button");
            Require(window.Settings.PreviewCacheCount >= 3, "grouped choices own distinct cached source configurations");
            var candidate = window.Settings.GetVisualDescendants().OfType<Button>().Single(button =>
                button.Content is StackPanel panel && panel.Children.OfType<TextBlock>().Any(text => text.Text == "窦性停搏（无逸搏）"));
            var candidateSize = candidate.Bounds.Size;
            var unchanged = window.Session;
            candidate.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Capture(window, "ui-preview-chooser-selected.png");
            Require(window.Settings.EcgSelection == 1 && ReferenceEquals(unchanged, window.Session), "candidate updates draft preview without replacing live source");
            var selectedCandidate = window.Settings.GetVisualDescendants().OfType<Button>().Single(button =>
                AutomationProperties.GetName(button) == "窦性停搏（无逸搏），已选择");
            Require(selectedCandidate.Bounds.Size == candidateSize && candidateSize == homeSize, "candidate sizes stay equal before and after selection");
            Require(selectedCandidate.IsFocused, "keyboard focus follows rebuilt selected candidate");
            Require(window.Settings.GetVisualDescendants().OfType<Button>().Any(button =>
                AutomationProperties.GetName(button) == "窦性心律，当前分组" && button.Content?.ToString()?.Contains('✓') == true),
                "group selection is labelled and not color-only");
            var expansion = window.Settings.GetVisualDescendants().OfType<Expander>().Single();
            expansion.IsExpanded = true;
            Capture(window, "ui-preview-chooser-leads.png");
            Require(window.Settings.GetVisualDescendants().OfType<EcgStyleLeadPreview>().Count() == 12, "selected ECG expands to twelve cached lead traces");
            window.Settings.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "返回波形设置")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Capture(window, "ui-preview-settings-return.png");
            window.Settings.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b)?.StartsWith("呼吸样式，", StringComparison.Ordinal) == true)
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Capture(window, "ui-preview-respiration.png");
            window.Settings.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "异常呼吸示意，选择分组")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Capture(window, "ui-preview-respiration-patterns.png");
            window.Settings.EcgSelection = 0;
            window.Settings.Tabs.SelectedIndex = 1; Capture(window, "ui-preview-display.png");
            Require(AutomationProperties.GetName(window.Settings.Slots[0].Speed) == "第1行扫描速度，相对毫米每秒", "speed control has a contextual accessibility name");
            Require(window.Settings.Slots[0].Channel.Bounds.Height > 0, "display tab content has completed layout");
            window.Width = 1000; window.Height = 720; Capture(window, "ui-preview-display-compact.png");
            window.Width = 1440; window.Height = 940;
            window.Settings.Tabs.SelectedIndex = 2; Capture(window, "ui-preview-audio.png");
            window.Settings.Tabs.SelectedIndex = 3; Capture(window, "ui-preview-alarms.png");
            Require(window.Settings.Parent is not null && window.Settings.Tabs.ItemCount == 4, "generation/display/audio/alarms live in settings tabs");
            var source = window.Session;
            window.Settings.Slots[0].Minimum.Text = "NaN";
            window.ApplySettings();
            Require(ReferenceEquals(window.Session, source) && ReferenceEquals(window.ActiveTimer, timer), "invalid settings preserve live session and timer");
            window.Settings.Skin.SelectedIndex = 0;
            window.Settings.Slots[0].Auto.IsChecked = false;
            window.Settings.Slots[0].Minimum.Text = "-0.01";
            window.Settings.Slots[0].Maximum.Text = "0.01";
            window.Settings.Slots[1].Speed.SelectedIndex = 2;
            window.Settings.Slots[2].Speed.SelectedIndex = 0;
            window.ApplySettings();
            Require(!ReferenceEquals(source, window.Session) && window.Session.Display.Slots.Count == 3, "apply changes fixed skin and restarts");
            window.Pulse(timer, 50_000_000);
            Require(window.Session.SimulationTimeNs == 0, "callbacks from old run are fenced");
            timer = window.ActiveTimer;
            for (int i = 0; i < 150; i++) { window.Pulse(timer, 50_000_000); }
            VerifyClamping(window.MonitorTrace);
            for (int i = 0; i < 300; i++) { window.Pulse(timer, 50_000_000); }
            Require(window.Session.Ranges.RowCycle(0) == 2 && window.Session.Ranges.RowCycle(1) == 4 && window.Session.Ranges.RowCycle(2) == 1, "native sweep wraps twice with boundary range updates");
            window.SelectPage(0); Capture(window, "ui-preview-monitor-wrap.png");
            window.Pause(); long paused = window.Session.SimulationTimeNs;
            window.Pulse(timer, 50_000_000); Require(window.Session.SimulationTimeNs == paused, "pause holds simulation");
            window.Start(); window.SelectPage(0); Require(window.Session.SimulationTimeNs == paused, "navigation/resume never regenerates history");
            foreach (var choice in new[] { (1, 1, 0), (2, 2, 1), (0, 0, 2), (0, 3, 3), (3, 0, 0), (4, 0, 0), (5, 0, 0) })
            {
                source = window.Session;
                window.Settings.EcgSelection = choice.Item1;
                window.Settings.RespirationSelection = choice.Item2;
                window.Settings.EjectionSelection = choice.Item3;
                window.ApplySettings();
                Require(!ReferenceEquals(source, window.Session), "supported rhythm/breathing/ejection combination applies");
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: responsive paper/live monitor, fixed skin slots, clipping, settings separation and timer lifecycle");
    }
    private static void VerifyRespirationOverview()
    {
        var regular = DesignPreviewWindow.CreateRespirationPreview(0);
        var tidal = DesignPreviewWindow.CreateRespirationPreview(1);
        var intermittent = DesignPreviewWindow.CreateRespirationPreview(2);
        var absent = DesignPreviewWindow.CreateRespirationPreview(3);
        Require(tidal[^1].TimeNs >= DesignPreviewSettings.RespirationPreviewDurationNs - 40_000_000,
            "respiration thumbnail includes the entire eleven-breath pattern");
        double Peak((long TimeNs, double Value)[] samples, int breath) => samples.Where(s => s.TimeNs >= breath * 3_750_000_000L && s.TimeNs < (breath + 1) * 3_750_000_000L).Max(s => Math.Abs(s.Value));
        Require(Peak(tidal, 4) > Peak(tidal, 0) * 4 && Peak(tidal, 8) < Peak(tidal, 4) / 4 && Peak(tidal, 9) == 0 && Peak(tidal, 10) == 0,
            "cached tidal overview shows crescendo, decrescendo and the entire pause");
        Require(Peak(regular, 9) > 0 && Peak(intermittent, 3) == 0 && Peak(intermittent, 5) > 0 && absent.All(s => s.Value == 0),
            "all four respiratory choices remain distinguishable from actual source data");
    }
    private static void VerifyStableSlowContours()
    {
        var slots = MonitorDisplayConfiguration.Default(MonitorSkin.SevenRows).Slots.Select(s => s with { Automatic = false, SpeedTenthsMmPerSecond = 125 }).ToArray();
        var session = new LocalMonitorPreviewSession(PhysiologyDemoConfiguration.Default, new(MonitorSkin.SevenRows, slots));
        var trace = new LiveMonitorTrace(session);
        while (session.SimulationTimeNs < 6_000_000_000) { session.Advance(50_000_000); }
        for (int step = 0; step < 15; step++)
        {
            var before = Raster(trace, 1000, 700);
            int right = (int)(132 + 850 * session.FrontierNs / 20_000_000_000d) - 3;
            session.Advance(200_000_000);
            var after = Raster(trace, 1000, 700);
            foreach (int row in Enumerable.Range(0, slots.Length).Where(i => slots[i].Channel is 3 or 4 or 5))
                for (int y = row * 100 + 10; y < row * 100 + 90; y++)
                    for (int x = 134; x < right; x++)
                    {
                        int offset = (y * 1000 + x) * 4;
                        Require(before.AsSpan(offset, 4).SequenceEqual(after.AsSpan(offset, 4)),
                            $"new slow-sweep CO2/ABP/PA samples never repaint visible slopes: step {step}, row {row}, pixel {x}/{y}, frontier {session.FrontierNs}, source end {session.Blocks[^1].StartSimTimeNs + 200_000_000}");
                    }
        }
    }
    private static void VerifyGapAndCalibration(LiveMonitorTrace trace)
    {
        byte[] data = Raster(new LiveMonitorTrace(new LocalMonitorPreviewSession(PhysiologyDemoConfiguration.Default, MonitorDisplayConfiguration.Default())), 800, 600);
        bool Green(int x, int y) => data[(y * 800 + x) * 4 + 1] > 130 && data[(y * 800 + x) * 4 + 1] > data[(y * 800 + x) * 4 + 2] * 1.3;
        double gutter = 0;
        int calibrationX = (int)Math.Round(132 + 650 * .2 / 10);
        int calibrationPixels = Enumerable.Range(9, 100).Count(y => Green(calibrationX, y) || Green(calibrationX - 1, y));
        Require(Math.Abs(calibrationPixels - 100 * 1000 / 2700d) < 4, "ECG calibration has a true 1mV height at current range");
        data = Raster(trace, 800, 600);
        double phase = trace.Session.FrontierNs % MonitorDisplayConfiguration.SweepDurationNs / (double)MonitorDisplayConfiguration.SweepDurationNs;
        int headX = (int)(132 + gutter + phase * (650 - gutter));
        Require(Enumerable.Range(10, 95).Count(y => Green(headX, y) || Green(headX + 1, y)) < 12, "no vertical sweep cursor");
        Require(!Enumerable.Range(10, 95).Any(y => Green(headX + 4, y)), "erase gap contains no trace");
    }
    private static void VerifyPaperEnd(DesignPreviewTrace paper)
    {
        byte[] data = Raster(paper, 1184, 596);
        bool Dark(int x, int y) => data[(y * 1184 + x) * 4] < 160 && data[(y * 1184 + x) * 4 + 1] < 160;
        for (int column = 0; column < 4; column++)
            for (int row = 0; row < 3; row++)
            {
                int x = 58 + column * 280, baseline = 136 + row * 120;
                Require(Enumerable.Range(baseline - 39, 38).Count(y => Dark(x, y) || Dark(x - 1, y)) > 30, "each ECG lead has an independent 1mV marker");
                Require(Enumerable.Range(x - 19, 18).Count(px => Dark(px, baseline - 40) || Dark(px, baseline - 41)) >= 17,
                    "paper calibration has a 200ms plateau, not a monitor line");
                Require(Enumerable.Range(baseline - 39, 38).Count(y => Dark(x - 20, y) || Dark(x - 21, y)) > 30,
                    "paper calibration has an independent rising edge");
            }
        Require(Enumerable.Range(39, 18).Count(x => Dark(x, 496) || Dark(x, 495)) >= 17, "long II has its own square calibration");
        Require(Enumerable.Range(500, 70).Any(y => Dark(1150, y)), "long lead reaches within one sample of paper grid right edge");
    }
    private static byte[] Raster(Control control, int width, int height)
    {
        control.Measure(new Size(width, height)); control.Arrange(new Rect(0, 0, width, height));
        using var image = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)); image.Render(control);
        using var pixels = new WriteableBitmap(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = pixels.Lock(); image.CopyPixels(buffer);
        byte[] data = new byte[width * height * 4];
        for (int y = 0; y < height; y++) { Marshal.Copy(buffer.Address + y * buffer.RowBytes, data, y * width * 4, width * 4); }
        return data;
    }
    private static void VerifyClamping(LiveMonitorTrace trace)
    {
        byte[] data = Raster(trace, 800, 360);
        bool Green(int x, int y) => data[(y * 800 + x) * 4 + 1] > 130 && data[(y * 800 + x) * 4 + 1] > data[(y * 800 + x) * 4 + 2] * 1.3;
        Require(Enumerable.Range(145, 290).Count(x => Green(x, 9) || Green(x, 10)) > 20, "overrange ECG flattens to upper edge");
        Require(!Enumerable.Range(140, 600).Any(x => Green(x, 116) || Green(x, 119) || Green(x, 123)), "trace never invades row separator or next row");
    }
    private static void Capture(DesignPreviewWindow window, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Directory.CreateDirectory("artifacts");
        using var image = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
        var root = (Control)window.Content!; root.InvalidateMeasure();
        root.Measure(new Size(window.Width, window.Height)); root.Arrange(new Rect(0, 0, window.Width, window.Height));
        image.Render(root); image.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }
    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Design preview: " + message); } }
}
