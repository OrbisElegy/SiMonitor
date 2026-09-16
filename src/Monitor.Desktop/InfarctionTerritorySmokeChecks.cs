// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class InfarctionTerritorySmokeChecks
{
    internal static void Verify()
    {
        WaveformDemoWindow window = new(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            window.InfarctionStageInput.SelectedIndex = (int)InfarctionIllustrationStage.AcuteQInvertedT;
            foreach (var territory in Enum.GetValues<InfarctionTerritory>().Skip(1).Append(InfarctionTerritory.CustomChest))
            {
                Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                window.InfarctionTerritoryInput.SelectedIndex = (int)territory;
                Click(window.ApplyEcgButton);
                window.Pulse(oldTimer);
                var config = window.EcgConfiguration;
                if (config.Infarction?.Territory != territory || window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Territory did not restart atomically."); }
                for (int step = 0; step < 14; step++) { Click(window.StepButton); }
                var expected = ProjectedEcgDemoSource.Create(config).AdvanceTo(2_800_000_000, 700, 14, 100);
                var blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 40, [EcgLead.I, EcgLead.II, EcgLead.III, EcgLead.AVL, EcgLead.AVF, EcgLead.V5]);
                EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 60, [EcgLead.I, EcgLead.II, EcgLead.AVL, EcgLead.AVF, EcgLead.V5]);
                var source = ProjectedEcgDemoSource.Create(config);
                List<byte[]> recovered = [];
                for (int step = 1; step <= 14; step++)
                {
                    recovered.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                    source = ElectrodeWaveformGroup.Restore(source.CaptureState());
                }
                if (expected.Count != recovered.Count || expected.Zip(recovered).Any(pair => !pair.First.SequenceEqual(pair.Second)))
                { throw new InvalidOperationException("Territory restore changed waveform bytes."); }
                if (territory == InfarctionTerritory.CustomChest)
                {
                    var baseline = ProjectedEcgDemoSource.Create(config with { Infarction = null }).AdvanceTo(2_800_000_000, 700, 14, 100);
                    if (expected.Zip(baseline).Any(pair => !pair.First.SequenceEqual(pair.Second)))
                    { throw new InvalidOperationException("Cleared territory did not restore reference."); }
                }
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace; var timer = window.ActiveTimer;
                long before = window.SimulationTimeNs;
                window.InfarctionTerritoryInput.SelectedIndex = -1;
                Click(window.ApplyEcgButton);
                if (window.EcgConfiguration != config || !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) || before != window.SimulationTimeNs)
                { throw new InvalidOperationException("Invalid territory changed accepted state."); }
                Click(window.ResetButton);
                if (window.InfarctionTerritoryInput.SelectedIndex != (int)territory || window.BlockCount != 0)
                { throw new InvalidOperationException("Reset lost accepted territory."); }
            }
        }
        finally { window.Close(); }
        Console.WriteLine("ok: named infarction territories include limb lead pixels, checkpoint bytes and atomic lifecycle");
    }
}
