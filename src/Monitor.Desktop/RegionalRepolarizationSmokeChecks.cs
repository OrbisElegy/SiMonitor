// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class RegionalRepolarizationSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.InfarctionInputs[2].IsChecked = true;
            window.InfarctionStageInput.SelectedIndex = (int)InfarctionIllustrationStage.HyperacuteT;
            window.UDelayInput.Text = "100"; window.UAmplitudeInputs[2].Text = "60";
            foreach (int delay in new[] { 80, 100, 0 })
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.RepolarizationDelayInput.Text = delay.ToString(CultureInfo.InvariantCulture);
                Click(window.ApplyEcgButton);
                window.Pulse(oldTimer);
                var config = window.EcgConfiguration;
                if (config.Infarction?.RepolarizationDelayNs != delay * 1_000_000L || window.SimulationTimeNs != 0 ||
                    window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Regional delay did not restart atomically."); }
                for (int step = 0; step < 14; step++) { Click(window.StepButton); }
                var expected = ProjectedEcgDemoSource.Create(config).AdvanceTo(2_800_000_000, 700, 14, 100);
                var blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 135, [EcgLead.V3]);
                var source = ProjectedEcgDemoSource.Create(config);
                List<byte[]> restored = [];
                for (int step = 1; step <= 14; step++)
                {
                    restored.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                    source = ElectrodeWaveformGroup.Restore(source.CaptureState());
                }
                if (expected.Count != restored.Count || expected.Zip(restored).Any(p => !p.First.SequenceEqual(p.Second)))
                { throw new InvalidOperationException("Regional delay recovery changed waveform bytes."); }
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace; var timer = window.ActiveTimer; long before = window.SimulationTimeNs;
                foreach (string invalid in new[] { "-1", "501", "1.5", "bad", "101", "281" })
                {
                    window.RepolarizationDelayInput.Text = invalid;
                    Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) ||
                        before != window.SimulationTimeNs || string.IsNullOrWhiteSpace(window.EcgConfigurationStatus.Text))
                    { throw new InvalidOperationException("Invalid delay or T-U overlap changed accepted source."); }
                }
                Click(window.ResetButton);
                if (window.RepolarizationDelayInput.Text != delay.ToString(CultureInfo.InvariantCulture) || window.BlockCount != 0)
                { throw new InvalidOperationException("Reset lost accepted regional delay."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: regional T duration and QT extension, late pixels, U overlap rejection and atomic lifecycle");
    }
}
