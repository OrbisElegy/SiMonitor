// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class WpwPhysiologySmokeChecks
{
    internal static void Verify()
    {
        Verify(false);
        Verify(true);
    }
    private static void Verify(bool negativeV1)
    {
        var config = PhysiologyDemoConfiguration.WpwPreset with { WpwNegativeV1 = negativeV1 };
        var plan = config.ResolvePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(1_600_000_000, 30);
        if (!events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 100_000_000, 900_000_000 }) ||
            !events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 180_000_000, 980_000_000 }))
        { throw new InvalidOperationException("Physiology WPW did not advance shared ventricular events."); }
        var blocks = DecodeCompletedOutput(config, 6_000_000_000);
        var positive = DecodeCompletedOutput(PhysiologyDemoConfiguration.WpwPreset, 6_000_000_000);
        for (int row = 0; row < 7; row++)
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(positive, row)))
            { throw new InvalidOperationException("Regional V1 variant changed lead II or other physiology."); }
        var shifted = DecodeCompletedOutput(PhysiologyDemoConfiguration.Default with
        { IndependentVentricularPeriodMilliseconds = 800, IndependentVentricularOffsetMilliseconds = 100 }, 6_000_000_000);
        var normal = DecodeCompletedOutput(PhysiologyDemoConfiguration.Default, 6_000_000_000);
        for (int row = 1; row < 7; row++)
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(shifted, row)))
            { throw new InvalidOperationException("WPW non-ECG waveforms do not follow the same shifted events."); }
        foreach (int row in new[] { 1, 4 })
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
            { throw new InvalidOperationException("WPW changed independent respiration or CO2."); }
        foreach (int row in new[] { 2, 3, 5, 6 })
            if (MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
            { throw new InvalidOperationException("WPW retained old perfusion timing."); }
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, WpwReference.CreateLeadIIBands(negativeV1)).GenerateBefore(6_000_000_000, 1500, 100);
        short[] ecgSamples = MechanicalUncouplingSmokeChecks.Samples(blocks, 0);
        if (ecgSamples.Length != ii.Count || ecgSamples.Zip(ii).Any(p => Math.Abs(p.First - p.Second.NormalizedValue) > 1))
        { throw new InvalidOperationException("Physiology WPW differs from shared lead II."); }
        foreach (long boundary in new[] { 200_000_000L, 400_000_000, 800_000_000 })
        {
            VerifyWireRecovery(config, boundary, "WPW");
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
            if (window.WpwNegativeV1Input.IsChecked != negativeV1 || window.WpwInput.IsChecked != true || !string.IsNullOrEmpty(window.IndependentVentricularPeriodInput.Text))
            { throw new InvalidOperationException("WPW physiology reset lost accepted source."); }
            window.WpwNegativeV1Input.IsChecked = !negativeV1; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != (config with { WpwNegativeV1 = !negativeV1 }))
            { throw new InvalidOperationException("WPW variant switch failed."); }
            window.WpwNegativeV1Input.IsChecked = negativeV1; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config)
            { throw new InvalidOperationException("WPW variant roundtrip failed."); }
            window.WpwInput.IsChecked = false; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != PhysiologyDemoConfiguration.Default)
            { throw new InvalidOperationException("WPW physiology clear failed."); }
            Click(window.WpwButton); Click(window.FibrillationButton);
            if (window.WpwInput.IsChecked == true || window.BreathConfiguration.Wpw)
            { throw new InvalidOperationException("AF loader retained WPW."); }
        }
        finally { window.Close(); }
        foreach (var invalid in new[] { config with { Wpw = false, WpwNegativeV1 = true }, config with { BundleBlock = EcgBundleBlockIllustration.CompleteRight }, config with { ConductionPattern = AvConductionPattern.AtrialFlutterIllustration }, config with { VentricularConductionRatio = 2 }, config with { CardiacActivity = CardiacActivity.VentricularOnly } })
        {
            try { PhysiologyDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "Wpw.ConflictingModes") { continue; }
            throw new InvalidOperationException("Conflicting WPW physiology accepted.");
        }
        Console.WriteLine("ok: WPW physiology shared early ECG/ejection, perfusion phase, unchanged respiration, pixels, recovery and atomic lifecycle");
    }

    private const long BlockDurationNs = 200_000_000;

    private static long AcquisitionLatencyNs => Math.Max(
        FrozenSignalAcquisitionProfiles.Get("AcqPleth125@1").LatencyNs,
        FrozenSignalAcquisitionProfiles.Get("AcqCO2_100@1").LatencyNs);

    // Advance through acquisition latency so the requested output interval is
    // complete on every channel, including the slow Pleth and CO2 channels.
    private static WaveformEnvelope[] DecodeCompletedOutput(PhysiologyDemoConfiguration config, long toExclusiveSimTimeNs)
    {
        var source = PhysiologyDemoSource.Create(config);
        var blocks = new List<WaveformEnvelope>();
        for (long timeNs = BlockDurationNs; timeNs <= toExclusiveSimTimeNs + AcquisitionLatencyNs; timeNs += BlockDurationNs)
        {
            blocks.AddRange(source.AdvanceTo(timeNs, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
        }
        if (blocks.Count != toExclusiveSimTimeNs / BlockDurationNs || blocks.Count == 0 ||
            blocks.Where((block, index) => block.StartSimTimeNs != index * BlockDurationNs || block.DurationNs != BlockDurationNs).Any() ||
            blocks[^1].StartSimTimeNs + blocks[^1].DurationNs != toExclusiveSimTimeNs)
        { throw new InvalidOperationException("Physiology fixture did not complete the requested output interval."); }
        return blocks.ToArray();
    }

    private static void VerifyWireRecovery(PhysiologyDemoConfiguration config, long checkpointSimTimeNs, string name)
    {
        var source = PhysiologyDemoSource.Create(config);
        for (long timeNs = BlockDurationNs; timeNs <= checkpointSimTimeNs; timeNs += BlockDurationNs)
        { source.AdvanceTo(timeNs, 50, 1, 100); }
        var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
        long throughSimTimeNs = Math.Max(checkpointSimTimeNs + BlockDurationNs, AcquisitionLatencyNs + BlockDurationNs);
        int comparedBlocks = 0;
        for (long timeNs = checkpointSimTimeNs + BlockDurationNs; timeNs <= throughSimTimeNs; timeNs += BlockDurationNs)
        {
            var expected = source.AdvanceTo(timeNs, 50, 1, 100);
            var actual = restored.AdvanceTo(timeNs, 50, 1, 100);
            if (expected.Count != actual.Count || expected.Zip(actual).Any(pair => !pair.First.SequenceEqual(pair.Second)))
            { throw new InvalidOperationException(name + " physiology wire recovery mismatch."); }
            comparedBlocks += expected.Count;
        }
        if (comparedBlocks == 0)
        { throw new InvalidOperationException(name + " physiology wire recovery did not publish any completed blocks."); }
    }
}
