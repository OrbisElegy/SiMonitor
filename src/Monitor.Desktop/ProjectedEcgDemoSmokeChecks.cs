// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class ProjectedEcgDemoSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Layout()
        {
            window.Trace.Measure(new Size(1044, 720));
            window.Trace.Arrange(new Rect(0, 0, 1044, 720));
        }
        try
        {
            Click(window.StepButton);
            if (window.BlockCount != 0 || window.ShapeButton.IsVisible)
            { throw new InvalidOperationException("Projected demo bypassed latency or enabled unrelated shape controls."); }
            Click(window.StepButton);
            Click(window.StepButton);
            Layout();
            var wires = ProjectedEcgDemoSource.Create().AdvanceTo(600_000_000, 150, 3, 100);
            var block = WaveformEnvelopeCodec.Decode(wires[1]);
            using (RenderTargetBitmap image = new(new PixelSize(1044, 720), new Vector(96, 96)))
            {
                image.Render(window.Trace);
                using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
                using ILockedFramebuffer buffer = pixels.Lock();
                image.CopyPixels(buffer);
                for (int lead = 0; lead < 12; lead++)
                {
                    var plane = block.Planes.Single(item => item.ChannelId == ProjectedEcgDemoSource.ChannelId((EcgLead)lead));
                    int y = (int)Math.Round(lead * 60 + 30 - plane.Samples[0] * 0.015);
                    // x=144 maps to 200ms in a 1000px two-second plot after label gutter.
                    if (!Enumerable.Range(y - 2, 5).Any(row => Marshal.ReadByte(buffer.Address + row * buffer.RowBytes + 144 * 4 + 1) > 100))
                    { throw new InvalidOperationException("Rendered row does not match its decoded lead sample."); }
                    if (!Enumerable.Range(lead * 60, 60).Any(row => Enumerable.Range(2, 36).Any(x =>
                        Marshal.ReadByte(buffer.Address + row * buffer.RowBytes + x * 4 + 1) > 100)))
                    { throw new InvalidOperationException("Lead label disappeared from the persistent gutter."); }
                }
            }
            Click(window.HoldButton);
            Control held = window.Trace;
            for (int step = 0; step < 15; step++) { Click(window.StepButton); }
            if (!ReferenceEquals(held, window.Trace) || window.BlockCount != 11)
            { throw new InvalidOperationException("Projected background retention changed the held view."); }
            Click(window.HoldButton);
            Layout();
            using (RenderTargetBitmap image = new(new PixelSize(1044, 720), new Vector(96, 96)))
            {
                image.Render(window.Trace);
                Directory.CreateDirectory("artifacts");
                image.Save("artifacts/desktop-electrode-demo.png", PngBitmapEncoderOptions.Default);
            }
            Click(window.ResetButton);
            Click(window.RunButton);
            var timer = window.ActiveTimer;
            for (int frame = 0; frame < 30; frame++) { window.Pulse(timer); }
            if (window.LiveFrontierNs != 240_000_000 || window.Trace.Height != 720)
            { throw new InvalidOperationException("Projected mode lost sub-block pacing or twelve-row height."); }
            Click(window.RunButton);
            window.Pulse(timer);
            if (window.SimulationTimeNs != 480_000_000)
            { throw new InvalidOperationException("Projected pause accepted stale timer work."); }
            Click(window.ResetButton);
            if (window.BlockCount != 0 || window.IsHeld || window.LiveFrontierNs != 0)
            { throw new InvalidOperationException("Projected reset retained data or held state."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: twelve projected native rows match decoded leads, labels, delay, hold, pacing and reset");
    }
}
