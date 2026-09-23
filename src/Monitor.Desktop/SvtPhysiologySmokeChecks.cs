// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Acquisition;
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
        var blocks = DecodeCompletedOutput(config, 6_000_000_000);
        var normal = DecodeCompletedOutput(PhysiologyDemoConfiguration.Default, 6_000_000_000);
        foreach (int row in new[] { 1, 4 })
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
            { throw new InvalidOperationException("SVT changed respiration/CO2."); }
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, SupraventricularTachycardiaReference.CreateLeadIIBands()).GenerateBefore(6_000_000_000, 1500, 100);
        short[] ecgSamples = MechanicalUncouplingSmokeChecks.Samples(blocks, 0);
        if (ecgSamples.Length != ii.Count || ecgSamples.Zip(ii).Any(p => Math.Abs(p.First - p.Second.NormalizedValue) > 1))
        { throw new InvalidOperationException("SVT physiology monitorII mismatch."); }
        var pleth = PlethRunoffSource.Create(plan, SvtPerfusionReference.Pleth);
        var abp = VascularPressureSource.Create(plan, SvtPerfusionReference.Arterial);
        var pa = VascularPressureSource.Create(plan, SvtPerfusionReference.Pulmonary);
        foreach (int row in new[] { 2, 3, 5 })
        {
            var samples = MechanicalUncouplingSmokeChecks.Samples(blocks, row);
            if (samples.Length != 750)
            { throw new InvalidOperationException("SVT perfusion did not publish six seconds of completed samples."); }
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
        if (!MechanicalUncouplingSmokeChecks.Samples(blocks, 6).SequenceEqual(cvp.Select(sample => sample.NormalizedValue)))
        { throw new InvalidOperationException("SVT CVP overlap mismatch."); }
        foreach (long boundary in new[] { 200_000_000L, 400_000_000, 800_000_000 })
        {
            VerifyWireRecovery(config, boundary, "SVT");
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
