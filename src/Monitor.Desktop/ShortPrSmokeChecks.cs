// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class ShortPrSmokeChecks
{
    internal static void Verify()
    {
        var ecg = ProjectedEcgDemoConfiguration.ShortPrPreset;
        var physiology = PhysiologyDemoConfiguration.ShortPrPreset;
        var blocks = DecodeCompletedOutput(physiology, 6_000_000_000);
        var wpw = DecodeCompletedOutput(PhysiologyDemoConfiguration.WpwPreset, 6_000_000_000);
        for (int row = 1; row < 7; row++)
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(wpw, row)))
            { throw new InvalidOperationException("Short PR changed perfusion despite identical mechanical events."); }
        var ii = PhysiologySignalGenerator.Start(physiology.ResolvePlan(), "AcqECGMonitor250@1", 1, ShortPrReference.CreateLeadIIBands()).GenerateBefore(6_000_000_000, 1500, 100);
        short[] ecgSamples = MechanicalUncouplingSmokeChecks.Samples(blocks, 0);
        if (ecgSamples.Length != ii.Count || ecgSamples.Zip(ii).Any(p => Math.Abs(p.First - p.Second.NormalizedValue) > 1))
        { throw new InvalidOperationException("Short PR monitorII source mismatch."); }
        foreach (bool projected in new[] { true, false })
        {
            var window = new WaveformDemoWindow(projected: projected, physiology: !projected);
            window.Show();
            void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Button apply = projected ? window.ApplyEcgButton : window.ApplyBreathButton;
            bool Accepted() => projected ? window.EcgConfiguration == ecg : window.BreathConfiguration == physiology;
            string? Status() => projected ? window.EcgConfigurationStatus.Text : window.BreathConfigurationStatus.Text;
            try
            {
                Click(window.WpwButton); window.WpwNegativeV1Input.IsChecked = true; Click(apply);
                Click(window.StepButton); Click(window.RunButton); var oldTimer = window.ActiveTimer;
                Click(window.ShortPrButton); window.Pulse(oldTimer);
                if (!Accepted() || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.WpwInput.IsChecked == true || window.WpwNegativeV1Input.IsChecked == true)
                { throw new InvalidOperationException("Short PR loader retained WPW/timer state."); }
                Click(apply);
                if (!Accepted() || !string.IsNullOrEmpty(Status())) { throw new InvalidOperationException("Short PR reapply failed."); }
                var source = ProjectedEcgDemoSource.Create(ecg);
                var projectedBlocks = new List<WaveformEnvelope>();
                for (int step = 1; step <= 30; step++)
                {
                    Click(window.StepButton);
                    projectedBlocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(b => WaveformEnvelopeCodec.Decode(b)));
                }
                if (projected)
                {
                    foreach (int sample in new[] { 0, 25, 40, 95 })
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, projectedBlocks.ToArray(), sample, [EcgLead.I, EcgLead.II, EcgLead.V1, EcgLead.V5]);
                    var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
                    var a = source.AdvanceTo(6_200_000_000, 50, 1, 100); var b = restored.AdvanceTo(6_200_000_000, 50, 1, 100);
                    if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second))) { throw new InvalidOperationException("Short PR wire recovery mismatch."); }
                }
                else
                {
                    MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 25);
                    VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
                    if (!window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("首次 QRS 偏移 100 ms") == true))
                    { throw new InvalidOperationException("Short PR summary offset mismatch."); }
                    VerifyWireRecovery(physiology, 200_000_000, "Short PR");
                }
                Click(window.HoldButton); Click(window.RunButton);
                var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
                window.WpwInput.IsChecked = true; Click(apply);
                if (!Accepted() || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(Status()))
                { throw new InvalidOperationException("Short PR conflict mutated accepted state."); }
                Click(window.ResetButton);
                if (window.ShortPrInput.IsChecked != true || window.WpwInput.IsChecked == true) { throw new InvalidOperationException("Short PR reset lost accepted source."); }
                window.ShortPrInput.IsChecked = false; Click(apply);
                if (projected ? window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default : window.BreathConfiguration != PhysiologyDemoConfiguration.Default)
                { throw new InvalidOperationException("Short PR clear failed."); }
                Click(window.ShortPrButton); Click(window.WpwButton);
                if (window.ShortPrInput.IsChecked == true) { throw new InvalidOperationException("WPW loader retained short PR mode."); }
            }
            finally { window.Close(); }
        }
        foreach (var invalid in new[] { ecg with { Wpw = true }, ecg with { WpwNegativeV1 = true }, ecg with { PrIntervalMilliseconds = 160 }, ecg with { QrsDurationMilliseconds = 140 }, ecg with { VentricularConductionRatio = 2 } })
        {
            try { ProjectedEcgDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "ShortPr.ConflictingModes") { continue; }
            throw new InvalidOperationException("Invalid short PR source accepted.");
        }
        foreach (var invalid in new[] { physiology with { Wpw = true }, physiology with { WpwNegativeV1 = true }, physiology with { IndependentVentricularPeriodMilliseconds = 800 }, physiology with { VentricularConductionRatio = 2 }, physiology with { BundleBlock = EcgBundleBlockIllustration.CompleteLeft } })
        {
            try { PhysiologyDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "ShortPr.ConflictingModes") { continue; }
            throw new InvalidOperationException("Invalid short PR physiology accepted.");
        }
        Console.WriteLine("ok: short PR/no delta both demos, shared ECG/perfusion, pixels, recovery and atomic lifecycle");
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
