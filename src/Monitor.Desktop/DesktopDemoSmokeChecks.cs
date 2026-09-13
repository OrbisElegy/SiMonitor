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
        DesktopStudyDemo.Start(window);
        window.Show();
        window.UpdateLayout();
        if (!window.HasDemoNotice || window.RecordControl is null || !window.ClearCursorButton.IsEnabled || window.DragInput is null)
        { throw new InvalidOperationException("Explicit demo did not bind labelled interactive content."); }
        Pointer pointer = new(99, PointerType.Mouse, true);
        DesktopCaptureSmokeChecks.Press(window, pointer, new(104, 100));
        DesktopCaptureSmokeChecks.Move(window, pointer, new(124, 100));
        DesktopCaptureSmokeChecks.Release(window, pointer, new(144, 100));
        if (window.CurrentPublication?.Input?.Display.Content.Content.Display.Content.Content.Study.Measurement?.First?.X.WholePixels != 35)
        { throw new InvalidOperationException("Demo drag did not update the synthetic record."); }
        window.UpdateLayout();
        using (RenderTargetBitmap image = new(new PixelSize(1280, 720), new Vector(96, 96)))
        {
            image.Render(window);
            image.Save(Path.Combine("artifacts", "desktop-study-demo.png"), PngBitmapEncoderOptions.Default);
        }
        window.ClearCursorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (window.CurrentPublication?.Input?.Display.CursorOverlaySvg is not null || window.ClearCursorButton.IsEnabled)
        { throw new InvalidOperationException("Demo clear did not remove synthetic calipers."); }
        window.Close();
        if (window.DragInput is not null || pointer.Captured is not null)
        { throw new InvalidOperationException("Demo close retained input routing."); }
        Console.WriteLine("ok: explicit labelled study demo drag, clear and close");
    }
}
