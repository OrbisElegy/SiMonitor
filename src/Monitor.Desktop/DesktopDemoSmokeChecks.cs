// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace Monitor.Desktop;

internal static class DesktopDemoSmokeChecks
{
    public static void Verify()
    {
        MainWindow window = new();
        if (window.ResetDemoButton.IsVisible || window.DemoViews.IsVisible || window.DemoMeasurement.IsVisible) { throw new InvalidOperationException("Ordinary startup exposed synthetic controls."); }
        DesktopStudyDemo.Start(window);
        window.Show();
        window.UpdateLayout();
        if (!window.HasDemoNotice || window.RecordControl is null || !window.ClearCursorButton.IsEnabled || window.DragInput is null)
        { throw new InvalidOperationException("Explicit demo did not bind labelled interactive content."); }
        if (window.MeasurementAccessibilityText != window.MeasurementReadoutText || window.MeasurementReadoutText != "Δt：100 ms    ΔV（终点−起点）：-0.05 mV")
        { throw new InvalidOperationException("Initial demo readout did not match calibrated evidence."); }
        Pointer pointer = new(99, PointerType.Mouse, true);
        DesktopCaptureSmokeChecks.Press(window, pointer, new(104, 100));
        DesktopCaptureSmokeChecks.Move(window, pointer, new(124, 100));
        DesktopCaptureSmokeChecks.Release(window, pointer, new(144, 100));
        if (window.CurrentPublication?.Input?.Display.Content.Content.Display.Content.Content.Study.Measurement?.First?.X.WholePixels != 35)
        { throw new InvalidOperationException("Demo drag did not update the synthetic record."); }
        if (window.MeasurementAccessibilityText != window.MeasurementReadoutText || window.MeasurementReadoutText != "Δt：80 ms    ΔV（终点−起点）：-0.05 mV")
        { throw new InvalidOperationException("Drag did not update the time readout."); }
        window.UpdateLayout();
        using (RenderTargetBitmap image = new(new PixelSize(1280, 720), new Vector(96, 96)))
        {
            image.Render(window);
            image.Save(Path.Combine("artifacts", "desktop-study-demo.png"), PngBitmapEncoderOptions.Default);
        }
        window.ClearCursorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (window.CurrentPublication?.Input?.Display.CursorOverlaySvg is not null || window.ClearCursorButton.IsEnabled || window.MeasurementReadoutText is not null || window.MeasurementAccessibilityText is not null)
        { throw new InvalidOperationException("Demo clear did not remove synthetic calipers."); }
        window.ResetDemoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RequireReset(window);
        DesktopCaptureSmokeChecks.Press(window, pointer, new(104, 100));
        DesktopCaptureSmokeChecks.Move(window, pointer, new(124, 100));
        window.ResetDemoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        RequireReset(window);
        if (pointer.Captured is not null) { throw new InvalidOperationException("Demo reset retained the previous capture."); }
        DesktopCaptureSmokeChecks.Release(window, pointer, new(144, 100));
        RequireReset(window);
        DesktopCaptureSmokeChecks.Press(window, pointer, new(104, 100));
        DesktopCaptureSmokeChecks.Release(window, pointer, new(144, 160));
        if (window.CurrentPublication?.Input?.Display.Content.Content.Display.Content.Content.Study.Measurement?.First?.X.WholePixels != 35)
        { throw new InvalidOperationException("Reset demo was not draggable."); }
        var moved = window.CurrentPublication.Input.Display.Content.Content.Display.Content.Content.Study.Measurement!;
        if (moved.First!.Y.PixelNumerator != 40 * moved.First.Y.PixelDenominator ||
            moved.Second!.Y.PixelNumerator != 35 * moved.Second.Y.PixelDenominator)
        { throw new InvalidOperationException("Demo incorrectly locked vertical amplitude order."); }
        if (window.MeasurementAccessibilityText != window.MeasurementReadoutText || window.MeasurementReadoutText != "Δt：80 ms    ΔV（终点−起点）：0.025 mV")
        { throw new InvalidOperationException("Vertical movement did not update signed amplitude readout."); }
        VerifyViews(window, pointer);
        VerifyPolicies(window, pointer);
        window.Close();
        if (window.DragInput is not null || pointer.Captured is not null)
        { throw new InvalidOperationException("Demo close retained input routing."); }
        Console.WriteLine("ok: explicit labelled study demo drag, clear, reset and close");
    }

