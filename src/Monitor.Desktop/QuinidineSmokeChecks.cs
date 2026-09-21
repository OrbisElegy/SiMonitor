// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class QuinidineSmokeChecks
{
    internal static void Verify()
    {
        var window = new WaveformDemoWindow(projected: true);
        window.Show();
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            foreach (var mode in Enum.GetValues<QuinidineIllustration>().Where(m => m != QuinidineIllustration.Reference))
                foreach (bool notchedP in new[] { false, true })
                {
                    Click(window.HypokalemiaButton); Click(window.StepButton); Click(window.RunButton);
                    var oldTimer = window.ActiveTimer;
                    window.QuinidineNotchedPInput.IsChecked = notchedP;
                    window.QuinidineInput.SelectedIndex = (int)mode; Click(window.QuinidineButton); window.Pulse(oldTimer);
                    var config = ProjectedEcgDemoConfiguration.QuinidinePreset(mode, notchedP);
                    if (window.EcgConfiguration != config || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.BlockCount != 0)
                    { throw new InvalidOperationException("Quinidine loader retained source/timer state."); }
                    Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || !string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                    { throw new InvalidOperationException("Quinidine preset reapply failed."); }
                    var timing = QuinidineEffectReference.ResolveTiming(mode);
                    if (config.ResolveTiming() != timing || window.QrsDurationInput.Text != (timing.QrsDurationNs / 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                        !window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains($"QT-u{QuinidineEffectReference.ResolveQuIntervalNs(mode) / 1_000_000}ms") == true))
                    { throw new InvalidOperationException("Quinidine displayed timing disagrees with source."); }
                    var source = ProjectedEcgDemoSource.Create(config);
                    var blocks = new List<WaveformEnvelope>();
                    for (int step = 1; step <= 30; step++)
                    {
                        Click(window.StepButton);
                        blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
                    }
                    foreach (int sample in mode is QuinidineIllustration.WideQrsLowT or QuinidineIllustration.WideQrsInvertedT ? new[] { 5, 60, 165, 205 } : new[] { 5, 55, 145, 180 })
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks.ToArray(), sample, [EcgLead.II, EcgLead.AVR, EcgLead.V2, EcgLead.V4]);
                    var restored = ElectrodeWaveformGroup.Restore(source.CaptureState());
                    var a = source.AdvanceTo(6_200_000_000, 50, 1, 100);
                    var b = restored.AdvanceTo(6_200_000_000, 50, 1, 100);
                    if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second)))
                    { throw new InvalidOperationException("Quinidine wire recovery diverged."); }
                    Click(window.HoldButton); Click(window.RunButton);
                    var trace = window.Trace; var timer = window.ActiveTimer; long time = window.SimulationTimeNs;
                    window.TContourInput.SelectedIndex = 5; Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != config || window.Trace != trace || window.ActiveTimer != timer || window.SimulationTimeNs != time || string.IsNullOrEmpty(window.EcgConfigurationStatus.Text))
                    { throw new InvalidOperationException("Conflicting quinidine edit mutated accepted state."); }
                    Click(window.ResetButton);
                    if (window.QuinidineInput.SelectedIndex != (int)mode || window.TContourInput.SelectedIndex != 0 || window.QuinidineNotchedPInput.IsChecked != notchedP)
                    { throw new InvalidOperationException("Quinidine reset lost accepted mode."); }
                    foreach (var invalid in new[] { config with { HypokalemiaRepolarization = true }, config with { QrsDurationMilliseconds = 120 }, config with { Placement = (EcgLimbPlacement)99 }, config with { CardiacActivity = CardiacActivity.VentricularOnly } })
                    {
                        try { ProjectedEcgDemoSource.Create(invalid); }
                        catch (EventWaveformException e) when (e.ReasonCode == "Quinidine.ConflictingModes") { continue; }
                        throw new InvalidOperationException("Conflicting quinidine mode accepted.");
                    }
                    window.QuinidineInput.SelectedIndex = (int)mode % 4 + 1; Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.QuinidinePreset((QuinidineIllustration)window.QuinidineInput.SelectedIndex, notchedP))
                    { throw new InvalidOperationException("Quinidine morphology switch failed."); }
                    window.QuinidineNotchedPInput.IsChecked = !notchedP; Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration.QuinidineNotchedP != !notchedP)
                    { throw new InvalidOperationException("Quinidine P-notch toggle failed."); }
                    window.QuinidineInput.SelectedIndex = 0; Click(window.ApplyEcgButton);
                    if (window.EcgConfiguration != ProjectedEcgDemoConfiguration.Default || window.QuinidineNotchedPInput.IsChecked == true)
                    { throw new InvalidOperationException("Quinidine clear failed."); }
                }
        }
        finally { window.Close(); }
        try { ProjectedEcgDemoSource.Create(ProjectedEcgDemoConfiguration.Default with { QuinidineNotchedP = true }); }
        catch (EventWaveformException e) when (e.ReasonCode == "Quinidine.ConflictingModes")
        { Console.WriteLine("ok: orphan quinidine P notch rejected"); return; }
        throw new InvalidOperationException("Orphan quinidine P notch accepted.");

    }
}
