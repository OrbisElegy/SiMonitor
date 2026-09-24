// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Monitor.Desktop;

internal static class MechanicalStrideSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(physiology: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.VascularReservoirInput.IsChecked = false;
            foreach (int stride in new[] { 2, 3, 4, 1 })
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.MechanicalEveryCyclesInput.SelectedIndex = stride - 1;
                Click(window.ApplyBreathButton);
                window.Pulse(oldTimer);
                var config = window.BreathConfiguration;
                if (config.MechanicalEveryCycles != stride || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Mechanical stride did not atomically restart the source."); }
                for (int step = 0; step < 30; step++) { Click(window.StepButton); }
                var actual = PhysiologyChannelSmokeChecks.DecodeCompletedOutput(config, 4_000_000_000);
                var normal = PhysiologyChannelSmokeChecks.DecodeCompletedOutput(config with { MechanicalEveryCycles = 1 }, 4_000_000_000);
                foreach (int row in new[] { 0, 1, 4 })
                {
                    if (!MechanicalUncouplingSmokeChecks.Samples(actual, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
                    { throw new InvalidOperationException("Mechanical stride changed ECG or independent respiration."); }
                }
                foreach (int row in new[] { 2, 3, 5 })
                {
                    short[] samples = MechanicalUncouplingSmokeChecks.Samples(actual, row);
                    for (int cycle = 0; cycle < 5; cycle++)
                    {
                        bool pulse = row == 2
                            ? samples.Skip(cycle * 100 + 40).Take(49).Zip(samples.Skip(cycle * 100 + 41)).Any(pair => pair.Second > pair.First)
                            : samples.Skip(cycle * 100 + 40).Take(50).Any(value => value > 0);
                        if (pulse != (cycle % stride == 0))
                        { throw new InvalidOperationException("Pleth/pressure pulse count does not follow the mechanical stride."); }
                    }
                }
                MechanicalUncouplingSmokeChecks.VerifyPixels(window, actual);
                MechanicalTransitionSmokeChecks.VerifyPressureTail(window, actual, [150, stride * 100 + 50]);
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace;
                var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                window.MechanicalEveryCyclesInput.SelectedIndex = -1;
                Click(window.ApplyBreathButton);
                if (window.BreathConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) ||
                    window.SimulationTimeNs != before || string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
                { throw new InvalidOperationException("Invalid mechanical stride changed source/view/timer."); }
                Click(window.ResetButton);
                if (window.MechanicalEveryCyclesInput.SelectedIndex != stride - 1 || window.BlockCount != 0 || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Reset lost the accepted mechanical stride."); }
            }
            window.MechanicalEveryCyclesInput.SelectedIndex = 1;
            window.VentricularMechanicalInput.IsChecked = false;
            window.MechanicalAfterCyclesInput.Text = "1";
            window.MechanicalDurationCyclesInput.Text = "2";
            Click(window.ApplyBreathButton);
            var scheduled = PhysiologyChannelSmokeChecks.DecodeCompletedOutput(window.BreathConfiguration, 4_000_000_000);
            short[] pulseSamples = MechanicalUncouplingSmokeChecks.Samples(scheduled, 2);
            if (pulseSamples.Skip(140).Take(249).Zip(pulseSamples.Skip(141)).Any(pair => pair.Second > pair.First) ||
                pulseSamples[140] <= 0 || pulseSamples.Skip(440).Take(50).Max() <= pulseSamples[439])
            { throw new InvalidOperationException("Recovery restarted the stride instead of retaining original eligible cycle4."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: mechanical1:2/1:3/1:4 pulse selection preserves ECG, pressure pixels, original recovery phase and lifecycle");
    }
}
