// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class AtrialFlutterSmokeChecks
{
    internal static void Verify()
    {
        try { PhysiologyDemoSource.Create(PhysiologyDemoConfiguration.Flutter(1) with { UseVascularReservoir = false }); throw new InvalidOperationException("Fast flutter accepted without reservoir."); }
        catch (EventWaveformException e) when (e.ReasonCode == "Flutter.OneToOneRequiresReservoir") { }
        foreach (bool projected in new[] { false, true })
        {
            WaveformDemoWindow window = new(physiology: !projected, projected: projected);
            window.Show();
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            try
            {
                foreach (int variant in new[] { 4, 3, 2, 1, 0 })
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
                        if (ratio == 1) { VerifyFastPerfusion(window.BreathConfiguration, blocks); }
                        MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 20);
                        // Fixed development pressure rows clip high samples. Verify visible
                        // trace points without changing the physiological source to fit UI.
                        int[] pressureIndices = ratio == 1
                            ? Enumerable.Range(100, 150).Where(i => MechanicalUncouplingSmokeChecks.Samples(blocks, 3)[i] < 16000 && MechanicalUncouplingSmokeChecks.Samples(blocks, 5)[i] < 4000).Where(i => i % 5 == 0).Take(3).ToArray()
                            : new[] { 150, 200 };
                        if (pressureIndices.Length < 2) { throw new InvalidOperationException("Insufficient visible fast flutter pressure samples."); }
                        VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, pressureIndices);
                    }
                    var id = projected ? ProjectedEcgDemoSource.ChannelId(EcgLead.II) : PhysiologyDemoSource.ChannelId(0);
                    short[] samples = blocks.SelectMany(b => b.Planes.Single(p => p.ChannelId == id).Samples).ToArray();
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
                    if (!projected && ratio == 1)
                    {
                        window.VascularReservoirInput.IsChecked = false;
                        Click(window.ApplyBreathButton); Unchanged();
                        if (string.IsNullOrEmpty(window.BreathConfigurationStatus.Text)) { throw new InvalidOperationException("Fast flutter reservoir rejection not shown."); }
                        window.VascularReservoirInput.IsChecked = true;
                    }
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
    private static void VerifyFastPerfusion(PhysiologyDemoConfiguration config, WaveformEnvelope[] blocks)
    {
        var plan = config.ResolvePlan();
        if (plan != AtrialFlutterReference.CreatePlan(1)) { throw new InvalidOperationException("Fast flutter lost shared event offsets."); }
        var normal = MechanicalUncouplingSmokeChecks.Decode(PhysiologyDemoConfiguration.Default);
        foreach (int row in new[] { 1, 4 })
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
            { throw new InvalidOperationException("Fast flutter changed default respiration/CO2."); }
        var pleth = PlethRunoffSource.Create(plan, FlutterOneToOnePerfusionReference.Pleth);
        var arterial = VascularPressureSource.Create(plan, FlutterOneToOnePerfusionReference.Arterial);
        var pulmonary = VascularPressureSource.Create(plan, FlutterOneToOnePerfusionReference.Pulmonary);
        foreach (int row in new[] { 2, 3, 5 })
        {
            short[] samples = MechanicalUncouplingSmokeChecks.Samples(blocks, row);
            for (int i = 0; i < samples.Length; i++)
            {
                long time = i * 8_000_000L;
                long expected = row == 2 ? pleth.EvaluateAt(time) : row == 3 ? arterial.EvaluateAt(time) : pulmonary.EvaluateAt(time);
                if (samples[i] != FixedPointMath.RoundDivideTiesToEven(expected, FixedPointMath.Q32One))
                { throw new InvalidOperationException("Fast flutter perfusion support/phase differs from shared source."); }
            }
        }
        var cvpPlan = FlutterOneToOnePerfusionReference.Venous.CreateChannel(plan, PhysiologyDemoSource.ChannelId(6), 0);
        var cvp = PhysiologySignalGenerator.Start(plan, "AcqPressure125@1", 1, cvpPlan.Bands).GenerateBefore(4_000_000_000, 500, 100);
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, AtrialFlutterReference.CreateLeadIIBands(1)).GenerateBefore(4_000_000_000, 1000, 100);
        if (!MechanicalUncouplingSmokeChecks.Samples(blocks, 6).SequenceEqual(cvp.Select(s => s.NormalizedValue)) || MechanicalUncouplingSmokeChecks.Samples(blocks, 0).Zip(ii).Any(p => Math.Abs(p.First - p.Second.NormalizedValue) > 1))
        { throw new InvalidOperationException("Fast flutter CVP/ECG mismatch."); }
        var source = PhysiologyDemoSource.Create(config);
        for (int step = 1; step <= 40; step++)
        {
            var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
            var a = source.AdvanceTo(step * 200_000_000L, 50, 1, 100); var b = restored.AdvanceTo(step * 200_000_000L, 50, 1, 100);
            if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second)))
            { throw new InvalidOperationException("Fast flutter wire recovery differs."); }
        }
    }

}
