// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class HypokalemiaSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (bool fuse in new[] { false, true })
                foreach (bool invert in new[] { false, true })
                {
                    Click(window.HyperkalemiaButton);
                    Click(window.StepButton); Click(window.RunButton);
                    var oldTimer = window.ActiveTimer;
                    Click(window.HypokalemiaButton); window.Pulse(oldTimer);
                    window.HypokalemiaFusionInput.IsChecked = fuse;
                    window.HypokalemiaInvertedTInput.IsChecked = invert;
                    Click(window.ApplyEcgButton);
                    var config = ProjectedEcgDemoConfiguration.Hypokalemia with { HypokalemiaTuFusion = fuse, HypokalemiaInvertedT = invert };
                    if (window.EcgConfiguration != config || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.BlockCount != 0)
                    { throw new InvalidOperationException("Hypokalemia loader retained disorganized source/timer."); }
                    Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || !string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                    { throw new InvalidOperationException("Hypokalemia example cannot reapply."); }
                    var source = ProjectedEcgDemoSource.Create(config);
                    var blocks = new List<WaveformEnvelope>();
                    for (int step = 1; step <= 30; step++)
                    {
                        Click(window.StepButton);
                        blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
                    }
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks.ToArray(), 65, [EcgLead.II, EcgLead.V2, EcgLead.V4, EcgLead.V6]);
                    if (invert) { EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks.ToArray(), 95, [EcgLead.II, EcgLead.AVR, EcgLead.V2, EcgLead.V4]); }
                    if (fuse) { EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks.ToArray(), 135, [EcgLead.II, EcgLead.V2, EcgLead.V3, EcgLead.V4]); }
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks.ToArray(), 155, [EcgLead.I, EcgLead.II, EcgLead.AVR, EcgLead.V2, EcgLead.V4, EcgLead.V6]);
                    var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
                    var a = source.AdvanceTo(6_200_000_000, 50, 1, 100);
                    var b = restored.AdvanceTo(6_200_000_000, 50, 1, 100);
                    if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second)))
                    { throw new InvalidOperationException("Hypokalemia wire recovery diverged."); }
                    Click(window.HoldButton); Click(window.RunButton);
                    var trace = window.Trace; var timer = window.ActiveTimer; long time = window.SimulationTimeNs;
                    window.TContourInput.SelectedIndex = 5; Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || window.Trace != trace || window.ActiveTimer != timer || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                    { throw new InvalidOperationException("Conflicting hypokalemia edit changed accepted state."); }
                    Click(window.ResetButton);
                    if (window.HypokalemiaInvertedTInput.IsChecked != invert || window.HypokalemiaFusionInput.IsChecked != fuse || window.HypokalemiaInput.IsChecked != true || window.TContourInput.SelectedIndex != 0)
                    { throw new InvalidOperationException("Hypokalemia reset lost accepted state."); }
                    window.HypokalemiaInvertedTInput.IsChecked = !invert; Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != (config with { HypokalemiaInvertedT = !invert }))
                    { throw new InvalidOperationException("Low-K T inversion toggle lost the U mode."); }
                    window.HypokalemiaInvertedTInput.IsChecked = invert; Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config)
                    { throw new InvalidOperationException("Low-K T inversion roundtrip failed."); }
                    foreach (var invalid in new[] { config with { HyperkalemiaRepolarization = true }, config with { QrsDurationMilliseconds = 160 }, config with { VentricularConductionRatio = 2 }, config with { Placement = (EcgLimbPlacement)99 } })
                    {
                        try { ProjectedEcgDemoSource.Create(invalid); }
                        catch (EventWaveformException e) when (e.ReasonCode == "Hypokalemia.ConflictingModes") { continue; }
                        throw new InvalidOperationException("Conflicting hypokalemia configuration accepted.");
                    }
                    if (fuse || invert)
                    {
                        window.HypokalemiaInput.IsChecked = false; Click(window.ApplyEcgButton);
                        if (window.EcgConfiguration != config || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                        { throw new InvalidOperationException("Orphan T-U fusion accepted."); }
                    }
                    window.HypokalemiaInvertedTInput.IsChecked = false;
                    window.HypokalemiaFusionInput.IsChecked = false;
                    window.HypokalemiaInput.IsChecked = false; Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default)
                    { throw new InvalidOperationException("Hypokalemia example did not clear."); }
                }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: native ST depression/low T/prominent U with distinct source QT/QU, optional inverted T and T-U overlap examples, signed pixels, wire recovery and atomic preset lifecycle");
    }
}
