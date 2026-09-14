// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Monitor.Simulation.Acquisition;

namespace Monitor.Desktop;

internal static class WaveformDemoSmokeChecks
{
    public static void Verify()
    {
        RespCo2CouplingSmokeChecks.Verify();
        VerifyPhysiology();
        VerifyProgressiveSweep();
        DemoSweepResizeSmokeChecks.VerifyWrapAndRetention();
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
            if (window.BlockCount != 16) { throw new InvalidOperationException("Displayed block retention is not bounded."); }
            window.UpdateLayout();
            VerifyPixels(window, true);
            byte[] retainedColumn = ReadColumn(window.Trace, 80);
            VerifyGap(window.Trace, 410);
            Click(window.StepButton);
            window.UpdateLayout();
            _ = ReadColumn(window.Trace, 410);
            VerifyGap(window.Trace, 435);
            if (!retainedColumn.SequenceEqual(ReadColumn(window.Trace, 80)))
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
            DemoSweepResizeSmokeChecks.VerifyFixedSpeed(window);
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
                window.SimulationTimeNs != 6_200_000_000 || window.BlockCount != 21)
            { throw new InvalidOperationException("Background generation or eviction altered the pinned view."); }
            Click(window.HoldButton);
            if (window.IsHeld || ReferenceEquals(pinned, window.Trace) || window.DisplayStartNs != 0 ||
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

    private static void VerifyProgressiveSweep()
    {
        foreach (bool physiology in new[] { false, true })
        {
            WaveformDemoWindow window = new(physiology);
            window.Show();
            try
            {
                int frames = 150;
                for (int frame = 0; frame < frames; frame++) { window.Advance(16_000_000, progressive: true); }
                if (window.LiveFrontierNs != 200_000_000 || window.BlockCount != 2)
                { throw new InvalidOperationException("Progressive display did not buffer complete acquired blocks."); }
                void Layout()
                {
                    window.Trace.Measure(new Size(1000, window.Trace.Height));
                    window.Trace.Arrange(new Rect(0, 0, 1000, window.Trace.Height));
                }
                Layout();
                VerifyGap(window.Trace, 27);
                byte[] retained = ReadColumn(window.Trace, 8);
                window.Advance(16_000_000, progressive: true);
                if (window.LiveFrontierNs != 216_000_000 || window.BlockCount != 2)
                { throw new InvalidOperationException("Sweep frontier waited for a new block."); }
                Layout();
                VerifyGap(window.Trace, 27);
                window.Advance(16_000_000, progressive: true);
                Layout();
                _ = ReadColumn(window.Trace, 27);
                if (!retained.SequenceEqual(ReadColumn(window.Trace, 8)))
                { throw new InvalidOperationException("Progressive reveal shifted existing source pixels."); }
                Click(window.HoldButton);
                Control held = window.Trace;
                for (int frame = 0; frame < 150; frame++) { window.Advance(16_000_000, progressive: true); }
                if (!ReferenceEquals(held, window.Trace) || window.BlockCount != 14)
                { throw new InvalidOperationException("Progressive wrap or eviction changed held geometry."); }
                Click(window.HoldButton);
                if (window.LiveFrontierNs != 2_632_000_000 || ReferenceEquals(held, window.Trace))
                { throw new InvalidOperationException("Return failed to join the progressive frontier."); }
                Layout();
                VerifyGap(window.Trace, 340);
                _ = ReadColumn(window.Trace, 250);
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: sub-block progressive pixels, buffered availability, fixed columns, wrap, hold and return");
    }

    private static void VerifyShapes(WaveformDemoWindow window)
    {
        for (int step = 0; step < 11; step++) { Click(window.StepButton); }
        window.UpdateLayout();
        byte[] triangle = ReadColumn(window.Trace, 8);
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
        byte[] pulse = ReadColumn(window.Trace, 8);
        if (triangle.SequenceEqual(pulse)) { throw new InvalidOperationException("Different synthetic tables produced identical fixture pixels."); }
        Click(window.ResetButton);
        if (!window.UsesPulse) { throw new InvalidOperationException("Reset lost the selected shape."); }
        for (int step = 0; step < 11; step++) { Click(window.StepButton); }
        window.UpdateLayout();
        if (!pulse.SequenceEqual(ReadColumn(window.Trace, 8)))
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
        if (!delivered || window.SimulationTimeNs <= 0 || window.SimulationTimeNs > 50_000_000)
        { throw new InvalidOperationException("Native timer did not deliver one simulation step."); }
        for (int tick = 0; tick < 150; tick++) { window.Pulse(first); }
        long beforeHold = window.SimulationTimeNs;
        Click(window.HoldButton);
        Control held = window.Trace;
        window.Pulse(first);
        if (window.SimulationTimeNs != beforeHold + 16_000_000 || !ReferenceEquals(held, window.Trace))
        { throw new InvalidOperationException("Automatic generation altered held geometry."); }
        Click(window.RunButton);
        window.Pulse(first);
        if (window.ActiveTimer is not null || first.IsEnabled || !window.StepButton.IsEnabled ||
            window.SimulationTimeNs != beforeHold + 16_000_000)
        { throw new InvalidOperationException("Pause did not fence its old timer."); }
        Click(window.RunButton);
        var second = window.ActiveTimer;
        window.Pulse(first);
        if (ReferenceEquals(first, second) || window.SimulationTimeNs != beforeHold + 16_000_000)
        { throw new InvalidOperationException("Resume accepted a stale callback."); }
        window.Pulse(second);
        if (window.SimulationTimeNs != beforeHold + 32_000_000) { throw new InvalidOperationException("Resume did not take one fixed step."); }
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
        using RenderTargetBitmap image = new(new PixelSize((int)trace.Bounds.Width, (int)trace.Height), new Vector(96, 96));
        image.Render(trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        byte[] column = new byte[(int)trace.Height * 4];
        for (int y = 0; y < (int)trace.Height; y++)
        { Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, column, y * 4, 4); }
        if (!requireTrace) { return column; }
        if (!column.Where((_, index) => index % 4 == 1).Any(value => value > 100))
        { throw new InvalidOperationException("Sweep stability evidence column has no trace."); }
        for (int channel = 0; channel < (int)trace.Height / 120; channel++)
        {
            int crossings = Enumerable.Range(channel * 120, 120).Count(y => column[y * 4 + (channel is 3 or 5 or 6 ? 2 : 1)] > 100);
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
            for (int step = 0; step < 10; step++) { Click(window.StepButton); }
            if (window.BlockCount != 0) { throw new InvalidOperationException("Pleth processing delay was bypassed."); }
            Click(window.StepButton);
            if (window.BlockCount != 1 || window.ShapeButton.IsVisible)
            { throw new InvalidOperationException("Event source did not produce its first shared block."); }
            Click(window.StepButton);
            Click(window.StepButton);
            VerifyMechanicalPulsePixels(window);
            VerifyMechanicalPulsePixels(window, 700);
            Click(window.HoldButton);
            Control held = window.Trace;
            for (int step = 0; step < 20; step++) { Click(window.StepButton); }
            if (!ReferenceEquals(held, window.Trace) || window.BlockCount != 23)
            { throw new InvalidOperationException("Continuous event output lost held view or bounded retention."); }
            Click(window.HoldButton);
            window.UpdateLayout();
            VerifyCapnogramPixels(window);
            VerifyCapnogramPixels(window, 700);
            VerifyCvpPixels(window);
            VerifyCvpPixels(window, 700);
            DemoSweepResizeSmokeChecks.VerifyFixedSpeed(window);
            window.UpdateLayout();
            VerifyPixels(window, true, resp: true);
            Click(window.ResetButton);
            Click(window.ShapeButton);
            if (window.SimulationTimeNs != 0 || window.UsesPulse || window.BlockCount != 0)
            { throw new InvalidOperationException("Event source reset or hidden shape command failed."); }
            window.UpdateLayout();
            VerifyPixels(window, false, resp: true);
        }
        finally { window.Close(); }
        Console.WriteLine("ok: event-driven ECG/Resp/Pleth/ABP/CO2/PA/CVP native pixels, shared delay, held generation and reset");
    }

    private static void VerifyMechanicalPulsePixels(WaveformDemoWindow window, int width = 1000)
    {
        Control trace = window.Trace;
        trace.Measure(new Size(width, 840));
        trace.Arrange(new Rect(0, 0, width, 840));
        if (window.SimulationTimeNs != 2_600_000_000 || window.LiveFrontierNs != 600_000_000 || trace.Height != 840)
        { throw new InvalidOperationException("Seven-channel display did not wait for the shared acquired frontier."); }
        var blocks = PhysiologyDemoSource.Create().AdvanceTo(2_600_000_000, 650, 13, 100)
            .Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
        using RenderTargetBitmap image = new(new PixelSize(width, 840), new Vector(96, 96));
        image.Render(trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        // Use explicit row bindings: wire UUID order is ECG, Pleth, Resp, ABP, CO2, PA, CVP.
        foreach (var (row, time) in new[] { (0, 196_000_000L), (1, 416_000_000L), (2, 312_000_000L), (2, 416_000_000L), (3, 312_000_000L), (3, 424_000_000L), (4, 310_000_000L), (5, 272_000_000L), (5, 400_000_000L) })
        {
            var block = blocks.Single(item => time >= item.StartSimTimeNs && time < item.StartSimTimeNs + 200_000_000);
            var plane = block.Planes.Single(item => item.ChannelId == PhysiologyDemoSource.ChannelId(row));
            int sample = (int)((time - block.StartSimTimeNs) * plane.SampleRateNumerator / 1_000_000_000 / plane.SampleRateDenominator);
            int value = plane.Samples[sample];
            int x = (int)Math.Round(time / (double)DemoSweepLayout.NanosecondsPerPixel);
            int y = row == 5 ? (time == 272_000_000 ? 685 : 648) : row == 4 ? 590 : row == 3 ? (time == 312_000_000 ? 420 : 395) : (int)Math.Round(row * 120 + 60 - value * 0.05);
            if (row == 3 && (value != (time == 312_000_000 ? 0 : 4000) || plane.ScaleNumerator != 1 ||
                plane.ScaleDenominator != 100 || plane.OffsetNumerator != 80 || plane.OffsetDenominator != 1))
            { throw new InvalidOperationException("Arterial wire baseline, pressure resolution or peak changed unexpectedly."); }
            if (row == 5 && (value != (time == 272_000_000 ? 0 : 1500) || plane.ScaleNumerator != 1 ||
                plane.ScaleDenominator != 100 || plane.OffsetNumerator != 10 || plane.OffsetDenominator != 1))
            { throw new InvalidOperationException("PA wire scale, baseline or independent arrival/peak changed."); }
            if (row == 2 && value != (time == 416_000_000 ? 1000 : 0))
            { throw new InvalidOperationException("Mechanical transit or pulse peak changed unexpectedly."); }
            bool Colored(int line)
            {
                int offset = line * buffer.RowBytes + x * 4;
                byte b = Marshal.ReadByte(buffer.Address, offset), g = Marshal.ReadByte(buffer.Address, offset + 1), r = Marshal.ReadByte(buffer.Address, offset + 2);
                return row == 5 ? r > 100 && b > 100 && g == 0 : row == 4 ? r > 100 && g > 100 && b > 100 : row == 3 ? r > 100 && g == 0 && b == 0 : g > 100 && (row == 0 ? b == 0 && r == 0 : row == 1 ? b == 0 && r > 100 : b > 100 && r == 0);
            }
            if (!Enumerable.Range(y - 2, 5).Any(Colored))
            { throw new InvalidOperationException("Native row pixels do not match the configured decoded physiology channel."); }
        }
    }

    private static void VerifyCapnogramPixels(WaveformDemoWindow window, int width = 1000)
    {
        if (window.SimulationTimeNs != 6_600_000_000 || window.LiveFrontierNs != 4_600_000_000)
        { throw new InvalidOperationException("CO2 display bypassed the shared processing delay."); }
        var source = PhysiologyDemoSource.Create();
        List<WaveformEnvelope> blocks = [];
        for (int step = 1; step <= 33; step++)
        { blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes))); }
        int Sample(int row, long time)
        {
            var block = blocks.Single(item => time >= item.StartSimTimeNs && time < item.StartSimTimeNs + 200_000_000);
            var plane = block.Planes.Single(item => item.ChannelId == PhysiologyDemoSource.ChannelId(row));
            if (row == 4 && (plane.ScaleNumerator != 1 || plane.ScaleDenominator != 100 || plane.OffsetNumerator != 0 ||
                plane.OffsetDenominator != 1 || plane.SampleRateNumerator != 100))
            { throw new InvalidOperationException("CO2 wire units or native rate changed."); }
            return plane.Samples[(int)((time - block.StartSimTimeNs) * plane.SampleRateNumerator / 1_000_000_000 / plane.SampleRateDenominator)];
        }
        if (Sample(4, 3_750_000_000) != 4000 || Sample(4, 3_800_000_000) is <= 0 or >= 4000 ||
            Sample(4, 3_950_000_000) != 0 || Sample(1, 3_752_000_000) > 1 || Sample(1, 3_800_000_000) <= 0)
        { throw new InvalidOperationException("Capnogram fall does not align with the shared next inspiration."); }
        Control trace = window.Trace;
        trace.Measure(new Size(width, 840));
        trace.Arrange(new Rect(0, 0, width, 840));
        using RenderTargetBitmap image = new(new PixelSize(width, 840), new Vector(96, 96));
        image.Render(trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        foreach (long time in new[] { 3_000_000_000L, 3_750_000_000, 3_800_000_000, 3_950_000_000, 4_250_000_000 })
        {
            int raw = Sample(4, time);
            int x = (int)Math.Round(time / (double)DemoSweepLayout.NanosecondsPerPixel);
            int y = (int)Math.Round(590 - raw / 100.0 * 1.25);
            if (!Enumerable.Range(y - 2, 5).Any(line => Enumerable.Range(x - 1, 3).Any(column =>
            {
                int offset = line * buffer.RowBytes + column * 4;
                return Marshal.ReadByte(buffer.Address, offset) > 100 && Marshal.ReadByte(buffer.Address, offset + 1) > 100 &&
                    Marshal.ReadByte(buffer.Address, offset + 2) > 100;
            })))
            { throw new InvalidOperationException("Native CO2 plateau, inspiratory fall or baseline differs from acquired mmHg samples."); }
        }
    }

    private static void VerifyCvpPixels(WaveformDemoWindow window, int width = 1000)
    {
        var source = PhysiologyDemoSource.Create();
        List<WaveformEnvelope> blocks = [];
        for (int step = 1; step <= 33; step++)
        { blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes))); }
        Control trace = window.Trace;
        trace.Measure(new Size(width, 840));
        trace.Arrange(new Rect(0, 0, width, 840));
        using RenderTargetBitmap image = new(new PixelSize(width, 840), new Vector(96, 96));
        image.Render(trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        // Visible native ticks in x, v, a, c, y respectively, outside the erase gap.
        foreach (var (time, positive) in new[] { (2_816_000_000L, false), (2_952_000_000L, true),
            (3_344_000_000L, true), (3_480_000_000L, true), (3_920_000_000L, false) })
        {
            var block = blocks.Single(item => time >= item.StartSimTimeNs && time < item.StartSimTimeNs + 200_000_000);
            var plane = block.Planes.Single(item => item.ChannelId == PhysiologyDemoSource.ChannelId(6));
            if (plane.ScaleNumerator != 1 || plane.ScaleDenominator != 100 || plane.OffsetNumerator != 600 ||
                plane.OffsetDenominator != 100 || plane.SampleRateNumerator != 125 || plane.SampleRateDenominator != 1)
            { throw new InvalidOperationException("CVP lost centi-mmHg baseline or native pressure rate."); }
            int raw = plane.Samples[(int)((time - block.StartSimTimeNs) / 8_000_000)];
            if (positive ? raw <= 0 : raw >= 0)
            { throw new InvalidOperationException("CVP cardiac waves or signed descents disappeared under respiratory pressure."); }
            int x = (int)Math.Round(time / (double)DemoSweepLayout.NanosecondsPerPixel);
            // Six mmHg baseline is y=775 on the declared -5..15 mmHg axis;
            // each acquired centi-mmHg moves five hundredths of a logical pixel.
            int y = (int)Math.Round(775 - raw * 0.05);
            if (!Enumerable.Range(y - 2, 5).Any(line => Enumerable.Range(x - 1, 3).Any(column =>
            {
                int offset = line * buffer.RowBytes + column * 4;
                return Marshal.ReadByte(buffer.Address, offset) == 0 && Marshal.ReadByte(buffer.Address, offset + 1) > 50 &&
                    Marshal.ReadByte(buffer.Address, offset + 2) > 100;
            })))
            { throw new InvalidOperationException($"Native CVP pixels lost signed pressure, baseline or resized source time: width={width}, time={time}, raw={raw}, x={x}, y={y}."); }
        }
    }

