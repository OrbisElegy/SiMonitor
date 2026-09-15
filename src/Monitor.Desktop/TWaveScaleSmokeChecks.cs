// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class TWaveScaleSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.UAmplitudeInputs[2].Text = "60";
            foreach (string scale in new[] { "-1.25", "0", "4", "-4", "1" })
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.TScaleInputs[2].Text = scale;
                Click(window.ApplyEcgButton);
                window.Pulse(oldTimer);
                var config = window.EcgConfiguration;
                if (window.TScaleInputs[2].Text != scale || (config.TWave is null) != (scale == "1") ||
                    window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("T gain did not atomically replace the source."); }
                for (int step = 0; step < 14; step++) { Click(window.StepButton); }
                var expected = ProjectedEcgDemoSource.Create(config).AdvanceTo(2_800_000_000, 700, 14, 100);
                var blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                var original = ProjectedEcgDemoSource.Create(config with { TWave = null }).AdvanceTo(2_800_000_000, 700, 14, 100)
                    .Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                foreach (EcgLead lead in Enum.GetValues<EcgLead>())
                {
                    short[] Samples(WaveformEnvelope[] values) => values.SelectMany(block => block.Planes.Single(plane => plane.ChannelId == ProjectedEcgDemoSource.ChannelId(lead)).Samples).ToArray();
                    short[] actualSamples = Samples(blocks);
                    short[] referenceSamples = Samples(original);
                    if (lead != EcgLead.V3 || scale == "1")
                    {
                        if (!actualSamples.SequenceEqual(referenceSamples)) { throw new InvalidOperationException("T gain changed an unrelated lead or default output."); }
                    }
                    else if (actualSamples.SequenceEqual(referenceSamples) || actualSamples.Where((_, i) => i % 200 < 85 || i % 200 >= 130)
                        .SequenceEqual(referenceSamples.Where((_, i) => i % 200 < 85 || i % 200 >= 130)) == false)
                    { throw new InvalidOperationException("T gain lost its isolated repolarization support or changed U/P/QRS."); }
                }
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 105);
                var source = ProjectedEcgDemoSource.Create(config);
                List<byte[]> restored = [];
                for (int step = 1; step <= 14; step++)
                {
                    restored.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                    source = ElectrodeWaveformGroup.Restore(source.CaptureState());
                }
                if (expected.Count != restored.Count || expected.Zip(restored).Any(pair => !pair.First.SequenceEqual(pair.Second)))
                { throw new InvalidOperationException("T gain checkpoint changed published bytes."); }
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace;
                var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                foreach (string invalid in new[] { "4.001", "-4.001", "0.0001", "bad", "" })
                {
                    window.TScaleInputs[2].Text = invalid;
                    Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before)
                    { throw new InvalidOperationException("Invalid T scale changed accepted source/view/timer."); }
                }
                Click(window.ResetButton);
                if (window.TScaleInputs[2].Text != scale || window.BlockCount != 0 || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Reset lost accepted T scale."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: signed chest T gains preserve other leads/P/QRS/U, raw checkpoint bytes, native pixels and atomic lifecycle");
    }
}
