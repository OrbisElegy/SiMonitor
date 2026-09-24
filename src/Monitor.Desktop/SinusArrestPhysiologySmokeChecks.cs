// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class SinusArrestPhysiologySmokeChecks
{
    internal static void Verify()
    {
        var config = PhysiologyDemoConfiguration.SinusArrestPreset;
        var blocks = PhysiologyChannelSmokeChecks.Verify("SinusArrest", config, SinusArrestReference.CreatePlan(),
            SinusArrestReference.CreateLeadIIBands(), FixedPerfusionPresets.SinglePulse, verifyMechanicalSuppression: true);
        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 0).Skip(550).Take(350).Any(v => v != 0))
        { throw new InvalidOperationException("Sinus arrest must remove both P and QRS during the pause."); }
        foreach (int row in new[] { 2, 3, 5 })
        {
            short[] samples = MechanicalUncouplingSmokeChecks.Samples(blocks, row);
            if (samples[425] <= 0 || samples[350] <= samples[425])
            { throw new InvalidOperationException("Perfusion must retain declining runoff through the pause."); }
        }
        var window = new WaveformDemoWindow(physiology: true);
        window.Show();
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.VentricularDisorganizationButton); Click(window.StepButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            Click(window.SinusArrestButton); Click(window.ApplyBreathButton); window.Pulse(oldTimer);
            if (window.BreathConfiguration != config || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.ConductionInput.SelectedIndex != 42)
            { throw new InvalidOperationException("sinus arrest physiology loader retained VF state."); }
            Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || !string.IsNullOrEmpty(window.BreathConfigurationStatus.Text)) { throw new InvalidOperationException("sinus arrest physiology reapply failed."); }
            if (!window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("首次 QRS 偏移 160 ms") == true)) { throw new InvalidOperationException("sinus arrest physiology summary offset mismatch."); }
            for (int i = 0; i < 40; i++) { Click(window.StepButton); }
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 0);
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 245);
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 445);
            VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
            Click(window.HoldButton); Click(window.RunButton);
            var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
            window.IndependentVentricularPeriodInput.Text = "800"; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
            { throw new InvalidOperationException("sinus arrest physiology conflict mutated accepted state."); }
            Click(window.ResetButton);
            if (window.ConductionInput.SelectedIndex != 42 || window.VtInput.IsChecked == true || !string.IsNullOrEmpty(window.IndependentVentricularPeriodInput.Text))
            { throw new InvalidOperationException("sinus arrest reset lost source."); }
            window.ConductionInput.SelectedIndex = 0; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != PhysiologyDemoConfiguration.Default) { throw new InvalidOperationException("sinus arrest physiology clear failed."); }
            Click(window.SinusArrestButton); Click(window.WpwButton);
            if (window.ConductionInput.SelectedIndex == 42 || window.VtInput.IsChecked == true || window.VtFusionInput.IsChecked == true || window.VtCaptureInput.IsChecked == true || window.VtBidirectionalInput.IsChecked == true || window.VtTwistingInput.IsChecked == true) { throw new InvalidOperationException("WPW loader retained sinus arrest."); }
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
            throw new InvalidOperationException("Invalid sinus arrest physiology accepted.");
        }
        Console.WriteLine("ok: sinus arrest physiology full supports, bounded overlap, shared samples/pixels, recovery and atomic lifecycle");
    }
}
