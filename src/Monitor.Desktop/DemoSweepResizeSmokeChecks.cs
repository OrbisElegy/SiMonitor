// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Monitor.Desktop;

internal static class DemoSweepResizeSmokeChecks
{
    internal static void VerifyFixedSpeed(WaveformDemoWindow window, bool projected = false)
    {
        int gutter = projected ? 85 : 0;
        long time = window.SimulationTimeNs, frontier = window.LiveFrontierNs;
        int count = window.BlockCount;
        byte[] narrow = Render(window.Trace, 700 + gutter);
        if (window.DisplayDurationNs != 5_600_000_000)
        { throw new InvalidOperationException("Narrow viewport did not change visible time at fixed paper speed."); }
        byte[] normal = Render(window.Trace, 1000 + gutter);
        if (window.DisplayDurationNs != 8_000_000_000)
        { throw new InvalidOperationException("Demo modes do not share the eight-second reference window."); }
        VerifyPrefix(narrow, 700 + gutter, normal, 1000 + gutter, (int)window.Trace.Height);
        byte[] wide = Render(window.Trace, 1600 + gutter);
        VerifyPrefix(normal, 1000 + gutter, wide, 1600 + gutter, (int)window.Trace.Height);
        if (window.DisplayDurationNs != 12_800_000_000 || window.SimulationTimeNs != time ||
            window.LiveFrontierNs != frontier || window.BlockCount != count)
        { throw new InvalidOperationException("Resize changed acquisition or stretched a fixed time window."); }
        // Before the first wrap, newly exposed future columns must remain empty.
        if (Enumerable.Range(0, (int)window.Trace.Height).Any(y => wide[(y * (1600 + gutter) + 1500 + gutter) * 4 + 1] != 0))
        { throw new InvalidOperationException("Resize invented future waveform samples."); }
        _ = Render(window.Trace, 1000 + gutter);
        Console.WriteLine("ok: resize preserves waveform/calibration pixels and reveals time at 125 logical pixels per second");
    }

    internal static void VerifyWrapAndRetention()
    {
        WaveformDemoWindow window = new();
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            for (int step = 0; step < 55; step++) { window.Advance(200_000_000); }
            byte[] narrow = Render(window.Trace, 500);
            if (window.DisplayStartNs != 5_000_000_000)
            { throw new InvalidOperationException("Narrow wrap chose the wrong source interval."); }
            byte[] wide = Render(window.Trace, 1000);
            if (window.DisplayStartNs != 1_000_000_000)
            { throw new InvalidOperationException("Widening failed to recover available earlier source samples."); }
            int Columns(byte[] pixels, int width) => Enumerable.Range(0, width).Count(x =>
                Enumerable.Range(0, 120).Any(y => pixels[(y * width + x) * 4 + 1] > 100));
            if (Columns(narrow, 500) < 460 || Columns(wide, 1000) < 960)
            { throw new InvalidOperationException("Resized wrap lost acquired history or joined across the erase gap."); }
            // At frontier 9s the 200ms gap is x=125..150 at either width.
            foreach (var (pixels, width) in new[] { (narrow, 500), (wide, 1000) })
            {
                if (Enumerable.Range(0, 240).Any(y => pixels[(y * width + 137) * 4 + 1] != 0))
                { throw new InvalidOperationException("Resize stretched the 200ms erase gap."); }
            }
            Click(window.HoldButton);
            long? heldStart = window.DisplayStartNs;
            _ = Render(window.Trace, 1600);
            if (window.DisplayDurationNs != 8_000_000_000 || window.DisplayStartNs != heldStart)
            { throw new InvalidOperationException("Resizing extended the frozen source interval."); }
            for (int step = 0; step < 310; step++) { window.Advance(200_000_000); }
            byte[] held = Render(window.Trace, 1000);
            if (!wide.SequenceEqual(held) || window.BlockCount != DemoSweepLayout.RetainedBlockCount)
            { throw new InvalidOperationException("Bounded history eviction altered pinned pixels."); }
            Click(window.HoldButton);
            _ = Render(window.Trace, 1000);
            if (window.DisplayStartNs != 63_000_000_000 || window.SimulationTimeNs != 73_000_000_000)
            { throw new InvalidOperationException("Return replayed history instead of joining the resized Live window."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: resized wrap exposes more history, preserves fixed gaps and frozen ranges, and bounds retention");
    }

    private static void VerifyPrefix(byte[] small, int smallWidth, byte[] large, int largeWidth, int height)
    {
        // Exclude the clipped right boundary itself, including stroke coverage.
        for (int y = 0; y < height; y++)
        {
            if (!small.AsSpan(y * smallWidth * 4, (smallWidth - 2) * 4)
                .SequenceEqual(large.AsSpan(y * largeWidth * 4, (smallWidth - 2) * 4)))
            { throw new InvalidOperationException("Resize moved or stretched existing waveform/calibration pixels."); }
        }
    }

    private static byte[] Render(Control trace, int width)
    {
        int height = (int)trace.Height;
        trace.Measure(new Size(width, height));
        trace.Arrange(new Rect(0, 0, width, height));
        using RenderTargetBitmap image = new(new PixelSize(width, height), new Vector(96, 96));
        image.Render(trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        byte[] result = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        { Marshal.Copy(buffer.Address + y * buffer.RowBytes, result, y * width * 4, width * 4); }
        return result;
    }
}
