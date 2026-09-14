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
        VerifyPhysiology();
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
            if (window.BlockCount != 11) { throw new InvalidOperationException("Displayed block retention is not bounded."); }
            window.UpdateLayout();
            VerifyPixels(window, true);
            byte[] retainedColumn = ReadColumn(window.Trace, 350);
            VerifyGap(window.Trace, 650);
            Click(window.StepButton);
            window.UpdateLayout();
            _ = ReadColumn(window.Trace, 650);
            VerifyGap(window.Trace, 750);
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
                window.SimulationTimeNs != 6_200_000_000 || window.BlockCount != 11)
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
            VerifyShapes(window);
            VerifyCompactLayout(window);
            VerifyAutomaticSteps(window);
        }
        finally { window.Close(); }
        Console.WriteLine("ok: generated multi-rate native traces, delay, bounded retention, failure, resize and reset");
        Console.WriteLine("ok: pinned generated trace survives background eviction and returns directly to latest data");
        Console.WriteLine("ok: automatic step timer, pause, stale callback fencing, held generation, reset and close");
    }

    private static void VerifyShapes(WaveformDemoWindow window)
    {
        for (int step = 0; step < 11; step++) { Click(window.StepButton); }
        window.UpdateLayout();
        byte[] triangle = ReadColumn(window.Trace, 30);
        Click(window.HoldButton);
        Click(window.RunButton);
        var oldTimer = window.ActiveTimer;
        Click(window.ShapeButton);
        window.Pulse(oldTimer);
        if (!window.UsesPulse || window.IsHeld || window.ActiveTimer is not null ||
            window.SimulationTimeNs != 0 || window.BlockCount != 0)
        { throw new InvalidOperationException("Shape change retained old source, view or timer state."); }
        for (int step = 0; step < 10; step++) { Click(window.StepButton); }
        if (window.BlockCount != 0) { throw new InvalidOperationException("New shape bypassed acquisition latency."); }
        Click(window.StepButton);
        window.UpdateLayout();
        byte[] pulse = ReadColumn(window.Trace, 30);
        if (triangle.SequenceEqual(pulse)) { throw new InvalidOperationException("Different synthetic tables produced identical fixture pixels."); }
        Click(window.ResetButton);
        if (!window.UsesPulse) { throw new InvalidOperationException("Reset lost the selected shape."); }
        for (int step = 0; step < 11; step++) { Click(window.StepButton); }
        window.UpdateLayout();
        if (!pulse.SequenceEqual(ReadColumn(window.Trace, 30)))
        { throw new InvalidOperationException("Selected shape did not restart deterministically."); }
        Click(window.ShapeButton);
        if (window.UsesPulse || window.BlockCount != 0)
        { throw new InvalidOperationException("Triangle selection did not restore an empty source."); }
        Console.WriteLine("ok: synthetic shape change resets acquisition, held state and timers; reset preserves selected shape");
    }

    private static void VerifyCompactLayout(WaveformDemoWindow window)
    {
        window.LayoutRoot.Measure(new Size(440, 300));
        window.LayoutRoot.Arrange(new Rect(0, 0, 440, 300));
        if (window.ContentScroll.Extent.Height <= window.ContentScroll.Viewport.Height ||
            window.TeachingNotice.Bounds.Bottom > window.ContentScroll.Bounds.Top ||
            window.ContentScroll.Extent.Width > window.ContentScroll.Viewport.Width + 1)
        { throw new InvalidOperationException("Compact layout hides the notice or requires horizontal scrolling."); }
        Rect notice = window.TeachingNotice.Bounds;
        window.ContentScroll.ScrollToEnd();
        window.LayoutRoot.Measure(new Size(440, 300));
        window.LayoutRoot.Arrange(new Rect(0, 0, 440, 300));
        if (window.ContentScroll.Offset.Y <= 0 || window.TeachingNotice.Bounds != notice ||
            window.Trace.Bounds.Width <= 0 || window.Trace.Bounds.Height != 240)
        { throw new InvalidOperationException("Compact scroll did not preserve notice and full trace geometry."); }
        window.ContentScroll.ScrollToHome();
        Console.WriteLine("ok: compact waveform layout wraps content, scrolls vertically and pins teaching notice");
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

    private static void VerifyGap(Control trace, int x)
    {
        byte[] column = ReadColumn(trace, x, requireTrace: false);
        if (column.Where((_, index) => index % 4 != 3).Any(value => value != 0))
        { throw new InvalidOperationException("Erase gap retained waveform pixels."); }
    }

    private static byte[] ReadColumn(Control trace, int x, bool requireTrace = true)
    {
        using RenderTargetBitmap image = new(new PixelSize((int)trace.Bounds.Width, 240), new Vector(96, 96));
        image.Render(trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        byte[] column = new byte[240 * 4];
        for (int y = 0; y < 240; y++)
        { Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, column, y * 4, 4); }
        if (!requireTrace) { return column; }
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

    private static void VerifyPhysiology()
    {
        WaveformDemoWindow window = new(physiology: true);
        window.Show();
        try
        {
            Click(window.StepButton);
            if (window.BlockCount != 0) { throw new InvalidOperationException("Resp delay was bypassed."); }
            Click(window.StepButton);
            if (window.BlockCount != 1 || window.ShapeButton.IsVisible)
            { throw new InvalidOperationException("Event source did not produce its first shared block."); }
            Click(window.HoldButton);
            Control held = window.Trace;
            for (int step = 0; step < 20; step++) { Click(window.StepButton); }
            if (!ReferenceEquals(held, window.Trace) || window.BlockCount != 11)
            { throw new InvalidOperationException("Continuous event output lost held view or bounded retention."); }
            Click(window.HoldButton);
            window.UpdateLayout();
            VerifyPixels(window, true, resp: true);
            Click(window.ResetButton);
            Click(window.ShapeButton);
            if (window.SimulationTimeNs != 0 || window.UsesPulse || window.BlockCount != 0)
            { throw new InvalidOperationException("Event source reset or hidden shape command failed."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: event-driven ECG/Resp native pixels, shared delay, held generation and reset");
    }

    private static void VerifyPixels(WaveformDemoWindow window, bool populated, bool resp = false)
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
                byte red = Marshal.ReadByte(buffer.Address, offset + 2);
                if (y < 120 && value > 100 && blue == 0) { green++; }
                if (y >= 120 && value > 100 && (resp ? red > 100 && blue == 0 : blue > 100)) { cyan++; }
            }
        }
        if (populated ? green < 100 || cyan < 100 : green != 0 || cyan != 0)
        { throw new InvalidOperationException("Generated native trace pixels do not match available source planes."); }
        if (populated)
        {
            Directory.CreateDirectory("artifacts");
            image.Save(Path.Combine("artifacts", resp ? "desktop-physiology-demo.png" : "desktop-waveform-demo.png"), PngBitmapEncoderOptions.Default);
        }
    }
}
