// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class InfarctionZoneSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (var (ischemia, injury, necrosis) in new[] { (9, 7, 3), (10, 11, 3) })
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var stale = window.ActiveTimer;
                window.SeparateZonesInput.IsChecked = true;
                window.IschemiaZoneInput.SelectedIndex = ischemia;
                window.InjuryZoneInput.SelectedIndex = injury;
                window.NecrosisZoneInput.SelectedIndex = necrosis;
                window.NecrosisShapeInput.SelectedIndex = 1;
                window.QrsTemplateInput.Text = "37.5";
                window.ComponentTInput.Text = "-500";
                window.ComponentJInput.Text = "200";
                window.ComponentEndInput.Text = "100";
                window.RepolarizationDelayInput.Text = "80";
                window.ChestJInput.Text = "70"; // Retained draft in separate-zone mode.
                Click(window.ApplyEcgButton); window.Pulse(stale);
                var config = window.EcgConfiguration;
                if (config.Zones is not { } zones || zones.Ischemia != InfarctionZoneSelection.Resolve(ischemia) ||
                    zones.Components.QrsTemplatePermille != 375 || zones.Injury != InfarctionZoneSelection.Resolve(injury) || zones.Necrosis != InfarctionZoneSelection.Resolve(necrosis) ||
                    window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Separate zones did not restart atomically."); }
                for (int step = 0; step < 14; step++) { Click(window.StepButton); }
                var expected = ProjectedEcgDemoSource.Create(config).AdvanceTo(2_800_000_000, 700, 14, 100);
                var blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 40, [EcgLead.V3]);
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 60, [EcgLead.I, EcgLead.V1, EcgLead.V5]);
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 135, [EcgLead.II, EcgLead.V3, EcgLead.V5]);
                var source = ProjectedEcgDemoSource.Create(config); List<byte[]> restored = [];
                for (int step = 1; step <= 14; step++)
                {
                    restored.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                    source = ElectrodeWaveformGroup.Restore(source.CaptureState());
                }
                if (expected.Count != restored.Count || expected.Zip(restored).Any(p => !p.First.SequenceEqual(p.Second)))
                { throw new InvalidOperationException("Separate-zone recovery changed bytes."); }
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace; var timer = window.ActiveTimer; long before = window.SimulationTimeNs;
                foreach (var selector in new[] { window.IschemiaZoneInput, window.InjuryZoneInput, window.NecrosisZoneInput })
                {
                    int accepted = selector.SelectedIndex; selector.SelectedIndex = -1; Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace) ||
                        !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before)
                    { throw new InvalidOperationException("Invalid zone changed accepted source."); }
                    selector.SelectedIndex = accepted;
                }
                window.ComponentTInput.Text = "-2001"; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config) { throw new InvalidOperationException("Invalid zone amplitude accepted."); }
                Click(window.ResetButton);
                if (window.IschemiaZoneInput.SelectedIndex != ischemia || window.InjuryZoneInput.SelectedIndex != injury ||
                    window.NecrosisZoneInput.SelectedIndex != necrosis || window.SeparateZonesInput.IsChecked != true ||
                    window.ComponentTInput.Text != "-500" || window.QrsTemplateInput.Text != "37.5" || window.RepolarizationDelayInput.Text != "80")
                { throw new InvalidOperationException("Reset lost accepted zones."); }
                window.SeparateZonesInput.IsChecked = false; Click(window.ApplyEcgButton);
                var off = window.EcgConfiguration;
                var actual = ProjectedEcgDemoSource.Create(off).AdvanceTo(2_800_000_000, 700, 14, 100);
                var previous = ProjectedEcgDemoSource.Create(config with { Zones = null }).AdvanceTo(2_800_000_000, 700, 14, 100);
                if (off.Zones is not null || off.ChestJMicrovolts != 70 || actual.Zip(previous).Any(p => !p.First.SequenceEqual(p.Second)))
                { throw new InvalidOperationException("Disabling zones did not restore retained manual settings."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: independent infarction zones, chest/limb pixels, recovery and atomic lifecycle");
    }
}
