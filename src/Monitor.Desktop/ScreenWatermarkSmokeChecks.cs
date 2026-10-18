// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Monitor.Desktop;

internal static class ScreenWatermarkSmokeChecks
{
    internal static void Verify()
    {
        Directory.CreateDirectory("artifacts/watermark");
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            for (int index = 0; index < 160; index++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            window.Pause();
            long time = window.Session.SimulationTimeNs;
            foreach (var (name, skin) in new[]
            {
                ("generic", MonitorSkinPackage.Generic),
                ("bedside", MonitorSkinPackage.BedsideMonitor),
                ("d3", MonitorSkinPackage.DefibrillatorEnclosure)
            })
            {
                window.ChangeMonitorSkin(skin);
                SavePair(window, name);
            }
            SavePair(window, "dpi-150", 1.5);
            SavePair(window, "dpi-200", 2);
            window.SelectPage(1);
            SavePair(window, "ecg");
            var print = new Ecg12PrintWindow(window.EcgPage!.Paper.Blocks, window.Localization);
            print.Show();
            try { SavePair(print, "ecg-print"); }
            finally { print.Close(); }
            var livePrint = new LiveMonitorPrintWindow(window.Session.CapturePrintSnapshot(), false, window.Localization);
            livePrint.Show();
            try { SavePair(livePrint, "live-print"); }
            finally { livePrint.Close(); }
            Require(window.Session.SimulationTimeNs == time, "watermark rendering leaves acquisition paused");
            window.SelectPage(0);
            window.ChangeMonitorSkin(MonitorSkinPackage.Generic);
            window.Start();
            for (int frame = 0; frame < 12; frame++)
            {
                window.Pulse(window.ActiveTimer, 100_000_000);
                using var image = new RenderTargetBitmap(new PixelSize(1120, 800), new Vector(96, 96));
                image.Render((Control)window.Content!);
                image.Save($"artifacts/watermark/video-{frame:00}.png", PngBitmapEncoderOptions.Default);
            }
            window.Pause();
        }
        finally { window.Close(); }
    }

    private static void SavePair(Window window, string name, double scale = 1)
    {
        var preview = window as DesignPreviewWindow;
        var measurements = preview?.Session.Measurements;
        DesktopViewportSmokeChecks.Layout(window, 1120, 800);
        var root = (Grid)window.Content!;
        var overlay = root.Children.OfType<ScreenWatermark>().Single();
        Require(overlay.Bounds == new Rect(root.Bounds.Size) && !overlay.IsHitTestVisible && !overlay.Focusable,
            "watermark spans the complete content without taking input or focus");
        var size = new PixelSize((int)(1120 * scale), (int)(800 * scale));
        var dpi = new Vector(96 * scale, 96 * scale);
        using var marked = new RenderTargetBitmap(size, dpi);
        marked.Render(root);
        marked.Save($"artifacts/watermark/{name}-marked.png", PngBitmapEncoderOptions.Default);
        overlay.IsVisible = false;
        try
        {
            using var clean = new RenderTargetBitmap(size, dpi);
            clean.Render(root);
            clean.Save($"artifacts/watermark/{name}-clean.png", PngBitmapEncoderOptions.Default);
            byte[] before = Pixels(clean), after = Pixels(marked);
            int changed = 0;
            for (int offset = 0; offset < before.Length; offset += 4)
            {
                Require(before[offset + 3] == after[offset + 3], "watermark preserves opaque window alpha");
                for (int channel = 0; channel < 3; channel++)
                {
                    int difference = Math.Abs(before[offset + channel] - after[offset + channel]);
                    Require(difference <= 2, "screen perturbation stays within two code values per channel");
                    if (difference != 0) { changed++; }
                }
            }
            Require(changed > size.Width * size.Height / 4, "watermark reaches the rendered screen pixels");
        }
        finally { overlay.IsVisible = true; }
        Require(preview is null || preview.Session.Measurements == measurements,
            "watermark rendering leaves measurements untouched");
    }

    private static byte[] Pixels(RenderTargetBitmap image)
    {
        using var copy = new WriteableBitmap(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = copy.Lock();
        image.CopyPixels(buffer);
        int stride = image.PixelSize.Width * 4;
        byte[] pixels = new byte[stride * image.PixelSize.Height];
        for (int y = 0; y < image.PixelSize.Height; y++)
        { Marshal.Copy(buffer.Address + y * buffer.RowBytes, pixels, y * stride, stride); }
        return pixels;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
