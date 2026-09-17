// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class JunctionalEscapeSmokeChecks
{
    internal static void Verify()
    {
        foreach (bool projected in new[] { false, true })
        {
            WaveformDemoWindow window = new(physiology: !projected, projected: projected);
            window.Show();
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            try
            {
                Click(window.StepButton); Click(window.RunButton);
                var oldTimer = window.ActiveTimer;
                Click(window.JunctionalEscapeButton); window.Pulse(oldTimer);
                if (window.ConductionInput.SelectedIndex != 7 || window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Junctional preset failed to reset and fence timer."); }
                Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                if (!string.IsNullOrEmpty(projected ? window.EcgConfigurationStatus.Text : window.BreathConfigurationStatus.Text))
                { throw new InvalidOperationException("Junctional preset could not reapply displayed parameters."); }
                for (int step = 0; step < 30; step++) { Click(window.StepButton); }
                WaveformEnvelope[] blocks;
                if (projected)
                {
                    var source = ProjectedEcgDemoSource.Create(window.EcgConfiguration);
                    List<byte[]> expected = [];
                    for (int part = 1; part <= 3; part++)
                    { expected.AddRange(source.AdvanceTo(part * 2_000_000_000L, 500, 10, 100)); }
                    source = ProjectedEcgDemoSource.Create(window.EcgConfiguration);
                    List<byte[]> recovered = [];
                    for (int step = 1; step <= 30; step++)
                    {
                        recovered.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                        source = ElectrodeWaveformGroup.Restore(source.CaptureState());
                    }
                    if (expected.Count != recovered.Count || expected.Zip(recovered).Any(p => !p.First.SequenceEqual(p.Second)))
                    { throw new InvalidOperationException("Junctional checkpoint changed twelve-lead blocks."); }
                    blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 100);
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 400);
                }
                else
                {
                    blocks = MechanicalUncouplingSmokeChecks.Decode(window.BreathConfiguration);
                    MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 100);
                    VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
                }
                var id = projected ? ProjectedEcgDemoSource.ChannelId(EcgLead.II) : PhysiologyDemoSource.ChannelId(0);
                var ecg = blocks.SelectMany(b => b.Planes.Single(p => p.ChannelId == id).Samples).ToArray();
                if (!ecg.Skip(100).Take(20).Any(v => v > 500) || !ecg.Skip(400).Take(20).Any(v => v > 500) ||
                    !ecg.Skip(200).Take(25).Any(v => v > 0) || ecg.Skip(240).Take(20).Any(v => v > 500))
                { throw new InvalidOperationException("Native escape QRS is not independent of regular P."); }
                Click(window.HoldButton); Click(window.RunButton);
                var held = window.Trace; var timer = window.ActiveTimer;
                var acceptedEcg = window.EcgConfiguration; var acceptedBreath = window.BreathConfiguration;
                long before = window.SimulationTimeNs;
                foreach (string invalid in new[] { "999", "1501", "" })
                {
                    window.IndependentVentricularPeriodInput.Text = invalid;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    Unchanged();
                }
                window.IndependentVentricularPeriodInput.Text = "1200";
                window.CardiacActivityInput.SelectedIndex = (int)CardiacActivity.VentricularOnly;
                Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton); Unchanged();
                Click(window.ResetButton);
                if (window.ConductionInput.SelectedIndex != 7 || window.IndependentVentricularPeriodInput.Text != "1200" || window.ActiveTimer is not null)
                { throw new InvalidOperationException("Reset lost escape preset."); }
                if (projected)
                {
                    window.VentricularInput.SelectedIndex = (int)EcgVentricularIllustration.RightHypertrophyWithStrain;
                    Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != acceptedEcg) { throw new InvalidOperationException("Escape preset accepted conflicting QRS morphology."); }
                    Click(window.ResetButton);
                }
                foreach (string valid in new[] { "1000", "1500" })
                {
                    window.IndependentVentricularPeriodInput.Text = valid;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    if ((projected ? window.EcgConfiguration.IndependentVentricularPeriodMilliseconds : window.BreathConfiguration.IndependentVentricularPeriodMilliseconds)?.ToString(System.Globalization.CultureInfo.InvariantCulture) != valid)
                    { throw new InvalidOperationException("Escape rate boundary rejected."); }
                }
                void Unchanged()
                {
                    if (window.EcgConfiguration != acceptedEcg || window.BreathConfiguration != acceptedBreath ||
                        !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before)
                    { throw new InvalidOperationException("Invalid escape input changed accepted state."); }
                }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: junctional complete-AVB preset preserves independent P/QRS, pressure pixels, recovery and atomic controls");
    }
}
