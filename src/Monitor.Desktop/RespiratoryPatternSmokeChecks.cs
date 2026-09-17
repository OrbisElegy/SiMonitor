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

internal static class RespiratoryPatternSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(physiology: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            window.BreathPeriodInput.Text = "1000";
            window.InspirationInput.Text = "400";
            window.RespiratoryPatternInput.SelectedIndex = 1;
            Click(window.ApplyBreathButton); window.Pulse(oldTimer);
            var config = window.BreathConfiguration;
            if (config.RespiratoryPattern != RespiratoryPattern.CheyneStokesIllustration || window.SimulationTimeNs != 0 || window.IsHeld || window.ActiveTimer is not null)
            { throw new InvalidOperationException("Pattern apply did not atomically restart."); }
            var source = PhysiologyDemoSource.Create(config);
            var blocks = new List<WaveformEnvelope>();
            // The shared envelope waits for the frozen 2s Pleth/CO2 latency.
            for (int i = 1; i <= 72; i++)
            {
                Click(window.StepButton);
                blocks.AddRange(source.AdvanceTo(i * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
                if (i == 42) { VerifyPixels(window, blocks, early: true); }
            }
            var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
            if (source.AdvanceTo(14_600_000_000, 50, 1, 100).Zip(restored.AdvanceTo(14_600_000_000, 50, 1, 100)).Any(p => !p.First.SequenceEqual(p.Second)))
            { throw new InvalidOperationException("Pattern recovery changed wire bytes."); }
            VerifyPixels(window, blocks);
            Click(window.HoldButton); Click(window.RunButton);
            var trace = window.Trace; var timer = window.ActiveTimer; long time = window.SimulationTimeNs;
            window.RespiratoryActivityInput.SelectedIndex = 1; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || window.Trace != trace || window.ActiveTimer != timer || window.SimulationTimeNs != time)
            { throw new InvalidOperationException("Conflicting activity changed pattern state."); }
            Click(window.ResetButton);
            if (window.RespiratoryPatternInput.SelectedIndex != 1 || window.RespiratoryActivityInput.SelectedIndex != 0)
            { throw new InvalidOperationException("Reset lost accepted breathing pattern."); }
            window.RespiratoryPatternInput.SelectedIndex = -1; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config) { throw new InvalidOperationException("Invalid pattern accepted."); }
            window.RespiratoryPatternInput.SelectedIndex = 0; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration.RespiratoryPattern != RespiratoryPattern.Regular)
            { throw new InvalidOperationException("Pattern did not clear."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: native crescendo/decrescendo depth, central pause, gas resumption, pixels and atomic pattern lifecycle");
    }
    private static void VerifyPixels(WaveformDemoWindow window, List<WaveformEnvelope> blocks, bool early = false)
    {
        var trace = window.Trace;
        trace.Measure(new Size(1000, 840)); trace.Arrange(new Rect(0, 0, 1000, 840));
        using RenderTargetBitmap image = new(new PixelSize(1000, 840), new Vector(96, 96));
        image.Render(trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock(); image.CopyPixels(buffer);
        (int, long, int)[] points = early ? [(1, 4_400_000_000, 1000), (1, 5_400_000_000, 800)] : [(1, 7_400_000_000, 400), (1, 8_400_000_000, 200), (1, 9_400_000_000, 0), (1, 11_400_000_000, 200), (4, 10_000_000_000, 0), (4, 11_900_000_000, -1)];
        foreach (var (row, time, expected) in points)
        {
            var plane = blocks.Single(b => time >= b.StartSimTimeNs && time < b.StartSimTimeNs + 200_000_000).Planes.Single(p => p.ChannelId == PhysiologyDemoSource.ChannelId(row));
            int raw = plane.Samples[(int)(time % 200_000_000 * plane.SampleRateNumerator / 1_000_000_000)];
            if (expected == -1 ? raw <= 0 : raw != expected) { throw new InvalidOperationException("Pattern sample landmark mismatch."); }
            int x = (int)Math.Round(time % 8_000_000_000 / 8_000_000.0);
            int y = (int)Math.Round(row == 1 ? 180 - raw * 0.05 : 590 - (raw * (double)plane.ScaleNumerator / plane.ScaleDenominator + (double)plane.OffsetNumerator / plane.OffsetDenominator) * 1.25);
            bool visible = Enumerable.Range(y - 2, 5).Any(line => Enumerable.Range(x - 1, 3).Any(column =>
            {
                int offset = line * buffer.RowBytes + column * 4;
                byte b = Marshal.ReadByte(buffer.Address, offset), g = Marshal.ReadByte(buffer.Address, offset + 1), r = Marshal.ReadByte(buffer.Address, offset + 2);
                return r > 100 && g > 100 && (row == 4 ? b > 100 : b == 0);
            }));
            if (!visible) { throw new InvalidOperationException($"Pattern landmark absent: row{row}, time{time}, raw{raw}, x{x}, y{y}."); }
        }
    }
}
