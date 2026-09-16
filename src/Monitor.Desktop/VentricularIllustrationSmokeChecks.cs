// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class VentricularIllustrationSmokeChecks
{
    internal static void Verify()
    {
        foreach (int mode in new[] { 1, 2 })
            foreach (int selection in new[] { 0, 3 })
            {
                WaveformDemoWindow window = new(projected: true);
                window.Show();
                void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                try
                {
                    Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                    var oldTimer = window.ActiveTimer;
                    window.AtrialInput.SelectedIndex = selection;
                    window.VentricularInput.SelectedIndex = mode;
                    Click(window.ApplyEcgButton);
                    window.Pulse(oldTimer);
                    var config = window.EcgConfiguration;
                    if (config.Ventricular != (EcgVentricularIllustration)mode || config.ResolveTiming().QrsDurationNs != (mode == 1 ? 100_000_000 : 80_000_000) ||
                        window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                    { throw new InvalidOperationException("Ventricular illustration did not atomically restart."); }
                    for (int i = 0; i < 14; i++) { Click(window.StepButton); }
                    var source = ProjectedEcgDemoSource.Create(config);
                    var expected = source.AdvanceTo(2_800_000_000, 700, 14, 100);
                    var blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 0, [EcgLead.I, EcgLead.AVL, EcgLead.V1, EcgLead.V2, EcgLead.V5, EcgLead.V6]);
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 12, [EcgLead.I, EcgLead.AVL, EcgLead.V1, EcgLead.V2, EcgLead.V5, EcgLead.V6]);
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 40, [EcgLead.I, EcgLead.V5, EcgLead.V6]);
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 60, [EcgLead.V1, EcgLead.V2]);
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 95, [EcgLead.I, EcgLead.V1, EcgLead.V5, EcgLead.V6]);
                    var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
                    var tail = source.AdvanceTo(3_000_000_000, 50, 1, 100);
                    var recovered = restored.AdvanceTo(3_000_000_000, 50, 1, 100);
                    if (tail.Count != recovered.Count || tail.Zip(recovered).Any(x => !x.First.SequenceEqual(x.Second)))
                    { throw new InvalidOperationException("Ventricular illustration lost checkpoint bytes."); }
                    Click(window.HoldButton); Click(window.RunButton);
                    var trace = window.Trace;
                    var timer = window.ActiveTimer;
                    long time = window.SimulationTimeNs;
                    foreach (int bad in new[] { -1, mode })
                    {
                        window.VentricularInput.SelectedIndex = bad;
                        window.ChestJInput.Text = "100";
                        Click(window.ApplyEcgButton);
                        if (window.EcgConfiguration != config || window.Trace != trace || window.ActiveTimer != timer || window.SimulationTimeNs != time)
                        { throw new InvalidOperationException("Rejected ventricular input changed active source or view."); }
                    }
                    Click(window.ResetButton);
                    if (window.VentricularInput.SelectedIndex != mode || window.ChestJInput.Text != "0" || window.BlockCount != 0 || window.ActiveTimer is not null)
                    { throw new InvalidOperationException("Reset lost ventricular selection."); }
                    window.AtrialInput.SelectedIndex = 0;
                    window.VentricularInput.SelectedIndex = 0;
                    Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default)
                    { throw new InvalidOperationException("Clearing ventricular illustration did not restore reference configuration."); }
                }
                finally { window.Close(); }
            }
        Console.WriteLine("ok: native left/right ventricular illustration, projected pixels, recovery and atomic lifecycle");
    }
}
