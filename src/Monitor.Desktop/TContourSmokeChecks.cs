// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class TContourSmokeChecks
{
    internal static void Verify()
    {
        foreach (int shape in new[] { 1, 2, 3 })
        {
            WaveformDemoWindow window = new(projected: true);
            window.Show();
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            try
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.TContourInput.SelectedIndex = shape;
                window.TContourLeadInput.SelectedIndex = shape == 1 ? 0 : shape == 2 ? 4 : 6;
                Click(window.ApplyEcgButton); window.Pulse(oldTimer);
                var config = window.EcgConfiguration;
                if (config.TContour?.Shape != (EcgTContourShape)shape || window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("T contour did not atomically restart."); }
                for (int i = 0; i < 14; i++) { Click(window.StepButton); }
                var source = ProjectedEcgDemoSource.Create(config);
                var blocks = source.AdvanceTo(2_800_000_000, 700, 14, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 90, [EcgLead.V1, EcgLead.V5, EcgLead.V6]);
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 110, [EcgLead.V1, EcgLead.V5, EcgLead.V6]);
                var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
                if (source.AdvanceTo(3_000_000_000, 50, 1, 100).Zip(restored.AdvanceTo(3_000_000_000, 50, 1, 100)).Any(p => !p.First.SequenceEqual(p.Second)))
                { throw new InvalidOperationException("Contour recovery changed bytes."); }
                Click(window.HoldButton); Click(window.RunButton);
                var trace = window.Trace; var timer = window.ActiveTimer; long time = window.SimulationTimeNs;
                foreach (string invalid in new[] { "0", "2001", "bad", "1.5" })
                {
                    window.TContourPeakInput.Text = invalid; Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || window.Trace != trace || window.ActiveTimer != timer || window.SimulationTimeNs != time)
                    { throw new InvalidOperationException("Invalid contour altered current state."); }
                }
                window.TContourPeakInput.Text = "300"; window.ChestJInput.Text = "100"; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config || window.Trace != trace || window.ActiveTimer != timer)
                { throw new InvalidOperationException("Conflicting ST changed active contour."); }
                Click(window.ResetButton);
                if (window.TContourInput.SelectedIndex != shape || window.TContourPeakInput.Text != "300" || window.ChestJInput.Text != "0")
                { throw new InvalidOperationException("Reset lost contour."); }
                window.TContourInput.SelectedIndex = 0; Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default)
                { throw new InvalidOperationException("Clearing contour did not recover reference."); }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: native biphasic/notched T contours, signed pixels, recovery and atomic rejection");
    }
}