    private static void RequireReset(MainWindow window)
    {
        var measurement = window.CurrentPublication?.Input?.Display.Content.Content.Display.Content.Content.Study.Measurement;
        if (!window.ResetDemoButton.IsVisible || !window.ClearCursorButton.IsEnabled ||
            measurement?.First?.X.WholePixels != 25 || measurement.Second?.X.WholePixels != 75)
        { throw new InvalidOperationException("Demo reset did not restore both initial calipers."); }
    }

    private static void VerifyViews(MainWindow window, Pointer pointer)
    {
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Click(window.ResetDemoButton);
        var initial = window.CurrentPublication!.Input!.Display.Content.Content.Display.Content.Content.Study.Measurement!;
        foreach (var (button, width) in new[] { (window.DemoViews.Zoom100, 100d), (window.DemoViews.Zoom200, 200d), (window.DemoViews.Zoom400, 400d) })
        {
            Click(button);
            var measurement = window.CurrentPublication!.Input!.Display.Content.Content.Display.Content.Content.Study.Measurement!;
            if (window.RecordControl!.Width != width || measurement.First!.Cursor != initial.First!.Cursor ||
                measurement.Second!.Cursor != initial.Second!.Cursor || window.MeasurementReadoutText != "Δt：100 ms    ΔV（终点−起点）：-0.05 mV" || button.IsEnabled)
            { throw new InvalidOperationException("Zoom changed record coordinates or did not update selection."); }
            var selected = window.CurrentPublication;
            Click(button);
            if (!ReferenceEquals(selected, window.CurrentPublication))
            { throw new InvalidOperationException("Selected scale unnecessarily refreshed the study."); }
        }
        Click(window.DemoViews.Dark);
        if (window.CurrentPublication!.Input!.Display.Content.Content.Display.Content.Theme!.Theme != Monitor.Domain.Presentation.Ecg12Theme.MonitorDarkGreen || window.DemoViews.Dark.IsEnabled)
        { throw new InvalidOperationException("Dark theme was not selected."); }
        Click(window.DemoViews.Zoom200);
        DesktopCaptureSmokeChecks.Press(window, pointer, new(50, 50));
        DesktopCaptureSmokeChecks.Release(window, pointer, new(70, 50));
        if (window.MeasurementReadoutText != "Δt：80 ms    ΔV（终点−起点）：-0.05 mV")
        { throw new InvalidOperationException("Scaled dark-theme drag did not preserve calibrated mapping."); }
        window.UpdateLayout();
        using (RenderTargetBitmap image = new(new PixelSize(1280, 720), new Vector(96, 96)))
        {
            image.Render(window);
            image.Save(Path.Combine("artifacts", "desktop-study-dark.png"), PngBitmapEncoderOptions.Default);
        }
        DesktopCaptureSmokeChecks.Press(window, pointer, new(70, 50));
        DesktopCaptureSmokeChecks.Move(window, pointer, new(80, 50));
        Click(window.DemoViews.Zoom400);
        if (pointer.Captured is not null || window.MeasurementReadoutText != "Δt：80 ms    ΔV（终点−起点）：-0.05 mV")
        { throw new InvalidOperationException("Zoom during drag did not restore the original measurement."); }
        var restored = window.CurrentPublication;
        DesktopCaptureSmokeChecks.Release(window, pointer, new(180, 100));
        if (!ReferenceEquals(restored, window.CurrentPublication))
        { throw new InvalidOperationException("Late pre-zoom release mutated the new view."); }
        DesktopCaptureSmokeChecks.Press(window, pointer, new(140, 100));
        DesktopCaptureSmokeChecks.Move(window, pointer, new(160, 100));
        Click(window.DemoViews.Paper);
        if (pointer.Captured is not null || window.MeasurementReadoutText != "Δt：80 ms    ΔV（终点−起点）：-0.05 mV")
        { throw new InvalidOperationException("Theme during drag did not roll back."); }
        Click(window.ClearCursorButton);
        Click(window.DemoViews.Dark);
        Click(window.DemoViews.Zoom100);
        if (window.MeasurementReadoutText is not null || window.ClearCursorButton.IsEnabled)
        { throw new InvalidOperationException("View changes recreated cleared calipers."); }
        Click(window.ResetDemoButton);
        RequireReset(window);
        if (window.RecordControl!.Width != 400 || window.DemoViews.Paper.IsEnabled || window.DemoViews.Zoom400.IsEnabled)
        { throw new InvalidOperationException("Reset did not restore initial view selection."); }
        Console.WriteLine("ok: grouped demo zoom, theme, coordinate preservation, drag interruption and reset");
    }

