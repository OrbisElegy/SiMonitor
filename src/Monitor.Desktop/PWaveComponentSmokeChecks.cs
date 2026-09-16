// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class PWaveComponentSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.TPeakInput.Text = "50";
            window.UAmplitudeInputs[2].Text = "60";
            window.ChestJInput.Text = "200";
            window.ChestStEndInput.Text = "-100";
            foreach (var (early, late) in new[] { ("150", "-150"), ("150", "150"), ("-150", "150"), ("0", "0"), ("1000", "-1000"), ("", "") })
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.PEarlyInput.Text = early;
                window.PLateInput.Text = late;
                Click(window.ApplyEcgButton);
                window.Pulse(oldTimer);
                var config = window.EcgConfiguration;
                if ((config.ChestP is null) != (early == "") ||
                    (config.ChestP is { } p && (p.EarlyMicrovolts.ToString(CultureInfo.InvariantCulture) != early || p.LateMicrovolts.ToString(CultureInfo.InvariantCulture) != late)) ||
                    window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("P components did not atomically replace the source."); }
                for (int step = 0; step < 14; step++) { Click(window.StepButton); }
                var expected = ProjectedEcgDemoSource.Create(config).AdvanceTo(2_800_000_000, 700, 14, 100);
                var blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                var reference = ProjectedEcgDemoSource.Create(config with { ChestP = null }).AdvanceTo(2_800_000_000, 700, 14, 100)
                    .Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                bool differs = false;
                foreach (EcgLead lead in Enum.GetValues<EcgLead>())
                {
                    short[] Samples(WaveformEnvelope[] values) => values.SelectMany(block => block.Planes.Single(plane => plane.ChannelId == ProjectedEcgDemoSource.ChannelId(lead)).Samples).ToArray();
                    short[] actual = Samples(blocks);
                    short[] original = Samples(reference);
                    for (int i = 0; i < actual.Length; i++)
                    {
                        bool same = actual[i] == original[i];
                        if ((lead != EcgLead.V1 || early == "" || i % 200 >= 25) && !same)
                        { throw new InvalidOperationException("P components changed other leads or QRS/ST/T/U."); }
                        differs |= !same;
                    }
                }
                if (differs != (early != "")) { throw new InvalidOperationException("P components did not affect acquired C1 P data."); }
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 0, [EcgLead.V1]);
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 12, [EcgLead.V1]);
                var source = ProjectedEcgDemoSource.Create(config);
                List<byte[]> restored = [];
                for (int step = 1; step <= 14; step++)
                {
                    restored.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                    source = ElectrodeWaveformGroup.Restore(source.CaptureState());
                }
                if (expected.Count != restored.Count || expected.Zip(restored).Any(pair => !pair.First.SequenceEqual(pair.Second)))
                { throw new InvalidOperationException("P components changed checkpoint wire bytes."); }
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace;
                var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                foreach (var (badEarly, badLate) in new[] { ("1001", "0"), ("0", "-1001"), ("1.1", "0"), ("bad", "0"), ("100", ""), ("", "100") })
                {
                    window.PEarlyInput.Text = badEarly;
                    window.PLateInput.Text = badLate;
                    Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before ||
                        string.IsNullOrWhiteSpace(window.EcgConfigurationStatus.Text))
                    { throw new InvalidOperationException("Invalid P components changed the accepted source/view/timer."); }
                }
                Click(window.ResetButton);
                if (window.PEarlyInput.Text != early || window.PLateInput.Text != late || window.BlockCount != 0 || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Reset lost accepted P components."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: native notched/biphasic P components, signed pixels, other-band parity, checkpoint bytes and atomic lifecycle");
    }
}
