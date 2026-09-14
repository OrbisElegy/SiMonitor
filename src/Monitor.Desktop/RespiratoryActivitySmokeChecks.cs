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

internal static class RespiratoryActivitySmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(physiology: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (var activity in new[] { RespiratoryActivity.EffortOnly, RespiratoryActivity.Absent, RespiratoryActivity.Breathing })
            {
                for (int step = 0; step < 12; step++) { Click(window.StepButton); }
                Click(window.HoldButton);
                Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.BreathPeriodInput.Text = "4000";
                window.InspirationInput.Text = "2000";
                window.InspiratoryPauseInput.Text = window.ExpiratoryPauseInput.Text = "800";
                window.RespAmplitudeInput.Text = "800";
                window.Co2BaselineInput.Text = "5";
                window.RespCardiacArtifactInput.Text = activity == RespiratoryActivity.Absent ? "160" : "0";
                window.RespiratoryActivityInput.SelectedIndex = (int)activity;
                Click(window.ApplyBreathButton);
                window.Pulse(oldTimer);
                var config = window.BreathConfiguration;
                if (config.RespiratoryActivity != activity || window.SimulationTimeNs != 0 || window.BlockCount != 0 ||
                    window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Activity change did not restart the source and fence prior work."); }
                var source = PhysiologyDemoSource.Create(config);
                List<WaveformEnvelope> blocks = [];
                for (int step = 1; step <= 32; step++)
                {
                    Click(window.StepButton);
                    blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
                }
                var co2 = blocks.SelectMany(block => block.Planes.Single(plane => plane.ChannelId == PhysiologyDemoSource.ChannelId(4)).Samples);
                if (activity == RespiratoryActivity.Breathing ? !co2.Any(value => value > 0) : co2.Any(value => value != 0))
                { throw new InvalidOperationException("Native CO2 does not follow source respiratory activity."); }
                VerifyPixels(window, blocks, activity);
                Click(window.HoldButton);
                Click(window.RunButton);
                var held = window.Trace;
                var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                window.RespiratoryActivityInput.SelectedIndex = -1;
                Click(window.ApplyBreathButton);
                if (window.BreathConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) ||
                    window.SimulationTimeNs != before || string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
                { throw new InvalidOperationException("Invalid respiratory activity changed accepted source or view."); }
                Click(window.ResetButton);
                if (window.BreathConfiguration != config || window.RespiratoryActivityInput.SelectedIndex != (int)activity ||
                    window.ActiveTimer is not null || window.BlockCount != 0 || window.IsHeld)
                { throw new InvalidOperationException("Reset lost accepted activity or retained draft state."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: native respiratory activity separates chest effort, gas cycles and cardiac artifact with atomic lifecycle");
    }

    private static void VerifyPixels(WaveformDemoWindow window, List<WaveformEnvelope> blocks, RespiratoryActivity activity)
    {
        var trace = window.Trace;
        trace.Measure(new Size(1000, 840));
        trace.Arrange(new Rect(0, 0, 1000, 840));
        using RenderTargetBitmap image = new(new PixelSize(1000, 840), new Vector(96, 96));
        image.Render(trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        List<(int Row, long Time, int Raw)> points = [(4, 4_000_000_000, activity == RespiratoryActivity.Breathing ? 3500 : 0)];
        if (activity == RespiratoryActivity.Absent)
        { points.Add((1, 440_000_000, 160)); points.Add((1, 840_000_000, -160)); }
        else { points.Add((1, 1_600_000_000, 800)); }
        foreach (var (row, time, expected) in points)
        {
            var plane = blocks.Single(block => time >= block.StartSimTimeNs && time < block.StartSimTimeNs + 200_000_000)
                .Planes.Single(plane => plane.ChannelId == PhysiologyDemoSource.ChannelId(row));
            int raw = plane.Samples[(int)(time % 200_000_000 * plane.SampleRateNumerator / 1_000_000_000)];
            if (raw != expected) { throw new InvalidOperationException("Activity landmark does not match decoded native data."); }
            int x = (int)Math.Round(time / 8_000_000.0);
            int y = (int)Math.Round(row == 1 ? 180 - raw * 0.05 : 590 - (raw / 100.0 + 5) * 1.25);
            bool visible = Enumerable.Range(y - 2, 5).Any(line => Enumerable.Range(x - 1, 3).Any(column =>
            {
                int offset = line * buffer.RowBytes + column * 4;
                byte b = Marshal.ReadByte(buffer.Address, offset), g = Marshal.ReadByte(buffer.Address, offset + 1), r = Marshal.ReadByte(buffer.Address, offset + 2);
                return r > 100 && g > 100 && (row == 4 ? b > 100 : b == 0);
            }));
            if (!visible) { throw new InvalidOperationException("Source activity is not visible in native Resp/CO2 pixels."); }
        }
    }
}
