// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Monitor.Simulation.Acquisition;

namespace Monitor.Desktop;

internal static class MechanicalTransitionSmokeChecks
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
            window.VentricularMechanicalInput.IsChecked = false;
            window.MechanicalAfterCyclesInput.Text = "1";
            Click(window.ApplyBreathButton);
            window.Pulse(oldTimer);
            var config = window.BreathConfiguration;
            if (config.MechanicalAfterCycles != 1 || config.VentricularMechanicalEnabled || window.BlockCount != 0 ||
                window.SimulationTimeNs != 0 || window.IsHeld || window.ActiveTimer is not null)
            { throw new InvalidOperationException("Scheduled mechanical cutoff did not atomically restart."); }
            for (int step = 1; step <= 30; step++)
            {
                Click(window.StepButton);
                if (window.SimulationTimeNs != step * 200_000_000L)
                { throw new InvalidOperationException("Mechanical cutoff reset the running source clock."); }
            }
            var actual = MechanicalUncouplingSmokeChecks.Decode(config);
            var normal = MechanicalUncouplingSmokeChecks.Decode(config with { VentricularMechanicalEnabled = true, MechanicalAfterCycles = null });
            foreach (int row in new[] { 0, 1, 4 })
            {
                if (!MechanicalUncouplingSmokeChecks.Samples(actual, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
                { throw new InvalidOperationException("Mechanical cutoff changed ECG or respiration."); }
            }
            foreach (int row in new[] { 2, 3, 5 })
            {
                var pulse = MechanicalUncouplingSmokeChecks.Samples(actual, row);
                if (!pulse.Take(125).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row).Take(125)) ||
                    !pulse.Skip(100).Take(4).Any(value => value > 0) || pulse.Skip(125).Any(value => value != 0))
                { throw new InvalidOperationException("Mechanical cutoff lost its first pulse tail or retained later pulses."); }
            }
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, actual);
            VerifyPressureTail(window, actual);
            Click(window.HoldButton); Click(window.RunButton);
            var held = window.Trace;
            var timer = window.ActiveTimer;
            long before = window.SimulationTimeNs;
            foreach (string invalid in new[] { "0", "101", "-1", "1.5", "bad" })
            {
                window.MechanicalAfterCyclesInput.Text = invalid;
                Click(window.ApplyBreathButton);
                VerifyUnchanged();
            }
            window.MechanicalAfterCyclesInput.Text = "1";
            window.VentricularMechanicalInput.IsChecked = true;
            Click(window.ApplyBreathButton);
            VerifyUnchanged();
            Click(window.ResetButton);
            if (window.MechanicalAfterCyclesInput.Text != "1" || window.VentricularMechanicalInput.IsChecked != false || window.BlockCount != 0)
            { throw new InvalidOperationException("Reset lost the accepted mechanical schedule."); }
            window.MechanicalAfterCyclesInput.Text = "";
            Click(window.ApplyBreathButton);
            if (window.BreathConfiguration.MechanicalAfterCycles is not null ||
                MechanicalUncouplingSmokeChecks.Samples(MechanicalUncouplingSmokeChecks.Decode(window.BreathConfiguration), 2).Any(value => value != 0))
            { throw new InvalidOperationException("Clearing the count did not restore immediate mechanical suppression."); }

            void VerifyUnchanged()
            {
                if (window.BreathConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) ||
                    window.SimulationTimeNs != before || string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
                { throw new InvalidOperationException("Invalid mechanical schedule changed source/view/timer."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: scheduled mechanical cutoff keeps ECG and sweep clocks, completes pulse tails, preserves pressure pixels and validates/reset inputs");
    }

    private static void VerifyPressureTail(WaveformDemoWindow window, WaveformEnvelope[] blocks)
    {
        window.Trace.Measure(new Size(1044, 840));
        window.Trace.Arrange(new Rect(0, 0, 1044, 840));
        using RenderTargetBitmap image = new(new PixelSize(1044, 840), new Vector(96, 96));
        image.Render(window.Trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        var samples = MechanicalUncouplingSmokeChecks.Samples(blocks, 5);
        foreach (int index in new[] { 105, 150 })
        {
            int y = (int)Math.Round(710 - (10 + samples[index] / 100.0) * 2.5);
            if (!Enumerable.Range(y - 1, 3).Any(line => Enumerable.Range(index - 1, 3).Any(column =>
                Marshal.ReadByte(buffer.Address + line * buffer.RowBytes + column * 4) > 100 &&
                Marshal.ReadByte(buffer.Address + line * buffer.RowBytes + column * 4 + 2) > 100)))
            { throw new InvalidOperationException("PA tail or later baseline pixel missing after cutoff."); }
        }
    }
}
