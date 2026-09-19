// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class AtrialFibrillationAberrancySmokeChecks
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
                foreach (bool fine in new[] { false, true })
                {
                    Click(window.FibrillationButton);
                    window.ConductionInput.SelectedIndex = fine ? 12 : 11;
                    window.AfAberrancyInput.IsChecked = true;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    if (!(projected ? window.EcgConfiguration.IllustrateAfAberrancy : window.BreathConfiguration.IllustrateAfAberrancy) ||
                        !string.IsNullOrEmpty(projected ? window.EcgConfigurationStatus.Text : window.BreathConfigurationStatus.Text))
                    { throw new InvalidOperationException("AF aberrancy failed to apply."); }
                    for (int i = 0; i < 85; i++) { Click(window.StepButton); }
                    var actual = Decode(true); var original = Decode(false);
                    Guid lead = projected ? ProjectedEcgDemoSource.ChannelId(EcgLead.V1) : PhysiologyDemoSource.ChannelId(0);
                    short[] values = actual.SelectMany(b => b.Planes.Single(p => p.ChannelId == lead).Samples).ToArray();
                    short[] reference = original.SelectMany(b => b.Planes.Single(p => p.ChannelId == lead).Samples).ToArray();
                    if (!values.Take(3242).SequenceEqual(reference.Take(3242)) ||
                        values.Skip(3242).Take(100).SequenceEqual(reference.Skip(3242).Take(100)) ||
                        !values.Skip(3342).SequenceEqual(reference.Skip(3342)))
                    { throw new InvalidOperationException("AF aberrancy changed ordinary beats or failed the selected wide beat."); }
                    if (projected)
                    {
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, actual, 3262, [EcgLead.V1, EcgLead.V2, EcgLead.V6], width: 2285);
                        if (!window.QrsMeasurementStatus.Text!.Contains("单一形态摘要", StringComparison.Ordinal))
                        { throw new InvalidOperationException("Mixed AF still reports one QRS shape."); }
                    }
                    else
                    {
                        foreach (int row in new[] { 1, 2, 3, 4, 5, 6 })
                        {
                            if (!MechanicalUncouplingSmokeChecks.Samples(actual, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(original, row)))
                            { throw new InvalidOperationException("AF aberrancy altered perfusion or respiration."); }
                        }
                    }
                    Click(window.HoldButton); Click(window.RunButton);
                    var ecg = window.EcgConfiguration; var physiology = window.BreathConfiguration;
                    var trace = window.Trace; var timer = window.ActiveTimer; long time = window.SimulationTimeNs;
                    window.ConductionInput.SelectedIndex = 0;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    if (window.EcgConfiguration != ecg || window.BreathConfiguration != physiology ||
                        !ReferenceEquals(window.Trace, trace) || !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != time)
                    { throw new InvalidOperationException("Invalid AF aberrancy mode changed accepted state."); }
                    Click(window.ResetButton);
                    if (window.AfAberrancyInput.IsChecked != true || window.BlockCount != 0)
                    { throw new InvalidOperationException("Reset lost AF aberrancy."); }
                    window.AfAberrancyInput.IsChecked = false;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                    if (projected ? window.EcgConfiguration.IllustrateAfAberrancy : window.BreathConfiguration.IllustrateAfAberrancy)
                    { throw new InvalidOperationException("Disabling AF aberrancy failed."); }

                    WaveformEnvelope[] Decode(bool enabled)
                    {
                        List<byte[]> bytes = [];
                        if (projected)
                        {
                            var source = ProjectedEcgDemoSource.Create(window.EcgConfiguration with { IllustrateAfAberrancy = enabled });
                            for (int step = 1; step <= 85; step++)
                            {
                                bytes.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                                if (step == 66) { source = ElectrodeWaveformGroup.Restore(source.CaptureState()); }
                            }
                        }
                        else
                        {
                            var source = PhysiologyDemoSource.Create(window.BreathConfiguration with { IllustrateAfAberrancy = enabled });
                            for (int step = 1; step <= 85; step++)
                            {
                                bytes.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                                if (step == 66) { source = PhysiologyWaveformGroup.Restore(source.CaptureState()); }
                            }
                        }
                        return bytes.Select(b => WaveformEnvelopeCodec.Decode(b)).ToArray();
                    }
                }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: AF Ashman example preserves other beats/f/perfusion, wide-QRS pixels, recovery and atomic lifecycle in both demos");
    }
}
