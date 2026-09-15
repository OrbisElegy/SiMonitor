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

internal static class CardiacActivitySmokeChecks
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
                window.VascularReservoirInput.IsChecked = false;
                foreach (CardiacActivity activity in new[] { CardiacActivity.AtrialOnly, CardiacActivity.Absent, CardiacActivity.AtrialAndVentricular })
                {
                    Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                    var oldTimer = window.ActiveTimer;
                    window.CardiacActivityInput.SelectedIndex = (int)activity;
                    window.ConductionInput.SelectedIndex = 1;
                    if (projected) { window.UAmplitudeInputs[2].Text = "100"; }
                    else { window.RespCardiacArtifactInput.Text = "160"; }
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    window.Pulse(oldTimer);
                    if ((projected ? window.EcgConfiguration.CardiacActivity : window.BreathConfiguration.CardiacActivity) != activity ||
                        window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                    { throw new InvalidOperationException("Cardiac activity did not replace the source atomically."); }
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
                    short[] ecg = Samples(blocks, id);
                    if ((activity == CardiacActivity.Absent ? ecg.Any(value => value != 0) : !ecg.Take(25).Any(value => value > 0)) ||
                        (activity == CardiacActivity.AtrialAndVentricular ? !ecg.Skip(40).Take(20).Any(value => value > 500) :
                            ecg.Where((_, index) => index % 200 >= 25).Any(value => value != 0)))
                    { throw new InvalidOperationException("Native P and QRS do not follow cardiac activity."); }
                    if (projected && activity != CardiacActivity.AtrialAndVentricular &&
                        Samples(blocks, ProjectedEcgDemoSource.ChannelId(EcgLead.V3)).Skip(138).Take(30).Any(value => value != 0))
                    { throw new InvalidOperationException("Optional U persisted without a ventricular event."); }
                    if (!projected)
                    {
                        foreach (int row in new[] { 2, 3, 5 })
                        {
                            var pulse = Samples(blocks, PhysiologyDemoSource.ChannelId(row));
                            if (activity == CardiacActivity.AtrialAndVentricular ? !pulse.Any(value => value != 0) : pulse.Any(value => value != 0))
                            { throw new InvalidOperationException("Mechanical pulse excursions did not follow ventricular event availability."); }
                        }
                        if (!Samples(blocks, PhysiologyDemoSource.ChannelId(1)).Any(value => value != 0) ||
                            !Samples(blocks, PhysiologyDemoSource.ChannelId(4)).Any(value => value != 0))
                        { throw new InvalidOperationException("Independent respiration or expired gas stopped with cardiac activity."); }
                    }
                    VerifyPixels(window, projected, ecg);
                    Click(window.HoldButton); Click(window.RunButton);
                    var held = window.Trace;
                    var timer = window.ActiveTimer;
                    long before = window.SimulationTimeNs;
                    window.CardiacActivityInput.SelectedIndex = -1;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    if (!ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) ||
                        window.SimulationTimeNs != before ||
                        (projected ? window.EcgConfiguration.CardiacActivity : window.BreathConfiguration.CardiacActivity) != activity)
                    { throw new InvalidOperationException("Rejected cardiac activity changed the accepted view or source."); }
                    Click(window.ResetButton);
                    if (window.CardiacActivityInput.SelectedIndex != (int)activity || window.BlockCount != 0 || window.ActiveTimer is not null)
                    { throw new InvalidOperationException("Reset lost accepted cardiac activity."); }
                }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: native cardiac activity controls distinguish P-only and absent sources, pulses/U, independent breathing and lifecycle");
    }

    private static short[] Samples(WaveformEnvelope[] blocks, Guid id) => blocks.SelectMany(block =>
        block.Planes.Single(plane => plane.ChannelId == id).Samples).ToArray();

    private static void VerifyPixels(WaveformDemoWindow window, bool projected, short[] samples)
    {
        int height = projected ? 1920 : 840;
        window.Trace.Measure(new Size(1044, height));
        window.Trace.Arrange(new Rect(0, 0, 1044, height));
        using RenderTargetBitmap image = new(new PixelSize(1044, height), new Vector(96, 96));
        image.Render(window.Trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        foreach (int index in new[] { Enumerable.Range(0, 25).MaxBy(i => samples[i]), Enumerable.Range(40, 20).MaxBy(i => samples[i]), 150 })
        {
            int x = (int)Math.Round((projected ? 85 : 0) + index / 2.0);
            int y = (int)Math.Round((projected ? 240 : 60) - samples[index] * (projected ? 0.04 : 0.05));
            if (!Enumerable.Range(y - 1, 3).Any(row => Enumerable.Range(Math.Max(0, x - 1), 3).Any(column =>
                Marshal.ReadByte(buffer.Address + row * buffer.RowBytes + column * 4 + 1) > 100)))
            { throw new InvalidOperationException("Native cardiac P/QRS/baseline pixels differ from source samples."); }
        }
    }
}
