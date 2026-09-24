// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class AtrialFibrillationSmokeChecks
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
                foreach (bool fine in new[] { false, true })
                {
                    Click(window.StepButton); Click(window.RunButton);
                    var stale = window.ActiveTimer;
                    Click(window.FibrillationButton); window.Pulse(stale);
                    if (window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null)
                    { throw new InvalidOperationException("Fibrillation preset failed atomic reset."); }
                    int selection = fine ? 12 : 11;
                    window.ConductionInput.SelectedIndex = selection;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    if (!string.IsNullOrEmpty(projected ? window.EcgConfigurationStatus.Text : window.BreathConfigurationStatus.Text) ||
                        (projected ? window.EcgConfiguration.ConductionPattern : window.BreathConfiguration.ConductionPattern) != ConductionSelection.Pattern(selection))
                    { throw new InvalidOperationException($"AF fine={fine} failed to apply: {window.EcgConfigurationStatus.Text} {window.BreathConfigurationStatus.Text}"); }
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
                        { throw new InvalidOperationException("AF checkpoint changed wire samples."); }
                        blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 20);
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 100,
                            [EcgLead.II, EcgLead.III, EcgLead.AVF, EcgLead.V1]);
                    }
                    else
                    {
                        blocks = PhysiologyChannelSmokeChecks.DecodeCompletedOutput(window.BreathConfiguration, 4_000_000_000);
                        MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 20);
                        VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
                    }
                    var id = projected ? ProjectedEcgDemoSource.ChannelId(EcgLead.II) : PhysiologyDemoSource.ChannelId(0);
                    short[] samples = blocks.SelectMany(b => b.Planes.Single(p => p.ChannelId == id).Samples).ToArray();
                    if (!samples.Skip(20).Take(20).Any(v => v > 500) || !samples.Skip(303).Take(20).Any(v => v > 500))
                    { throw new InvalidOperationException("Fibrillation ventricular RR did not follow irregular beat times."); }
                    if (samples.Skip(220).Take(20).Any(value => value > 500) || !samples.Skip(502).Take(18).Any(value => value > 500))
                    { throw new InvalidOperationException("AF QRS still follows a regular 800ms grid."); }
                    if (!projected)
                    {
                        short[] pleth = MechanicalUncouplingSmokeChecks.Samples(blocks, 2);
                        var perfusionPlan = window.BreathConfiguration.ResolvePlan();
                        var source = PhysiologySignalGenerator.Start(perfusionPlan, "AcqPleth125@1", 1, [],
                            plethRunoff: new(80_000_000, 512_000_000, 1250, UseAtrialFibrillationPerfusion: true));
                        if (!pleth.SequenceEqual(source.GenerateBefore(pleth.Length * 8_000_000L, pleth.Length, 200).Select(s => s.NormalizedValue)))
                        { throw new InvalidOperationException("AF demo lost shared beat gains or full Pleth support."); }
                        var checkpoint = PhysiologyDemoSource.Create(window.BreathConfiguration).CaptureState();
                        if (checkpoint.Channels.Count(c => c.Generator.VascularPressure?.UseAtrialFibrillationPerfusion == true) != 2)
                        { throw new InvalidOperationException("AF ABP/PA did not select the shared perfusion model."); }
                        if (pleth.Skip(155).Take(9).Zip(pleth.Skip(156)).Any(pair => pair.Second > pair.First) ||
                            pleth[155] <= 0 || pleth.Skip(185).Take(20).Max() <= pleth[164])
                        { throw new InvalidOperationException("AF pleth did not follow irregular ventricular mechanics."); }
                    }
                    Click(window.HoldButton); Click(window.RunButton);
                    var held = window.Trace; var timer = window.ActiveTimer;
                    long before = window.SimulationTimeNs;
                    var ecg = window.EcgConfiguration; var physiology = window.BreathConfiguration;
                    if (!projected)
                    {
                        window.VascularReservoirInput.IsChecked = false;
                        Click(window.ApplyBreathButton); Unchanged();
                        window.VascularReservoirInput.IsChecked = true;
                    }
                    window.IndependentVentricularPeriodInput.Text = "1200";
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton); Unchanged();
                    window.IndependentVentricularPeriodInput.Text = "";
                    window.CardiacActivityInput.SelectedIndex = (int)CardiacActivity.VentricularOnly;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton); Unchanged();
                    Click(window.ResetButton);
                    if (window.ConductionInput.SelectedIndex != selection || window.ActiveTimer is not null || window.BlockCount != 0)
                    { throw new InvalidOperationException("Fibrillation reset lost coarse/fine selection."); }
                    if (projected)
                    {
                        window.AtrialInput.SelectedIndex = (int)EcgAtrialIllustration.LeftAtrialAbnormality;
                        Click(window.ApplyEcgButton);
                        if (window.EcgConfiguration != ecg) { throw new InvalidOperationException("Fibrillation accepted normal P illustration."); }
                        Click(window.ResetButton);
                    }
                    void Unchanged()
                    {
                        if (window.EcgConfiguration != ecg || window.BreathConfiguration != physiology ||
                            !ReferenceEquals(window.Trace, held) || !ReferenceEquals(window.ActiveTimer, timer) || window.SimulationTimeNs != before)
                        { throw new InvalidOperationException("Rejected fibrillation input changed accepted state."); }
                    }
                }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: coarse/fine AF controls, irregular QRS/pleth, f pixels, pressure, recovery and atomic rejection");
    }
}
