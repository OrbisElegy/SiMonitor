// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class AtrialIllustrationSmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
            var oldTimer = window.ActiveTimer;
            window.AtrialInput.SelectedIndex = 1;
            Click(window.ApplyEcgButton);
            window.Pulse(oldTimer);
            var config = window.EcgConfiguration;
            if (config.Atrial != EcgAtrialIllustration.LeftAtrialAbnormality || config.ResolveTiming().PDurationNs != 140_000_000 ||
                window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
            { throw new InvalidOperationException("Atrial illustration did not atomically restart."); }
            for (int i = 0; i < 14; i++) { Click(window.StepButton); }
            var source = ProjectedEcgDemoSource.Create(config);
            var expected = source.AdvanceTo(2_800_000_000, 700, 14, 100);
            var blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
            EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 0, [EcgLead.I, EcgLead.II, EcgLead.AVL, EcgLead.V1]);
            EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 12, [EcgLead.I, EcgLead.II, EcgLead.AVL, EcgLead.V1]);
            var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
            var tail = source.AdvanceTo(3_000_000_000, 50, 1, 100);
            var recovered = restored.AdvanceTo(3_000_000_000, 50, 1, 100);
            if (tail.Count != recovered.Count || tail.Zip(recovered).Any(x => !x.First.SequenceEqual(x.Second)))
            { throw new InvalidOperationException("Atrial illustration lost checkpoint bytes."); }
            Click(window.HoldButton); Click(window.RunButton);
            var trace = window.Trace;
            var timer = window.ActiveTimer;
            long time = window.SimulationTimeNs;
            foreach (int bad in new[] { -1, 1 })
            {
                window.AtrialInput.SelectedIndex = bad;
                window.PEarlyInput.Text = "100";
                window.PLateInput.Text = "-100";
                Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config || window.Trace != trace || window.ActiveTimer != timer || window.SimulationTimeNs != time)
                { throw new InvalidOperationException("Rejected atrial input changed active source or view."); }
            }
            Click(window.ResetButton);
            if (window.AtrialInput.SelectedIndex != 1 || window.PEarlyInput.Text != "" || window.BlockCount != 0 || window.ActiveTimer is not null)
            { throw new InvalidOperationException("Reset lost atrial selection."); }
            window.AtrialInput.SelectedIndex = 0;
            Click(window.ApplyEcgButton);
            if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default)
            { throw new InvalidOperationException("Clearing atrial illustration did not restore reference configuration."); }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: native left atrial P illustration, projected pixels, recovery and atomic lifecycle");
    }
}
