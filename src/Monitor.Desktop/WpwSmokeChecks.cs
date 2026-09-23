// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class WpwSmokeChecks
{
    internal static void Verify()
    {
        Verify(false);
        Verify(true);
    }
    private static void Verify(bool negativeV1)
    {
        var window = new WaveformDemoWindow(projected: true);
        window.Show();
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.VentricularDisorganizationButton); Click(window.StepButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            Click(window.WpwButton); window.Pulse(oldTimer);
            window.WpwNegativeV1Input.IsChecked = negativeV1;
            Click(window.ApplyEcgButton);
            var config = ProjectedEcgDemoConfiguration.WpwPreset with { WpwNegativeV1 = negativeV1 };
            if (window.EcgConfiguration != config || window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null)
            { throw new InvalidOperationException("WPW loader retained previous source."); }
            Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config || !string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
            { throw new InvalidOperationException("WPW reapply failed."); }
            var source = ProjectedEcgDemoSource.Create(config);
            var blocks = new List<WaveformEnvelope>();
            for (int step = 1; step <= 30; step++)
            {
                Click(window.StepButton);
                blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
            }
            foreach (int sample in new[] { 25, 40, 60, 95 })
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks.ToArray(), sample, [EcgLead.I, EcgLead.II, EcgLead.V1, EcgLead.V5]);
            var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
            var expected = source.AdvanceTo(6_200_000_000, 50, 1, 100);
            var actual = restored.AdvanceTo(6_200_000_000, 50, 1, 100);
            if (expected.Count == 0 || expected.Count != actual.Count || expected.Zip(actual).Any(p => !p.First.SequenceEqual(p.Second)))
            { throw new InvalidOperationException("WPW wire recovery diverged."); }
            Click(window.HoldButton); Click(window.RunButton);
            var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
            window.DigitalisInput.IsChecked = true; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config || window.ActiveTimer != timer || window.Trace != trace || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
            { throw new InvalidOperationException("WPW conflicting edit mutated accepted state."); }
            Click(window.ResetButton);
            if (window.WpwNegativeV1Input.IsChecked != negativeV1 || window.WpwInput.IsChecked != true || window.DigitalisInput.IsChecked == true)
            { throw new InvalidOperationException("WPW reset lost accepted state."); }
            foreach (var invalid in new[] { config with { Wpw = false, WpwNegativeV1 = true }, config with { PrIntervalMilliseconds = 160 }, config with { BundleBlock = EcgBundleBlockIllustration.CompleteLeft }, config with { VentricularConductionRatio = 2 } })
            {
                try { ProjectedEcgDemoSource.Create(invalid); }
                catch (EventWaveformException e) when (e.ReasonCode == "Wpw.ConflictingModes") { continue; }
                throw new InvalidOperationException("Conflicting WPW source accepted.");
            }
            window.WpwNegativeV1Input.IsChecked = !negativeV1; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != (config with { WpwNegativeV1 = !negativeV1 }))
            { throw new InvalidOperationException("WPW variant switch failed."); }
            window.WpwNegativeV1Input.IsChecked = negativeV1; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config)
            { throw new InvalidOperationException("WPW variant roundtrip failed."); }
            window.WpwInput.IsChecked = false; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default)
            { throw new InvalidOperationException("WPW clear failed."); }
            Click(window.WpwButton); Click(window.QuinidineButton);
            if (window.WpwInput.IsChecked == true) { throw new InvalidOperationException("Other loader retained WPW."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: native WPW delta/QRS/ST/T pixels, shortened PR, wire recovery and atomic lifecycle");
    }
}
