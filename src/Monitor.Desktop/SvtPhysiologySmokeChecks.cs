// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class SvtPhysiologySmokeChecks
{
    internal static void Verify()
    {
        var config = PhysiologyDemoConfiguration.SvtPreset;
        var plan = config.ResolvePlan();
        if (plan != SupraventricularTachycardiaReference.CreatePlan()) { throw new InvalidOperationException("SVT physiology plan differs from shared source."); }
        var blocks = MechanicalUncouplingSmokeChecks.Decode(config);
        var normal = MechanicalUncouplingSmokeChecks.Decode(PhysiologyDemoConfiguration.Default);
        foreach (int row in new[] { 1, 4 })
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
            { throw new InvalidOperationException("SVT changed respiration/CO2."); }
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, SupraventricularTachycardiaReference.CreateLeadIIBands()).GenerateBefore(6_000_000_000, 1500, 100);
        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 0).Zip(ii).Any(p => Math.Abs(p.First - p.Second.NormalizedValue) > 1))
        { throw new InvalidOperationException("SVT physiology monitorII mismatch."); }
        var pleth = PlethRunoffSource.Create(plan, SvtPerfusionReference.Pleth);
        var abp = VascularPressureSource.Create(plan, SvtPerfusionReference.Arterial);
        var pa = VascularPressureSource.Create(plan, SvtPerfusionReference.Pulmonary);
        foreach (int row in new[] { 2, 3, 5 })
        {
            var samples = MechanicalUncouplingSmokeChecks.Samples(blocks, row);
            for (int i = 0; i < samples.Length; i++)
            {
                long time = i * 8_000_000L;
                long q32 = row == 2 ? pleth.EvaluateAt(time) : row == 3 ? abp.EvaluateAt(time) : pa.EvaluateAt(time);
                if (samples[i] != (short)FixedPointMath.RoundDivideTiesToEven(q32, FixedPointMath.Q32One))
                { throw new InvalidOperationException("SVT perfusion support or phase mismatch."); }
            }
            if (samples.Skip(250).Distinct().Count() < 10) { throw new InvalidOperationException("SVT perfusion became flat."); }
        }
        var cvpPlan = SvtPerfusionReference.Venous.CreateChannel(plan, PhysiologyDemoSource.ChannelId(6), 0);
        var cvp = PhysiologySignalGenerator.Start(plan, "AcqPressure125@1", 1, cvpPlan.Bands).GenerateBefore(6_000_000_000, 750, 200);
        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 6).Zip(cvp).Any(p => p.First != p.Second.NormalizedValue))
        { throw new InvalidOperationException("SVT CVP overlap mismatch."); }
        foreach (long boundary in new[] { 200_000_000L, 400_000_000, 800_000_000 })
        {
            var source = PhysiologyDemoSource.Create(config);
            for (long time = 200_000_000; time <= boundary; time += 200_000_000) { source.AdvanceTo(time, 50, 1, 100); }
            var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
            var a = source.AdvanceTo(boundary + 200_000_000, 50, 1, 100); var b = restored.AdvanceTo(boundary + 200_000_000, 50, 1, 100);
            if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second))) { throw new InvalidOperationException("SVT physiology wire recovery mismatch."); }
        }
        var window = new WaveformDemoWindow(physiology: true);
        window.Show();
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.VentricularDisorganizationButton); Click(window.StepButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            Click(window.SvtButton); window.Pulse(oldTimer);
            if (window.BreathConfiguration != config || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.SvtInput.IsChecked != true)
            { throw new InvalidOperationException("SVT physiology loader retained VF state."); }
            Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || !string.IsNullOrEmpty(window.BreathConfigurationStatus.Text)) { throw new InvalidOperationException("SVT physiology reapply failed."); }
            if (!window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("首次 QRS 偏移 0 ms") == true)) { throw new InvalidOperationException("SVT physiology summary offset mismatch."); }
            for (int i = 0; i < 30; i++) { Click(window.StepButton); }
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 0);
            VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
            Click(window.HoldButton); Click(window.RunButton);
            var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
            window.IndependentVentricularPeriodInput.Text = "800"; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
            { throw new InvalidOperationException("SVT physiology conflict mutated accepted state."); }
            Click(window.ResetButton);
            if (window.SvtInput.IsChecked != true || !string.IsNullOrEmpty(window.IndependentVentricularPeriodInput.Text)) { throw new InvalidOperationException("SVT physiology reset lost source."); }
            window.SvtInput.IsChecked = false; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != PhysiologyDemoConfiguration.Default) { throw new InvalidOperationException("SVT physiology clear failed."); }
            Click(window.SvtButton); Click(window.WpwButton);
            if (window.SvtInput.IsChecked == true) { throw new InvalidOperationException("WPW loader retained SVT."); }
        }
        finally { window.Close(); }
        foreach (var invalid in new[] { config with { UseVascularReservoir = false }, config with { Wpw = true }, config with { VentricularConductionRatio = 2 }, config with { IndependentVentricularPeriodMilliseconds = 800 }, config with { BundleBlock = EcgBundleBlockIllustration.CompleteLeft } })
        {
            try { PhysiologyDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "Svt.ConflictingModes") { continue; }
            throw new InvalidOperationException("Invalid SVT physiology accepted.");
        }
        Console.WriteLine("ok: SVT physiology full supports, bounded overlap, shared samples/pixels, recovery and atomic lifecycle");
    }
}
