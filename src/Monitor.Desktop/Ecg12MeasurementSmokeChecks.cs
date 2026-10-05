// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Desktop;

internal static class Ecg12MeasurementSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            window.SelectPage(1);
            Dispatcher.UIThread.RunJobs();
            var page = window.EcgPage!;
            Require(page.Calipers.IsChecked != true && !page.Overlay.IsHitTestVisible && page.Readout.Text == "" && !page.ClearCalipers.IsEnabled,
                "calipers start off so the paper reads as a clean record");
            page.Calipers.IsChecked = true;
            Require(page.Overlay.IsHitTestVisible && page.Readout.Text == "在导联上拖动以测量；方向键微调，Esc 清除。", "enabling calipers shows the drag hint");

            var lead = page.Paper.Layout.Region(Ecg12PaperLayout.LeadIIIndex);
            Require(page.Overlay.Press(new Point(lead.XAt(100_000_000), lead.Baseline)), "a press on lead II starts the calipers");
            page.Overlay.Drag(new Point(lead.XAt(900_000_000), lead.Baseline + 40));
            page.Overlay.Release();
            var display = page.Overlay.Measurement.Display;
            Require(display is { Region.Lead: Ecg12PaperLayout.LeadIIIndex, Start.TimeNs: 100_000_000, End.TimeNs: 900_000_000 } &&
                page.Readout.Text!.StartsWith("II  Δt 800 ms  ΔV ", StringComparison.Ordinal) && page.Readout.Text.EndsWith("  频率 75 次/分", StringComparison.Ordinal) &&
                page.ClearCalipers.IsEnabled, $"dragging on lead II reads the interval, amplitude and rate: {page.Readout.Text}");

            page.Overlay.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right, KeyModifiers = KeyModifiers.Shift });
            var plane = page.Paper.Blocks[0].Planes.Single(p => p.ChannelId == ProjectedEcgDemoSource.ChannelId(Monitor.Simulation.Physiology.EcgLead.II));
            long samplePeriod = 1_000_000_000L * plane.SampleRateDenominator / plane.SampleRateNumerator;
            Require(page.Overlay.Measurement.Display.End!.TimeNs == 900_000_000 + 10 * samplePeriod, "Shift+Right moves the moving end by ten samples");
            page.Overlay.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left, KeyModifiers = KeyModifiers.Shift });
            Require(page.Overlay.Measurement.Display.End!.TimeNs == 900_000_000, "Shift+Left returns the moving end");

            byte[] pixels = Raster(page.Overlay, (int)page.Overlay.Width, (int)page.Overlay.Height);
            int Blue(int x, int y) => pixels[(y * (int)page.Overlay.Width + x) * 4];
            int cursor = (int)Math.Round(lead.XAt(900_000_000));
            Require(Blue(cursor, (int)lead.Top + 5) > 100 && Blue(cursor, (int)lead.Bottom - 5) > 100 && Blue(cursor + 40, (int)lead.Top + 5) == 0,
                "both caliper lines are drawn across the measured lead only");

            window.SelectPage(0);
            window.SelectPage(1);
            page = window.EcgPage!;
            Require(page.Calipers.IsChecked == true && page.Overlay.Measurement.Display.Region?.Lead == Ecg12PaperLayout.LeadIIIndex,
                "calipers survive page switches while the paper is unchanged");

            window.Settings.Language.SelectedIndex = 0;
            Require(page.Readout.Text!.EndsWith("  rate 75/min", StringComparison.Ordinal) && page.Calipers.Content?.ToString() == "Manual calipers",
                $"readout and toolbar follow the selected language: {page.Readout.Text}");
            window.Settings.Language.SelectedIndex = 1;

            page.ClearCalipers.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(page.Overlay.Measurement.Display.Region is null && !page.ClearCalipers.IsEnabled && page.Readout.Text == "在导联上拖动以测量；方向键微调，Esc 清除。",
                "clearing removes the calipers and returns to the hint");
            Require(page.Overlay.Press(new Point(lead.XAt(200_000_000), lead.Baseline)), "calipers can be placed again");
            page.Overlay.Release();

            window.SetMeasurementPolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
            page = window.EcgPage!;
            Require(!page.Calipers.IsEnabled && !page.Overlay.IsHitTestVisible && page.Overlay.Measurement.Display.Region is null &&
                page.Readout.Text == "课程已锁定手动测量。" && !page.Overlay.Press(new Point(lead.XAt(200_000_000), lead.Baseline)),
                "a course lock withdraws the calipers and explains why");
            window.SetMeasurementPolicy(SystemViewCommandAssessmentPolicy.Enabled);
            page = window.EcgPage!;
            Require(page.Calipers.IsEnabled && page.Overlay.IsHitTestVisible, "unlocking restores the calipers toggle");

            Require(page.Overlay.Press(new Point(lead.XAt(200_000_000), lead.Baseline)), "calipers placed before a layout change");
            window.Settings.PaperLayout.SelectedIndex = 1;
            window.SelectPage(1);
            Require(window.EcgPage!.Paper.SixRows && window.EcgPage.Overlay.Measurement.Display.Region is null,
                "a different paper layout starts without stale calipers");
            window.Settings.PaperLayout.SelectedIndex = 0;
        }
        finally { window.Close(); }
    }

    private static byte[] Raster(Control control, int width, int height)
    {
        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));
        using var image = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        image.Render(control);
        using var pixels = new WriteableBitmap(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = pixels.Lock();
        image.CopyPixels(buffer);
        byte[] data = new byte[width * height * 4];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, data, 0, data.Length);
        return data;
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Twelve-lead calipers: " + message); } }
}
