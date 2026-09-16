// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class InfarctionIllustrationSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.InfarctionInputs[2].IsChecked = true;
            window.InfarctionInputs[3].IsChecked = true;
            window.UAmplitudeInputs[2].Text = "60";
            window.FusionInputs[2].IsChecked = true;
            foreach (var stage in Enum.GetValues<InfarctionIllustrationStage>().Skip(1).Append(InfarctionIllustrationStage.None))
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.InfarctionStageInput.SelectedIndex = (int)stage;
                Click(window.ApplyEcgButton);
                window.Pulse(oldTimer);
                var config = window.EcgConfiguration;
                if (config.Infarction != new EcgChestInfarctionPlan(12, stage) || window.BlockCount != 0 ||
                    window.SimulationTimeNs != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Stage did not atomically replace the selected source."); }
                for (int step = 0; step < 14; step++) { Click(window.StepButton); }
                var expected = ProjectedEcgDemoSource.Create(config).AdvanceTo(2_800_000_000, 700, 14, 100);
                var blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                var baseline = ProjectedEcgDemoSource.Create(config with { Infarction = null }).AdvanceTo(2_800_000_000, 700, 14, 100)
                    .Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                foreach (EcgLead lead in Enum.GetValues<EcgLead>())
                {
                    short[] Samples(WaveformEnvelope[] values) => values.SelectMany(block => block.Planes.Single(plane => plane.ChannelId == ProjectedEcgDemoSource.ChannelId(lead)).Samples).ToArray();
                    short[] actual = Samples(blocks); short[] original = Samples(baseline);
                    bool selected = stage != InfarctionIllustrationStage.None && lead is EcgLead.V3 or EcgLead.V4;
                    bool changed = false;
                    for (int i = 0; i < actual.Length; i++)
                    {
                        bool same = actual[i] == original[i];
                        if ((!selected || i % 200 < 40 || i % 200 >= 130) && !same)
                        { throw new InvalidOperationException("Stage changed an unselected lead or P/U."); }
                        changed |= !same;
                    }
                    if (changed != selected) { throw new InvalidOperationException("Stage mask did not reach acquired lead data."); }
                }
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 40, [EcgLead.V3, EcgLead.V4]);
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 95, [EcgLead.V3, EcgLead.V4]);
                var source = ProjectedEcgDemoSource.Create(config);
                List<byte[]> recovered = [];
                for (int step = 1; step <= 14; step++)
                {
                    recovered.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                    source = ElectrodeWaveformGroup.Restore(source.CaptureState());
                }
                if (expected.Count != recovered.Count || expected.Zip(recovered).Any(pair => !pair.First.SequenceEqual(pair.Second)))
                { throw new InvalidOperationException("Stage recovery changed wire bytes."); }
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace; var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                window.InfarctionStageInput.SelectedIndex = -1;
                Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) ||
                    window.SimulationTimeNs != before || string.IsNullOrWhiteSpace(window.EcgConfigurationStatus.Text))
                { throw new InvalidOperationException("Invalid stage changed accepted state."); }
                Click(window.ResetButton);
                if (window.InfarctionStageInput.SelectedIndex != (int)stage || window.InfarctionInputs[2].IsChecked != true ||
                    window.InfarctionInputs[3].IsChecked != true || window.BlockCount != 0)
                { throw new InvalidOperationException("Reset lost accepted stage or region."); }
            }
            window.InfarctionInputs[2].IsChecked = false;
            window.InfarctionInputs[3].IsChecked = false;
            window.InfarctionStageInput.SelectedIndex = (int)InfarctionIllustrationStage.AcuteQsInvertedT;
            Click(window.ApplyEcgButton);
            var disabled = window.EcgConfiguration;
            var emptyMask = ProjectedEcgDemoSource.Create(disabled).AdvanceTo(2_800_000_000, 700, 14, 100);
            var normal = ProjectedEcgDemoSource.Create(disabled with { Infarction = null }).AdvanceTo(2_800_000_000, 700, 14, 100);
            if (emptyMask.Zip(normal).Any(pair => !pair.First.SequenceEqual(pair.Second)))
            { throw new InvalidOperationException("Cleared region did not restore prior source."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: infarction stage illustrations, regional Q/QS and ST/T pixels, unchanged leads, wire recovery and atomic lifecycle");
    }
}
