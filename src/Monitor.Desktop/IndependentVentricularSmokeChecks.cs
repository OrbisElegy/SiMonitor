// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Monitor.Desktop;

internal static class IndependentVentricularSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(physiology: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (string text in new[] { "1100", "800", "3200", "" })
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.IndependentVentricularPeriodInput.Text = text;
                Click(window.ApplyBreathButton);
                window.Pulse(oldTimer);
                var config = window.BreathConfiguration;
                if (config.IndependentVentricularPeriodMilliseconds?.ToString(System.Globalization.CultureInfo.InvariantCulture) != (text == "" ? null : text) ||
                    window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException($"Independent ventricular period {text} did not atomically replace the source: {window.BreathConfigurationStatus.Text}"); }
                for (int step = 0; step < 30; step++) { Click(window.StepButton); }
                var actual = MechanicalUncouplingSmokeChecks.Decode(config);
                var normal = MechanicalUncouplingSmokeChecks.Decode(config with { IndependentVentricularPeriodMilliseconds = null });
                foreach (int row in new[] { 1, 4 })
                {
                    if (!MechanicalUncouplingSmokeChecks.Samples(actual, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
                    { throw new InvalidOperationException("Independent ventricular clock changed breathing or CO2."); }
                }
                if (text == "1100")
                {
                    short[] ecg = MechanicalUncouplingSmokeChecks.Samples(actual, 0);
                    if (!ecg.Skip(200).Take(25).Any(v => v > 0) || !ecg.Skip(315).Take(20).Any(v => v > 500) || ecg.Skip(240).Take(20).Any(v => v > 500))
                    { throw new InvalidOperationException("Atrial P and independent QRS did not retain separate periods."); }
                }
                MechanicalUncouplingSmokeChecks.VerifyPixels(window, actual);
                VascularPressureSmokeChecks.VerifyPressurePixels(window, actual, [150, 200]);
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace;
                var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                foreach (string invalid in new[] { "799", "3201", "-1", "1.1", "bad" })
                {
                    window.IndependentVentricularPeriodInput.Text = invalid;
                    Click(window.ApplyBreathButton);
                    if (window.BreathConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before)
                    { throw new InvalidOperationException("Invalid independent period changed accepted state."); }
                }
                window.IndependentVentricularPeriodInput.Text = "1100";
                window.ConductionInput.SelectedIndex = 1;
                Click(window.ApplyBreathButton);
                if (window.BreathConfiguration != config || !ReferenceEquals(timer, window.ActiveTimer))
                { throw new InvalidOperationException("Conflicting independent and ratio clocks were accepted."); }
                Click(window.ResetButton);
                if (window.IndependentVentricularPeriodInput.Text != text || window.ConductionInput.SelectedIndex != 0 || window.BlockCount != 0 || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Reset lost accepted independent period."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: independent ventricular period keeps atrial/respiratory clocks, pressure pixels and atomic native lifecycle");
    }
}
