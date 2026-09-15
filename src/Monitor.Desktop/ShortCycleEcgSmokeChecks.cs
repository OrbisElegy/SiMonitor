// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class ShortCycleEcgSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (var (rate, qtc, method) in new[] { (158, 386, 1), (158, 386, 2), (180, 386, 1), (200, 360, 1) })
            {
                Click(window.StepButton); Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.QtMethod.SelectedIndex = method;
                window.HeartRateInput.Text = rate.ToString(CultureInfo.InvariantCulture);
                window.QtcInput.Text = qtc.ToString(CultureInfo.InvariantCulture);
                window.PDurationInput.Text = "72";
                window.PrIntervalInput.Text = "94";
                window.QrsDurationInput.Text = "68";
                window.TDurationInput.Text = "105";
                Click(window.ApplyEcgButton);
                window.Pulse(oldTimer);
                var config = window.EcgConfiguration;
                if (config.HeartRateBpm != rate || config.PDurationMilliseconds != 72 || config.PrIntervalMilliseconds != 94 ||
                    config.QrsDurationMilliseconds != 68 || config.TDurationMilliseconds != 105 || window.SimulationTimeNs != 0 ||
                    window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Short ECG timing was rejected or did not atomically replace the source."); }
                if (rate == 158 && method == 1 && config.ResolveTiming().QtIntervalNs != 237_867_105)
                { throw new InvalidOperationException("Reported158bpm timing did not resolve the expected Bazett QT."); }
                for (int step = 0; step < 8; step++) { Click(window.StepButton); }
                var blocks = ProjectedEcgDemoSource.Create(config).AdvanceTo(1_600_000_000, 400, 8, 100)
                    .Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                VerifyPixels(window, blocks, config.ResolveTiming());
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace;
                var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                foreach (var (input, invalid) in new[] { (window.PDurationInput, "95"), (window.PDurationInput, "0"),
                    (window.PrIntervalInput, "1000"), (window.QrsDurationInput, "0"), (window.QrsDurationInput, "1001"),
                    (window.TDurationInput, "1000"), (window.TDurationInput, "1.5"), (window.TDurationInput, "bad") })
                {
                    string? accepted = input.Text;
                    input.Text = invalid;
                    Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) ||
                        window.SimulationTimeNs != before || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                    { throw new InvalidOperationException("Invalid ECG duration changed accepted source, timer or held view."); }
                    input.Text = accepted;
                }
                Click(window.ResetButton);
                if (window.EcgConfiguration != config || window.PDurationInput.Text != "72" || window.PrIntervalInput.Text != "94" ||
                    window.QrsDurationInput.Text != "68" || window.TDurationInput.Text != "105" || window.BlockCount != 0 || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Reset lost accepted short-cycle ECG durations."); }
            }
            window.QtMethod.SelectedIndex = 0;
            Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default || window.PDurationInput.IsEnabled ||
                window.PrIntervalInput.IsEnabled || window.QrsDurationInput.IsEnabled || window.TDurationInput.IsEnabled || window.TDurationInput.Text != "180")
            { throw new InvalidOperationException("Fixed reference did not restore original duration values and disabled inputs."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: explicit P/PR/QRS/T supports158/180/200bpm, native P/T/QRS pixels, both QTc formulas and atomic validation");
    }

    private static void VerifyPixels(WaveformDemoWindow window, WaveformEnvelope[] blocks, EcgCycleTiming timing)
    {
        window.Trace.Measure(new Size(1044, 1920));
        window.Trace.Arrange(new Rect(0, 0, 1044, 1920));
        using RenderTargetBitmap image = new(new PixelSize(1044, 1920), new Vector(96, 96));
        image.Render(window.Trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        foreach (var (lead, start, end, minimum) in new[] {
            (EcgLead.II, 0L, timing.PDurationNs, 50),
            (EcgLead.II, timing.PrIntervalNs + timing.TOffsetFromQrsNs, timing.PrIntervalNs + timing.QtIntervalNs, 100),
            (EcgLead.V5, timing.RrIntervalNs + timing.PrIntervalNs, timing.RrIntervalNs + timing.PrIntervalNs + timing.QrsDurationNs, 1500) })
        {
            short[] samples = blocks.SelectMany(block => block.Planes.Single(plane => plane.ChannelId == ProjectedEcgDemoSource.ChannelId(lead)).Samples).ToArray();
            int first = (int)((start + 3_999_999) / 4_000_000), last = (int)((end - 1) / 4_000_000);
            int peak = Enumerable.Range(first, last - first + 1).MaxBy(index => samples[index]);
            int raw = samples[peak];
            int x = (int)Math.Round(85 + peak * 4_000_000L / 8_000_000.0);
            int y = (int)Math.Round((int)lead * 160 + 80 - raw * 0.04);
            if (raw < minimum || !Enumerable.Range(y - 2, 5).Any(row => Enumerable.Range(x - 1, 3).Any(column =>
                Marshal.ReadByte(buffer.Address + row * buffer.RowBytes + column * 4 + 1) > 100)))
            { throw new InvalidOperationException("Short-cycle P/T or subsequent QRS is absent from native samples/pixels."); }
        }
    }
}
