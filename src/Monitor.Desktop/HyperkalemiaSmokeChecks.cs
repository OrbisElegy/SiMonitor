// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class HyperkalemiaSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (bool conduction in new[] { false, true })
            {
                Click(window.VentricularDisorganizationButton);
                Click(window.StepButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.HyperkalemiaConductionInput.IsChecked = conduction;
                Click(window.HyperkalemiaButton); window.Pulse(oldTimer);
                var config = conduction ? ProjectedEcgDemoConfiguration.HyperkalemiaWithConduction : ProjectedEcgDemoConfiguration.Hyperkalemia;
                if (window.EcgConfiguration != config || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.BlockCount != 0)
                { throw new InvalidOperationException("Hyperkalemia loader retained disorganized source/timer."); }
                Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config || !string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                { throw new InvalidOperationException("Hyperkalemia example cannot reapply."); }
                var source = ProjectedEcgDemoSource.Create(config);
                var blocks = new List<WaveformEnvelope>();
                for (int step = 1; step <= 30; step++)
                {
                    Click(window.StepButton);
                    blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
                }
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks.ToArray(), conduction ? 140 : 90, [EcgLead.I, EcgLead.II, EcgLead.AVR, EcgLead.V2, EcgLead.V4, EcgLead.V6]);
                if (conduction) { EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks.ToArray(), 80, [EcgLead.V1, EcgLead.V5]); }
                var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
                var a = source.AdvanceTo(6_200_000_000, 50, 1, 100);
                var b = restored.AdvanceTo(6_200_000_000, 50, 1, 100);
                if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second)))
                { throw new InvalidOperationException("Hyperkalemia wire recovery diverged."); }
                Click(window.HoldButton); Click(window.RunButton);
                var trace = window.Trace; var timer = window.ActiveTimer; long time = window.SimulationTimeNs;
                window.TContourInput.SelectedIndex = 5; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config || window.Trace != trace || window.ActiveTimer != timer || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                { throw new InvalidOperationException("Conflicting hyperkalemia edit changed accepted state."); }
                Click(window.ResetButton);
                if (window.HyperkalemiaConductionInput.IsChecked != conduction || window.HyperkalemiaInput.IsChecked != true || window.TContourInput.SelectedIndex != 0)
                { throw new InvalidOperationException("Hyperkalemia reset lost accepted state."); }
                foreach (var invalid in new[] { config with { IllustrateAfAberrancy = true }, config with { QrsDurationMilliseconds = 160 }, config with { VentricularConductionRatio = 2 }, config with { Placement = (EcgLimbPlacement)99 } })
                {
                    try { ProjectedEcgDemoSource.Create(invalid); }
                    catch (EventWaveformException e) when (e.ReasonCode == "Hyperkalemia.ConflictingModes") { continue; }
                    throw new InvalidOperationException("Conflicting hyperkalemia configuration accepted.");
                }
                if (conduction)
                {
                    window.HyperkalemiaInput.IsChecked = false; Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                    { throw new InvalidOperationException("Orphan high-K conduction accepted."); }
                }
                window.HyperkalemiaConductionInput.IsChecked = false;
                window.HyperkalemiaInput.IsChecked = false; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default)
                { throw new InvalidOperationException("Hyperkalemia example did not clear."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: native high-K repolarization and low-P/long-PR/wide-QRS/ST-depression examples, signed pixels, wire recovery and atomic preset lifecycle");
    }
}
