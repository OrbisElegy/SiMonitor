// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class IndependentProjectedEcgSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (int method in new[] { 0, 1, 2 })
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.QtMethod.SelectedIndex = method;
                window.HeartRateInput.Text = "90";
                window.IndependentVentricularPeriodInput.Text = "1100";
                window.LimbPlacementInput.SelectedIndex = 1;
                window.UAmplitudeInputs[2].Text = "60";
                Click(window.ApplyEcgButton);
                window.Pulse(oldTimer);
                var config = window.EcgConfiguration;
                if (config.IndependentVentricularPeriodMilliseconds != 1100 || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Projected independent period did not atomically restart and fence the old timer."); }
                var timing = config.ResolveTiming();
                long expectedQt = method == 0 ? TextbookEcgReference.Timing.QtIntervalNs : new EcgQtCorrection(method == 1 ? EcgQtCorrection.Bazett : EcgQtCorrection.Fridericia, 400_000_000, 1_100_000_000).ResolveQtIntervalNs();
                var source = ProjectedEcgDemoSource.Create(config);
                var plan = source.CaptureState().Generator.Timeline.Plan;
                if (timing.RrIntervalNs != 1_100_000_000 || timing.QtIntervalNs != expectedQt ||
                    plan.HeartPeriodNs != (method == 0 ? 800_000_000 : 666_666_667) || plan.IndependentVentricularPeriodNs != 1_100_000_000)
                { throw new InvalidOperationException("QT correction or atrial clock used the wrong independent period."); }
                var expected = source.AdvanceTo(2_800_000_000, 700, 14, 100);
                source = ProjectedEcgDemoSource.Create(config);
                List<byte[]> recovered = [];
                for (int step = 1; step <= 14; step++)
                {
                    recovered.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                    source = ElectrodeWaveformGroup.Restore(source.CaptureState());
                    Click(window.StepButton);
                }
                if (expected.Count != recovered.Count || expected.Zip(recovered).Any(pair => !pair.First.SequenceEqual(pair.Second)))
                { throw new InvalidOperationException("Independent twelve-lead checkpoint lost samples, U or wiring."); }
                var blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks);
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 315);
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace;
                var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                foreach (string invalid in new[] { "799", "3201", "1.1", "bad" })
                {
                    window.IndependentVentricularPeriodInput.Text = invalid;
                    Click(window.ApplyEcgButton);
                    Unchanged();
                }
                window.IndependentVentricularPeriodInput.Text = "1100";
                window.ConductionInput.SelectedIndex = 1;
                Click(window.ApplyEcgButton);
                Unchanged();
                window.ConductionInput.SelectedIndex = 0;
                window.QtMethod.SelectedIndex = 1;
                window.HeartRateInput.Text = "30";
                Click(window.ApplyEcgButton);
                Unchanged();
                Click(window.ResetButton);
                if (window.IndependentVentricularPeriodInput.Text != "1100" || window.EcgConfiguration != config || window.BlockCount != 0 || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Reset lost accepted twelve-lead independent configuration."); }
                window.IndependentVentricularPeriodInput.Text = "";
                Click(window.ApplyEcgButton);
                if (window.EcgConfiguration.IndependentVentricularPeriodMilliseconds is not null ||
                    window.EcgConfiguration.ResolveTiming().RrIntervalNs != plan.HeartPeriodNs)
                { throw new InvalidOperationException("Clearing independent period did not restore ratio timing."); }

                void Unchanged()
                {
                    if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) ||
                        window.SimulationTimeNs != before || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                    { throw new InvalidOperationException("Invalid projected independent timing changed accepted state."); }
                }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: independent twelve-lead clocks use ventricular QTc, preserve U/wiring/recovery and native pixels with atomic lifecycle");
    }
}
