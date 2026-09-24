// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class NormalPrDeltaSmokeChecks
{
    internal static void Verify()
    { Verify(false); Verify(true); }
    private static void Verify(bool prolongedPr)
    {
        var ecg = ProjectedEcgDemoConfiguration.NormalPrDeltaPreset with { ProlongedPrDelta = prolongedPr, PrIntervalMilliseconds = prolongedPr ? 240 : 160 };
        var physiology = PhysiologyDemoConfiguration.NormalPrDeltaPreset with { ProlongedPrDelta = prolongedPr };
        var blocks = PhysiologyChannelSmokeChecks.DecodeCompletedOutput(physiology, 6_000_000_000);
        var normal = PhysiologyChannelSmokeChecks.DecodeCompletedOutput(prolongedPr ? PhysiologyDemoConfiguration.Default with { IndependentVentricularPeriodMilliseconds = 800, IndependentVentricularOffsetMilliseconds = 240 } : PhysiologyDemoConfiguration.Default, 6_000_000_000);
        for (int row = 1; row < 7; row++)
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
            { throw new InvalidOperationException("Normal PR delta changed perfusion despite identical mechanical events."); }
        var ii = PhysiologySignalGenerator.Start(physiology.ResolvePlan(), "AcqECGMonitor250@1", 1, NormalPrDeltaReference.CreateLeadIIBands(prolongedPr)).GenerateBefore(6_000_000_000, 1500, 100);
        short[] ecgSamples = MechanicalUncouplingSmokeChecks.Samples(blocks, 0);
        if (ecgSamples.Length != ii.Count || ecgSamples.Zip(ii).Any(p => Math.Abs(p.First - p.Second.NormalizedValue) > 1))
        { throw new InvalidOperationException("Normal PR delta monitorII source mismatch."); }
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
                Click(window.ShortPrButton); Click(window.NormalPrDeltaButton);
                window.ProlongedPrDeltaInput.IsChecked = prolongedPr; Click(apply);
                if (!Accepted() || window.ShortPrInput.IsChecked == true) { throw new InvalidOperationException("Normal PR delta retained short PR source."); }
                Click(window.WpwButton); window.WpwNegativeV1Input.IsChecked = true; Click(apply);
                Click(window.StepButton); Click(window.RunButton); var oldTimer = window.ActiveTimer;
                Click(window.NormalPrDeltaButton); window.Pulse(oldTimer);
                if (window.SimulationTimeNs != 0 || window.ActiveTimer is not null) { throw new InvalidOperationException("PR/delta loader retained timer."); }
                window.ProlongedPrDeltaInput.IsChecked = prolongedPr; Click(apply);
                if (!Accepted() || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.WpwInput.IsChecked == true || window.WpwNegativeV1Input.IsChecked == true)
                { throw new InvalidOperationException("Normal PR delta loader retained WPW/timer state."); }
                Click(apply);
                if (!Accepted() || !string.IsNullOrEmpty(Status())) { throw new InvalidOperationException("Normal PR delta reapply failed."); }
                var source = ProjectedEcgDemoSource.Create(ecg);
                var projectedBlocks = new List<WaveformEnvelope>();
                for (int step = 1; step <= 30; step++)
                {
                    Click(window.StepButton);
                    projectedBlocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(b => WaveformEnvelopeCodec.Decode(b)));
                }
                if (projected)
                {
                    foreach (int sample in new[] { 0, prolongedPr ? 60 : 40, prolongedPr ? 75 : 55, prolongedPr ? 130 : 110 })
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, projectedBlocks.ToArray(), sample, [EcgLead.I, EcgLead.II, EcgLead.V1, EcgLead.V5]);
                    var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
                    var a = source.AdvanceTo(6_200_000_000, 50, 1, 100); var b = restored.AdvanceTo(6_200_000_000, 50, 1, 100);
                    if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second))) { throw new InvalidOperationException("Normal PR delta wire recovery mismatch."); }
                }
                else
                {
                    MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, prolongedPr ? 60 : 40);
                    VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
                    if (!window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains(prolongedPr ? "首次 QRS 偏移 240 ms" : "首次 QRS 偏移 160 ms") == true))
                    { throw new InvalidOperationException("Normal PR delta summary offset mismatch."); }
                    PhysiologyChannelSmokeChecks.VerifyWireRecovery(physiology, 200_000_000, "PR with delta");
                }
                Click(window.HoldButton); Click(window.RunButton);
                var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
                window.WpwInput.IsChecked = true; Click(apply);
                if (!Accepted() || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(Status()))
                { throw new InvalidOperationException("Normal PR delta conflict mutated accepted state."); }
                Click(window.ResetButton);
                if (window.ProlongedPrDeltaInput.IsChecked != prolongedPr || window.NormalPrDeltaInput.IsChecked != true || window.WpwInput.IsChecked == true) { throw new InvalidOperationException("Normal PR delta reset lost accepted source."); }
                window.ProlongedPrDeltaInput.IsChecked = !prolongedPr; Click(apply);
                if (projected ? window.EcgConfiguration != (ecg with { ProlongedPrDelta = !prolongedPr, PrIntervalMilliseconds = prolongedPr ? 160 : 240 })
                    : window.BreathConfiguration != (physiology with { ProlongedPrDelta = !prolongedPr }))
                { throw new InvalidOperationException("PR/delta variant switch failed."); }
                window.ProlongedPrDeltaInput.IsChecked = prolongedPr; Click(apply);
                if (!Accepted()) { throw new InvalidOperationException("PR/delta variant roundtrip failed."); }
                window.NormalPrDeltaInput.IsChecked = false; Click(apply);
                if (projected ? window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default : window.BreathConfiguration != PhysiologyDemoConfiguration.Default)
                { throw new InvalidOperationException("Normal PR delta clear failed."); }
                Click(window.NormalPrDeltaButton); Click(window.WpwButton);
                if (window.NormalPrDeltaInput.IsChecked == true) { throw new InvalidOperationException("WPW loader retained normal PR delta mode."); }
            }
            finally { window.Close(); }
        }
        foreach (var invalid in new[] { ecg with { NormalPrDelta = false, ProlongedPrDelta = true }, ecg with { Wpw = true }, ecg with { ShortPr = true }, ecg with { WpwNegativeV1 = true }, ecg with { PrIntervalMilliseconds = 100 }, ecg with { QrsDurationMilliseconds = 80 }, ecg with { VentricularConductionRatio = 2 } })
        {
            try { ProjectedEcgDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "NormalPrDelta.ConflictingModes") { continue; }
            throw new InvalidOperationException("Invalid PR with delta source accepted.");
        }
        foreach (var invalid in new[] { physiology with { NormalPrDelta = false, ProlongedPrDelta = true }, physiology with { Wpw = true }, physiology with { ShortPr = true }, physiology with { WpwNegativeV1 = true }, physiology with { IndependentVentricularPeriodMilliseconds = 800 }, physiology with { VentricularConductionRatio = 2 }, physiology with { BundleBlock = EcgBundleBlockIllustration.CompleteLeft } })
        {
            try { PhysiologyDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "NormalPrDelta.ConflictingModes") { continue; }
            throw new InvalidOperationException("Invalid PR with delta physiology accepted.");
        }
        Console.WriteLine($"ok: {(prolongedPr ? "prolonged" : "normal")} PR with delta both demos, shared ECG/perfusion, pixels, recovery and atomic lifecycle");
    }
}
