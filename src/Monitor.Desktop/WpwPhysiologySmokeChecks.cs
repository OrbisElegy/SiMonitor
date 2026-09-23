// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class WpwPhysiologySmokeChecks
{
    internal static void Verify()
    {
        foreach (bool smallerDelta in new[] { false, true })
        { Verify(false, smallerDelta); Verify(true, smallerDelta); }
    }
    private static void Verify(bool negativeV1, bool smallerDelta)
    {
        var config = PhysiologyDemoConfiguration.WpwPreset with { WpwNegativeV1 = negativeV1, WpwSmallerDelta = smallerDelta };
        var plan = config.ResolvePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(1_600_000_000, 30);
        if (!events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 100_000_000, 900_000_000 }) ||
            !events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 180_000_000, 980_000_000 }))
        { throw new InvalidOperationException("Physiology WPW did not advance shared ventricular events."); }
        var blocks = MechanicalUncouplingSmokeChecks.Decode(config);
        var positive = MechanicalUncouplingSmokeChecks.Decode(PhysiologyDemoConfiguration.WpwPreset);
        for (int row = smallerDelta ? 1 : 0; row < 7; row++)
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(positive, row)))
            { throw new InvalidOperationException("Regional V1 variant changed lead II or other physiology."); }
        var shifted = MechanicalUncouplingSmokeChecks.Decode(PhysiologyDemoConfiguration.Default with
        { IndependentVentricularPeriodMilliseconds = 800, IndependentVentricularOffsetMilliseconds = 100 });
        var normal = MechanicalUncouplingSmokeChecks.Decode(PhysiologyDemoConfiguration.Default);
        for (int row = 1; row < 7; row++)
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(shifted, row)))
            { throw new InvalidOperationException("WPW non-ECG waveforms do not follow the same shifted events."); }
        foreach (int row in new[] { 1, 4 })
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
            { throw new InvalidOperationException("WPW changed independent respiration or CO2."); }
        foreach (int row in new[] { 2, 3, 5, 6 })
            if (MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
            { throw new InvalidOperationException("WPW retained old perfusion timing."); }
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, WpwReference.CreateLeadIIBands(negativeV1, smallerDelta)).GenerateBefore(6_000_000_000, 1500, 100);
        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 0).Zip(ii).Any(p => Math.Abs(p.First - p.Second.NormalizedValue) > 1))
        { throw new InvalidOperationException("Physiology WPW differs from shared lead II."); }
        foreach (long boundary in new[] { 200_000_000L, 400_000_000, 800_000_000 })
        {
            var source = PhysiologyDemoSource.Create(config);
            for (long time = 200_000_000; time <= boundary; time += 200_000_000) { source.AdvanceTo(time, 50, 1, 100); }
            var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
            var a = source.AdvanceTo(boundary + 200_000_000, 50, 1, 100);
            var b = restored.AdvanceTo(boundary + 200_000_000, 50, 1, 100);
            if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second)))
            { throw new InvalidOperationException("WPW physiology wire recovery diverged."); }
        }
        var window = new WaveformDemoWindow(physiology: true);
        window.Show();
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.VentricularDisorganizationButton); Click(window.StepButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            Click(window.WpwButton); window.Pulse(oldTimer);
            window.WpwNegativeV1Input.IsChecked = negativeV1;
            window.WpwSmallerDeltaInput.IsChecked = smallerDelta;
            Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.WpwInput.IsChecked != true)
            { throw new InvalidOperationException("WPW physiology loader retained previous state."); }
            if (!window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("首次 QRS 偏移 100 ms") == true))
            { throw new InvalidOperationException("WPW physiology summary retained old QRS offset."); }
            Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || !string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
            { throw new InvalidOperationException("WPW physiology reapply failed."); }
            for (int i = 0; i < 30; i++) { Click(window.StepButton); }
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 25);
            VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
            Click(window.HoldButton); Click(window.RunButton);
            var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
            window.IndependentVentricularPeriodInput.Text = "800"; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
            { throw new InvalidOperationException("Conflicting WPW edit mutated accepted state."); }
            Click(window.ResetButton);
            if (window.WpwSmallerDeltaInput.IsChecked != smallerDelta || window.WpwNegativeV1Input.IsChecked != negativeV1 || window.WpwInput.IsChecked != true || !string.IsNullOrEmpty(window.IndependentVentricularPeriodInput.Text))
            { throw new InvalidOperationException("WPW physiology reset lost accepted source."); }
            window.WpwNegativeV1Input.IsChecked = !negativeV1; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != (config with { WpwNegativeV1 = !negativeV1 }))
            { throw new InvalidOperationException("WPW variant switch failed."); }
            window.WpwNegativeV1Input.IsChecked = negativeV1; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config)
            { throw new InvalidOperationException("WPW variant roundtrip failed."); }
            window.WpwSmallerDeltaInput.IsChecked = !smallerDelta; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != (config with { WpwSmallerDelta = !smallerDelta }))
            { throw new InvalidOperationException("WPW smaller-delta switch failed."); }
            window.WpwSmallerDeltaInput.IsChecked = smallerDelta; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config) { throw new InvalidOperationException("WPW smaller-delta roundtrip failed."); }
            window.WpwInput.IsChecked = false; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != PhysiologyDemoConfiguration.Default)
            { throw new InvalidOperationException("WPW physiology clear failed."); }
            Click(window.WpwButton); Click(window.FibrillationButton);
            if (window.WpwInput.IsChecked == true || window.BreathConfiguration.Wpw)
            { throw new InvalidOperationException("AF loader retained WPW."); }
        }
        finally { window.Close(); }
        foreach (var invalid in new[] { config with { Wpw = false, WpwSmallerDelta = true }, config with { Wpw = false, WpwNegativeV1 = true }, config with { BundleBlock = EcgBundleBlockIllustration.CompleteRight }, config with { ConductionPattern = AvConductionPattern.AtrialFlutterIllustration }, config with { VentricularConductionRatio = 2 }, config with { CardiacActivity = CardiacActivity.VentricularOnly } })
        {
            try { PhysiologyDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "Wpw.ConflictingModes") { continue; }
            throw new InvalidOperationException("Conflicting WPW physiology accepted.");
        }
        Console.WriteLine("ok: WPW physiology shared early ECG/ejection, perfusion phase, unchanged respiration, pixels, recovery and atomic lifecycle");
    }
}