    private static void VerifyPixels(WaveformDemoWindow window, bool populated, bool resp = false)
    {
        Control trace = window.Trace;
        using RenderTargetBitmap image = new(new PixelSize((int)trace.Bounds.Width, (int)trace.Height), new Vector(96, 96));
        image.Render(trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        int green = 0;
        int cyan = 0;
        int pleth = 0;
        int arterial = 0;
        int co2 = 0;
        int pa = 0;
        int cvp = 0;
        for (int y = 0; y < (int)trace.Height; y++)
        {
            for (int x = 0; x < image.PixelSize.Width; x++)
            {
                int offset = y * buffer.RowBytes + x * 4;
                byte blue = Marshal.ReadByte(buffer.Address, offset);
                byte value = Marshal.ReadByte(buffer.Address, offset + 1);
                byte red = Marshal.ReadByte(buffer.Address, offset + 2);
                if (y < 120 && value > 100 && blue == 0) { green++; }
                if (y >= 120 && y < 240 && value > 100 && (resp ? red > 100 && blue == 0 : blue > 100)) { cyan++; }
                if (y >= 240 && y < 360 && value > 100 && blue > 100 && red == 0) { pleth++; }
                if (y >= 360 && y < 480 && red > 100 && value == 0 && blue == 0) { arterial++; }
                if (y >= 480 && y < 600 && red > 100 && value > 100 && blue > 100) { co2++; }
                if (y >= 600 && y < 720 && red > 100 && blue > 100 && value == 0) { pa++; }
                if (y >= 720 && red > 100 && value > 50 && blue == 0) { cvp++; }
            }
        }
        if (populated ? green < 100 || cyan < 100 || (resp && (pleth < 100 || arterial < 100 || co2 < 100 || pa < 100 || cvp < 100)) : green != 0 || cyan != 0 || pleth != 0 || arterial != 0 || co2 != 0 || pa != 0 || cvp != 0)
        { throw new InvalidOperationException("Generated native trace pixels do not match available source planes."); }
        if (populated)
        {
            Directory.CreateDirectory("artifacts");
            image.Save(Path.Combine("artifacts", resp ? "desktop-physiology-demo.png" : "desktop-waveform-demo.png"), PngBitmapEncoderOptions.Default);
        }
    }
}
