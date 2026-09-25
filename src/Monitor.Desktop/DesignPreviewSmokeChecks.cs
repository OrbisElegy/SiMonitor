// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class DesignPreviewSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow(); window.Show();
        try
        {
            var timer = window.ActiveTimer;
            for (int i = 0; i < 150; i++) { window.Pulse(timer, 50_000_000); }
            Require(window.Page == 0 && window.Session.FrontierNs > 0 && window.Settings.Parent is null, "live monitor without settings controls");
            Capture(window, "ui-preview-monitor.png");
            double wideMonitor = window.MonitorTrace.Bounds.Width;
            window.Width = 1000; window.Height = 720;
            Capture(window, "ui-preview-monitor-compact.png");
            Require(window.MonitorTrace.Bounds.Width < wideMonitor && window.Session.Display.Slots.Count == 5, "monitor resizes but skin row count stays fixed");
            window.Width = 1440; window.Height = 940;
            window.SelectPage(1); Capture(window, "ui-preview-paper.png");
            var root = (Control)window.Content!;
            double wideScale = window.CurrentPaper!.TransformToVisual(root)!.Value.M11;
            Require(window.CurrentPaper!.BlockCount == 50 && window.Settings.Parent is null, "complete paper snapshot with no settings controls");
            window.Width = 1000; window.Height = 720;
            Capture(window, "ui-preview-compact.png");
            double narrowScale = window.CurrentPaper!.TransformToVisual(root)!.Value.M11;
            Require(narrowScale < wideScale, "paper including waves and calibration scales with the viewport");
            window.Width = 1440; window.Height = 940; window.SelectPage(2);
            Capture(window, "ui-preview-settings.png");
            window.Settings.Tabs.SelectedIndex = 1; Capture(window, "ui-preview-display.png");
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
            window.ApplySettings();
            Require(!ReferenceEquals(source, window.Session) && window.Session.Display.Slots.Count == 3, "apply changes fixed skin and restarts");
            window.Pulse(timer, 50_000_000);
            Require(window.Session.SimulationTimeNs == 0, "callbacks from old run are fenced");
            timer = window.ActiveTimer;
            for (int i = 0; i < 150; i++) { window.Pulse(timer, 50_000_000); }
            VerifyClamping(window.MonitorTrace);
            for (int i = 0; i < 300; i++) { window.Pulse(timer, 50_000_000); }
            Require(window.Session.Ranges.Cycle == 2, "native sweep wraps twice with boundary range updates");
            window.SelectPage(0); Capture(window, "ui-preview-monitor-wrap.png");
            window.Pause(); long paused = window.Session.SimulationTimeNs;
            window.Pulse(timer, 50_000_000); Require(window.Session.SimulationTimeNs == paused, "pause holds simulation");
            window.Start(); window.SelectPage(0); Require(window.Session.SimulationTimeNs == paused, "navigation/resume never regenerates history");
            foreach (var choice in new[] { (1, 1, 0), (2, 2, 1), (0, 0, 2), (0, 3, 3) })
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
    private static void VerifyClamping(LiveMonitorTrace trace)
    {
        trace.Measure(new Size(800, 360)); trace.Arrange(new Rect(0, 0, 800, 360));
        using var image = new RenderTargetBitmap(new PixelSize(800, 360), new Vector(96, 96)); image.Render(trace);
        using var pixels = new WriteableBitmap(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = pixels.Lock(); image.CopyPixels(buffer);
        byte[] data = new byte[800 * 360 * 4];
        for (int y = 0; y < 360; y++) { Marshal.Copy(buffer.Address + y * buffer.RowBytes, data, y * 800 * 4, 800 * 4); }
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
