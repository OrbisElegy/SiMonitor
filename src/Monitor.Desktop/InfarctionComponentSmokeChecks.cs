// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class InfarctionComponentSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.InfarctionInputs[2].IsChecked = true;
            window.RepolarizationDelayInput.Text = "80";
            foreach (var (q, t, j, end, weight) in new[] { (0, "-500", "0", "0", "100"), (1, "", "0", "0", "100"), (0, "", "200", "100", "100"), (2, "-500", "200", "100", "100"), (1, "-500", "200", "100", "37.5"), (2, "-500", "200", "100", "0") })
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var stale = window.ActiveTimer;
                window.IndependentComponentsInput.IsChecked = true;
                window.NecrosisShapeInput.SelectedIndex = q;
                window.QrsTemplateInput.Text = weight;
                window.ComponentTInput.Text = t; window.ComponentJInput.Text = j; window.ComponentEndInput.Text = end;
                Click(window.ApplyEcgButton); window.Pulse(stale);
                var config = window.EcgConfiguration;
                if (config.Infarction?.Components is not { } components || (int)components.Necrosis != q ||
                    components.QrsTemplatePermille != (int)(decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture) * 10) ||
                    config.Infarction.Stage != InfarctionIllustrationStage.None || window.SimulationTimeNs != 0 ||
                    window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Independent components did not apply without a stage."); }
                for (int step = 0; step < 14; step++) { Click(window.StepButton); }
                var expected = ProjectedEcgDemoSource.Create(config).AdvanceTo(2_800_000_000, 700, 14, 100);
                var blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 40, [EcgLead.V3]);
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 60, [EcgLead.V3]);
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 110, [EcgLead.V3]);
                var source = ProjectedEcgDemoSource.Create(config); List<byte[]> restored = [];
                for (int step = 1; step <= 14; step++)
                {
                    restored.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                    source = ElectrodeWaveformGroup.Restore(source.CaptureState());
                }
                if (expected.Count != restored.Count || expected.Zip(restored).Any(p => !p.First.SequenceEqual(p.Second)))
                { throw new InvalidOperationException("Independent composition recovery changed bytes."); }
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace; var timer = window.ActiveTimer; long before = window.SimulationTimeNs;
                foreach (var (input, invalid) in new[] { (window.ComponentTInput, "-2001"), (window.ComponentJInput, "1001"),
                    (window.ComponentEndInput, "bad"), (window.ComponentArchInput, "-1001"), (window.QrsTemplateInput, "100.1"), (window.QrsTemplateInput, "-1"),
                    (window.QrsTemplateInput, "37.55"), (window.QrsTemplateInput, "bad") })
                {
                    string? old = input.Text; input.Text = invalid; Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace) ||
                        !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before)
                    { throw new InvalidOperationException("Invalid component input changed accepted source."); }
                    input.Text = old;
                }
                window.NecrosisShapeInput.SelectedIndex = -1; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config) { throw new InvalidOperationException("Unknown necrosis shape accepted."); }
                Click(window.ResetButton);
                if (window.NecrosisShapeInput.SelectedIndex != q || window.ComponentTInput.Text != t || window.QrsTemplateInput.Text != weight || window.IndependentComponentsInput.IsChecked != true)
                { throw new InvalidOperationException("Reset lost accepted components."); }
            }
            window.IndependentComponentsInput.IsChecked = false; Click(window.ApplyEcgButton);
            var off = window.EcgConfiguration;
            var actual = ProjectedEcgDemoSource.Create(off).AdvanceTo(2_800_000_000, 700, 14, 100);
            var reference = ProjectedEcgDemoSource.Create(off with { Infarction = null }).AdvanceTo(2_800_000_000, 700, 14, 100);
            if (actual.Count != reference.Count || actual.Zip(reference).Any(p => !p.First.SequenceEqual(p.Second)))
            { throw new InvalidOperationException("Disabling independent mode did not restore stage-none reference."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: independent Q/ST/T composition without a stage, pixels, recovery and atomic lifecycle");
    }
}
