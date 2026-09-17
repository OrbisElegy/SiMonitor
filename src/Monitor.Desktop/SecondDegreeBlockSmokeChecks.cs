// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class SecondDegreeBlockSmokeChecks
{
    internal static void Verify()
    {
        foreach (bool projected in new[] { false, true })
        {
            WaveformDemoWindow window = new(physiology: !projected, projected: projected);
            window.Show();
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            try
            {
                foreach (int priorMode in Enumerable.Range(0, 7))
                    foreach (int preset in Enumerable.Range(0, 6))
                    {
                        Click(priorMode switch { 3 => window.VentricularEscapeButton, 4 => window.JunctionalEscapeButton, 5 => window.FibrillationButton, 6 => window.FlutterButton, _ => window.VentricularDisorganizationButton });
                        if (priorMode is 1 or 2)
                        {
                            window.ConductionInput.SelectedIndex = 13 + priorMode;
                            Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                        }
                        Click(window.StepButton); Click(window.RunButton);
                        var stale = window.ActiveTimer;
                        window.ConductionInput.SelectedIndex = SecondDegreeBlockPreset.Selection(preset);
                        Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                        window.Pulse(stale);
                        if (window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.ActiveTimer is not null ||
                            (projected ? window.EcgConfiguration != SecondDegreeBlockPreset.Ecg(preset) : window.BreathConfiguration != SecondDegreeBlockPreset.Physiology(preset)))
                        { throw new InvalidOperationException("Second-degree transition retained incompatible prior rhythm state."); }
                        Click(window.StepButton);
                        if (window.SimulationTimeNs != 200_000_000) { throw new InvalidOperationException("Second-degree source cannot run after transition."); }
                    }
                foreach (int preset in Enumerable.Range(0, 6))
                {
                    window.SecondDegreePresetInput.SelectedIndex = preset;
                    Click(window.SecondDegreePresetButton);
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    if (!string.IsNullOrEmpty(projected ? window.EcgConfigurationStatus.Text : window.BreathConfigurationStatus.Text))
                    { throw new InvalidOperationException("Loaded second-degree preset cannot be reapplied."); }
                    Click(window.ResetButton);
                    if (window.ConductionInput.SelectedIndex != SecondDegreeBlockPreset.Selection(preset))
                    { throw new InvalidOperationException("Second-degree reset lost selected template."); }
                }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: second-degree templates load/reapply and replace incompatible VF/flutter/escape state atomically");
    }
}
