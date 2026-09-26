// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class DesignPreviewSmokeChecks
{
    private static void VerifySoundSettings()
    {
        var completion = new TaskCompletionSource<Monitor.Infrastructure.Audio.SoundPreviewResult>();
        CancellationToken token = default; int calls = 0;
        var panel = new SoundSettingsPanel((volume, cancellation) =>
        { Require(volume == 50, "volume passed to output"); calls++; token = cancellation; return completion.Task; });
        Require(panel.AlarmEnabled.IsChecked == false && panel.HeartbeatEnabled.IsChecked == true &&
            new MonitorAlertSettings().CriticalInterval.Value == 1.5m, "selected mixing defaults preserve explicit master sound opt-in");
        var pending = panel.PreviewAsync();
        Require(!panel.Audition.IsEnabled && !panel.Volume.IsEnabled && panel.Stop.IsEnabled, "preview locks settings until output joined");
        panel.PreviewAsync().GetAwaiter().GetResult();
        Require(calls == 1, "double click does not start second output");
        panel.StopPreview(); Require(token.IsCancellationRequested, "stop requests background cancellation");
        completion.SetResult(Monitor.Infrastructure.Audio.SoundPreviewResult.Stopped);
        Require(pending.IsCompleted && panel.Audition.IsEnabled && !panel.Stop.IsEnabled, "joined output restores controls");
        Require(panel.Audition.HorizontalContentAlignment == Avalonia.Layout.HorizontalAlignment.Center && panel.Stop.MinHeight == 44,
            "sound controls preserve centered accessible target sizes");
        var failed = new SoundSettingsPanel((_, _) => Task.FromResult(Monitor.Infrastructure.Audio.SoundPreviewResult.StopFailed));
        failed.PreviewAsync().GetAwaiter().GetResult();
        Require(!failed.Audition.IsEnabled, "failed join blocks new UI playback");
        panel.Close(); panel.PreviewAsync().GetAwaiter().GetResult(); Require(calls == 1, "closed view cannot replay");
        var late = new TaskCompletionSource<Monitor.Infrastructure.Audio.SoundPreviewResult>();
        var closing = new SoundSettingsPanel((_, cancellation) => { token = cancellation; return late.Task; });
        var closingTask = closing.PreviewAsync(); closing.Close();
        Require(token.IsCancellationRequested, "window close cancels active output");
        string? closingStatus = closing.Status.Text;
        late.SetResult(Monitor.Infrastructure.Audio.SoundPreviewResult.Completed);
        Require(closingTask.IsCompleted && closing.Status.Text == closingStatus && !closing.Audition.IsEnabled,
            "late completion cannot revive a closed sound page");
        var unavailable = new SoundSettingsPanel((_, _) => Task.FromResult(Monitor.Infrastructure.Audio.SoundPreviewResult.Unavailable));
        unavailable.PreviewAsync().GetAwaiter().GetResult();
        Require(unavailable.Audition.IsEnabled && unavailable.Status.Text!.Contains("不可用", StringComparison.Ordinal),
            "missing output is visible and retryable");
    }
    internal static void Verify()
    {
        VerifyStableSlowContours();
        VerifyRespirationOverview();
        var window = new DesignPreviewWindow(); window.Show();
        try
        {
            Require(window.MonitorView.NumericTexts.All(t => t == "---"), "no configured targets displayed before acquisition");
            var timer = window.ActiveTimer;
            for (int i = 0; i < 150; i++) { window.Pulse(timer, 50_000_000); }
            Require(window.Page == 0 && window.Session.FrontierNs > 0 && window.Settings.Parent is null, "live monitor without settings controls");
            Require(window.MonitorView.NumericTexts[0] == "75" && window.MonitorView.NumericTexts[1] == "---", "sample-derived HR visible, absent optical source not invented");
            VerifyGapAndCalibration(window.MonitorTrace);
            Capture(window, "ui-preview-monitor.png");
            double wideMonitor = window.MonitorTrace.Bounds.Width;
            window.Width = 1000; window.Height = 720;
            Capture(window, "ui-preview-monitor-compact.png");
            Require(window.MonitorTrace.Bounds.Width < wideMonitor && window.Session.Display.Slots.Count == 5, "monitor resizes but skin row count stays fixed");
            window.Width = 1440; window.Height = 940;
            for (int i = 0; i < 450; i++) { window.Pulse(timer, 50_000_000); }
            Capture(window, "ui-preview-monitor-auto.png");
            Require(window.MonitorView.NumericTexts[4] == "16" && window.MonitorView.NumericTexts[3] == "40", "independent RESP rate and CO2 amplitude reach actual visible labels");
            var liveReading = window.Session.Measurements!;
            var seven = new LiveMonitorView(new LiveMonitorTrace(new LocalMonitorPreviewSession(
                PhysiologyDemoConfiguration.Default, MonitorDisplayConfiguration.Default(MonitorSkin.SevenRows))));
            seven.RefreshReadings(liveReading);
            string Mean(MeanPressureReading reading) => ((decimal)reading.MeanCentiMmHg! / 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            Require(seven.NumericTexts[2] == Mean(liveReading.AbpMean) && seven.NumericTexts[5] == Mean(liveReading.PaMean) &&
                seven.NumericTexts[6] == Mean(liveReading.CvpMean), "all three pressure channels bind independent measured means");
            _ = Raster(seven, 700, 480);
            window.MonitorView.RefreshReadings(liveReading with
            {
                HeartRate = new(WaveformMeasurementStatus.Uncountable, null, null),
                Capnography = new(new(WaveformMeasurementStatus.PoorSignal, null, null), liveReading.Capnography.RespirationsMilliPerMinute)
            });
            Require(window.MonitorView.NumericTexts[0] == "-?-" && window.MonitorView.NumericTexts[3] == "---" &&
                window.MonitorView.ActiveNotices.Any(n => n.Text.Contains("信号质量不足", StringComparison.Ordinal)), "invalid values and technical status replace digits without preset classification");
            window.MonitorView.RefreshReadings(liveReading);
            window.Settings.Alerts.HeartRateEnabled.IsChecked = true;
            window.Settings.Alerts.WarningHeartRate.Value = 60;
            window.Settings.Alerts.CriticalHeartRate.Value = 70;
            window.MonitorView.RefreshReadings(liveReading);
            Require(window.MonitorView.HighestNotice == MonitorNoticeLevel.Critical && window.MonitorView.Notice.Text == "Critical · HR 极高",
                "valid measured HR triggers the configured critical threshold and one banner");
            Capture(window, "ui-preview-critical.png");
            window.Settings.Alerts.HeartRateEnabled.IsChecked = false;
            foreach (int level in new[] { 1, 2, 3, 4 })
            {
                window.Settings.Alerts.TestLevel.SelectedIndex = level;
                window.MonitorView.RefreshReadings(liveReading);
                Require(window.MonitorView.HighestNotice == (MonitorNoticeLevel)(level - 1), "explicit four-level test is available without hardware output");
            }
            window.Settings.Alerts.TestLevel.SelectedIndex = 0;
            window.MonitorView.RefreshReadings(liveReading);
            window.SelectPage(1); Capture(window, "ui-preview-paper.png");
            window.Settings.PaperLayout.SelectedIndex = 1; window.SelectPage(1);
            Capture(window, "ui-preview-paper-six-rows.png");
            VerifySixRowPaper(window.CurrentPaper!);
            window.Settings.PaperLayout.SelectedIndex = 0; window.SelectPage(1);
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
            Require(!window.Settings.GetVisualDescendants().OfType<Expander>().Any(), "redundant twelve-lead chooser expansion is removed");
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
            VerifySoundSettings();
            window.Settings.Tabs.SelectedIndex = 3; Capture(window, "ui-preview-alarms.png");
            window.Settings.Tabs.SelectedIndex = 4; Capture(window, "ui-preview-vitals.png");
            window.Settings.Tabs.SelectedIndex = 5; Capture(window, "ui-preview-advanced.png");
            Require(window.Settings.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("窦性参考", StringComparison.Ordinal) == true), "advanced parameters track current selected style");
            Require(window.Settings.Parent is not null && window.Settings.Tabs.ItemCount == 6, "generation/display/audio/alarms/vitals/advanced live in settings tabs");
            var source = window.Session;
            window.Settings.Slots[0].Minimum.Text = "NaN";
            window.ApplySettings();
            Require(ReferenceEquals(window.Session, source) && ReferenceEquals(window.ActiveTimer, timer), "invalid settings preserve live session and timer");
            window.Settings.Skin.SelectedIndex = 0;
            window.Settings.OpticalEnabled.IsChecked = true;
            window.Settings.OpticalTarget.Value = 98;
            window.Settings.Slots[0].Auto.IsChecked = false;
            window.Settings.Slots[0].Minimum.Text = "-0.01";
            window.Settings.Slots[0].Maximum.Text = "0.01";
            window.Settings.Slots[1].Speed.SelectedIndex = 2;
            window.Settings.Slots[2].Speed.SelectedIndex = 0;
            window.ApplySettings();
            Require(!ReferenceEquals(source, window.Session) && window.Session.Display.Slots.Count == 3, "apply changes fixed skin and restarts");
            Require(window.MonitorView.NumericTexts.All(t => t == "---"), "apply clears previous numeric readings until newly acquired");
            window.Pulse(timer, 50_000_000);
            Require(window.Session.SimulationTimeNs == 0, "callbacks from old run are fenced");
            timer = window.ActiveTimer;
            for (int i = 0; i < 150; i++) { window.Pulse(timer, 50_000_000); }
            Require(window.MonitorView.NumericTexts[1] == "98", "explicit optical source reaches measured on-screen SpO2");
            window.SelectPage(0); Capture(window, "ui-preview-optical-pi.png");
            Require(window.MonitorView.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("PI ", StringComparison.Ordinal) == true && !t.Text.Contains("---", StringComparison.Ordinal)), "measured PI is visible beside saturation");
            List<double> levels = [];
            for (int i = 0; i < 16; i++) { window.Pulse(timer, 50_000_000); levels.Add(window.MonitorView.PulseLevels[0]); }
            Require(levels.Max() - levels.Min() > .5, "perfusion bar follows sampled pulse excursion");
            VerifyClamping(window.MonitorTrace);
            for (int i = 0; i < 300; i++) { window.Pulse(timer, 50_000_000); }
            Require(window.Session.Ranges.RowCycle(0) == 2 && window.Session.Ranges.RowCycle(1) == 4 && window.Session.Ranges.RowCycle(2) == 1, "native sweep wraps twice with boundary range updates");
            window.SelectPage(0); Capture(window, "ui-preview-monitor-wrap.png");
            window.Pause(); long paused = window.Session.SimulationTimeNs;
            var held = window.MonitorView.NumericTexts.ToArray();
            window.Pulse(timer, 50_000_000); Require(window.Session.SimulationTimeNs == paused, "pause holds simulation");
            Require(held.SequenceEqual(window.MonitorView.NumericTexts), "paused numerics hold with patient time");
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
            window.Settings.Skin.SelectedIndex = 2;
            window.Settings.EcgSelection = window.Settings.RespirationSelection = window.Settings.EjectionSelection = 0;
            window.ApplySettings(); window.SelectPage(0);
            for (int i = 0; i < 240; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            Capture(window, "ui-preview-perfusion.png");
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
            foreach (int row in Enumerable.Range(0, slots.Length).Where(i => slots[i].Channel != 0))
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
        double baselineY = 9 + 100 * (1 - 1200 / 2700d);
        var inkRows = Enumerable.Range(9, 100).Where(y => Green(calibrationX, y) || Green(calibrationX - 1, y)).ToArray();
        Require(Math.Abs((inkRows[0] + inkRows[^1]) / 2d - baselineY) < 2,
            "monitor calibration is centered on baseline, from minus to plus 0.5mV");
        data = Raster(trace, 800, 600);
        double phase = trace.Session.FrontierNs % MonitorDisplayConfiguration.SweepDurationNs / (double)MonitorDisplayConfiguration.SweepDurationNs;
        int headX = (int)(132 + gutter + phase * (650 - gutter));
        Require(Enumerable.Range(10, 95).Count(y => Green(headX, y) || Green(headX + 1, y)) < 12, "no vertical sweep cursor");
        Require(!Enumerable.Range(10, 95).Any(y => Green(headX + 4, y)), "erase gap contains no trace");
    }
    private static void VerifySixRowPaper(DesignPreviewTrace paper)
    {
        Require(paper.SixRows && paper.LongDurationNs == 10_300_000_000, "six-by-two paper retains five-second short leads and aligned long-II");
        byte[] data = Raster(paper, 1124, 956);
        bool Dark(int x, int y) => data[(y * 1124 + x) * 4] < 160 && data[(y * 1124 + x) * 4 + 1] < 160;
        for (int column = 0; column < 2; column++)
            for (int row = 0; row < 6; row++)
            {
                int x = 58 + column * 530, baseline = 136 + row * 120;
                Require(Enumerable.Range(baseline - 39, 38).Count(y => Dark(x, y) || Dark(x - 1, y)) > 30, "all six rows have independent paper calibration");
            }
        Require(Enumerable.Range(855, 90).Any(y => Dark(1090, y)), "six-row long-II reaches the grid right edge");
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
