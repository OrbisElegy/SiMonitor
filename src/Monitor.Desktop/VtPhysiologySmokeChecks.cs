// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class VtPhysiologySmokeChecks
{
    internal static void Verify()
    {
        var config = PhysiologyDemoConfiguration.VtPreset;
        var plan = config.ResolvePlan();
        if (plan != VentricularTachycardiaReference.CreatePlan()) { throw new InvalidOperationException("VT physiology plan differs from shared source."); }
        var blocks = MechanicalUncouplingSmokeChecks.Decode(config);
        var normal = MechanicalUncouplingSmokeChecks.Decode(PhysiologyDemoConfiguration.Default);
        foreach (int row in new[] { 1, 4 })
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
            { throw new InvalidOperationException("VT changed respiration/CO2."); }
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, VentricularTachycardiaReference.CreateLeadIIBands()).GenerateBefore(6_000_000_000, 1500, 100);
        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 0).Zip(ii).Any(p => Math.Abs(p.First - p.Second.NormalizedValue) > 1))
        { throw new InvalidOperationException("VT physiology monitorII mismatch."); }
        var pleth = PlethRunoffSource.Create(plan, VtPerfusionReference.Pleth);
        var abp = VascularPressureSource.Create(plan, VtPerfusionReference.Arterial);
        var pa = VascularPressureSource.Create(plan, VtPerfusionReference.Pulmonary);
        foreach (int row in new[] { 2, 3, 5 })
        {
            var samples = MechanicalUncouplingSmokeChecks.Samples(blocks, row);
            for (int i = 0; i < samples.Length; i++)
            {
                long time = i * 8_000_000L;
                long q32 = row == 2 ? pleth.EvaluateAt(time) : row == 3 ? abp.EvaluateAt(time) : pa.EvaluateAt(time);
                if (samples[i] != (short)FixedPointMath.RoundDivideTiesToEven(q32, FixedPointMath.Q32One))
                { throw new InvalidOperationException("VT perfusion support or phase mismatch."); }
            }
            if (samples.Skip(250).Distinct().Count() < 10) { throw new InvalidOperationException("VT perfusion became flat."); }
        }
        var cvpPlan = VtPerfusionReference.Venous.CreateChannel(plan, PhysiologyDemoSource.ChannelId(6), 0);
        var cvp = PhysiologySignalGenerator.Start(plan, "AcqPressure125@1", 1, cvpPlan.Bands).GenerateBefore(6_000_000_000, 750, 200);
        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 6).Zip(cvp).Any(p => p.First != p.Second.NormalizedValue))
        { throw new InvalidOperationException("VT CVP overlap mismatch."); }
        var artifactConfig = config with { RespCardiacArtifactCounts = 200 };
        var artifactBlocks = MechanicalUncouplingSmokeChecks.Decode(artifactConfig);
        var artifactPlan = new RespirationPlan(1000, 200).CreateChannel(plan, PhysiologyDemoSource.ChannelId(1), 0);
        var expectedResp = PhysiologySignalGenerator.Start(plan, "AcqResp125@1", 1, artifactPlan.Bands).GenerateBefore(6_000_000_000, 750, 200);
        if (MechanicalUncouplingSmokeChecks.Samples(artifactBlocks, 1).Zip(expectedResp).Any(p => p.First != p.Second.NormalizedValue))
        { throw new InvalidOperationException("VT respiratory cardiac artifact clock mismatch."); }
        foreach (int row in new[] { 0, 2, 3, 4, 5, 6 })
            if (!MechanicalUncouplingSmokeChecks.Samples(artifactBlocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(blocks, row)))
            { throw new InvalidOperationException("VT respiratory artifact leaked to another channel."); }
        foreach (long boundary in new[] { 200_000_000L, 400_000_000, 800_000_000 })
        {
            var source = PhysiologyDemoSource.Create(config);
            for (long time = 200_000_000; time <= boundary; time += 200_000_000) { source.AdvanceTo(time, 50, 1, 100); }
            var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
            var a = source.AdvanceTo(boundary + 200_000_000, 50, 1, 100); var b = restored.AdvanceTo(boundary + 200_000_000, 50, 1, 100);
            if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second))) { throw new InvalidOperationException("VT physiology wire recovery mismatch."); }
        }
        var window = new WaveformDemoWindow(physiology: true);
        window.Show();
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.VentricularDisorganizationButton); Click(window.StepButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            Click(window.VtButton); window.Pulse(oldTimer);
            if (window.BreathConfiguration != config || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.VtInput.IsChecked != true)
            { throw new InvalidOperationException("VT physiology loader retained VF state."); }
            Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || !string.IsNullOrEmpty(window.BreathConfigurationStatus.Text)) { throw new InvalidOperationException("VT physiology reapply failed."); }
            if (!window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("首次 QRS 偏移 120 ms") == true)) { throw new InvalidOperationException("VT physiology summary offset mismatch."); }
            for (int i = 0; i < 30; i++) { Click(window.StepButton); }
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 0);
            VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
            Click(window.HoldButton); Click(window.RunButton);
            var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
            window.IndependentVentricularPeriodInput.Text = "800"; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
            { throw new InvalidOperationException("VT physiology conflict mutated accepted state."); }
            Click(window.ResetButton);
            if (window.VtInput.IsChecked != true || !string.IsNullOrEmpty(window.IndependentVentricularPeriodInput.Text)) { throw new InvalidOperationException("VT physiology reset lost source."); }
            window.VtInput.IsChecked = false; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != PhysiologyDemoConfiguration.Default) { throw new InvalidOperationException("VT physiology clear failed."); }
            Click(window.VtButton); Click(window.WpwButton);
            if (window.VtInput.IsChecked == true) { throw new InvalidOperationException("WPW loader retained VT."); }
        }
        finally { window.Close(); }
        foreach (var invalid in new[] { config with { UseVascularReservoir = false }, config with { Wpw = true }, config with { Svt = true }, config with { VentricularConductionRatio = 2 }, config with { IndependentVentricularPeriodMilliseconds = 800 }, config with { BundleBlock = EcgBundleBlockIllustration.CompleteLeft } })
        {
            try { PhysiologyDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "Vt.ConflictingModes") { continue; }
            throw new InvalidOperationException("Invalid VT physiology accepted.");
        }
        Console.WriteLine("ok: VT physiology full supports, bounded overlap, shared samples/pixels, recovery and atomic lifecycle");
    }
}
