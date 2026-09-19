// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;

namespace Monitor.Desktop;

internal static class AtrialFibrillationDeficitSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(physiology: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (bool fine in new[] { false, true })
            {
                Click(window.FibrillationButton);
                window.ConductionInput.SelectedIndex = fine ? 12 : 11;
                window.AfPulseDeficitInput.IsChecked = true;
                Click(window.ApplyBreathButton);
                var config = window.BreathConfiguration;
                if (!config.IllustrateAfSystemicPulseDeficit || !string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
                { throw new InvalidOperationException("AF pulse-deficit option failed to load."); }
                var enabled = PhysiologyDemoSource.Create(config);
                var normal = PhysiologyDemoSource.Create(config with { IllustrateAfSystemicPulseDeficit = false });
                List<WaveformEnvelope> actual = [], reference = [];
                for (int step = 1; step <= 85; step++)
                {
                    Click(window.StepButton);
                    actual.AddRange(enabled.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
                    reference.AddRange(normal.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
                    if (step == 30) { MechanicalUncouplingSmokeChecks.VerifyPixels(window, actual.ToArray(), 20); }
                }
                foreach (int row in new[] { 0, 1, 4, 5, 6 })
                {
                    if (!MechanicalUncouplingSmokeChecks.Samples(actual.ToArray(), row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(reference.ToArray(), row)))
                    { throw new InvalidOperationException("Systemic AF deficit changed ECG, PA, CVP or respiration."); }
                }
                foreach (int row in new[] { 2, 3 })
                {
                    short[] pulse = MechanicalUncouplingSmokeChecks.Samples(actual.ToArray(), row);
                    short[] baseline = MechanicalUncouplingSmokeChecks.Samples(reference.ToArray(), row);
                    // First authored long-short event arrives at13.126s. Native
                    //125Hz samples bracket it at indices1640/1641.
                    if (!pulse.Take(1641).SequenceEqual(baseline.Take(1641)) ||
                        !pulse.Skip(1641).Zip(baseline.Skip(1641)).Any(pair => pair.First < pair.Second) ||
                        pulse[1641] <= 0 || pulse[1661] >= pulse[1641])
                    { throw new InvalidOperationException("AF deficit lost old runoff or retained the selected new systemic pulse."); }
                }
                Click(window.HoldButton); Click(window.RunButton);
                var trace = window.Trace; var timer = window.ActiveTimer; long time = window.SimulationTimeNs;
                window.ConductionInput.SelectedIndex = 0;
                Click(window.ApplyBreathButton);
                if (window.BreathConfiguration != config || !ReferenceEquals(trace, window.Trace) ||
                    !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
                { throw new InvalidOperationException("Non-AF deficit conflict changed accepted source or view."); }
                Click(window.ResetButton);
                if (window.AfPulseDeficitInput.IsChecked != true || !window.BreathConfiguration.IllustrateAfSystemicPulseDeficit || window.BlockCount != 0)
                { throw new InvalidOperationException("Reset lost accepted AF pulse-deficit configuration."); }
                window.AfPulseDeficitInput.IsChecked = false;
                Click(window.ApplyBreathButton);
                if (window.BreathConfiguration.IllustrateAfSystemicPulseDeficit || window.ActiveTimer is not null || window.BlockCount != 0)
                { throw new InvalidOperationException("Disabling AF pulse deficit failed atomic restart."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: AF systemic pulse deficit retains ECG/PA/CVP/breathing, old tails, source timing, reset and atomic rejection");
    }
}
