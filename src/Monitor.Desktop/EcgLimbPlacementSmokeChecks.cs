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

internal static class EcgLimbPlacementSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (int selection in new[] { 1, 2, 3, 0 })
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.LimbPlacementInput.SelectedIndex = selection;
                Click(window.ApplyEcgButton);
                window.Pulse(oldTimer);
                var config = window.EcgConfiguration;
                if ((int)config.Placement != selection || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Limb placement did not atomically replace the source."); }
                for (int step = 0; step < 8; step++) { Click(window.StepButton); }
                var blocks = ProjectedEcgDemoSource.Create(config).AdvanceTo(1_600_000_000, 400, 8, 100)
                    .Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                VerifyPixels(window, blocks);
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace;
                var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                window.LimbPlacementInput.SelectedIndex = -1;
                Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) ||
                    window.SimulationTimeNs != before || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                { throw new InvalidOperationException("Invalid limb wiring changed the accepted source/view/timer."); }
                Click(window.ResetButton);
                if (window.LimbPlacementInput.SelectedIndex != selection || window.EcgConfiguration != config || window.BlockCount != 0)
                { throw new InvalidOperationException("Reset lost accepted limb wiring."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: native limb electrode swaps and standard restoration, signed lead pixels and atomic configuration lifecycle");
    }

    internal static void VerifyPixels(WaveformDemoWindow window, WaveformEnvelope[] blocks, int firstSample = 40)
    {
        window.Trace.Measure(new Size(1044, 1920));
        window.Trace.Arrange(new Rect(0, 0, 1044, 1920));
        using RenderTargetBitmap image = new(new PixelSize(1044, 1920), new Vector(96, 96));
        image.Render(window.Trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        foreach (EcgLead lead in new[] { EcgLead.I, EcgLead.II, EcgLead.AVR, EcgLead.V5 })
        {
            var samples = blocks.SelectMany(block => block.Planes.Single(plane => plane.ChannelId == ProjectedEcgDemoSource.ChannelId(lead)).Samples).ToArray();
            int peak = Enumerable.Range(firstSample, 20).MaxBy(i => Math.Abs((int)samples[i]));
            int x = (int)Math.Round(85 + peak / 2.0);
            int y = (int)Math.Round((int)lead * 160 + 80 - samples[peak] * 0.04);
            if (!Enumerable.Range(y - 1, 3).Any(row => Enumerable.Range(x - 1, 3).Any(column =>
                Marshal.ReadByte(buffer.Address + row * buffer.RowBytes + column * 4 + 1) > 100)))
            { throw new InvalidOperationException("Native limb wiring pixels do not match projected signed samples."); }
        }
    }
}
