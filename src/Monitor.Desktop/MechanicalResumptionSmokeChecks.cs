// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Monitor.Desktop;

internal static class MechanicalResumptionSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(physiology: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.VascularReservoirInput.IsChecked = false;
            Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            window.VentricularMechanicalInput.IsChecked = false;
            window.MechanicalAfterCyclesInput.Text = "1";
            window.MechanicalDurationCyclesInput.Text = "1";
            Click(window.ApplyBreathButton);
            window.Pulse(oldTimer);
            var config = window.BreathConfiguration;
            if (config.MechanicalDurationCycles != 1 || config.MechanicalAfterCycles != 1 || window.BlockCount != 0 ||
                window.SimulationTimeNs != 0 || window.IsHeld || window.ActiveTimer is not null)
            { throw new InvalidOperationException("Mechanical recovery configuration did not atomically restart."); }
            for (int step = 1; step <= 30; step++)
            {
                Click(window.StepButton);
                if (window.SimulationTimeNs != step * 200_000_000L)
                { throw new InvalidOperationException("Mechanical recovery reset the source clock."); }
            }
            var actual = MechanicalUncouplingSmokeChecks.Decode(config);
            var normal = MechanicalUncouplingSmokeChecks.Decode(config with { VentricularMechanicalEnabled = true, MechanicalAfterCycles = null, MechanicalDurationCycles = null });
            foreach (int row in new[] { 0, 1, 4 })
            {
                if (!MechanicalUncouplingSmokeChecks.Samples(actual, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
                { throw new InvalidOperationException("Mechanical recovery changed ECG or breathing."); }
            }
            foreach (int row in new[] { 2, 3, 5 })
            {
                short[] pulse = MechanicalUncouplingSmokeChecks.Samples(actual, row);
                short[] reference = MechanicalUncouplingSmokeChecks.Samples(normal, row);
                int resumed = row == 5 ? 235 : 240;
                bool gapValid = row == 2
                    ? !pulse.Skip(140).Take(59).Zip(pulse.Skip(141)).Any(pair => pair.Second > pair.First)
                    : !pulse.Skip(140).Take(60).Any(value => value != 0);
                bool resumedValid = row == 2
                    ? !pulse.Skip(resumed).Zip(reference.Skip(resumed)).Any(pair => pair.First > pair.Second) &&
                      pulse.Skip(resumed).Take(64).Max() > pulse[resumed]
                    : pulse.Skip(resumed).SequenceEqual(reference.Skip(resumed));
                if (!pulse.Take(125).SequenceEqual(reference.Take(125)) || !gapValid || !resumedValid ||
                    !pulse.Skip(resumed).Take(64).Any(value => value > 0))
                { throw new InvalidOperationException("Mechanical recovery lost its first tail, gap or original resumed pulse timing."); }
            }
            MechanicalTransitionSmokeChecks.VerifyPressureTail(window, actual, [150, 260]);
            Click(window.HoldButton); Click(window.RunButton);
            var held = window.Trace;
            var timer = window.ActiveTimer;
            long before = window.SimulationTimeNs;
            foreach (string invalid in new[] { "0", "101", "-1", "1.5", "bad" })
            {
                window.MechanicalDurationCyclesInput.Text = invalid;
                Click(window.ApplyBreathButton);
                VerifyUnchanged();
            }
            window.MechanicalDurationCyclesInput.Text = "1";
            window.MechanicalAfterCyclesInput.Text = "";
            Click(window.ApplyBreathButton);
            VerifyUnchanged();
            Click(window.ResetButton);
            if (window.MechanicalAfterCyclesInput.Text != "1" || window.MechanicalDurationCyclesInput.Text != "1" || window.BlockCount != 0)
            { throw new InvalidOperationException("Reset lost the accepted mechanical recovery schedule."); }
            window.MechanicalDurationCyclesInput.Text = "";
            Click(window.ApplyBreathButton);
            short[] suppressed = MechanicalUncouplingSmokeChecks.Samples(MechanicalUncouplingSmokeChecks.Decode(window.BreathConfiguration), 2);
            if (window.BreathConfiguration.MechanicalDurationCycles is not null ||
                suppressed.Skip(125).Zip(suppressed.Skip(126)).Any(pair => pair.Second > pair.First))
            { throw new InvalidOperationException("Clearing duration did not restore permanent suppression."); }

            void VerifyUnchanged()
            {
                if (window.BreathConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) ||
                    window.SimulationTimeNs != before || string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
                { throw new InvalidOperationException("Invalid mechanical duration changed source/view/timer."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: scheduled mechanical resumption preserves pulse transit, native gap/recovery pixels, ECG clocks and configuration lifecycle");
    }
}
