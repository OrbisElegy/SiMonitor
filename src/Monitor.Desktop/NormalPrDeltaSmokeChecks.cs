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
    {
        var ecg = ProjectedEcgDemoConfiguration.NormalPrDeltaPreset;
        var physiology = PhysiologyDemoConfiguration.NormalPrDeltaPreset;
        var blocks = MechanicalUncouplingSmokeChecks.Decode(physiology);
        var normal = MechanicalUncouplingSmokeChecks.Decode(PhysiologyDemoConfiguration.Default);
        for (int row = 1; row < 7; row++)
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
            { throw new InvalidOperationException("Normal PR delta changed perfusion despite identical mechanical events."); }
        var ii = PhysiologySignalGenerator.Start(physiology.ResolvePlan(), "AcqECGMonitor250@1", 1, NormalPrDeltaReference.CreateLeadIIBands()).GenerateBefore(6_000_000_000, 1500, 100);
        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 0).Zip(ii).Any(p => Math.Abs(p.First - p.Second.NormalizedValue) > 1))
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
                if (!Accepted() || window.ShortPrInput.IsChecked == true) { throw new InvalidOperationException("Normal PR delta retained short PR source."); }
                Click(window.WpwButton); window.WpwNegativeV1Input.IsChecked = true; Click(apply);
                Click(window.StepButton); Click(window.RunButton); var oldTimer = window.ActiveTimer;
                Click(window.NormalPrDeltaButton); window.Pulse(oldTimer);
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
                    foreach (int sample in new[] { 0, 40, 55, 110 })
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, projectedBlocks.ToArray(), sample, [EcgLead.I, EcgLead.II, EcgLead.V1, EcgLead.V5]);
                    var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
                    var a = source.AdvanceTo(6_200_000_000, 50, 1, 100); var b = restored.AdvanceTo(6_200_000_000, 50, 1, 100);
                    if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second))) { throw new InvalidOperationException("Normal PR delta wire recovery mismatch."); }
                }
                else
                {
                    MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 40);
                    VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
                    if (!window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("首次 QRS 偏移 160 ms") == true))
                    { throw new InvalidOperationException("Normal PR delta summary offset mismatch."); }
                    var source2 = PhysiologyDemoSource.Create(physiology); source2.AdvanceTo(200_000_000, 50, 1, 100);
                    var restored = PhysiologyWaveformGroup.Restore(source2.CaptureState());
                    var a = source2.AdvanceTo(400_000_000, 50, 1, 100); var b = restored.AdvanceTo(400_000_000, 50, 1, 100);
                    if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second))) { throw new InvalidOperationException("Normal PR delta physiology recovery mismatch."); }
                }
                Click(window.HoldButton); Click(window.RunButton);
                var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
                window.WpwInput.IsChecked = true; Click(apply);
                if (!Accepted() || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(Status()))
                { throw new InvalidOperationException("Normal PR delta conflict mutated accepted state."); }
                Click(window.ResetButton);
                if (window.NormalPrDeltaInput.IsChecked != true || window.WpwInput.IsChecked == true) { throw new InvalidOperationException("Normal PR delta reset lost accepted source."); }
                window.NormalPrDeltaInput.IsChecked = false; Click(apply);
                if (projected ? window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default : window.BreathConfiguration != PhysiologyDemoConfiguration.Default)
                { throw new InvalidOperationException("Normal PR delta clear failed."); }
                Click(window.NormalPrDeltaButton); Click(window.WpwButton);
                if (window.NormalPrDeltaInput.IsChecked == true) { throw new InvalidOperationException("WPW loader retained normal PR delta mode."); }
            }
            finally { window.Close(); }
        }
        foreach (var invalid in new[] { ecg with { Wpw = true }, ecg with { ShortPr = true }, ecg with { WpwNegativeV1 = true }, ecg with { PrIntervalMilliseconds = 100 }, ecg with { QrsDurationMilliseconds = 80 }, ecg with { VentricularConductionRatio = 2 } })
        {
            try { ProjectedEcgDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "NormalPrDelta.ConflictingModes") { continue; }
            throw new InvalidOperationException("Invalid short PR source accepted.");
        }
        foreach (var invalid in new[] { physiology with { Wpw = true }, physiology with { ShortPr = true }, physiology with { WpwNegativeV1 = true }, physiology with { IndependentVentricularPeriodMilliseconds = 800 }, physiology with { VentricularConductionRatio = 2 }, physiology with { BundleBlock = EcgBundleBlockIllustration.CompleteLeft } })
        {
            try { PhysiologyDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "NormalPrDelta.ConflictingModes") { continue; }
            throw new InvalidOperationException("Invalid short PR physiology accepted.");
        }
        Console.WriteLine("ok: normal PR with delta both demos, shared ECG/perfusion, pixels, recovery and atomic lifecycle");
    }
}
