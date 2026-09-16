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

internal static class UWaveSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (int mode in new[] { 0, 1, 2, 3 })
            {
                window.QtMethod.SelectedIndex = mode == 2 ? 1 : mode == 3 ? 0 : mode;
                window.HeartRateInput.Text = mode == 2 ? "158" : "75";
                window.QtcInput.Text = mode == 2 ? "386" : "400";
                window.PDurationInput.Text = mode == 2 ? "72" : "100";
                window.PrIntervalInput.Text = mode == 2 ? "94" : "160";
                window.QrsDurationInput.Text = mode == 2 ? "68" : "80";
                window.TDurationInput.Text = mode == 2 ? "105" : "180";
                window.UDelayInput.Text = mode == 2 ? "10" : mode == 3 ? "0" : "30";
                window.UDurationInput.Text = mode == 2 ? "20" : mode == 3 ? "280" : "120";
                int[] amplitudes = mode == 3 ? [10, 1000, -1000, 40, 20, 30] : [10, 80, -100, 40, 20, 30];
                for (int index = 0; index < 6; index++)
                { window.UAmplitudeInputs[index].Text = amplitudes[index].ToString(CultureInfo.InvariantCulture); }
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                Click(window.ApplyEcgButton);
                window.Pulse(oldTimer);
                var config = window.EcgConfiguration;
                if (config.UWave is null || !config.UWave.ChestAmplitudes.SequenceEqual(amplitudes) ||
                    window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("U configuration did not replace the source atomically."); }
                for (int index = 0; index < 8; index++) { Click(window.StepButton); }
                var on = Decode(config);
                var off = Decode(config with { UWave = null });
                var timing = config.ResolveTiming();
                long start = timing.PrIntervalNs + timing.QtIntervalNs + config.UWave.DelayMilliseconds * 1_000_000L;
                long end = start + config.UWave.DurationMilliseconds * 1_000_000L;
                foreach (EcgLead lead in Enum.GetValues<EcgLead>())
                {
                    short[] actual = Samples(on, lead), original = Samples(off, lead);
                    for (int sample = 0; sample < actual.Length; sample++)
                    {
                        long phase = sample * 4_000_000L % timing.RrIntervalNs;
                        if (lead < EcgLead.V1 || phase < start || phase >= end)
                        {
                            if (actual[sample] != original[sample])
                            { throw new InvalidOperationException("U changed limb leads or samples outside its support."); }
                        }
                    }
                }
                VerifyPixels(window, on, start, end);
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace;
                var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                foreach (var (input, invalid) in new[] { (window.UDelayInput, "-1"), (window.UDelayInput, "1001"),
                    (window.UDurationInput, "0"), (window.UDurationInput, "1001"), (window.UDurationInput, "1000"), (window.UAmplitudeInputs[2], "1001"),
                    (window.UAmplitudeInputs[2], "-1001"), (window.UAmplitudeInputs[2], "1.5"), (window.UAmplitudeInputs[2], "bad") })
                {
                    string? accepted = input.Text;
                    input.Text = invalid;
                    Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace) ||
                        !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before ||
                        string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                    { throw new InvalidOperationException("Invalid U input changed the accepted source or view."); }
                    input.Text = accepted;
                }
                window.UAmplitudeInputs[0].Text = "999";
                Click(window.ResetButton);
                if (window.EcgConfiguration != config || window.UAmplitudeInputs[0].Text != "10" ||
                    window.UDurationInput.Text != config.UWave.DurationMilliseconds.ToString(CultureInfo.InvariantCulture) ||
                    window.BlockCount != 0 || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Reset did not retain accepted U inputs and discard drafts."); }
                foreach (var input in window.UAmplitudeInputs) { input.Text = "0"; }
                Click(window.ApplyEcgButton);
                var disabled = Decode(window.EcgConfiguration);
                if (!disabled.Zip(off).All(pair => Enum.GetValues<EcgLead>().All(lead =>
                    Samples([pair.First], lead).SequenceEqual(Samples([pair.Second], lead)))))
                { throw new InvalidOperationException("Disabling U did not restore identical source samples."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: native optional U timing, signed chest pixels, unchanged QT/limb leads, short cycles, rejection and reset");
    }

    private static WaveformEnvelope[] Decode(ProjectedEcgDemoConfiguration config) =>
        ProjectedEcgDemoSource.Create(config).AdvanceTo(1_600_000_000, 400, 8, 100)
            .Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();

    private static short[] Samples(WaveformEnvelope[] blocks, EcgLead lead) => blocks.SelectMany(block =>
        block.Planes.Single(plane => plane.ChannelId == ProjectedEcgDemoSource.ChannelId(lead)).Samples).ToArray();

    private static void VerifyPixels(WaveformDemoWindow window, WaveformEnvelope[] blocks, long start, long end)
    {
        window.Trace.Measure(new Size(1044, (12 * ProjectedEcgPlotLayout.RowHeight)));
        window.Trace.Arrange(new Rect(0, 0, 1044, (12 * ProjectedEcgPlotLayout.RowHeight)));
        using RenderTargetBitmap image = new(new PixelSize(1044, (12 * ProjectedEcgPlotLayout.RowHeight)), new Vector(96, 96));
        image.Render(window.Trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        foreach (EcgLead lead in new[] { EcgLead.V2, EcgLead.V3 })
        {
            short[] samples = Samples(blocks, lead);
            int first = (int)((start + 3_999_999) / 4_000_000), last = (int)((end - 1) / 4_000_000);
            int peak = Enumerable.Range(first, last - first + 1).MaxBy(index => Math.Abs((int)samples[index]));
            int raw = samples[peak];
            int x = (int)Math.Round(85 + peak / 2.0);
            int y = (int)Math.Round((int)lead * ProjectedEcgPlotLayout.RowHeight + ProjectedEcgPlotLayout.RowHeight / 2 - raw * 0.04);
            if ((lead == EcgLead.V2 ? raw < 60 : raw > -75) ||
                !Enumerable.Range(y - 1, 3).Any(row => Enumerable.Range(x - 1, 3).Any(column =>
                    Marshal.ReadByte(buffer.Address + row * buffer.RowBytes + column * 4 + 1) > 100)))
            { throw new InvalidOperationException("Signed U peak is missing from native source or pixels."); }
        }
    }
}
