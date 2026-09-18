// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class BundleBlockSmokeChecks
{
    internal static void Verify()
    {
        foreach (bool projected in new[] { true, false })
        {
            WaveformDemoWindow window = new(projected: projected, physiology: !projected);
            window.Show();
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            try
            {
                foreach (var mode in new[] { EcgBundleBlockIllustration.CompleteRight, EcgBundleBlockIllustration.IncompleteRight, EcgBundleBlockIllustration.CompleteLeft })
                {
                    // Exercise resets from both a disorganized and a dropped-beat source.
                    foreach (bool priorBlock in new[] { false, true })
                    {
                        if (priorBlock) { window.SecondDegreePresetInput.SelectedIndex = 7; Click(window.SecondDegreePresetButton); }
                        else { Click(window.VentricularDisorganizationButton); }
                        Click(window.StepButton); Click(window.RunButton);
                        var stale = window.ActiveTimer;
                        window.BundleBlockInput.SelectedIndex = (int)mode;
                        Click(window.BundleBlockButton); window.Pulse(stale);
                        if (window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.ConductionInput.SelectedIndex != 0 ||
                            (projected ? window.EcgConfiguration != BundleBlockPreset.Ecg(mode) : window.BreathConfiguration != BundleBlockPreset.Physiology(mode)))
                        { throw new InvalidOperationException("Standalone bundle loader retained prior rhythm or timer."); }
                    }
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    if (!string.IsNullOrEmpty(projected ? window.EcgConfigurationStatus.Text : window.BreathConfigurationStatus.Text))
                    { throw new InvalidOperationException("Standalone bundle cannot reapply."); }
                    for (int step = 0; step < 30; step++) { Click(window.StepButton); }
                    if (projected)
                    {
                        var source = ProjectedEcgDemoSource.Create(window.EcgConfiguration);
                        var blocks = Enumerable.Range(1, 30).SelectMany(step => source.AdvanceTo(step * 200_000_000L, 50, 1, 100)).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 40, [EcgLead.I, EcgLead.V1, EcgLead.V2, EcgLead.V5, EcgLead.V6]);
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 110, [EcgLead.V1, EcgLead.V5]);
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 640, [EcgLead.V1, EcgLead.V5]);
                        var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
                        if (source.AdvanceTo(6_200_000_000, 50, 1, 100).Zip(restored.AdvanceTo(6_200_000_000, 50, 1, 100)).Any(p => !p.First.SequenceEqual(p.Second)))
                        { throw new InvalidOperationException("Standalone bundle electrode wire recovery diverged."); }
                    }
                    else
                    {
                        var blocks = MechanicalUncouplingSmokeChecks.Decode(window.BreathConfiguration);
                        var normal = MechanicalUncouplingSmokeChecks.Decode(PhysiologyDemoConfiguration.Default);
                        if (blocks.Zip(normal).Any(pair => pair.First.Planes.Where(p => p.ChannelId != PhysiologyDemoSource.ChannelId(0))
                            .Any(p => !p.Samples.SequenceEqual(pair.Second.Planes.Single(q => q.ChannelId == p.ChannelId).Samples))))
                        { throw new InvalidOperationException("Standalone bundle morphology altered non-ECG physiology."); }
                        MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks);
                        VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
                        var source = PhysiologyDemoSource.Create(window.BreathConfiguration);
                        for (int step = 1; step <= 12; step++) { source.AdvanceTo(step * 200_000_000L, 50, 1, 100); }
                        var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
                        if (source.AdvanceTo(2_600_000_000, 50, 1, 100).Zip(restored.AdvanceTo(2_600_000_000, 50, 1, 100)).Any(p => !p.First.SequenceEqual(p.Second)))
                        { throw new InvalidOperationException("Standalone bundle physiology wire recovery diverged."); }
                    }
                    Click(window.HoldButton); Click(window.RunButton);
                    var ecg = window.EcgConfiguration; var physiology = window.BreathConfiguration;
                    var trace = window.Trace; var timer = window.ActiveTimer; long before = window.SimulationTimeNs;
                    window.IndependentVentricularPeriodInput.Text = "1200";
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    if (window.EcgConfiguration != ecg || window.BreathConfiguration != physiology || window.Trace != trace || window.ActiveTimer != timer || window.SimulationTimeNs != before ||
                        string.IsNullOrEmpty(projected ? window.EcgConfigurationStatus.Text : window.BreathConfigurationStatus.Text))
                    { throw new InvalidOperationException("Invalid standalone bundle edit changed accepted state."); }
                    Click(window.ResetButton);
                    if (window.BundleBlockInput.SelectedIndex != (int)mode || window.BlockCount != 0)
                    { throw new InvalidOperationException("Standalone bundle reset lost its morphology."); }
                }
                window.BundleBlockInput.SelectedIndex = 0; Click(window.BundleBlockButton);
                if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default || window.BreathConfiguration != PhysiologyDemoConfiguration.Default)
                { throw new InvalidOperationException("Leaving standalone bundle did not restore reference."); }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: standalone complete/incomplete bundle shapes, fourth-beat pixels, wire recovery, unchanged perfusion and atomic lifecycle");
    }
}
