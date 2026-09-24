// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class PressureZeroSmokeChecks
{
    internal static void Verify()
    {
        foreach (var config in new[] { PhysiologyDemoConfiguration.Default,
            PhysiologyDemoConfiguration.Default with { UseVascularReservoir = false },
            PhysiologyDemoConfiguration.SinusArrestPreset })
        {
            var offsets = new PressureZeroOffsets(1000, -1000, 250);
            var normal = PhysiologyDemoSource.Create(config);
            var measured = PhysiologyDemoSource.Create(config, offsets);
            int blocks = 0;
            for (int step = 1; step <= 40; step++)
            {
                var expected = normal.AdvanceTo(step * 200_000_000L, 50, 1, 100);
                var actual = measured.AdvanceTo(step * 200_000_000L, 50, 1, 100);
                Require(expected.Count == actual.Count, "block cadence");
                foreach (var pair in expected.Zip(actual))
                {
                    blocks++;
                    var a = WaveformEnvelopeCodec.Decode(pair.First);
                    var b = WaveformEnvelopeCodec.Decode(pair.Second);
                    Require(a.Planes.Count == b.Planes.Count, "channel count");
                    foreach (var p in a.Planes.Zip(b.Planes))
                    {
                        Require(p.First.ChannelId == p.Second.ChannelId && p.First.Samples.Count == p.Second.Samples.Count,
                            "channel identity and sample count");
                        int offset = p.First.ChannelId == PhysiologyDemoSource.ChannelId(3) ? 1000
                            : p.First.ChannelId == PhysiologyDemoSource.ChannelId(5) ? -1000
                            : p.First.ChannelId == PhysiologyDemoSource.ChannelId(6) ? 250 : 0;
                        Require(p.First.Samples.Zip(p.Second.Samples).All(s => s.Second - s.First == offset), "selected pressure only");
                        Require(p.First.OffsetNumerator == p.Second.OffsetNumerator && p.First.OffsetDenominator == p.Second.OffsetDenominator,
                            "true baseline unchanged");
                    }
                }
                measured = PhysiologyWaveformGroup.Restore(measured.CaptureState());
            }
            Require(blocks == 30, "six seconds of recovered seven-channel output");
        }
        foreach (string invalid in new[] { "", "abc", "NaN", "1e1", "1,25", "0.001", "10.01", "-10.01" })
        {
            bool rejected = false;
            try { _ = PressureZeroOffsets.Parse(invalid); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "invalid decimal rejected");
        }
        Require(PressureZeroOffsets.Parse("-0.01") == -1 && PressureZeroOffsets.Parse("+10.00") == 1000, "signed centi-mmHg precision");
        var window = new WaveformDemoWindow(physiology: true);
        window.Show();
        try
        {
            for (int step = 0; step < 15; step++) { Click(window.StepButton); }
            Click(window.HoldButton);
            Click(window.RunButton);
            var timer = window.ActiveTimer;
            window.PressureZeroInputs[0].Text = "2.50";
            window.PressureZeroInputs[1].Text = "-1.25";
            window.PressureZeroInputs[2].Text = "0.01";
            Click(window.ApplyPressureZeroButton);
            window.Pulse(timer);
            Require(window.PressureOffsets == new PressureZeroOffsets(250, -125, 1) && window.SimulationTimeNs == 0 &&
                window.BlockCount == 0 && !window.IsHeld && window.ActiveTimer is null, "successful apply resets and fences timer");
            Click(window.StepButton);
            Click(window.RunButton);
            timer = window.ActiveTimer;
            long time = window.SimulationTimeNs;
            window.PressureZeroInputs[2].Text = "10.01";
            Click(window.ApplyPressureZeroButton);
            Require(window.SimulationTimeNs == time && window.ActiveTimer == timer && window.PressureOffsets.Cvp == 1 &&
                window.PressureZeroStatus.Text!.StartsWith("未应用", StringComparison.Ordinal), "invalid apply is atomic");
            window.PressureZeroInputs[2].Text = "0.01";
            window.ConductionInput.SelectedIndex = 1;
            Click(window.ApplyBreathButton);
            Require(window.BreathConfiguration.VentricularConductionRatio == 2 && window.PressureOffsets == new PressureZeroOffsets(250, -125, 1), "other settings retain acquisition error");
            foreach (var input in window.PressureZeroInputs) { input.Text = "0"; }
            Click(window.ApplyPressureZeroButton);
            Require(window.PressureOffsets == new PressureZeroOffsets(), "explicit zero removes error");
        }
        finally { window.Close(); }
        Console.WriteLine("ok: pressure offset demo isolates ABP/PA/CVP, recovers samples and applies atomically");
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Pressure zero demo: " + message); }
    }
}
