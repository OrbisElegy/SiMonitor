// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class QrsContributionSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (bool zonesMode in new[] { false, true })
            {
                window.SeparateZonesInput.IsChecked = zonesMode;
                window.IndependentComponentsInput.IsChecked = !zonesMode;
                window.InfarctionInputs[2].IsChecked = true;
                window.NecrosisZoneInput.SelectedIndex = 11;
                window.IschemiaZoneInput.SelectedIndex = 9;
                window.InjuryZoneInput.SelectedIndex = 7;
                window.ContributionLossInput.IsChecked = true;
                window.ContributionAmountInput.Text = "50";
                window.ComponentTInput.Text = "-500";
                window.ComponentJInput.Text = "200";
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var stale = window.ActiveTimer;
                Click(window.ApplyEcgButton); window.Pulse(stale);
                var config = window.EcgConfiguration;
                var components = config.Zones?.Components ?? config.Infarction?.Components;
                if (components?.ContributionLoss?.LossPermille != 500 || window.SimulationTimeNs != 0 ||
                    window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Contribution removal failed atomic application."); }
                for (int step = 0; step < 14; step++) { Click(window.StepButton); }
                var bytes = ProjectedEcgDemoSource.Create(config).AdvanceTo(2_800_000_000, 700, 14, 100);
                var blocks = bytes.Select(b => WaveformEnvelopeCodec.Decode(b)).ToArray();
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 40, [EcgLead.I, EcgLead.V3, EcgLead.V5]);
                var source = ProjectedEcgDemoSource.Create(config); List<byte[]> restored = [];
                for (int step = 1; step <= 14; step++)
                {
                    restored.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                    source = ElectrodeWaveformGroup.Restore(source.CaptureState());
                }
                if (bytes.Count != restored.Count || bytes.Zip(restored).Any(p => !p.First.SequenceEqual(p.Second)))
                { throw new InvalidOperationException("Contribution checkpoint changed source bytes."); }
                Click(window.HoldButton); Click(window.RunButton);
                var timer = window.ActiveTimer; var trace = window.Trace; long time = window.SimulationTimeNs;
                string? report = window.QrsMeasurementStatus.Text;
                foreach (var (input, bad) in new[] { (window.ContributionAmplitudeInput, "2001"),
                    (window.ContributionDurationInput, "9"), (window.ContributionAmountInput, "101"), (window.ContributionAmountInput, "1.5") })
                {
                    string? old = input.Text; input.Text = bad; Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || window.QrsMeasurementStatus.Text != report ||
                        !ReferenceEquals(timer, window.ActiveTimer) || !ReferenceEquals(trace, window.Trace) || time != window.SimulationTimeNs)
                    { throw new InvalidOperationException("Invalid contribution input changed accepted source."); }
                    input.Text = old;
                }
                window.NecrosisShapeInput.SelectedIndex = 2; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config) { throw new InvalidOperationException("Conflicting QRS template was accepted."); }
                Click(window.ResetButton);
                if (window.ContributionLossInput.IsChecked != true || window.ContributionAmountInput.Text != "50" || window.NecrosisShapeInput.SelectedIndex != 0)
                { throw new InvalidOperationException("Reset lost accepted contribution plan."); }
                window.ContributionAmountInput.Text = "0"; Click(window.ApplyEcgButton);
                var neutral = ProjectedEcgDemoSource.Create(window.EcgConfiguration).AdvanceTo(200_000_000, 50, 1, 100);
                window.ContributionLossInput.IsChecked = false; Click(window.ApplyEcgButton);
                var disabled = ProjectedEcgDemoSource.Create(window.EcgConfiguration).AdvanceTo(200_000_000, 50, 1, 100);
                if (neutral.Count != disabled.Count || neutral.Zip(disabled).Any(p => !p.First.SequenceEqual(p.Second)))
                { throw new InvalidOperationException("Zero removal did not restore source identity."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: explicit QRS contribution removal, chest/limb pixels, zero parity and atomic lifecycle");
    }
}
