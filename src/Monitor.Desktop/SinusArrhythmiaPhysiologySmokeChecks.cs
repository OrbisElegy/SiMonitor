// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class SinusArrhythmiaPhysiologySmokeChecks
{
    internal static void Verify()
    {
        var config = PhysiologyDemoConfiguration.SinusArrhythmiaPreset;
        var blocks = PhysiologyChannelSmokeChecks.Verify("SinusArrhythmia", config, SinusArrhythmiaReference.CreatePlan(),
            SinusArrhythmiaReference.CreateLeadIIBands(), FixedPerfusionPresets.PulmonaryOverlap, verifyMechanicalSuppression: true);
        var window = new WaveformDemoWindow(physiology: true);
        window.Show();
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.VentricularDisorganizationButton); Click(window.StepButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            Click(window.SinusArrhythmiaButton); Click(window.ApplyBreathButton); window.Pulse(oldTimer);
            if (window.BreathConfiguration != config || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.ConductionInput.SelectedIndex != 41)
            { throw new InvalidOperationException("sinus arrhythmia physiology loader retained VF state."); }
            Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || !string.IsNullOrEmpty(window.BreathConfigurationStatus.Text)) { throw new InvalidOperationException("sinus arrhythmia physiology reapply failed."); }
            if (!window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("首次 QRS 偏移 160 ms") == true)) { throw new InvalidOperationException("sinus arrhythmia physiology summary offset mismatch."); }
            for (int i = 0; i < 40; i++) { Click(window.StepButton); }
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 0);
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 245);
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 495);
            VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
            Click(window.HoldButton); Click(window.RunButton);
            var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
            window.IndependentVentricularPeriodInput.Text = "800"; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
            { throw new InvalidOperationException("sinus arrhythmia physiology conflict mutated accepted state."); }
            Click(window.ResetButton);
            if (window.ConductionInput.SelectedIndex != 41 || window.VtInput.IsChecked == true || !string.IsNullOrEmpty(window.IndependentVentricularPeriodInput.Text))
            { throw new InvalidOperationException("sinus arrhythmia reset lost source."); }
            window.ConductionInput.SelectedIndex = 0; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != PhysiologyDemoConfiguration.Default) { throw new InvalidOperationException("sinus arrhythmia physiology clear failed."); }
            Click(window.SinusArrhythmiaButton); Click(window.WpwButton);
            if (window.ConductionInput.SelectedIndex == 41 || window.VtInput.IsChecked == true || window.VtFusionInput.IsChecked == true || window.VtCaptureInput.IsChecked == true || window.VtBidirectionalInput.IsChecked == true || window.VtTwistingInput.IsChecked == true) { throw new InvalidOperationException("WPW loader retained sinus arrhythmia."); }
        }
        finally { window.Close(); }
        foreach (var invalid in new[] { config with { UseVascularReservoir = false }, config with { Wpw = true },
            config with { Ajr = true }, config with { Aivr = true }, config with { Vt = true }, config with { VtFusion = true }, config with { VtCapture = true },
            config with { VtTwisting = true }, config with { VtBidirectional = true }, config with { Svt = true },
            config with { VentricularConductionRatio = 2 }, config with { IndependentVentricularPeriodMilliseconds = 750 },
            config with { BundleBlock = EcgBundleBlockIllustration.CompleteLeft }, config with { Aar = true },
            config with { AtrialEscape = true }, config with { MechanicalEveryCycles = 2 },
            config with { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 1 } })
        {
            try { PhysiologyDemoSource.Create(invalid); }
            catch (ArgumentException) { continue; }
            throw new InvalidOperationException("Invalid sinus arrhythmia physiology accepted.");
        }
        Console.WriteLine("ok: sinus arrhythmia physiology full supports, bounded overlap, shared samples/pixels, recovery and atomic lifecycle");
    }
}
