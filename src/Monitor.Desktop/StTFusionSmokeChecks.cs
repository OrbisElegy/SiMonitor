// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class StTFusionSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.TPeakInput.Text = "50";
            window.TScaleInputs[1].Text = "-1.25";
            window.ChestJInput.Text = "100";
            window.ChestStEndInput.Text = "-100";
            window.ChestStArchInput.Text = "200";
            window.UAmplitudeInputs[2].Text = "60";
            window.PEarlyInput.Text = "150"; window.PLateInput.Text = "-150";
            foreach (var (mask, position, sign) in new[] { (6, "5", 1), (14, "50", 1), (63, "50", -1), (2, "0.1", 1), (4, "99.9", 1), (0, "50", 1) })
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                for (int i = 0; i < 6; i++) { window.FusionInputs[i].IsChecked = (mask & (1 << i)) != 0; }
                window.FusionPositionInput.Text = position;
                window.FusionJInput.Text = (sign * 200).ToString(CultureInfo.InvariantCulture);
                window.FusionPeakInput.Text = (sign * 500).ToString(CultureInfo.InvariantCulture);
                Click(window.ApplyEcgButton);
                window.Pulse(oldTimer);
                var config = window.EcgConfiguration;
                var fusion = config.Fusion ?? ProjectedEcgFusionConfiguration.Default;
                if (fusion.ChestMask != mask || fusion.PeakPositionPermille != decimal.Parse(position, CultureInfo.InvariantCulture) * 10 ||
                    window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Fusion did not atomically replace the selected source."); }
                for (int step = 0; step < 14; step++) { Click(window.StepButton); }
                var expected = ProjectedEcgDemoSource.Create(config).AdvanceTo(2_800_000_000, 700, 14, 100);
                var blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                var baseline = ProjectedEcgDemoSource.Create(config with { Fusion = null }).AdvanceTo(2_800_000_000, 700, 14, 100)
                    .Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                foreach (EcgLead lead in Enum.GetValues<EcgLead>())
                {
                    short[] Samples(WaveformEnvelope[] values) => values.SelectMany(block => block.Planes.Single(plane => plane.ChannelId == ProjectedEcgDemoSource.ChannelId(lead)).Samples).ToArray();
                    short[] actual = Samples(blocks); short[] original = Samples(baseline);
                    bool selected = (int)lead >= 6 && (mask & (1 << ((int)lead - 6))) != 0;
                    bool differs = false;
                    for (int i = 0; i < actual.Length; i++)
                    {
                        bool same = actual[i] == original[i];
                        if ((!selected || i % 200 < 55 || i % 200 >= 130) && !same)
                        { throw new InvalidOperationException("Fusion changed an unselected lead or unrelated sample."); }
                        differs |= !same;
                    }
                    if (differs != selected) { throw new InvalidOperationException("Fusion region does not match selected leads."); }
                }
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 60, [EcgLead.V2, EcgLead.V3, EcgLead.V4]);
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 90, [EcgLead.V2, EcgLead.V3, EcgLead.V4]);
                var source = ProjectedEcgDemoSource.Create(config);
                List<byte[]> recovered = [];
                for (int step = 1; step <= 14; step++)
                {
                    recovered.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                    source = ElectrodeWaveformGroup.Restore(source.CaptureState());
                }
                if (expected.Count != recovered.Count || expected.Zip(recovered).Any(pair => !pair.First.SequenceEqual(pair.Second)))
                { throw new InvalidOperationException("Fusion recovery changed wire bytes."); }
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace; var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                foreach (var (input, invalid) in new[] { (window.FusionJInput, "1001"), (window.FusionPeakInput, "-2001"),
                    (window.FusionPositionInput, "0"), (window.FusionPositionInput, "100"), (window.FusionPositionInput, "0.15"), (window.FusionPeakInput, "bad") })
                {
                    string accepted = input.Text!;
                    input.Text = invalid; Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace) ||
                        !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before ||
                        string.IsNullOrWhiteSpace(window.EcgConfigurationStatus.Text))
                    { throw new InvalidOperationException("Invalid fusion changed accepted source/view/timer."); }
                    input.Text = accepted;
                }
                window.FusionInputs[0].IsChecked = null;
                Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace))
                { throw new InvalidOperationException("Unknown fusion selection was accepted."); }
                Click(window.ResetButton);
                if (window.FusionPositionInput.Text != position || window.BlockCount != 0 || window.ActiveTimer is not null ||
                    window.FusionInputs.Where((input, i) => input.IsChecked != ((mask & (1 << i)) != 0)).Any())
                { throw new InvalidOperationException("Reset lost accepted fusion parameters."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: regional ST-T fusion preserves unselected leads, signed pixels, recovery and atomic lifecycle");
    }
}
