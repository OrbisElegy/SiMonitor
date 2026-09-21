// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class CalciumSmokeChecks
{
    internal static void Verify()
    {
        var window = new WaveformDemoWindow(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (var mode in new[] { CalciumIllustration.High, CalciumIllustration.Low })
            {
                Click(window.HypokalemiaButton); Click(window.StepButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.CalciumInput.SelectedIndex = (int)mode; Click(window.CalciumButton); window.Pulse(oldTimer);
                var config = ProjectedEcgDemoConfiguration.CalciumPreset(mode);
                if (window.EcgConfiguration != config || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.BlockCount != 0)
                { throw new InvalidOperationException("Calcium loader retained source/timer state."); }
                Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config || !string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                { throw new InvalidOperationException("Calcium preset reapply failed."); }
                var source = ProjectedEcgDemoSource.Create(config);
                var blocks = new List<WaveformEnvelope>();
                for (int step = 1; step <= 30; step++)
                {
                    Click(window.StepButton);
                    blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
                }
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks.ToArray(), mode == CalciumIllustration.High ? 90 : 135, [EcgLead.I, EcgLead.II, EcgLead.AVR, EcgLead.V4, EcgLead.V6]);
                var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
                var a = source.AdvanceTo(6_200_000_000, 50, 1, 100);
                var b = restored.AdvanceTo(6_200_000_000, 50, 1, 100);
                if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second)))
                { throw new InvalidOperationException("Calcium wire recovery diverged."); }
                Click(window.HoldButton); Click(window.RunButton);
                var trace = window.Trace; var timer = window.ActiveTimer; long time = window.SimulationTimeNs;
                window.TContourInput.SelectedIndex = 5; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config || window.Trace != trace || window.ActiveTimer != timer || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                { throw new InvalidOperationException("Conflicting calcium edit mutated accepted state."); }
                Click(window.ResetButton);
                if (window.CalciumInput.SelectedIndex != (int)mode || window.TContourInput.SelectedIndex != 0)
                { throw new InvalidOperationException("Calcium reset lost accepted mode."); }
                foreach (var invalid in new[] { config with { HypokalemiaRepolarization = true }, config with { QrsDurationMilliseconds = 120 }, config with { Placement = (EcgLimbPlacement)99 }, config with { CardiacActivity = CardiacActivity.VentricularOnly } })
                {
                    try { ProjectedEcgDemoSource.Create(invalid); }
                    catch (EventWaveformException e) when (e.ReasonCode == "Calcium.ConflictingModes") { continue; }
                    throw new InvalidOperationException("Conflicting calcium mode accepted.");
                }
                window.CalciumInput.SelectedIndex = mode == CalciumIllustration.High ? 2 : 1; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.CalciumPreset((CalciumIllustration)window.CalciumInput.SelectedIndex))
                { throw new InvalidOperationException("Calcium high/low switch failed."); }
                window.CalciumInput.SelectedIndex = 0; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default)
                { throw new InvalidOperationException("Calcium clear failed."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: native high/low calcium ST-QT presets, T pixels, wire recovery and atomic lifecycle");
    }
}
