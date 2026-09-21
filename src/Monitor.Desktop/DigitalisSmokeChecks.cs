// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class DigitalisSmokeChecks
{
    internal static void Verify()
    {
        var window = new WaveformDemoWindow(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.HyperkalemiaFusionInput.IsChecked = true; Click(window.HyperkalemiaButton);
            Click(window.StepButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            Click(window.DigitalisButton); window.Pulse(oldTimer);
            var config = ProjectedEcgDemoConfiguration.Digitalis;
            if (window.EcgConfiguration != config || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.BlockCount != 0)
            { throw new InvalidOperationException("Digitalis loader retained prior source/timer."); }
            Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config || !string.IsNullOrEmpty(window.EcgConfigurationStatus.Text) || window.TDurationInput.Text != "—")
            { throw new InvalidOperationException("Digitalis reapply or interval summary failed."); }
            var source = ProjectedEcgDemoSource.Create(config);
            var blocks = new List<WaveformEnvelope>();
            for (int step = 1; step <= 30; step++)
            {
                Click(window.StepButton);
                blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
            }
            foreach (int sample in new[] { 70, 100 })
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks.ToArray(), sample, [EcgLead.I, EcgLead.II, EcgLead.AVR, EcgLead.V4, EcgLead.V6]);
            var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
            var a = source.AdvanceTo(6_200_000_000, 50, 1, 100);
            var b = restored.AdvanceTo(6_200_000_000, 50, 1, 100);
            if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second)))
            { throw new InvalidOperationException("Digitalis wire recovery diverged."); }
            Click(window.HoldButton); Click(window.RunButton);
            var trace = window.Trace; var timer = window.ActiveTimer; long time = window.SimulationTimeNs;
            window.TContourInput.SelectedIndex = 5; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config || window.Trace != trace || window.ActiveTimer != timer || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
            { throw new InvalidOperationException("Conflicting digitalis edit changed accepted state."); }
            Click(window.ResetButton);
            if (window.DigitalisInput.IsChecked != true || window.TContourInput.SelectedIndex != 0)
            { throw new InvalidOperationException("Digitalis reset lost mode."); }
            foreach (var invalid in new[] { config with { HypokalemiaRepolarization = true }, config with { Calcium = CalciumIllustration.High }, config with { QrsDurationMilliseconds = 120 }, config with { Placement = (EcgLimbPlacement)99 } })
            {
                try { ProjectedEcgDemoSource.Create(invalid); }
                catch (EventWaveformException e) when (e.ReasonCode == "Digitalis.ConflictingModes") { continue; }
                throw new InvalidOperationException("Conflicting digitalis configuration accepted.");
            }
            window.DigitalisInput.IsChecked = false; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default)
            { throw new InvalidOperationException("Digitalis clear failed."); }
            window.DigitalisInput.IsChecked = true; Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != config)
            { throw new InvalidOperationException("Digitalis return failed."); }
            window.CalciumInput.SelectedIndex = 1; Click(window.CalciumButton);
            if (window.DigitalisInput.IsChecked != false || window.EcgConfiguration != ProjectedEcgDemoConfiguration.CalciumPreset(CalciumIllustration.High))
            { throw new InvalidOperationException("Calcium loader retained digitalis mode."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: native digitalis ST scoop/terminal T pixels, wire restore and atomic preset lifecycle");
    }
}
