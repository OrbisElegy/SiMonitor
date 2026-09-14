// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Monitor.Desktop;

internal static class WaveformDemoSmokeChecks
{
    public static void Verify()
    {
        WaveformDemoWindow window = new();
        window.Show();
        try
        {
            window.UpdateLayout();
            VerifyPixels(window, false);
            for (int step = 0; step < 10; step++) { Click(window.StepButton); }
            if (window.BlockCount != 0 || window.SimulationTimeNs != 2_000_000_000)
            { throw new InvalidOperationException("Slow channel did not hold back shared output."); }
            Click(window.StepButton);
            if (window.BlockCount != 1) { throw new InvalidOperationException("First complete block missing."); }
            Control previous = window.Trace;
            bool rejected = false;
            try { window.Advance(400_000_000); }
            catch (ArgumentException) { rejected = true; }
            if (!rejected || window.SimulationTimeNs != 2_200_000_000 || !ReferenceEquals(previous, window.Trace))
            { throw new InvalidOperationException("Rejected generation changed the displayed fixture."); }
            for (int step = 0; step < 15; step++) { Click(window.StepButton); }
            if (window.BlockCount != 10) { throw new InvalidOperationException("Displayed block retention is not bounded."); }
            window.UpdateLayout();
            VerifyPixels(window, true);
            window.Width = 800;
            window.UpdateLayout();
            // Native window resize delivery is asynchronous; exercise the trace's
            // new layout bounds explicitly within this synchronous smoke callback.
            window.Trace.Measure(new Size(700, 240));
            window.Trace.Arrange(new Rect(0, 0, 700, 240));
            if (window.Trace.Bounds.Width != 700)
            { throw new InvalidOperationException("Trace resize did not update its drawing bounds."); }
            VerifyPixels(window, true);
            Click(window.ResetButton);
            window.UpdateLayout();
            if (window.BlockCount != 0 || window.SimulationTimeNs != 0)
            { throw new InvalidOperationException("Reset retained source data."); }
            VerifyPixels(window, false);
            for (int step = 0; step < 11; step++) { Click(window.StepButton); }
            if (window.BlockCount != 1) { throw new InvalidOperationException("Reset source did not restart."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: generated multi-rate native traces, delay, bounded retention, failure, resize and reset");
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void VerifyPixels(WaveformDemoWindow window, bool populated)
    {
        Control trace = window.Trace;
        using RenderTargetBitmap image = new(new PixelSize((int)trace.Bounds.Width, 240), new Vector(96, 96));
        image.Render(trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        int green = 0;
        int cyan = 0;
        for (int y = 0; y < 240; y++)
        {
            for (int x = 0; x < image.PixelSize.Width; x++)
            {
                int offset = y * buffer.RowBytes + x * 4;
                byte blue = Marshal.ReadByte(buffer.Address, offset);
                byte value = Marshal.ReadByte(buffer.Address, offset + 1);
                if (y < 120 && value > 100 && blue == 0) { green++; }
                if (y >= 120 && value > 100 && blue > 100) { cyan++; }
            }
        }
        if (populated ? green < 100 || cyan < 100 : green != 0 || cyan != 0)
        { throw new InvalidOperationException("Generated native trace pixels do not match available source planes."); }
        if (populated)
        {
            Directory.CreateDirectory("artifacts");
            image.Save(Path.Combine("artifacts", "desktop-waveform-demo.png"), PngBitmapEncoderOptions.Default);
        }
    }
}
