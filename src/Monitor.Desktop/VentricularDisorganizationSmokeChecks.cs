// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class VentricularDisorganizationSmokeChecks
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
                foreach (int selection in new[] { 13, 14, 15 })
                {
                    Click(window.StepButton); Click(window.RunButton);
                    var stale = window.ActiveTimer;
                    Click(window.VentricularDisorganizationButton); window.Pulse(stale);
                    if (window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null)
                    { throw new InvalidOperationException("Ventricular preset failed atomic reset."); }
                    window.ConductionInput.SelectedIndex = selection;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    if (!string.IsNullOrEmpty(projected ? window.EcgConfigurationStatus.Text : window.BreathConfigurationStatus.Text) ||
                        (projected ? window.EcgConfiguration.ConductionPattern : window.BreathConfiguration.ConductionPattern) != ConductionSelection.Pattern(selection))
                    { throw new InvalidOperationException($"Ventricular preset {selection} failed: {window.EcgConfigurationStatus.Text} {window.BreathConfigurationStatus.Text}"); }
                    for (int step = 0; step < 30; step++) { Click(window.StepButton); }
                    WaveformEnvelope[] blocks;
                    if (projected)
                    {
                        var source = ProjectedEcgDemoSource.Create(window.EcgConfiguration);
                        var trial = ProjectedEcgDemoSource.Create(window.EcgConfiguration);
                        List<byte[]> expected = [], recovered = [];
                        for (int step = 1; step <= 30; step++)
                        {
                            expected.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                            recovered.AddRange(trial.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                            trial = ElectrodeWaveformGroup.Restore(trial.CaptureState());
                        }
                        if (expected.Count != recovered.Count || expected.Zip(recovered).Any(p => !p.First.SequenceEqual(p.Second)))
                        { throw new InvalidOperationException("Ventricular disorganization checkpoint changed bytes."); }
                        blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 20);
                        if (!AuthoredQrsSummary.Create(source, CardiacActivity.VentricularOnly).Contains("无独立 QRS"))
                        { throw new InvalidOperationException("Disorganized activity was measured as normal QRS."); }
                    }
                    else
                    {
                        blocks = PhysiologyChannelSmokeChecks.DecodeCompletedOutput(window.BreathConfiguration, 4_000_000_000);
                        MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 20);
                        VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
                        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 2).Any(v => v != 0))
                        { throw new InvalidOperationException("Disorganized ventricles generated pleth pulses."); }
                        foreach (int row in new[] { 3, 5 })
                        {
                            short[] pressure = MechanicalUncouplingSmokeChecks.Samples(blocks, row);
                            if (pressure.First() <= pressure.Last() || pressure.Last() <= 0 || pressure.Zip(pressure.Skip(1)).Any(p => p.Second > p.First))
                            { throw new InvalidOperationException("Pressure failed passive decay."); }
                        }
                    }
                    Click(window.HoldButton); Click(window.RunButton);
                    var held = window.Trace; var timer = window.ActiveTimer;
                    long before = window.SimulationTimeNs;
                    var ecg = window.EcgConfiguration; var physiology = window.BreathConfiguration;
                    window.IndependentVentricularPeriodInput.Text = "1200";
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton); Unchanged();
                    window.IndependentVentricularPeriodInput.Text = "";
                    window.CardiacActivityInput.SelectedIndex = (int)CardiacActivity.AtrialAndVentricular;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton); Unchanged();
                    window.CardiacActivityInput.SelectedIndex = (int)CardiacActivity.VentricularOnly;
                    if (!projected)
                    {
                        window.VentricularMechanicalInput.IsChecked = true;
                        Click(window.ApplyBreathButton); Unchanged();
                        window.VentricularMechanicalInput.IsChecked = false;
                        window.VascularReservoirInput.IsChecked = false;
                        Click(window.ApplyBreathButton); Unchanged();
                    }
                    Click(window.ResetButton);
                    if (window.ConductionInput.SelectedIndex != selection || window.ActiveTimer is not null || window.BlockCount != 0)
                    { throw new InvalidOperationException("Ventricular reset lost selected mode."); }
                    void Unchanged()
                    {
                        if (window.EcgConfiguration != ecg || window.BreathConfiguration != physiology ||
                            !ReferenceEquals(window.Trace, held) || !ReferenceEquals(window.ActiveTimer, timer) || window.SimulationTimeNs != before)
                        { throw new InvalidOperationException("Rejected ventricular input changed accepted state."); }
                    }
                }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: ventricular flutter/coarse/fine VF controls, pixels, no pleth, pressure decay, recovery and atomic rejection");
    }
}
