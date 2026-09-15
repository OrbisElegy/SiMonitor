// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class TWaveShapeSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.TScaleInputs[2].Text = "-1.25";
            window.UAmplitudeInputs[2].Text = "60";
            foreach (string text in new[] { "10", "50", "62.5", "90", "" })
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.TPeakInput.Text = text;
                Click(window.ApplyEcgButton);
                window.Pulse(oldTimer);
                var config = window.EcgConfiguration;
                int? peak = config.TWave?.PeakPositionPermille;
                if ((peak is { } value ? (value / 10m).ToString("0.#", CultureInfo.InvariantCulture) : "") != text ||
                    window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("T peak did not atomically replace the source."); }
                for (int step = 0; step < 14; step++) { Click(window.StepButton); }
                var expected = ProjectedEcgDemoSource.Create(config).AdvanceTo(2_800_000_000, 700, 14, 100);
                var blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                var reference = ProjectedEcgDemoSource.Create(config with { TWave = config.TWave! with { PeakPositionPermille = null } })
                    .AdvanceTo(2_800_000_000, 700, 14, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                bool differs = false;
                foreach (EcgLead lead in Enum.GetValues<EcgLead>())
                {
                    short[] Samples(WaveformEnvelope[] values) => values.SelectMany(block => block.Planes.Single(plane => plane.ChannelId == ProjectedEcgDemoSource.ChannelId(lead)).Samples).ToArray();
                    short[] actualSamples = Samples(blocks);
                    short[] referenceSamples = Samples(reference);
                    for (int i = 0; i < actualSamples.Length; i++)
                    {
                        bool same = actualSamples[i] == referenceSamples[i];
                        if ((peak is null or 625 || i % 200 < 85 || i % 200 >= 130) && !same)
                        { throw new InvalidOperationException("T peak changed reference output or P/QRS/U support."); }
                        differs |= !same;
                    }
                }
                if (differs != (peak is not (null or 625))) { throw new InvalidOperationException("Manual T peak did not change the raw shape."); }
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, Math.Max(85, 85 + 45 * (peak ?? 625) / 1000 - 5));
                var source = ProjectedEcgDemoSource.Create(config);
                List<byte[]> restored = [];
                for (int step = 1; step <= 14; step++)
                {
                    restored.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                    source = ElectrodeWaveformGroup.Restore(source.CaptureState());
                }
                if (expected.Count != restored.Count || expected.Zip(restored).Any(pair => !pair.First.SequenceEqual(pair.Second)))
                { throw new InvalidOperationException("T phase map changed checkpoint bytes."); }
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace;
                var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                foreach (string invalid in new[] { "9.9", "90.1", "50.01", "-1", "bad" })
                {
                    window.TPeakInput.Text = invalid;
                    Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before)
                    { throw new InvalidOperationException("Invalid T shape changed accepted source/view/timer."); }
                }
                Click(window.ResetButton);
                if (window.TPeakInput.Text != text || window.BlockCount != 0 || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Reset lost accepted T shape."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: shared T peak mapping preserves support/amplitude, signed native pixels, U, checkpoint bytes and atomic lifecycle");
    }
}
