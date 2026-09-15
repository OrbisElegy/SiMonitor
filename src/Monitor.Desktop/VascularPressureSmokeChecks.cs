// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Monitor.Simulation.Acquisition;

namespace Monitor.Desktop;

internal static class VascularPressureSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(physiology: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            if (!window.BreathConfiguration.UseVascularReservoir || window.VascularReservoirInput.IsChecked != true ||
                new PhysiologyDemoConfiguration(3750, 1875, 1000).UseVascularReservoir)
            { throw new InvalidOperationException("The demo must enable RC pressure while old explicit configurations retain template behavior."); }
            foreach (bool scheduled in new[] { false, true })
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.VentricularMechanicalInput.IsChecked = false;
                window.MechanicalAfterCyclesInput.Text = scheduled ? "1" : "";
                window.MechanicalDurationCyclesInput.Text = scheduled ? "2" : "";
                Click(window.ApplyBreathButton);
                window.Pulse(oldTimer);
                var config = window.BreathConfiguration;
                if (!config.UseVascularReservoir || window.BlockCount != 0 || window.SimulationTimeNs != 0 ||
                    window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("RC configuration did not atomically restart and fence the old timer."); }
                for (int step = 1; step <= 30; step++)
                {
                    Click(window.StepButton);
                    if (window.SimulationTimeNs != step * 200_000_000L)
                    { throw new InvalidOperationException("RC pressure changed the shared source clock."); }
                }
                var blocks = MechanicalUncouplingSmokeChecks.Decode(config);
                var legacy = MechanicalUncouplingSmokeChecks.Decode(config with { UseVascularReservoir = false });
                foreach (int row in new[] { 0, 1, 2, 4, 6 })
                {
                    if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(legacy, row)))
                    { throw new InvalidOperationException("RC selection changed ECG, breathing, Pleth, gas or CVP samples."); }
                }
                var normal = MechanicalUncouplingSmokeChecks.Decode(config with
                { VentricularMechanicalEnabled = true, MechanicalAfterCycles = null, MechanicalDurationCycles = null });
                foreach (int row in new[] { 3, 5 })
                {
                    var samples = MechanicalUncouplingSmokeChecks.Samples(blocks, row);
                    int initial = row == 3 ? 8000 : 1000, asymptote = row == 3 ? 1000 : 500;
                    if (samples[0] != initial || samples.Any(value => value < asymptote) ||
                        blocks.Select(block => block.Planes.Single(plane => plane.ChannelId == PhysiologyDemoSource.ChannelId(row)))
                            .Any(plane => plane.ScaleNumerator != 1 || plane.ScaleDenominator != 100 ||
                                plane.OffsetNumerator != 0 || plane.OffsetDenominator != 1 ||
                                plane.SampleRateNumerator != 125 || plane.SampleRateDenominator != 1))
                    { throw new InvalidOperationException("RC source lost initial/residual pressure or absolute centi-mmHg wire metadata."); }
                    if (!scheduled)
                    {
                        if (samples[^1] >= samples[0] || Enumerable.Range(1, samples.Length - 1).Any(index => samples[index] > samples[index - 1]))
                        { throw new InvalidOperationException("Pressure failed to decay independently without a mechanical event."); }
                    }
                    else
                    {
                        int firstEnd = row == 3 ? 70 : 60, resume = row == 3 ? 340 : 335, resumedEnd = row == 3 ? 370 : 360;
                        var reference = MechanicalUncouplingSmokeChecks.Samples(normal, row);
                        if (!samples.Take(firstEnd + 1).SequenceEqual(reference.Take(firstEnd + 1)) ||
                            Enumerable.Range(firstEnd + 1, resume - firstEnd).Any(index => samples[index] > samples[index - 1]) ||
                            samples[resume] >= samples[firstEnd] || samples[resume + 5] <= samples[resume] ||
                            Math.Abs(samples[resume] - samples[resume - 1]) > 20 ||
                            samples[resumedEnd] >= reference[resumedEnd] || samples[resumedEnd + 100] <= samples[resumedEnd])
                        { throw new InvalidOperationException("RC pressure lost runoff, continuous delayed resumption or cumulative refill."); }
                    }
                }
                VerifyPressurePixels(window, blocks, scheduled ? [100, 300, 350, 370, 470] : [100, 300, 470]);
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace;
                var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                string? mode = window.VascularPressureModeStatus.Text;
                window.VascularReservoirInput.IsChecked = null;
                Click(window.ApplyBreathButton);
                VerifyUnchanged();
                window.VascularReservoirInput.IsChecked = false;
                window.MechanicalDurationCyclesInput.Text = "0";
                Click(window.ApplyBreathButton);
                VerifyUnchanged();
                Click(window.ResetButton);
                if (window.VascularReservoirInput.IsChecked != true || window.BreathConfiguration != config ||
                    window.BlockCount != 0 || window.ActiveTimer is not null || window.VascularPressureModeStatus.Text != mode)
                { throw new InvalidOperationException("Reset lost the accepted RC model or retained an invalid pending selection."); }

                void VerifyUnchanged()
                {
                    if (window.BreathConfiguration != config || !ReferenceEquals(held, window.Trace) ||
                        !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before ||
                        string.IsNullOrEmpty(window.BreathConfigurationStatus.Text) || window.VascularPressureModeStatus.Text != mode)
                    { throw new InvalidOperationException("Invalid pressure selection changed accepted source, view, timer or mode label."); }
                }
            }
            foreach (bool enabled in new[] { false, true })
            {
                window.VascularReservoirInput.IsChecked = enabled;
                Click(window.ApplyBreathButton);
                if (window.BreathConfiguration.UseVascularReservoir != enabled || window.SimulationTimeNs != 0 || window.BlockCount != 0 ||
                    !(window.VascularPressureModeStatus.Text?.Contains(enabled ? "已应用血管储压模型" : "已应用固定基线形态模板", StringComparison.Ordinal) ?? false))
                { throw new InvalidOperationException("Switching the source pressure model did not update the accepted configuration and mode label."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: default ABP/PA RC source decays without ejection, refills after resumption, preserves other channels, decoded pressure pixels and lifecycle");
    }

    private static void VerifyPressurePixels(WaveformDemoWindow window, WaveformEnvelope[] blocks, int[] sampleIndices)
    {
        window.Trace.Measure(new Size(1044, 840));
        window.Trace.Arrange(new Rect(0, 0, 1044, 840));
        using RenderTargetBitmap image = new(new PixelSize(1044, 840), new Vector(96, 96));
        image.Render(window.Trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        foreach (int row in new[] { 3, 5 })
        {
            var plane = blocks[0].Planes.Single(item => item.ChannelId == PhysiologyDemoSource.ChannelId(row));
            var samples = MechanicalUncouplingSmokeChecks.Samples(blocks, row);
            foreach (int index in sampleIndices)
            {
                double pressure = samples[index] * (double)plane.ScaleNumerator / plane.ScaleDenominator +
                    (double)plane.OffsetNumerator / plane.OffsetDenominator;
                int y = (int)Math.Round(row * 120 + 110 - pressure * (row == 3 ? 0.625 : 2.5));
                if (!Enumerable.Range(y - 1, 3).Any(line => Enumerable.Range(index - 1, 3).Any(column =>
                    Marshal.ReadByte(buffer.Address + line * buffer.RowBytes + column * 4 + 2) > 100 &&
                    Marshal.ReadByte(buffer.Address + line * buffer.RowBytes + column * 4 + 1) == 0 &&
                    (row == 3 ? Marshal.ReadByte(buffer.Address + line * buffer.RowBytes + column * 4) == 0 :
                        Marshal.ReadByte(buffer.Address + line * buffer.RowBytes + column * 4) > 100))))
                { throw new InvalidOperationException($"RC row {row} pixel at source index {index} does not match decoded pressure."); }
            }
        }
    }
}
