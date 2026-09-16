// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class StSegmentSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.TPeakInput.Text = "50";
            window.TScaleInputs[2].Text = "-1";
            window.UAmplitudeInputs[2].Text = "60";
            foreach (var (j, end, arch) in new[] { (200, 200, 200), (200, 200, 0), (200, -100, 200), (-200, -200, -200), (0, 0, 200), (1000, -1000, 1000), (0, 0, 0) })
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.ChestStArchInput.Text = arch.ToString(CultureInfo.InvariantCulture);
                window.ChestJInput.Text = j.ToString(CultureInfo.InvariantCulture);
                window.ChestStEndInput.Text = end.ToString(CultureInfo.InvariantCulture);
                Click(window.ApplyEcgButton);
                window.Pulse(oldTimer);
                var config = window.EcgConfiguration;
                if (config.ChestStArchMicrovolts != arch || config.ChestJMicrovolts != j || config.ChestStEndMicrovolts != end || window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("ST offsets did not atomically replace the source."); }
                for (int step = 0; step < 14; step++) { Click(window.StepButton); }
                var expected = ProjectedEcgDemoSource.Create(config).AdvanceTo(2_800_000_000, 700, 14, 100);
                var blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                var reference = ProjectedEcgDemoSource.Create(config with { ChestJMicrovolts = 0, ChestStEndMicrovolts = 0, ChestStArchMicrovolts = 0 })
                    .AdvanceTo(2_800_000_000, 700, 14, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                bool differs = false;
                foreach (EcgLead lead in Enum.GetValues<EcgLead>())
                {
                    short[] Samples(WaveformEnvelope[] values) => values.SelectMany(block => block.Planes.Single(plane => plane.ChannelId == ProjectedEcgDemoSource.ChannelId(lead)).Samples).ToArray();
                    short[] actual = Samples(blocks);
                    short[] original = Samples(reference);
                    for (int i = 0; i < actual.Length; i++)
                    {
                        bool same = actual[i] == original[i];
                        if (((int)lead < 6 || j == 0 && end == 0 && arch == 0 || i % 200 < 55 || i % 200 >= 130) && !same)
                        { throw new InvalidOperationException("ST offsets changed limbs or samples outside declared support."); }
                        differs |= !same;
                    }
                }
                if (differs != (j != 0 || end != 0 || arch != 0)) { throw new InvalidOperationException("ST offsets did not change sampled chest potentials."); }
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 60);
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 80);
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 65, [EcgLead.V1, EcgLead.V3, EcgLead.V5]);
                var source = ProjectedEcgDemoSource.Create(config);
                List<byte[]> restored = [];
                for (int step = 1; step <= 14; step++)
                {
                    restored.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                    source = ElectrodeWaveformGroup.Restore(source.CaptureState());
                }
                if (expected.Count != restored.Count || expected.Zip(restored).Any(pair => !pair.First.SequenceEqual(pair.Second)))
                { throw new InvalidOperationException("ST source checkpoint changed wire bytes."); }
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace;
                var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                foreach (var input in new[] { window.ChestJInput, window.ChestStEndInput, window.ChestStArchInput })
                {
                    string accepted = input.Text!;
                    foreach (string invalid in new[] { "1001", "-1001", "1.1", "bad" })
                    {
                        input.Text = invalid;
                        Click(window.ApplyEcgButton);
                        if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before)
                        { throw new InvalidOperationException("Invalid ST offsets changed accepted source/view/timer."); }
                    }
                    input.Text = accepted;
                }
                Click(window.ResetButton);
                if (window.ChestStArchInput.Text != arch.ToString(CultureInfo.InvariantCulture) || window.ChestJInput.Text != j.ToString(CultureInfo.InvariantCulture) || window.ChestStEndInput.Text != end.ToString(CultureInfo.InvariantCulture) || window.BlockCount != 0 || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Reset lost accepted ST offsets."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: independent J/ST endpoints and signed arches preserve limbs/QT/U, signed pixels, checkpoint bytes and atomic lifecycle");
    }
}
