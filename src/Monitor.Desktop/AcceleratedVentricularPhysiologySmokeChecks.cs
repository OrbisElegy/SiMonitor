// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class AcceleratedVentricularPhysiologySmokeChecks
{
    internal static void Verify() { Verify(false); Verify(true); Verify(false, true); }

    private static void Verify(bool fusion, bool capture = false)
    {
        var config = PhysiologyDemoConfiguration.AivrPreset with { AivrFusion = fusion, AivrCapture = capture };
        var plan = config.ResolvePlan();
        if (plan != AcceleratedVentricularReference.CreatePlan()) { throw new InvalidOperationException("AIVR physiology plan differs from shared source."); }
        var blocks = Decode(config);
        var plain = Decode(PhysiologyDemoConfiguration.AivrPreset);
        foreach (int row in new[] { 1, 2, 3, 4, 5, 6 })
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(plain, row)))
            { throw new InvalidOperationException("AIVR fusion changed fixed non-ECG inputs."); }
        var normal = Decode(PhysiologyDemoConfiguration.Default);
        foreach (int row in new[] { 1, 4 })
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
            { throw new InvalidOperationException("AIVR changed respiration/CO2."); }
        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 0).Length != 1500)
        { throw new InvalidOperationException("AIVR test must include six seconds of completed raw ECG after acquisition delay."); }
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, AcceleratedVentricularReference.CreateLeadIIBands(fusion, capture)).GenerateBefore(6_000_000_000, 1500, 100);
        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 0).Zip(ii).Any(p => Math.Abs(p.First - p.Second.NormalizedValue) > 1))
        { throw new InvalidOperationException("AIVR physiology monitorII mismatch."); }
        var pleth = PlethRunoffSource.Create(plan, FixedPerfusionPresets.AcceleratedVentricular.Pleth);
        var abp = VascularPressureSource.Create(plan, FixedPerfusionPresets.AcceleratedVentricular.Arterial);
        var pa = VascularPressureSource.Create(plan, FixedPerfusionPresets.AcceleratedVentricular.Pulmonary);
        foreach (int row in new[] { 2, 3, 5 })
        {
            short[] samples = MechanicalUncouplingSmokeChecks.Samples(blocks, row);
            for (int i = 0; i < samples.Length; i++)
            {
                long time = i * 8_000_000L;
                long q32 = row == 2 ? pleth.EvaluateAt(time) : row == 3 ? abp.EvaluateAt(time) : pa.EvaluateAt(time);
                if (samples[i] != (short)FixedPointMath.RoundDivideTiesToEven(q32, FixedPointMath.Q32One))
                { throw new InvalidOperationException("AIVR perfusion support or phase mismatch."); }
            }
            if (samples.Skip(250).Distinct().Count() < 10) { throw new InvalidOperationException("AIVR perfusion became flat."); }
        }
        var cvpPlan = FixedPerfusionPresets.AcceleratedVentricular.Venous.CreateChannel(plan, PhysiologyDemoSource.ChannelId(6), 0);
        var cvp = PhysiologySignalGenerator.Start(plan, "AcqPressure125@1", 1, cvpPlan.Bands,
            pressureBaselineCentiMmHg: cvpPlan.PressureBaselineCentiMmHg).GenerateBefore(6_000_000_000, 750, 200);
        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 6).Zip(cvp).Any(p => p.First != p.Second.NormalizedValue))
        { throw new InvalidOperationException("AIVR CVP overlap mismatch."); }
        var artifactConfig = config with { RespCardiacArtifactCounts = 200 };
        var artifactBlocks = Decode(artifactConfig);
        var artifactPlan = new RespirationPlan(1000, 200).CreateChannel(plan, PhysiologyDemoSource.ChannelId(1), 0);
        var expectedResp = PhysiologySignalGenerator.Start(plan, "AcqResp125@1", 1, artifactPlan.Bands).GenerateBefore(6_000_000_000, 750, 200);
        if (MechanicalUncouplingSmokeChecks.Samples(artifactBlocks, 1).Zip(expectedResp).Any(p => p.First != p.Second.NormalizedValue))
        { throw new InvalidOperationException("AIVR respiratory cardiac artifact clock mismatch."); }
        foreach (int row in new[] { 0, 2, 3, 4, 5, 6 })
            if (!MechanicalUncouplingSmokeChecks.Samples(artifactBlocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(blocks, row)))
            { throw new InvalidOperationException("AIVR respiratory artifact leaked to another channel."); }
        foreach (long boundary in new[] { 200_000_000L, 400_000_000, 800_000_000, 4_200_000_000, 4_400_000_000, 5_000_000_000, 5_200_000_000 })
        {
            var source = PhysiologyDemoSource.Create(config);
            for (long time = 200_000_000; time <= boundary; time += 200_000_000) { source.AdvanceTo(time, 50, 1, 100); }
            var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
            var a = source.AdvanceTo(boundary + 200_000_000, 50, 1, 100); var b = restored.AdvanceTo(boundary + 200_000_000, 50, 1, 100);
            if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second))) { throw new InvalidOperationException("AIVR physiology wire recovery mismatch."); }
        }
        var window = new WaveformDemoWindow(physiology: true);
        window.Show();
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.VentricularDisorganizationButton); Click(window.StepButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            Click(window.AivrButton); window.AivrCaptureInput.IsChecked = capture; window.AivrFusionInput.IsChecked = fusion; Click(window.ApplyBreathButton); window.Pulse(oldTimer);
            if (window.BreathConfiguration != config || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.AivrInput.IsChecked != true)
            { throw new InvalidOperationException("AIVR physiology loader retained VF state."); }
            Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || !string.IsNullOrEmpty(window.BreathConfigurationStatus.Text)) { throw new InvalidOperationException("AIVR physiology reapply failed."); }
            if (!window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("首次 QRS 偏移 120 ms") == true)) { throw new InvalidOperationException("AIVR physiology summary offset mismatch."); }
            for (int i = 0; i < 40; i++) { Click(window.StepButton); }
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 0);
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 35);
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 220);
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 410);
            VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
            Click(window.HoldButton); Click(window.RunButton);
            var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
            window.IndependentVentricularPeriodInput.Text = "800"; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
            { throw new InvalidOperationException("AIVR physiology conflict mutated accepted state."); }
            Click(window.ResetButton); Click(window.RunButton);
            timer = window.ActiveTimer; trace = window.Trace; time = window.SimulationTimeNs;
            window.AivrFusionInput.IsChecked = true; window.AivrCaptureInput.IsChecked = true; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
            { throw new InvalidOperationException("AIVR capture/fusion conflict changed native state."); }
            Click(window.ResetButton);
            if (window.AivrInput.IsChecked != true || window.AivrFusionInput.IsChecked != fusion || window.AivrCaptureInput.IsChecked != capture || window.VtInput.IsChecked == true || !string.IsNullOrEmpty(window.IndependentVentricularPeriodInput.Text))
            { throw new InvalidOperationException("AIVR reset lost source."); }
            window.AivrCaptureInput.IsChecked = false; window.AivrFusionInput.IsChecked = !fusion; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != (config with { AivrFusion = !fusion, AivrCapture = false })) { throw new InvalidOperationException("AIVR fusion toggle failed."); }
            window.AivrCaptureInput.IsChecked = capture; window.AivrFusionInput.IsChecked = fusion; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config) { throw new InvalidOperationException("AIVR fusion toggle restore failed."); }
            window.AivrInput.IsChecked = false; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != PhysiologyDemoConfiguration.Default) { throw new InvalidOperationException("AIVR physiology clear failed."); }
            Click(window.AivrButton); Click(window.WpwButton);
            if (window.AivrInput.IsChecked == true || window.AivrFusionInput.IsChecked == true || window.AivrCaptureInput.IsChecked == true || window.VtInput.IsChecked == true || window.VtFusionInput.IsChecked == true || window.VtCaptureInput.IsChecked == true || window.VtBidirectionalInput.IsChecked == true || window.VtTwistingInput.IsChecked == true) { throw new InvalidOperationException("WPW loader retained AIVR."); }
        }
        finally { window.Close(); }
        foreach (var invalid in new[] { config with { Aivr = false, AivrFusion = true }, config with { Aivr = false, AivrFusion = false, AivrCapture = true }, config with { AivrFusion = true, AivrCapture = true }, config with { UseVascularReservoir = false }, config with { Wpw = true },
            config with { Vt = true }, config with { VtFusion = true }, config with { VtCapture = true },
            config with { VtTwisting = true }, config with { VtBidirectional = true }, config with { Svt = true },
            config with { VentricularConductionRatio = 2 }, config with { IndependentVentricularPeriodMilliseconds = 750 },
            config with { BundleBlock = EcgBundleBlockIllustration.CompleteLeft } })
        {
            try { PhysiologyDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "Aivr.ConflictingModes") { continue; }
            throw new InvalidOperationException("Invalid AIVR physiology accepted.");
        }
        Console.WriteLine("ok: AIVR physiology full supports, bounded overlap, shared samples/pixels, recovery and atomic lifecycle");
    }
    // Pleth has2s acquisition delay; advance to8s to publish the first6s
    // across all channels, including several drifting atrial/ventricular phases.
    private static WaveformEnvelope[] Decode(PhysiologyDemoConfiguration config)
    {
        var source = PhysiologyDemoSource.Create(config);
        return Enumerable.Range(1, 40).SelectMany(step => source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
            .Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
    }

}
