// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class AcceleratedAtrialPhysiologySmokeChecks
{
    internal static void Verify()
    {
        var config = PhysiologyDemoConfiguration.AarPreset;
        var blocks = PhysiologyChannelSmokeChecks.Verify("AAR", config, AcceleratedAtrialReference.CreatePlan(),
            AcceleratedAtrialReference.CreateLeadIIBands(), FixedPerfusionPresets.AcceleratedSupraventricular);
        var window = new WaveformDemoWindow(physiology: true);
        window.Show();
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.VentricularDisorganizationButton); Click(window.StepButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            Click(window.AarButton); Click(window.ApplyBreathButton); window.Pulse(oldTimer);
            if (window.BreathConfiguration != config || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.AarInput.IsChecked != true)
            { throw new InvalidOperationException("AAR physiology loader retained VF state."); }
            Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || !string.IsNullOrEmpty(window.BreathConfigurationStatus.Text)) { throw new InvalidOperationException("AAR physiology reapply failed."); }
            if (!window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("首次 QRS 偏移 160 ms") == true)) { throw new InvalidOperationException("AAR physiology summary offset mismatch."); }
            for (int i = 0; i < 40; i++) { Click(window.StepButton); }
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 0);
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 195);
            MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 345);
            VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
            Click(window.HoldButton); Click(window.RunButton);
            var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
            window.IndependentVentricularPeriodInput.Text = "800"; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.BreathConfigurationStatus.Text))
            { throw new InvalidOperationException("AAR physiology conflict mutated accepted state."); }
            Click(window.ResetButton);
            if (window.AarInput.IsChecked != true || window.VtInput.IsChecked == true || !string.IsNullOrEmpty(window.IndependentVentricularPeriodInput.Text))
            { throw new InvalidOperationException("AAR reset lost source."); }
            window.AarInput.IsChecked = false; Click(window.ApplyBreathButton);
            if (window.BreathConfiguration != PhysiologyDemoConfiguration.Default) { throw new InvalidOperationException("AAR physiology clear failed."); }
            Click(window.AarButton); Click(window.WpwButton);
            if (window.AarInput.IsChecked == true || window.VtInput.IsChecked == true || window.VtFusionInput.IsChecked == true || window.VtCaptureInput.IsChecked == true || window.VtBidirectionalInput.IsChecked == true || window.VtTwistingInput.IsChecked == true) { throw new InvalidOperationException("WPW loader retained AAR."); }
        }
        finally { window.Close(); }
        foreach (var invalid in new[] { config with { UseVascularReservoir = false }, config with { Wpw = true },
            config with { Ajr = true }, config with { Aivr = true }, config with { Vt = true }, config with { VtFusion = true }, config with { VtCapture = true },
            config with { VtTwisting = true }, config with { VtBidirectional = true }, config with { Svt = true },
            config with { VentricularConductionRatio = 2 }, config with { IndependentVentricularPeriodMilliseconds = 750 },
            config with { BundleBlock = EcgBundleBlockIllustration.CompleteLeft } })
        {
            try { PhysiologyDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "Aar.ConflictingModes") { continue; }
            throw new InvalidOperationException("Invalid AAR physiology accepted.");
        }
        Console.WriteLine("ok: AAR physiology full supports, bounded overlap, shared samples/pixels, recovery and atomic lifecycle");
    }
}
