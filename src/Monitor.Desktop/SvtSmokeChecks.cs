// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class SvtSmokeChecks
{
    internal static void Verify() { Verify(false); Verify(true); }

    private static void Verify(bool rbbb)
    {
        var config = rbbb ? ProjectedEcgDemoConfiguration.SvtRightBundlePreset : ProjectedEcgDemoConfiguration.SvtPreset;
        var window = new WaveformDemoWindow(projected: true);
        window.Show();
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.WpwButton); Click(window.StepButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            Click(window.SvtButton); window.SvtRbbbInput.IsChecked = rbbb; Click(window.ApplyEcgButton); window.Pulse(oldTimer);
            if (window.EcgConfiguration != config || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.WpwInput.IsChecked == true)
            { throw new InvalidOperationException("SVT loader retained WPW state."); }
            Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config || !string.IsNullOrEmpty(window.EcgConfigurationStatus.Text)) { throw new InvalidOperationException("SVT reapply failed."); }
            if (window.PrIntervalInput.Text != "—" || window.PrIntervalInput.IsEnabled || window.PDurationInput.Text != "—" ||
                !window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("PR不可单独测量") == true))
            { throw new InvalidOperationException("SVT exposed structural PR as measurable interval."); }
            var source = ProjectedEcgDemoSource.Create(config);
            var blocks = new List<WaveformEnvelope>();
            for (int step = 1; step <= 30; step++)
            {
                Click(window.StepButton);
                blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(b => WaveformEnvelopeCodec.Decode(b)));
            }
            foreach (int sample in new[] { 0, 35, 75, 110 })
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks.ToArray(), sample, [EcgLead.I, EcgLead.II, EcgLead.V1, EcgLead.V5]);
            // Compare the completed raw record range, excluding samples still in acquisition delay.
            long capturedEnd = blocks[^1].StartSimTimeNs + blocks[^1].DurationNs;
            var expected = ElectrodeSignalGenerator.Start(SupraventricularTachycardiaReference.CreatePlan(), "AcqECGMonitor250@1", 1, SupraventricularTachycardiaReference.CreateElectrodes(rbbb)).GenerateBefore(capturedEnd, 1500, 200);
            foreach (var lead in Enum.GetValues<EcgLead>())
            {
                var actual = blocks.SelectMany(b => b.Planes.Single(p => p.ChannelId == ProjectedEcgDemoSource.ChannelId(lead)).Samples).ToArray();
                if (actual.Length != expected.Count || actual.Zip(expected).Any(p => Math.Abs(p.First - p.Second.MicrovoltValues[(int)lead]) > 1))
                { throw new InvalidOperationException("SVT source did not use shared zero-offset schedule."); }
            }
            var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
            var a = source.AdvanceTo(6_200_000_000, 50, 1, 100); var b2 = restored.AdvanceTo(6_200_000_000, 50, 1, 100);
            if (a.Count != b2.Count || a.Zip(b2).Any(p => !p.First.SequenceEqual(p.Second))) { throw new InvalidOperationException("SVT wire recovery mismatch."); }
            Click(window.HoldButton); Click(window.RunButton);
            var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
            window.WpwInput.IsChecked = true; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
            { throw new InvalidOperationException("SVT conflict mutated accepted state."); }
            Click(window.ResetButton);
            if (window.SvtInput.IsChecked != true || window.SvtRbbbInput.IsChecked != rbbb || window.WpwInput.IsChecked == true) { throw new InvalidOperationException("SVT reset lost accepted state."); }
            window.SvtRbbbInput.IsChecked = !rbbb; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != ((rbbb ? ProjectedEcgDemoConfiguration.SvtPreset : ProjectedEcgDemoConfiguration.SvtRightBundlePreset))) { throw new InvalidOperationException("SVT RBBB toggle failed."); }
            window.SvtRbbbInput.IsChecked = rbbb; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config) { throw new InvalidOperationException("SVT RBBB toggle restore failed."); }
            window.SvtInput.IsChecked = false; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default || window.PrIntervalInput.Text == "—") { throw new InvalidOperationException("SVT clear failed."); }
            Click(window.SvtButton); Click(window.WpwButton);
            if (window.SvtInput.IsChecked == true || window.SvtRbbbInput.IsChecked == true) { throw new InvalidOperationException("WPW loader retained SVT."); }
        }
        finally { window.Close(); }
        foreach (var invalid in new[] { config with { Svt = false, SvtRbbb = true }, config with { Wpw = true }, config with { HeartRateBpm = 75 }, config with { VentricularConductionRatio = 2 }, config with { IndependentVentricularPeriodMilliseconds = 800 }, config with { QrsDurationMilliseconds = rbbb ? 80 : 140 } })
        {
            try { ProjectedEcgDemoSource.Create(invalid); }
            catch (EventWaveformException e) when (e.ReasonCode == "Svt.ConflictingModes") { continue; }
            throw new InvalidOperationException("Invalid SVT source accepted.");
        }
        Console.WriteLine("ok: SVT200bpm narrow/RBBB variants, shared samples, overlap pixels, hidden PR, recovery and atomic lifecycle");
    }
}