    private static void VerifyPolicies(MainWindow window, Pointer pointer)
    {
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        DesktopCaptureSmokeChecks.Press(window, pointer, new(104, 100));
        DesktopCaptureSmokeChecks.Move(window, pointer, new(144, 100));
        Click(window.DemoMeasurement.Disabled);
        if (pointer.Captured is not null || window.ClearCursorButton.IsVisible || window.RecordControl!.IsHitTestVisible ||
            window.MeasurementReadoutText is not null || window.MeasurementAccessibilityText is not null)
        { throw new InvalidOperationException("Demo disabled policy retained measurement interaction."); }
        var disabled = window.CurrentPublication;
        Click(window.ClearCursorButton);
        DesktopCaptureSmokeChecks.Release(window, pointer, new(180, 100));
        if (!ReferenceEquals(disabled, window.CurrentPublication))
        { throw new InvalidOperationException("Disabled demo accepted stale native input."); }
        Click(window.DemoMeasurement.Locked);
        if (!window.ClearCursorButton.IsVisible || window.ClearCursorButton.IsEnabled ||
            !Equals(ToolTip.GetTip(window.ClearCursorButton), "课程已锁定快速测量"))
        { throw new InvalidOperationException("Demo course lock lost its disabled explanation."); }
        Click(window.DemoMeasurement.Enabled);
        if (window.MeasurementReadoutText != "Δt：100 ms    ΔV（终点−起点）：-0.05 mV")
        { throw new InvalidOperationException("Policy recovery did not preserve pre-drag evidence."); }
        window.DemoMeasurement.Auxiliary.IsChecked = true;
        if (window.MeasurementReadoutText != "Δt：100 ms    ΔV（终点−起点）：-0.05 mV    辅助频率：600 次/分")
        { throw new InvalidOperationException("Demo auxiliary permission did not show the calibrated result."); }
        DesktopCaptureSmokeChecks.Press(window, pointer, new(100, 100));
        DesktopCaptureSmokeChecks.Release(window, pointer, new(140, 100));
        if (window.MeasurementReadoutText != "Δt：80 ms    ΔV（终点−起点）：-0.05 mV    辅助频率：750 次/分")
        { throw new InvalidOperationException("Auxiliary result did not follow manual movement."); }
        window.DemoMeasurement.Auxiliary.IsChecked = false;
        if (window.MeasurementReadoutText != "Δt：80 ms    ΔV（终点−起点）：-0.05 mV")
        { throw new InvalidOperationException("Revoked auxiliary permission retained the result."); }
        window.DemoMeasurement.Auxiliary.IsChecked = true;
        DesktopCaptureSmokeChecks.Press(window, pointer, new(140, 100));
        DesktopCaptureSmokeChecks.Release(window, pointer, new(300, 100));
        if (window.MeasurementReadoutText != "Δt：0 ms    ΔV（终点−起点）：-0.05 mV")
        { throw new InvalidOperationException("Zero time interval fabricated an auxiliary rate."); }
        Click(window.ClearCursorButton);
        Click(window.DemoMeasurement.Disabled);
        Click(window.DemoMeasurement.Enabled);
        if (window.MeasurementReadoutText is not null || window.ClearCursorButton.IsEnabled)
        { throw new InvalidOperationException("Policy switching recreated cleared measurements."); }
        Click(window.ResetDemoButton);
        RequireReset(window);
        if (window.DemoMeasurement.Auxiliary.IsChecked == true || window.DemoMeasurement.Enabled.IsEnabled)
        { throw new InvalidOperationException("Reset did not restore demo policy defaults."); }
        Console.WriteLine("ok: grouped demo policies, auxiliary gating, zero interval and recovery");
    }
}
