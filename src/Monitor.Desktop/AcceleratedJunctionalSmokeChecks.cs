// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class AcceleratedJunctionalSmokeChecks
{
    internal static void Verify()
    {
        var config = ProjectedEcgDemoConfiguration.AjrPreset;
        foreach (var invalid in new[] { config with { Aivr = true }, config with { Vt = true }, config with { VtCapture = true },
            config with { VtTwisting = true }, config with { Svt = true }, config with { Wpw = true },
            config with { HeartRateBpm = 80 }, config with { IndependentVentricularPeriodMilliseconds = 750 },
            config with { QrsDurationMilliseconds = 160 }, config with { VentricularConductionRatio = 2 },
            config with { MethodId = "Bazett" } })
        {
            try { ProjectedEcgDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "Ajr.ConflictingModes") { continue; }
            throw new InvalidOperationException("Undefined AJR configuration accepted.");
        }
        var window = new WaveformDemoWindow(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.VtButton); window.VtTwistingInput.IsChecked = true; Click(window.ApplyEcgButton);
            Click(window.StepButton); Click(window.RunButton); var previousTimer = window.ActiveTimer;
            Click(window.AjrButton); window.Pulse(previousTimer);
            if (window.EcgConfiguration != config || window.BlockCount != 0 || window.SimulationTimeNs != 0 ||
                window.ActiveTimer is not null || window.VtInput.IsChecked == true || window.VtTwistingInput.IsChecked == true)
            { throw new InvalidOperationException("AJR loader retained previous VT state."); }
            Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config || !string.IsNullOrEmpty(window.EcgConfigurationStatus.Text) ||
                window.PrIntervalInput.Text != "—" || window.PrIntervalInput.IsEnabled ||
                !window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("室率100次/分，RR600ms") == true))
            { throw new InvalidOperationException("AJR reapply or independent-rate summary failed."); }
            var source = ProjectedEcgDemoSource.Create(config);
            List<WaveformEnvelope> blocks = [];
            for (int step = 1; step <= 30; step++)
            {
                Click(window.StepButton);
                blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(b => WaveformEnvelopeCodec.Decode(b)));
            }
            foreach (int sample in new[] { 0, 35, 185, 335, 485, 635 })
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks.ToArray(), sample, [EcgLead.I, EcgLead.II, EcgLead.V1, EcgLead.V5]);
            long end = blocks[^1].StartSimTimeNs + blocks[^1].DurationNs;
            var expected = ElectrodeSignalGenerator.Start(AcceleratedJunctionalReference.CreatePlan(), "AcqECGMonitor250@1", 1,
                AcceleratedJunctionalReference.CreateElectrodes()).GenerateBefore(end, 1500, 200);
            foreach (var lead in Enum.GetValues<EcgLead>())
            {
                short[] actual = blocks.SelectMany(b => b.Planes.Single(p => p.ChannelId == ProjectedEcgDemoSource.ChannelId(lead)).Samples).ToArray();
                if (actual.Length != expected.Count || actual.Zip(expected).Any(p => Math.Abs(p.First - p.Second.MicrovoltValues[(int)lead]) > 1))
                { throw new InvalidOperationException("AJR demo differs from shared electrode source."); }
            }
            var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
            var a = source.AdvanceTo(6_200_000_000, 50, 1, 100); var b2 = restored.AdvanceTo(6_200_000_000, 50, 1, 100);
            if (a.Count != b2.Count || a.Zip(b2).Any(p => !p.First.SequenceEqual(p.Second)))
            { throw new InvalidOperationException("AJR wire recovery mismatch."); }
            Click(window.HoldButton); Click(window.RunButton);
            var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
            window.VtInput.IsChecked = true; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
            { throw new InvalidOperationException("AJR conflict changed accepted state."); }
            Click(window.ResetButton);
            if (window.AjrInput.IsChecked != true || window.VtInput.IsChecked == true || window.EcgConfiguration != config)
            { throw new InvalidOperationException("AJR reset lost accepted state."); }
            window.AjrInput.IsChecked = false; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default || window.PrIntervalInput.Text == "—")
            { throw new InvalidOperationException("AJR clear did not restore default."); }
            Click(window.AjrButton); Click(window.VentricularDisorganizationButton); Click(window.AjrButton); Click(window.WpwButton);
            if (window.AjrInput.IsChecked == true || window.EcgConfiguration != ProjectedEcgDemoConfiguration.WpwPreset)
            { throw new InvalidOperationException("AJR flag leaked into another loader."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: AJR100/75 independent clocks, shared 12-lead samples/pixels, hidden PR, wire recovery and atomic lifecycle");
    }
}
