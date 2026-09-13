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
        if (window.ResetDemoButton.IsVisible) { throw new InvalidOperationException("Ordinary startup exposed synthetic reset."); }
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
}
