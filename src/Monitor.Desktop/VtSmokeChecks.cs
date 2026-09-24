// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class VtSmokeChecks
{
    internal static void Verify()
    {
        Verify(false, false);
        Verify(true, false);
        Verify(false, true);
        Verify(true, true);
        Verify(false, false, true);
        Verify(false, false, false, true);
    }

    private static void Verify(bool fusion, bool capture, bool bidirectional = false, bool twisting = false)
    {
        var config = ProjectedEcgDemoConfiguration.VtPreset with { VtFusion = fusion, VtCapture = capture, VtBidirectional = bidirectional, VtTwisting = twisting };
        var window = new WaveformDemoWindow(projected: true);
        window.Show();
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.WpwButton); Click(window.StepButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            Click(window.VtButton); window.VtTwistingInput.IsChecked = twisting; window.VtBidirectionalInput.IsChecked = bidirectional; window.VtCaptureInput.IsChecked = capture; window.VtFusionInput.IsChecked = fusion; Click(window.ApplyEcgButton); window.Pulse(oldTimer);
            if (window.EcgConfiguration != config || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.WpwInput.IsChecked == true)
            { throw new InvalidOperationException("VT loader retained WPW state."); }
            Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config || !string.IsNullOrEmpty(window.EcgConfigurationStatus.Text)) { throw new InvalidOperationException("VT reapply failed."); }
            if (window.PrIntervalInput.Text != "—" || window.PrIntervalInput.IsEnabled ||
                !window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("无固定PR") == true))
            { throw new InvalidOperationException("VT exposed structural PR as measurable interval."); }
            var source = ProjectedEcgDemoSource.Create(config);
            var blocks = new List<WaveformEnvelope>();
            for (int step = 1; step <= 30; step++)
            {
                Click(window.StepButton);
                blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(b => WaveformEnvelopeCodec.Decode(b)));
            }
            foreach (int sample in twisting ? new[] { 35, 130, 220, 410, 600 } : new[] { 0, 35, 75, 110, 1050, 1248, 1275 })
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks.ToArray(), sample, [EcgLead.I, EcgLead.II, EcgLead.V1, EcgLead.V5]);
            // Compare the completed raw record range, excluding samples still in acquisition delay.
            long capturedEnd = blocks[^1].StartSimTimeNs + blocks[^1].DurationNs;
            var expected = ElectrodeSignalGenerator.Start(VentricularTachycardiaReference.CreatePlan(capture), "AcqECGMonitor250@1", 1, VentricularTachycardiaReference.CreateElectrodes(fusion, capture, bidirectional, twisting)).GenerateBefore(capturedEnd, 1500, 200);
            foreach (var lead in Enum.GetValues<EcgLead>())
            {
                short[] actual = blocks.SelectMany(b => b.Planes.Single(p => p.ChannelId == ProjectedEcgDemoSource.ChannelId(lead)).Samples).ToArray();
                if (actual.Length != expected.Count || actual.Zip(expected).Any(p => Math.Abs(p.First - p.Second.MicrovoltValues[(int)lead]) > 1))
                { throw new InvalidOperationException("VT source did not use shared independent ventricular schedule."); }
            }
            var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
            var a = source.AdvanceTo(6_200_000_000, 50, 1, 100); var b2 = restored.AdvanceTo(6_200_000_000, 50, 1, 100);
            if (a.Count != b2.Count || a.Zip(b2).Any(p => !p.First.SequenceEqual(p.Second))) { throw new InvalidOperationException("VT wire recovery mismatch."); }
            Click(window.HoldButton); Click(window.RunButton);
            var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
            if (bidirectional || twisting)
            {
                window.VtFusionInput.IsChecked = true; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                { throw new InvalidOperationException("Bidirectional/fusion conflict mutated accepted state."); }
                window.VtFusionInput.IsChecked = false;
            }
            window.WpwInput.IsChecked = true; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
            { throw new InvalidOperationException("VT conflict mutated accepted state."); }
            Click(window.ResetButton);
            if (window.VtInput.IsChecked != true || window.VtFusionInput.IsChecked != fusion || window.VtCaptureInput.IsChecked != capture || window.VtBidirectionalInput.IsChecked != bidirectional || window.VtTwistingInput.IsChecked != twisting || window.WpwInput.IsChecked == true) { throw new InvalidOperationException("VT reset lost accepted state."); }
            if (!bidirectional && !twisting)
            {
                window.VtFusionInput.IsChecked = !fusion; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != (config with { VtFusion = !fusion })) { throw new InvalidOperationException("VT fusion toggle failed."); }
                window.VtCaptureInput.IsChecked = capture; window.VtFusionInput.IsChecked = fusion; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config) { throw new InvalidOperationException("VT fusion toggle restore failed."); }
                window.VtCaptureInput.IsChecked = !capture; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != (config with { VtCapture = !capture })) { throw new InvalidOperationException("VT capture toggle failed."); }
                window.VtCaptureInput.IsChecked = capture; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config) { throw new InvalidOperationException("VT capture toggle restore failed."); }
            }
            if (!fusion && !capture && !twisting)
            {
                window.VtBidirectionalInput.IsChecked = !bidirectional; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != (config with { VtBidirectional = !bidirectional })) { throw new InvalidOperationException("Bidirectional VT toggle failed."); }
                window.VtBidirectionalInput.IsChecked = bidirectional; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config) { throw new InvalidOperationException("Bidirectional VT toggle restore failed."); }
            }
            if (!fusion && !capture && !bidirectional)
            {
                window.VtTwistingInput.IsChecked = !twisting; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != (config with { VtTwisting = !twisting })) { throw new InvalidOperationException("Twisting VT toggle failed."); }
                window.VtTwistingInput.IsChecked = twisting; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config) { throw new InvalidOperationException("Twisting VT toggle restore failed."); }
            }
            window.VtInput.IsChecked = false; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default || window.PrIntervalInput.Text == "—") { throw new InvalidOperationException("VT clear failed."); }
            Click(window.VtButton); Click(window.WpwButton);
            if (window.VtInput.IsChecked == true || window.VtFusionInput.IsChecked == true || window.VtCaptureInput.IsChecked == true || window.VtBidirectionalInput.IsChecked == true || window.VtTwistingInput.IsChecked == true) { throw new InvalidOperationException("WPW loader retained VT."); }
        }
        finally { window.Close(); }
        foreach (var invalid in new[] { config with { Wpw = true }, config with { Vt = false, VtTwisting = true }, config with { VtTwisting = true, VtBidirectional = true }, config with { VtTwisting = true, VtFusion = true }, config with { VtTwisting = true, VtCapture = true }, config with { Vt = false, VtBidirectional = true }, config with { VtBidirectional = true, VtFusion = true }, config with { VtBidirectional = true, VtCapture = true }, config with { Vt = false, VtFusion = true }, config with { Vt = false, VtCapture = true }, config with { HeartRateBpm = 160 }, config with { VentricularConductionRatio = 2 }, config with { IndependentVentricularPeriodMilliseconds = 800 }, config with { QrsDurationMilliseconds = 80 } })
        {
            try { ProjectedEcgDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "Vt.ConflictingModes") { continue; }
            throw new InvalidOperationException("Invalid VT source accepted.");
        }
        Console.WriteLine("ok: VT160bpm with independent atria75bpm, shared samples, overlap pixels, hidden PR, recovery and atomic lifecycle");
    }
}
