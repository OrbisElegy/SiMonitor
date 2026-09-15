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

internal static class ConductionRatioSmokeChecks
{
    internal static void Verify()
    {
        foreach (bool projected in new[] { false, true })
        {
            WaveformDemoWindow window = new(physiology: !projected, projected: projected);
            window.Show();
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            try
            {
                foreach (int ratio in new[] { 2, 3, 4, 1 })
                {
                    for (int step = 0; step < 12; step++) { Click(window.StepButton); }
                    Click(window.HoldButton);
                    Click(window.RunButton);
                    var oldTimer = window.ActiveTimer;
                    window.ConductionInput.SelectedIndex = ratio - 1;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    window.Pulse(oldTimer);
                    if ((projected ? window.EcgConfiguration.VentricularConductionRatio : window.BreathConfiguration.VentricularConductionRatio) != ratio ||
                        window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                    { throw new InvalidOperationException("Conduction input did not atomically restart the source."); }
                    for (int step = 0; step < 30; step++) { Click(window.StepButton); }
                    Func<long, IReadOnlyList<byte[]>> advance;
                    if (projected)
                    {
                        var source = ProjectedEcgDemoSource.Create(window.EcgConfiguration);
                        advance = time => source.AdvanceTo(time, 50, 1, 100);
                    }
                    else
                    {
                        var source = PhysiologyDemoSource.Create(window.BreathConfiguration);
                        advance = time => source.AdvanceTo(time, 50, 1, 100);
                    }
                    var blocks = Enumerable.Range(1, 30).SelectMany(step => advance(step * 200_000_000L))
                        .Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                    Guid id = projected ? ProjectedEcgDemoSource.ChannelId(EcgLead.II) : PhysiologyDemoSource.ChannelId(0);
                    short[] samples = blocks.SelectMany(block => block.Planes.Single(plane => plane.ChannelId == id).Samples).ToArray();
                    if (!samples.Skip(200).Take(25).Any(value => value > 0) ||
                        (ratio == 1 ? !samples.Skip(240).Take(20).Any(value => value > 500) : samples.Skip(240).Take(20).Any(value => value != 0)))
                    { throw new InvalidOperationException("Native P/QRS data did not follow the selected conduction ratio."); }
                    VerifyPixels(window, projected, samples, ratio);
                    Click(window.HoldButton);
                    Click(window.RunButton);
                    var held = window.Trace;
                    var timer = window.ActiveTimer;
                    long before = window.SimulationTimeNs;
                    window.ConductionInput.SelectedIndex = -1;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    if (!ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before)
                    { throw new InvalidOperationException("Rejected conduction ratio changed source or held view."); }
                    Click(window.ResetButton);
                    if (window.ConductionInput.SelectedIndex != ratio - 1 || window.BlockCount != 0 || window.ActiveTimer is not null)
                    { throw new InvalidOperationException("Reset did not retain accepted conduction ratio."); }
                }
                if (projected)
                {
                    window.QtMethod.SelectedIndex = 1;
                    window.HeartRateInput.Text = "75";
                    window.QtcInput.Text = "400";
                    window.ConductionInput.SelectedIndex = 1;
                    Click(window.ApplyEcgButton);
                    var timing = window.EcgConfiguration.ResolveTiming();
                    if (timing.RrIntervalNs != 1_600_000_000 || timing.QtIntervalNs !=
                        new EcgQtCorrection(EcgQtCorrection.Bazett, 400_000_000, 1_600_000_000).ResolveQtIntervalNs())
                    { throw new InvalidOperationException("QTc did not use conducted ventricular RR."); }
                }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: native physiology and12-lead conduction controls preserve P waves, drop QRS/pulses and use ventricular RR for QTc");
    }

    private static void VerifyPixels(WaveformDemoWindow window, bool projected, short[] samples, int ratio)
    {
        int height = projected ? 1920 : 840;
        window.Trace.Measure(new Size(1044, height));
        window.Trace.Arrange(new Rect(0, 0, 1044, height));
        using RenderTargetBitmap image = new(new PixelSize(1044, height), new Vector(96, 96));
        image.Render(window.Trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        int peak = Enumerable.Range(40, 20).MaxBy(index => samples[index]);
        foreach (int index in new[] { peak, peak + ratio * 200, 250 })
        {
            int x = (int)Math.Round((projected ? 85 : 0) + index * 4_000_000L / 8_000_000.0);
            int y = (int)Math.Round((projected ? 240 : 60) - samples[index] * (projected ? 0.04 : 0.05));
            if (!Enumerable.Range(y - 2, 5).Any(line => Enumerable.Range(x - 1, 3).Any(column =>
                Marshal.ReadByte(buffer.Address + line * buffer.RowBytes + column * 4 + 1) > 100)))
            { throw new InvalidOperationException($"Missing conduction pixel: projected={projected}, ratio={ratio}, index={index}, raw={samples[index]}, x={x}, y={y}."); }
        }
    }
}
