// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class AcceleratedJunctionalPhysiologySmokeChecks
{
    internal static void Verify()
    {
        var config = PhysiologyDemoConfiguration.AjrPreset;
        var blocks = PhysiologyChannelSmokeChecks.Verify("AJR", config, AcceleratedJunctionalReference.CreatePlan(),
            AcceleratedJunctionalReference.CreateLeadIIBands(), FixedPerfusionPresets.AcceleratedSupraventricular);
        var window = new WaveformDemoWindow(physiology: true);
        window.Show();
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.VentricularDisorganizationButton); Click(window.StepButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            Click(window.AjrButton); Click(window.ApplyBreathButton); window.Pulse(oldTimer);
            if (window.BreathConfiguration != config || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.AjrInput.IsChecked != true)
            { throw new InvalidOperationException("AJR physiology loader retained VF state."); }
            Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || !string.IsNullOrEmpty(window.BreathConfigurationStatus.Text)) { throw new InvalidOperationException("AJR physiology reapply failed."); }
            if (!window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("首次 QRS 偏移 120 ms") == true)) { throw new InvalidOperationException("AJR physiology summary offset mismatch."); }
            for (int i = 0; i < 40; i++) { Click(window.StepButton); }
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 0);
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 185);
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 335);
            VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
            Click(window.HoldButton); Click(window.RunButton);
            var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
            window.IndependentVentricularPeriodInput.Text = "800"; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
            { throw new InvalidOperationException("AJR physiology conflict mutated accepted state."); }
            Click(window.ResetButton);
            if (window.AjrInput.IsChecked != true || window.VtInput.IsChecked == true || !string.IsNullOrEmpty(window.IndependentVentricularPeriodInput.Text))
            { throw new InvalidOperationException("AJR reset lost source."); }
            window.AjrInput.IsChecked = false; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != PhysiologyDemoConfiguration.Default) { throw new InvalidOperationException("AJR physiology clear failed."); }
            Click(window.AjrButton); Click(window.WpwButton);
            if (window.AjrInput.IsChecked == true || window.VtInput.IsChecked == true || window.VtFusionInput.IsChecked == true || window.VtCaptureInput.IsChecked == true || window.VtBidirectionalInput.IsChecked == true || window.VtTwistingInput.IsChecked == true) { throw new InvalidOperationException("WPW loader retained AJR."); }
        }
        finally { window.Close(); }
        foreach (var invalid in new[] { config with { UseVascularReservoir = false }, config with { Wpw = true },
            config with { Aivr = true }, config with { Vt = true }, config with { VtFusion = true }, config with { VtCapture = true },
            config with { VtTwisting = true }, config with { VtBidirectional = true }, config with { Svt = true },
            config with { VentricularConductionRatio = 2 }, config with { IndependentVentricularPeriodMilliseconds = 750 },
            config with { BundleBlock = EcgBundleBlockIllustration.CompleteLeft } })
        {
            try { PhysiologyDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "Ajr.ConflictingModes") { continue; }
            throw new InvalidOperationException("Invalid AJR physiology accepted.");
        }
        Console.WriteLine("ok: AJR physiology full supports, bounded overlap, shared samples/pixels, recovery and atomic lifecycle");
    }
}
