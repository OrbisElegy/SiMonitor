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

internal static class MechanicalUncouplingSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(physiology: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (bool enabled in new[] { false, true })
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.VentricularMechanicalInput.IsChecked = enabled;
                window.RespCardiacArtifactInput.Text = "160";
                Click(window.ApplyBreathButton);
                window.Pulse(oldTimer);
                var config = window.BreathConfiguration;
                if (config.VentricularMechanicalEnabled != enabled || window.SimulationTimeNs != 0 || window.BlockCount != 0 ||
                    window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Mechanical selection did not atomically restart the source."); }
                for (int step = 0; step < 30; step++) { Click(window.StepButton); }
                var actual = Decode(config);
                var coupled = Decode(config with { VentricularMechanicalEnabled = true });
                foreach (int row in new[] { 0, 4 })
                {
                    if (!Samples(actual, row).SequenceEqual(Samples(coupled, row)))
                    { throw new InvalidOperationException("Mechanical uncoupling changed ECG or expired gas."); }
                }
                foreach (int row in new[] { 2, 3, 5 })
                {
                    var samples = Samples(actual, row);
                    if (enabled ? !samples.Any(value => value != 0) : samples.Any(value => value != 0))
                    { throw new InvalidOperationException("Pulse excursions did not follow mechanical availability."); }
                }
                if (!enabled)
                {
                    var atrial = Decode(config with { CardiacActivity = CardiacActivity.AtrialOnly });
                    if (!Samples(actual, 6).SequenceEqual(Samples(atrial, 6)) || !Samples(actual, 6).Any(value => value != 0))
                    { throw new InvalidOperationException("CVP did not preserve atrial and respiratory components alone."); }
                    var clean = Decode(config with { RespCardiacArtifactCounts = 0 });
                    if (!Samples(actual, 1).SequenceEqual(Samples(clean, 1)))
                    { throw new InvalidOperationException("Resp retained artifact without ventricular mechanics."); }
                }
                VerifyPixels(window, actual);
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace;
                var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                window.VentricularMechanicalInput.IsChecked = null;
                Click(window.ApplyBreathButton);
                if (window.BreathConfiguration != config || !ReferenceEquals(held, window.Trace) ||
                    !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before ||
                    string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
                { throw new InvalidOperationException("Unspecified mechanical input changed accepted state."); }
                Click(window.ResetButton);
                if (window.VentricularMechanicalInput.IsChecked != enabled || window.BreathConfiguration != config ||
                    window.BlockCount != 0 || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Reset lost accepted mechanical selection."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: mechanical uncoupling retains ECG/gas, removes pulses/artifact, preserves CVP a/respiration and native pixels/lifecycle");
    }

    private static WaveformEnvelope[] Decode(PhysiologyDemoConfiguration config)
    {
        var source = PhysiologyDemoSource.Create(config);
        return Enumerable.Range(1, 30).SelectMany(step => source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
            .Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
    }
    private static short[] Samples(WaveformEnvelope[] blocks, int row) => blocks.SelectMany(block =>
        block.Planes.Single(plane => plane.ChannelId == PhysiologyDemoSource.ChannelId(row)).Samples).ToArray();

    private static void VerifyPixels(WaveformDemoWindow window, WaveformEnvelope[] blocks)
    {
        window.Trace.Measure(new Size(1044, 840));
        window.Trace.Arrange(new Rect(0, 0, 1044, 840));
        using RenderTargetBitmap image = new(new PixelSize(1044, 840), new Vector(96, 96));
        image.Render(window.Trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        foreach (int row in new[] { 0, 2 })
        {
            var samples = Samples(blocks, row);
            int peak = row == 0 ? Enumerable.Range(40, 20).MaxBy(i => samples[i]) : Enumerable.Range(40, 64).MaxBy(i => samples[i]);
            int x = (int)Math.Round(row == 0 ? peak / 2.0 : peak);
            int y = (int)Math.Round(row * 120 + 60 - samples[peak] * 0.05);
            if (!Enumerable.Range(y - 1, 3).Any(line => Enumerable.Range(x - 1, 3).Any(column =>
                Marshal.ReadByte(buffer.Address + line * buffer.RowBytes + column * 4 + 1) > 100 &&
                (row == 0 || Marshal.ReadByte(buffer.Address + line * buffer.RowBytes + column * 4) > 100))))
            { throw new InvalidOperationException("ECG or Pleth source pixels missing after mechanical configuration."); }
        }
    }
}
