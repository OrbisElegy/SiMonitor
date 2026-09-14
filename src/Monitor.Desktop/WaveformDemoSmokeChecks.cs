// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

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
            Click(window.HoldButton);
            if (window.IsHeld || window.HoldButton.IsEnabled)
            { throw new InvalidOperationException("Empty source allowed a pinned view."); }
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
            byte[] retainedColumn = ReadColumn(window.Trace, 350);
            Click(window.StepButton);
            window.UpdateLayout();
            if (!retainedColumn.SequenceEqual(ReadColumn(window.Trace, 350)))
            { throw new InvalidOperationException("Unmodified sweep samples moved horizontally after history eviction."); }
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
            Click(window.HoldButton);
            Control pinned = window.Trace;
            long? pinnedStart = window.DisplayStartNs;
            for (int step = 0; step < 20; step++) { Click(window.StepButton); }
            if (!window.IsHeld || !ReferenceEquals(pinned, window.Trace) || window.DisplayStartNs != pinnedStart ||
                window.SimulationTimeNs != 6_200_000_000 || window.BlockCount != 10)
            { throw new InvalidOperationException("Background generation or eviction altered the pinned view."); }
            Click(window.HoldButton);
            if (window.IsHeld || ReferenceEquals(pinned, window.Trace) || window.DisplayStartNs != 2_200_000_000 ||
                window.SimulationTimeNs != 6_200_000_000)
            { throw new InvalidOperationException("Return did not join latest data without advancing simulation."); }
            window.UpdateLayout();
            VerifyPixels(window, true);
            Click(window.HoldButton);
            Click(window.ResetButton);
            if (window.IsHeld || window.HoldButton.IsEnabled || window.DisplayStartNs is not null)
            { throw new InvalidOperationException("Reset retained pinned data or capability."); }
            VerifyAutomaticSteps(window);
        }
        finally { window.Close(); }
        Console.WriteLine("ok: generated multi-rate native traces, delay, bounded retention, failure, resize and reset");
        Console.WriteLine("ok: pinned generated trace survives background eviction and returns directly to latest data");
        Console.WriteLine("ok: automatic step timer, pause, stale callback fencing, held generation, reset and close");
    }

    private static void VerifyAutomaticSteps(WaveformDemoWindow window)
    {
        Click(window.RunButton);
        var first = window.ActiveTimer;
        if (first is null || !first.IsEnabled || window.StepButton.IsEnabled)
        { throw new InvalidOperationException("Automatic run did not own an enabled timer."); }
        Click(window.StepButton);
        if (window.SimulationTimeNs != 0) { throw new InvalidOperationException("Manual step bypassed automatic ownership."); }
        DispatcherFrame frame = new();
        bool delivered = false;
        void ObserveTick(object? sender, EventArgs args) { delivered = true; frame.Continue = false; }
        DispatcherTimer timeout = new() { Interval = TimeSpan.FromSeconds(3) };
        timeout.Tick += (_, _) => frame.Continue = false;
        first.Tick += ObserveTick;
        timeout.Start();
        try { Dispatcher.UIThread.PushFrame(frame); }
        finally { timeout.Stop(); first.Tick -= ObserveTick; }
        if (!delivered || window.SimulationTimeNs != 200_000_000)
        { throw new InvalidOperationException("Native timer did not deliver one simulation step."); }
        for (int tick = 0; tick < 10; tick++) { window.Pulse(first); }
        Click(window.HoldButton);
        Control held = window.Trace;
        window.Pulse(first);
        if (window.SimulationTimeNs != 2_400_000_000 || !ReferenceEquals(held, window.Trace))
        { throw new InvalidOperationException("Automatic generation altered held geometry."); }
        Click(window.RunButton);
        window.Pulse(first);
        if (window.ActiveTimer is not null || first.IsEnabled || !window.StepButton.IsEnabled ||
            window.SimulationTimeNs != 2_400_000_000)
        { throw new InvalidOperationException("Pause did not fence its old timer."); }
        Click(window.RunButton);
        var second = window.ActiveTimer;
        window.Pulse(first);
        if (ReferenceEquals(first, second) || window.SimulationTimeNs != 2_400_000_000)
        { throw new InvalidOperationException("Resume accepted a stale callback."); }
        window.Pulse(second);
        if (window.SimulationTimeNs != 2_600_000_000) { throw new InvalidOperationException("Resume did not take one fixed step."); }
        Click(window.ResetButton);
        window.Pulse(second);
        if (window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.IsHeld)
        { throw new InvalidOperationException("Reset retained automatic generation or held data."); }
        Click(window.RunButton);
        var final = window.ActiveTimer;
        window.Close();
        window.Pulse(final);
        Click(window.RunButton);
        Click(window.StepButton);
        if (window.ActiveTimer is not null || final is null || final.IsEnabled || window.SimulationTimeNs != 0)
        { throw new InvalidOperationException("Closed window restarted generation."); }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static byte[] ReadColumn(Control trace, int x)
    {
        using RenderTargetBitmap image = new(new PixelSize((int)trace.Bounds.Width, 240), new Vector(96, 96));
        image.Render(trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        byte[] column = new byte[240 * 4];
        for (int y = 0; y < 240; y++)
        { Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, column, y * 4, 4); }
        if (!column.Where((_, index) => index % 4 == 1).Any(value => value > 100))
        { throw new InvalidOperationException("Sweep stability evidence column has no trace."); }
        for (int channel = 0; channel < 2; channel++)
        {
            int crossings = Enumerable.Range(channel * 120, 120).Count(y => column[y * 4 + 1] > 100);
            if (crossings is < 1 or > 4)
            { throw new InvalidOperationException("Sweep column contains a missing trace or a cross-wrap connector."); }
        }
        return column;
    }

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
