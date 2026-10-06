// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
        DesktopViewportSmokeChecks.Layout(window);
        try
        {
            window.SelectPage(1);
            Dispatcher.UIThread.RunJobs();
            var page = window.EcgPage!;
            Require(page.Calipers.IsChecked != true && !page.Overlay.IsHitTestVisible && page.Readout.Text == "" && !page.ClearCalipers.IsEnabled,
                "calipers start off so the paper reads as a clean record");
            page.Calipers.IsChecked = true;
            Require(page.Overlay.IsHitTestVisible && page.Readout.Text == "在同一导联上点击两点，拖动端点调整。", "enabling measurement shows the hint");

            var lead = page.Paper.Layout.Region(Ecg12PaperLayout.LeadIIIndex);
            page.Overlay.Move(new Point(lead.XAt(100_000_000), lead.Baseline));
            Require(page.Overlay.Measurement.Display.Hover?.TimeNs == 100_000_000, "a dot follows the pointer along lead II");
            Require(page.Overlay.Place(new Point(lead.XAt(100_000_000), lead.Baseline)) && page.Readout.Text == "II · 点击第二点完成测量",
                "the first click places a point and explains the next step");
            page.Overlay.Move(new Point(lead.XAt(900_000_000), lead.Baseline + 40));
            Require(page.Overlay.Measurement.Display.HoverResult is not null && page.Readout.Text!.Contains("Δt 800 ms", StringComparison.Ordinal) &&
                page.Readout.Text.EndsWith(" · 预览", StringComparison.Ordinal), "the toolbar previews distances before the second click");
            Require(page.Overlay.Place(new Point(lead.XAt(900_000_000), lead.Baseline + 40)), "the second click completes the measurement");
            var display = page.Overlay.Measurement.Display;
            Require(display is { Region.Lead: Ecg12PaperLayout.LeadIIIndex, Start.TimeNs: 100_000_000, End.TimeNs: 900_000_000 } &&
                page.Readout.Text!.StartsWith("II  Δt 800 ms  ΔV ", StringComparison.Ordinal) && page.Readout.Text.EndsWith("  频率 75 次/分", StringComparison.Ordinal) &&
                page.ClearCalipers.IsEnabled, $"two points on lead II read the interval, amplitude and rate: {page.Readout.Text}");

            var evidence = page.Overlay.Measurement.Display;
            window.Settings.MeasurementUnits.SelectedIndex = 1;
            string amplitudeMm = MeasurementReadout.Exact(new(evidence.Result!.AmplitudeChangeMillivolts.Numerator * 10,
                evidence.Result.AmplitudeChangeMillivolts.Denominator));
            Require(page.Readout.Text == $"II  Δx 20 mm  Δy {amplitudeMm} mm" && page.Overlay.Measurement.Display == evidence,
                "unit selection shows calibrated paper distances without moving points");
            window.SelectPage(0);
            window.SelectPage(1);
            page = window.EcgPage!;
            Require(page.Readout.Text!.Contains("Δx 20 mm", StringComparison.Ordinal), "selected units survive navigation");
            window.Settings.MeasurementUnits.SelectedIndex = 0;
            Require(page.Readout.Text!.Contains("Δt 800 ms", StringComparison.Ordinal), "converted units restore exactly");

            page.Overlay.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right, KeyModifiers = KeyModifiers.Shift });
            var plane = page.Paper.Blocks[0].Planes.Single(p => p.ChannelId == ProjectedEcgDemoSource.ChannelId(Monitor.Simulation.Physiology.EcgLead.II));
            long samplePeriod = 1_000_000_000L * plane.SampleRateDenominator / plane.SampleRateNumerator;
            Require(page.Overlay.Measurement.Display.End!.TimeNs == 900_000_000 + 10 * samplePeriod, "Shift+Right moves the latest point by ten samples");
            page.Overlay.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left, KeyModifiers = KeyModifiers.Shift });
            Require(page.Overlay.Measurement.Display.End!.TimeNs == 900_000_000, "Shift+Left returns the latest point");

            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            double fitted = page.Zoom;
            var wheelPointer = new Pointer(84, PointerType.Mouse, true);
            var anchor = page.Paper.TranslatePoint(new Point(450, 290), window)!.Value;
            page.Scroller.RaiseEvent(new PointerWheelEventArgs(page.Scroller, wheelPointer, window, anchor, 0,
                new PointerPointProperties(), KeyModifiers.None, new Vector(0, 4)));
            window.UpdateLayout();
            var zoomedAnchor = page.Paper.TranslatePoint(new Point(450, 290), window)!.Value;
            Require(Math.Abs(zoomedAnchor.X - anchor.X) < 1 && Math.Abs(zoomedAnchor.Y - anchor.Y) < 1, "wheel zoom keeps the pointer anchor stationary when scrolling is available");
            Require(page.Zoom > fitted * 1.5 && page.Overlay.Measurement.Display.Result!.ElapsedMilliseconds.Numerator == 800,
                "wheel zoom enlarges the paper without changing the measured interval");
            window.Settings.MeasurementUnits.SelectedIndex = 1;
            Require(page.Readout.Text!.Contains("Δx 20 mm", StringComparison.Ordinal), "paper millimeters do not change with screen zoom");
            window.Settings.MeasurementUnits.SelectedIndex = 0;
            var beforeDrag = page.Overlay.Measurement.Display.Start!;
            var pointer = new Pointer(85, PointerType.Mouse, true);
            Press(window, page, pointer, new Point(beforeDrag.X, beforeDrag.Y));
            Require(ReferenceEquals(pointer.Captured, page.Overlay), "a real routed press on an endpoint captures the pointer");
            Move(window, page, pointer, new Point(lead.XAt(1_100_000_000), lead.Baseline - 100));
            Release(window, page, pointer, new Point(lead.XAt(1_100_000_000), lead.Baseline - 100));
            Require(pointer.Captured is null && page.Overlay.Measurement.Display is { Start.TimeNs: 900_000_000, End.TimeNs: 1_100_000_000 },
                "dragging across the other endpoint stays on the selected lead after zoom");
            beforeDrag = page.Overlay.Measurement.Display.End!;
            Press(window, page, pointer, new Point(beforeDrag.X, beforeDrag.Y));
            Release(window, page, pointer, new Point(lead.XAt(100_000_000), lead.Baseline));
            Require(page.Overlay.Measurement.Display is { Start.TimeNs: 100_000_000, End.TimeNs: 900_000_000 }, "drag release applies the final pointer position");
            var measured = page.Overlay.Measurement.Display.Result;
            var offset = page.Scroller.Offset;
            Require(page.Scroller.Extent.Width - page.Scroller.Viewport.Width - offset.X >= 60 &&
                page.Scroller.Extent.Height - page.Scroller.Viewport.Height - offset.Y >= 40,
                "zoomed viewport has room for the requested hand drag on both axes");
            page.Hand.IsChecked = true;
            Require(!page.Overlay.IsHitTestVisible && page.Overlay.Measurement.Display.Result == measured, "hand tool preserves measurements and disables caliper placement");
            var panStart = page.Scroller.TranslatePoint(new Point(500, 300), window)!.Value;
            var panEnd = new Point(panStart.X - 60, panStart.Y - 40);
            page.PanSurface.RaiseEvent(new PointerPressedEventArgs(page.PanSurface, pointer, window, panStart, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
            Require(ReferenceEquals(pointer.Captured, page.PanSurface), "hand tool captures the pointer");
            page.PanSurface.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, page.PanSurface, pointer, window, panEnd, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
            page.PanSurface.RaiseEvent(new PointerReleasedEventArgs(page.PanSurface, pointer, window, panEnd, 0,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            Require(pointer.Captured is null && Math.Abs(page.Scroller.Offset.X - offset.X - 60) < 1 &&
                Math.Abs(page.Scroller.Offset.Y - offset.Y - 40) < 1 && page.Overlay.Measurement.Display.Result == measured,
                "hand drag pans the viewport without changing measurement values");
            page.ActualSize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(page.Zoom == 1 && page.Overlay.Measurement.Display.Result == measured, "1:1 restores exactly 100 percent without clearing the measurement");
            Require(page.Scroller.Bounds.Top == 0 && page.Scroller.Bounds.Height == page.Bounds.Height, "ECG viewport extends beneath the floating toolbar");
            page.Calipers.IsChecked = true;
            Require(page.Hand.IsChecked == false && !page.PanSurface.IsHitTestVisible, "selecting calipers releases the hand tool");
            page.Fit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(Math.Abs(page.Zoom - fitted) < .01, "Fit restores the whole paper");

            window.SelectPage(0);
            window.SelectPage(1);
            page = window.EcgPage!;
            Require(page.Calipers.IsChecked == true && page.Overlay.Measurement.Display.Region?.Lead == Ecg12PaperLayout.LeadIIIndex,
                "calipers survive page switches while the paper is unchanged");

            window.Settings.Language.SelectedIndex = 0;
            Require(page.Readout.Text!.EndsWith("  rate 75/min", StringComparison.Ordinal) && Avalonia.Automation.AutomationProperties.GetName(page.Calipers) == "Manual calipers",
                $"readout and toolbar follow the selected language: {page.Readout.Text}");
            window.Settings.Language.SelectedIndex = 1;

            page.ClearCalipers.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(page.Overlay.Measurement.Display.Region is null && !page.ClearCalipers.IsEnabled && page.Readout.Text == "在同一导联上点击两点，拖动端点调整。",
                "clearing removes the points and returns to the hint");
            Require(page.Overlay.Place(new Point(lead.XAt(200_000_000), lead.Baseline)), "points can be placed again");

            window.SetMeasurementPolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
            page = window.EcgPage!;
            Require(!page.Calipers.IsEnabled && !page.Overlay.IsHitTestVisible && page.Overlay.Measurement.Display.Region is null &&
                page.Readout.Text == "课程已锁定手动测量。" && !page.Overlay.Place(new Point(lead.XAt(200_000_000), lead.Baseline)),
                "a course lock withdraws the calipers and explains why");
            window.SetMeasurementPolicy(SystemViewCommandAssessmentPolicy.Enabled);
            page = window.EcgPage!;
            Require(page.Calipers.IsEnabled && page.Overlay.IsHitTestVisible, "unlocking restores the calipers toggle");

            Require(page.Overlay.Place(new Point(lead.XAt(200_000_000), lead.Baseline)), "a point placed before a layout change");
            window.Settings.PaperLayout.SelectedIndex = 1;
            window.SelectPage(1);
            Require(window.EcgPage!.Paper.SixRows && window.EcgPage.Overlay.Measurement.Display.Region is null,
                "a different paper layout starts without stale calipers");
            window.Settings.PaperLayout.SelectedIndex = 0;
        }
        finally { window.Close(); }
    }

    private static Point Position(DesignPreviewWindow window, Ecg12PaperPage page, Point point)
    {
        window.UpdateLayout();
        return page.Overlay.TranslatePoint(point, window)!.Value;
    }

    private static void Press(DesignPreviewWindow window, Ecg12PaperPage page, Pointer pointer, Point point) =>
        page.Overlay.RaiseEvent(new PointerPressedEventArgs(page.Overlay, pointer, window, Position(window, page, point), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));

    private static void Move(DesignPreviewWindow window, Ecg12PaperPage page, Pointer pointer, Point point) =>
        page.Overlay.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, page.Overlay, pointer, window, Position(window, page, point), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));

    private static void Release(DesignPreviewWindow window, Ecg12PaperPage page, Pointer pointer, Point point) =>
        page.Overlay.RaiseEvent(new PointerReleasedEventArgs(page.Overlay, pointer, window, Position(window, page, point), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Twelve-lead calipers: " + message); } }
}
