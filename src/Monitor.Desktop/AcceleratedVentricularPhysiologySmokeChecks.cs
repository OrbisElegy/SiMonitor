// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class AcceleratedVentricularPhysiologySmokeChecks
{
    internal static void Verify() { Verify(false); Verify(true); Verify(false, true); }

    private static void Verify(bool fusion, bool capture = false)
    {
        var config = PhysiologyDemoConfiguration.AivrPreset with { AivrFusion = fusion, AivrCapture = capture };
        var blocks = PhysiologyChannelSmokeChecks.Verify("AIVR", config, AcceleratedVentricularReference.CreatePlan(),
            AcceleratedVentricularReference.CreateLeadIIBands(fusion, capture), FixedPerfusionPresets.SinglePulse);
        var plain = PhysiologyChannelSmokeChecks.DecodeCompletedOutput(PhysiologyDemoConfiguration.AivrPreset, 6_000_000_000);
        foreach (int row in new[] { 1, 2, 3, 4, 5, 6 })
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(plain, row)))
            { throw new InvalidOperationException("AIVR fusion/capture changed fixed non-ECG inputs."); }
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
}
