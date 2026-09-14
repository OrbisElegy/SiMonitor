// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class PhysiologyConfigurationSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(physiology: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (var config in new[] { new PhysiologyDemoConfiguration(3000, 1000, -800, 1234), new(4800, 3200, 600, 4000),
                new(4000, 1000, 1000, 2500, 5, 50, 200, 400, 100, 600),
                new(4000, 1000, 1000, 5000, 5, 50, 200, 400, 100, 600, 100),
                new(4000, 2000, -800, 4000, InspiratoryPauseMilliseconds: 800),
                new(4000, 2000, 800, 4000, InspiratoryPauseMilliseconds: 800, ExpiratoryPauseMilliseconds: 800),
                new(4000, 2000, 0, 4000, InspiratoryPauseMilliseconds: 800, ExpiratoryPauseMilliseconds: 800, RespCardiacArtifactCounts: 160),
                new(4000, 2000, 0, 4000, InspiratoryPauseMilliseconds: 800, ExpiratoryPauseMilliseconds: 800, RespCardiacArtifactCounts: -160) })
            {
                for (int step = 0; step < 11; step++) { Click(window.StepButton); }
                Click(window.HoldButton);
                Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.BreathPeriodInput.Text = config.BreathPeriodMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.InspirationInput.Text = config.InspirationMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.ExpiratoryPauseInput.Text = config.ExpiratoryPauseMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.InspiratoryPauseInput.Text = config.InspiratoryPauseMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.RespCardiacArtifactInput.Text = config.RespCardiacArtifactCounts.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.RespAmplitudeInput.Text = config.RespAmplitudeCounts.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.Co2PlateauInput.Text = (config.Co2PlateauStartCentiMmHg!.Value / 100m).ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.Co2BaselineInput.Text = config.Co2BaselineMmHg.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.Co2EndInput.Text = config.Co2EndExpiratoryMmHg.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.Co2DeadSpaceInput.Text = config.Co2DeadSpaceMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.Co2RiseInput.Text = config.Co2RiseMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.Co2DispersionInput.Text = config.Co2DispersionStepMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.Co2TransportInput.Text = config.Co2TransportDelayMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.Co2FallInput.Text = config.Co2FallMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Click(window.ApplyBreathButton);
                window.Pulse(oldTimer);
                if (window.BreathConfiguration != config || window.SimulationTimeNs != 0 || window.BlockCount != 0 ||
                    window.IsHeld || window.ActiveTimer is not null || window.LiveFrontierNs != 0)
                { throw new InvalidOperationException("Applying breathing parameters did not restart all channels and fence prior work."); }
                var source = PhysiologyDemoSource.Create(config);
                var state = source.CaptureState();
                if (state.Channels.Any(channel => channel.Generator.Timeline.Plan != config.ResolvePlan()))
                { throw new InvalidOperationException("Breathing channels do not share the accepted source plan."); }
                var cvpBreath = state.Channels.Single(channel => channel.ChannelId == PhysiologyDemoSource.ChannelId(6))
                    .Generator.Bands.Single(band => band.Trigger == PhysiologyCycleEventKind.InspirationStart);
                var cvp = EventWaveformComposition.Restore(new([cvpBreath], [new(0, PhysiologyCycleEventKind.InspirationStart, 0)]));
                if (cvp.EvaluateAt(config.InspirationMilliseconds * 1_000_000L) != -100 * FixedPointMath.Q32One ||
                    cvp.EvaluateAt(config.BreathPeriodMilliseconds * 1_000_000L) != 0)
                { throw new InvalidOperationException("CVP respiratory pressure did not follow configured inspiration independently of Resp polarity."); }
                if (config.InspiratoryPauseMilliseconds > 0 &&
                    cvp.EvaluateAt((config.InspirationMilliseconds - config.InspiratoryPauseMilliseconds) * 1_000_000L) != -100 * FixedPointMath.Q32One)
                { throw new InvalidOperationException("CVP respiratory pressure did not enter the shared inspiratory pause."); }
                if (cvp.EvaluateAt((config.BreathPeriodMilliseconds - config.ExpiratoryPauseMilliseconds) * 1_000_000L) != 0)
                { throw new InvalidOperationException("CVP respiratory component did not return to baseline before expiratory pause."); }
                List<WaveformEnvelope> blocks = [];
                for (int step = 1; step <= (config.BreathPeriodMilliseconds + config.Co2TransportDelayMilliseconds + 2 * config.Co2DispersionStepMilliseconds + 2400) / 200; step++)
                {
                    Click(window.StepButton);
                    blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
                }
                VerifyPixels(window, config, blocks);
                Click(window.HoldButton);
                Click(window.RunButton);
                var held = window.Trace;
                var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                int count = window.BlockCount;
                foreach (var (period, inspiration, amplitude) in new[] { ("bad", "1000", "800"), ("2147483648", "1000", "800"),
                    ("999", "200", "800"), ("10001", "1000", "800"), ("3000", (config.Co2FallMilliseconds - 1).ToString(System.Globalization.CultureInfo.InvariantCulture), "800"),
                    ("1000", (1000 - config.Co2DeadSpaceMilliseconds - config.Co2RiseMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture), "800"), ("3000", "3000", "800"), ("3000", "1000", "1001") })
                {
                    window.BreathPeriodInput.Text = period;
                    window.InspirationInput.Text = inspiration;
                    window.RespAmplitudeInput.Text = amplitude;
                    Click(window.ApplyBreathButton);
                    if (window.BreathConfiguration != config || !window.IsHeld || !ReferenceEquals(held, window.Trace) ||
                        !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before || window.BlockCount != count ||
                        string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
                    { throw new InvalidOperationException("Invalid breathing parameters changed accepted source, timer or frozen view."); }
                }
                window.BreathPeriodInput.Text = config.BreathPeriodMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.InspirationInput.Text = config.InspirationMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.RespCardiacArtifactInput.Text = config.RespCardiacArtifactCounts.ToString(System.Globalization.CultureInfo.InvariantCulture);
                window.RespAmplitudeInput.Text = config.RespAmplitudeCounts.ToString(System.Globalization.CultureInfo.InvariantCulture);
                foreach (string invalid in new[] { "oops", "-1", (config.Co2EndExpiratoryMmHg + 0.01m).ToString(System.Globalization.CultureInfo.InvariantCulture), "1.001", "1,5", "999999999999999999999999999999999999" })
                {
                    window.Co2PlateauInput.Text = invalid;
                    Click(window.ApplyBreathButton);
                    if (window.BreathConfiguration != config || !ReferenceEquals(held, window.Trace) ||
                        !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before)
                    { throw new InvalidOperationException("Rejected CO2 plateau changed the active source or held view."); }
                }
                window.Co2PlateauInput.Text = (config.Co2PlateauStartCentiMmHg!.Value / 100m).ToString(System.Globalization.CultureInfo.InvariantCulture);
                foreach (var (input, invalid) in new[] { (window.RespCardiacArtifactInput, "-201"), (window.RespCardiacArtifactInput, "201"), (window.RespCardiacArtifactInput, "0.5"),
                    (window.ExpiratoryPauseInput, "-1"), (window.ExpiratoryPauseInput, "0.5"),
                    (window.ExpiratoryPauseInput, (config.BreathPeriodMilliseconds - config.InspirationMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    (window.InspiratoryPauseInput, "-1"), (window.InspiratoryPauseInput, "0.5"),
                    (window.InspiratoryPauseInput, config.InspirationMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    (window.Co2DispersionInput, "-1"), (window.Co2DispersionInput, "501"), (window.Co2DispersionInput, "0.5"), (window.Co2TransportInput, "-1"), (window.Co2TransportInput, "5001"), (window.Co2TransportInput, "0.5"), (window.Co2BaselineInput, "81"), (window.Co2EndInput, "-1"),
                    (window.Co2EndInput, "1"), (window.Co2BaselineInput, "1.5"), (window.Co2DeadSpaceInput, "0"),
                    (window.Co2RiseInput, "0"), (window.Co2RiseInput, "2147483648"),
                    (window.Co2FallInput, (config.InspirationMilliseconds + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)) })
                {
                    string? accepted = input.Text;
                    input.Text = invalid;
                    Click(window.ApplyBreathButton);
                    if (window.BreathConfiguration != config || !ReferenceEquals(held, window.Trace) ||
                        !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before)
                    { throw new InvalidOperationException("Invalid CO2 pressure or phase input changed accepted state."); }
                    input.Text = accepted;
                }
                Click(window.ResetButton);
                if (window.BreathConfiguration != config || window.ActiveTimer is not null || window.IsHeld || window.BlockCount != 0 ||
                    window.InspirationInput.Text != config.InspirationMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    window.RespCardiacArtifactInput.Text != config.RespCardiacArtifactCounts.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    window.ExpiratoryPauseInput.Text != config.ExpiratoryPauseMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    window.InspiratoryPauseInput.Text != config.InspiratoryPauseMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    window.Co2DispersionInput.Text != config.Co2DispersionStepMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    window.Co2TransportInput.Text != config.Co2TransportDelayMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    window.Co2BaselineInput.Text != config.Co2BaselineMmHg.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    window.Co2FallInput.Text != config.Co2FallMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    window.Co2PlateauInput.Text != (config.Co2PlateauStartCentiMmHg!.Value / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                { throw new InvalidOperationException("Reset lost accepted breathing settings or retained draft state."); }
            }
            window.Co2PlateauInput.Text = "";
            Click(window.ApplyBreathButton);
            if (window.BreathConfiguration.Co2PlateauStartCentiMmHg is not null || window.SimulationTimeNs != 0 ||
                window.Co2PlateauInput.Text != "")
            { throw new InvalidOperationException("Clearing the CO2 plateau did not restore reference scaling."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: breathing controls synchronize Resp/CO2/CVP, preserve native pixels and atomically restart or reject input");
    }

    private static void VerifyPixels(WaveformDemoWindow window, PhysiologyDemoConfiguration config, List<WaveformEnvelope> blocks)
    {
        int Co2At(long time)
        {
            var block = blocks.Single(block => time >= block.StartSimTimeNs && time < block.StartSimTimeNs + 200_000_000);
            return block.Planes.Single(plane => plane.ChannelId == PhysiologyDemoSource.ChannelId(4)).Samples[(int)(time % 200_000_000 / 10_000_000)];
        }
        long deadEnd = (config.InspirationMilliseconds + config.Co2DeadSpaceMilliseconds + config.Co2TransportDelayMilliseconds) / 10 * 10_000_000L;
        long fallEnd = (config.BreathPeriodMilliseconds + config.Co2TransportDelayMilliseconds + config.Co2FallMilliseconds + 2 * config.Co2DispersionStepMilliseconds + 9) / 10 * 10_000_000L;
        if (Co2At(deadEnd) != 0 || Co2At(deadEnd + 20_000_000) <= 0 || Co2At(fallEnd) != 0 ||
            Co2At(fallEnd - 20_000_000) <= 0)
        { throw new InvalidOperationException("CO2 dead space or inspiratory fall did not use accepted phase durations."); }
        var trace = window.Trace;
        trace.Measure(new Size(1000, 840));
        trace.Arrange(new Rect(0, 0, 1000, 840));
        using RenderTargetBitmap image = new(new PixelSize(1000, 840), new Vector(96, 96));
        image.Render(trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock();
        image.CopyPixels(buffer);
        long tailTime = (config.BreathPeriodMilliseconds + config.Co2TransportDelayMilliseconds +
            config.Co2FallMilliseconds + config.Co2DispersionStepMilliseconds) * 1_000_000L;
        List<(int Row, long Time)> landmarks = [(1, config.InspirationMilliseconds * 1_000_000L),
            (4, (config.BreathPeriodMilliseconds + config.Co2TransportDelayMilliseconds) * 1_000_000L),
            (4, ((config.InspirationMilliseconds + config.Co2DeadSpaceMilliseconds + config.Co2RiseMilliseconds + config.Co2TransportDelayMilliseconds + 2 * config.Co2DispersionStepMilliseconds + 9) / 10 * 10) * 1_000_000L), (6, config.InspirationMilliseconds * 1_000_000L)];
        if (config.Co2DispersionStepMilliseconds > 0) { landmarks.Add((4, tailTime)); }
        if (config.InspiratoryPauseMilliseconds > 0)
        {
            for (int time = config.InspirationMilliseconds - config.InspiratoryPauseMilliseconds;
                time < config.InspirationMilliseconds; time += 8)
            { landmarks.Add((1, time * 1_000_000L)); }
            landmarks.Add((6, (config.InspirationMilliseconds - config.InspiratoryPauseMilliseconds / 2) * 1_000_000L));
        }
        long expirationEnd = (config.BreathPeriodMilliseconds - config.ExpiratoryPauseMilliseconds) * 1_000_000L;
        if (config.ExpiratoryPauseMilliseconds > 0)
        {
            for (long time = expirationEnd; time < config.BreathPeriodMilliseconds * 1_000_000L; time += 8_000_000)
            { landmarks.Add((1, time)); }
            landmarks.Add((6, expirationEnd + config.ExpiratoryPauseMilliseconds * 500_000L));
        }
        var timing = config.ResolvePlan();
        if (config.RespCardiacArtifactCounts != 0)
        {
            landmarks.RemoveAll(point => point.Row == 1);
            for (long beat = timing.VentricularMechanicalOffsetNs; beat < timing.BreathPeriodNs; beat += timing.HeartPeriodNs)
            {
                landmarks.Add((1, beat + timing.HeartPeriodNs / 4));
                landmarks.Add((1, beat + 3 * timing.HeartPeriodNs / 4));
            }
        }
        foreach (var (row, time) in landmarks)
        {
            var plane = blocks.Single(block => time >= block.StartSimTimeNs && time < block.StartSimTimeNs + 200_000_000)
                .Planes.Single(plane => plane.ChannelId == PhysiologyDemoSource.ChannelId(row));
            int sample = (int)(time % 200_000_000 * plane.SampleRateNumerator / 1_000_000_000);
            int raw = plane.Samples[sample];
            bool invalidCo2 = row == 4 && (time == tailTime && config.Co2DispersionStepMilliseconds > 0
                ? raw <= 0 || raw >= (config.Co2EndExpiratoryMmHg - config.Co2BaselineMmHg) * 100
                : time == (config.BreathPeriodMilliseconds + config.Co2TransportDelayMilliseconds) * 1_000_000L
                    ? raw != (config.Co2EndExpiratoryMmHg - config.Co2BaselineMmHg) * 100
                    : Math.Abs(raw + config.Co2BaselineMmHg * 100 - config.Co2PlateauStartCentiMmHg!.Value) > 10);
            int expectedResp = config.RespCardiacArtifactCounts != 0
                ? ((time - timing.VentricularMechanicalOffsetNs) % timing.HeartPeriodNs < timing.HeartPeriodNs / 2
                    ? config.RespCardiacArtifactCounts : -config.RespCardiacArtifactCounts)
                : time >= expirationEnd ? 0 : config.RespAmplitudeCounts;
            if ((row == 1 && raw != expectedResp) || invalidCo2)
            { throw new InvalidOperationException("Configured Resp turn or CO2 plateau/peak/response tail is absent from native data."); }
            int x = (int)Math.Round(time / 8_000_000.0);
            int y = (int)Math.Round(row == 1 ? 180 - raw * 0.05 : row == 4 ? 590 - (raw / 100.0 + config.Co2BaselineMmHg) * 1.25 : 775 - raw * 0.05);
            if (!Enumerable.Range(y - 2, 5).Any(line => Enumerable.Range(x - 1, 3).Any(column =>
            {
                int offset = line * buffer.RowBytes + column * 4;
                byte b = Marshal.ReadByte(buffer.Address, offset), g = Marshal.ReadByte(buffer.Address, offset + 1), r = Marshal.ReadByte(buffer.Address, offset + 2);
                return r > 100 && g > (row == 6 ? 50 : 100) && (row == 4 ? b > 100 : b == 0);
            })))
            { throw new InvalidOperationException("Breathing controls did not reach native Resp/CO2/CVP pixels."); }
        }
    }
}
