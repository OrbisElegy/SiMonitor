// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class AcceleratedVentricularSmokeChecks
{
    internal static void Verify() { Verify(false); Verify(true); }

    private static void Verify(bool fusion)
    {
        var config = ProjectedEcgDemoConfiguration.AivrPreset with { AivrFusion = fusion };
        foreach (var invalid in new[] { config with { Aivr = false, AivrFusion = true }, config with { Vt = true }, config with { VtCapture = true },
            config with { VtTwisting = true }, config with { Svt = true }, config with { Wpw = true },
            config with { HeartRateBpm = 80 }, config with { IndependentVentricularPeriodMilliseconds = 750 },
            config with { QrsDurationMilliseconds = 80 }, config with { VentricularConductionRatio = 2 },
            config with { MethodId = "Bazett" } })
        {
            try { ProjectedEcgDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "Aivr.ConflictingModes") { continue; }
            throw new InvalidOperationException("Undefined AIVR configuration accepted.");
        }
        var window = new WaveformDemoWindow(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.VtButton); window.VtTwistingInput.IsChecked = true; Click(window.ApplyEcgButton);
            Click(window.StepButton); Click(window.RunButton); var previousTimer = window.ActiveTimer;
            Click(window.AivrButton); window.AivrFusionInput.IsChecked = fusion; Click(window.ApplyEcgButton); window.Pulse(previousTimer);
            if (window.EcgConfiguration != config || window.BlockCount != 0 || window.SimulationTimeNs != 0 ||
                window.ActiveTimer is not null || window.VtInput.IsChecked == true || window.VtTwistingInput.IsChecked == true)
            { throw new InvalidOperationException("AIVR loader retained previous VT state."); }
            Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config || !string.IsNullOrEmpty(window.EcgConfigurationStatus.Text) ||
                window.PrIntervalInput.Text != "—" || window.PrIntervalInput.IsEnabled ||
                !window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("室率80次/分，RR750ms") == true))
            { throw new InvalidOperationException("AIVR reapply or independent-rate summary failed."); }
            var source = ProjectedEcgDemoSource.Create(config);
            List<WaveformEnvelope> blocks = [];
            for (int step = 1; step <= 30; step++)
            {
                Click(window.StepButton);
                blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(b => WaveformEnvelopeCodec.Decode(b)));
            }
            foreach (int sample in new[] { 0, 35, 220, 410, 600, 795 })
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks.ToArray(), sample, [EcgLead.I, EcgLead.II, EcgLead.V1, EcgLead.V5]);
            long end = blocks[^1].StartSimTimeNs + blocks[^1].DurationNs;
            var expected = ElectrodeSignalGenerator.Start(AcceleratedVentricularReference.CreatePlan(), "AcqECGMonitor250@1", 1,
                AcceleratedVentricularReference.CreateElectrodes(fusion)).GenerateBefore(end, 1500, 200);
            foreach (var lead in Enum.GetValues<EcgLead>())
            {
                var actual = blocks.SelectMany(b => b.Planes.Single(p => p.ChannelId == ProjectedEcgDemoSource.ChannelId(lead)).Samples).ToArray();
                if (actual.Length != expected.Count || actual.Zip(expected).Any(p => Math.Abs(p.First - p.Second.MicrovoltValues[(int)lead]) > 1))
                { throw new InvalidOperationException("AIVR demo differs from shared electrode source."); }
            }
            var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
            var a = source.AdvanceTo(6_200_000_000, 50, 1, 100); var b2 = restored.AdvanceTo(6_200_000_000, 50, 1, 100);
            if (a.Count != b2.Count || a.Zip(b2).Any(p => !p.First.SequenceEqual(p.Second)))
            { throw new InvalidOperationException("AIVR wire recovery mismatch."); }
            Click(window.HoldButton); Click(window.RunButton);
            var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
            window.VtInput.IsChecked = true; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
            { throw new InvalidOperationException("AIVR conflict changed accepted state."); }
            Click(window.ResetButton);
            if (window.AivrInput.IsChecked != true || window.AivrFusionInput.IsChecked != fusion || window.VtInput.IsChecked == true || window.EcgConfiguration != config)
            { throw new InvalidOperationException("AIVR reset lost accepted state."); }
            window.AivrFusionInput.IsChecked = !fusion; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != (config with { AivrFusion = !fusion })) { throw new InvalidOperationException("AIVR fusion toggle failed."); }
            window.AivrFusionInput.IsChecked = fusion; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config) { throw new InvalidOperationException("AIVR fusion toggle restore failed."); }
            window.AivrInput.IsChecked = false; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default || window.PrIntervalInput.Text == "—")
            { throw new InvalidOperationException("AIVR clear did not restore default."); }
            Click(window.AivrButton); Click(window.VentricularDisorganizationButton); Click(window.AivrButton); Click(window.WpwButton);
            if (window.AivrInput.IsChecked == true || window.AivrFusionInput.IsChecked == true || window.EcgConfiguration != ProjectedEcgDemoConfiguration.WpwPreset)
            { throw new InvalidOperationException("AIVR flag leaked into another loader."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: AIVR80/75 independent clocks, shared 12-lead samples/pixels, hidden PR, wire recovery and atomic lifecycle");
    }
}
