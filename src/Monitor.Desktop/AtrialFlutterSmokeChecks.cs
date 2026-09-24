// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class AtrialFlutterSmokeChecks
{
    internal static void Verify()
    {
        try { PhysiologyDemoSource.Create(PhysiologyDemoConfiguration.Flutter(1)); throw new InvalidOperationException("Unvalidated1:1 flutter perfusion accepted."); }
        catch (EventWaveformException e) when (e.ReasonCode == "Flutter.OneToOnePerfusionPending") { }
        foreach (bool projected in new[] { false, true })
        {
            WaveformDemoWindow window = new(physiology: !projected, projected: projected);
            window.Show();
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            try
            {
                foreach (int variant in projected ? new[] { 4, 3, 2, 1, 0 } : new[] { 4, 3, 2, 0 })
                {
                    bool variable = variant == 0;
                    int ratio = variable ? 2 : variant;
                    Click(window.StepButton); Click(window.RunButton);
                    var stale = window.ActiveTimer;
                    Click(window.FlutterButton); window.Pulse(stale);
                    if (window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null)
                    { throw new InvalidOperationException("Flutter preset failed atomic reset."); }
                    int selection = variable ? 39 : ratio == 1 ? 40 : ratio == 2 ? 9 : ratio == 3 ? 38 : 10;
                    window.ConductionInput.SelectedIndex = selection;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    if (!string.IsNullOrEmpty(projected ? window.EcgConfigurationStatus.Text : window.BreathConfigurationStatus.Text) ||
                        (projected ? window.EcgConfiguration.VentricularConductionRatio : window.BreathConfiguration.VentricularConductionRatio) != ratio)
                    { throw new InvalidOperationException($"Flutter {ratio}:1 failed to apply: {window.EcgConfigurationStatus.Text} {window.BreathConfigurationStatus.Text}"); }
                    if (projected && (window.PDurationInput.Text != "—" || window.PrIntervalInput.Text != "—")) { throw new InvalidOperationException("Flutter exposed placeholder P/PR."); }
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
                        { throw new InvalidOperationException("Flutter F boundary checkpoint changed wire samples."); }
                        blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 20);
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, ratio == 2 ? 100 : 125,
                            [EcgLead.II, EcgLead.III, EcgLead.AVF, EcgLead.V1]);
                    }
                    else
                    {
                        blocks = MechanicalUncouplingSmokeChecks.Decode(window.BreathConfiguration);
                        MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 20);
                        VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
                    }
                    var id = projected ? ProjectedEcgDemoSource.ChannelId(EcgLead.II) : PhysiologyDemoSource.ChannelId(0);
                    var samples = blocks.SelectMany(b => b.Planes.Single(p => p.ChannelId == id).Samples).ToArray();
                    if (!samples.Skip(20).Take(20).Any(v => v > 500) || !samples.Skip(ratio * 50 + 20).Take(20).Any(v => v > 500))
                    { throw new InvalidOperationException("Flutter ventricular RR did not follow selected ratio."); }
                    if (variable)
                    {
                        if (samples.Skip(220).Take(20).Any(v => v > 500) || !samples.Skip(270).Take(20).Any(v => v > 500))
                        { throw new InvalidOperationException("Variable flutter reverted to fixed2:1 ventricular timing."); }
                        if (projected) { EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 270); }
                        else { MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 270); }
                    }
                    Click(window.HoldButton); Click(window.RunButton);
                    var held = window.Trace; var timer = window.ActiveTimer;
                    long before = window.SimulationTimeNs;
                    var ecg = window.EcgConfiguration; var physiology = window.BreathConfiguration;
                    window.IndependentVentricularPeriodInput.Text = "1200";
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton); Unchanged();
                    window.IndependentVentricularPeriodInput.Text = "";
                    window.CardiacActivityInput.SelectedIndex = (int)CardiacActivity.VentricularOnly;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton); Unchanged();
                    Click(window.ResetButton);
                    if (window.ConductionInput.SelectedIndex != selection || window.ActiveTimer is not null || window.BlockCount != 0)
                    { throw new InvalidOperationException("Flutter reset lost selected ratio."); }
                    if (projected)
                    {
                        window.AtrialInput.SelectedIndex = (int)EcgAtrialIllustration.LeftAtrialAbnormality;
                        Click(window.ApplyEcgButton);
                        if (window.EcgConfiguration != ecg) { throw new InvalidOperationException("Flutter accepted normal P illustration."); }
                        Click(window.ResetButton);
                    }
                    void Unchanged()
                    {
                        if (window.EcgConfiguration != ecg || window.BreathConfiguration != physiology ||
                            !ReferenceEquals(window.Trace, held) || !ReferenceEquals(window.ActiveTimer, timer) || window.SimulationTimeNs != before)
                        { throw new InvalidOperationException("Rejected flutter input changed accepted state."); }
                    }
                }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: flutter projected1:1, fixed2:1/3:1/4:1 and variable2/3/4 controls, continuous F/QRS pixels, pressure, recovery and atomic rejection");
    }
}
