// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Simulation.Acquisition;

namespace Monitor.Desktop;

internal static class VentricularPhaseSmokeChecks
{
    internal static void Verify()
    {
        foreach (bool projected in new[] { false, true })
        {
            WaveformDemoWindow window = new(physiology: !projected, projected: projected);
            window.Show();
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var apply = projected ? window.ApplyEcgButton : window.ApplyBreathButton;
            try
            {
                foreach (string text in new[] { "0", "900", "1019", "" })
                {
                    Click(window.StepButton); Click(window.HoldButton); Click(window.RunButton);
                    var oldTimer = window.ActiveTimer;
                    window.IndependentVentricularPeriodInput.Text = "1100";
                    window.IndependentVentricularOffsetInput.Text = text;
                    Click(apply);
                    window.Pulse(oldTimer);
                    var ecgConfig = window.EcgConfiguration;
                    var physiologyConfig = window.BreathConfiguration;
                    int? offset = projected ? ecgConfig.IndependentVentricularOffsetMilliseconds : physiologyConfig.IndependentVentricularOffsetMilliseconds;
                    if (offset?.ToString(CultureInfo.InvariantCulture) != (text == "" ? null : text) || window.SimulationTimeNs != 0 || window.BlockCount != 0 || window.IsHeld || window.ActiveTimer is not null)
                    { throw new InvalidOperationException("Initial ventricular phase did not atomically replace the source."); }
                    for (int step = 0; step < 30; step++) { Click(window.StepButton); }
                    if (projected)
                    {
                        var source = ProjectedEcgDemoSource.Create(ecgConfig);
                        var plan = source.CaptureState().Generator.Timeline.Plan;
                        if (plan.VentricularElectricalOffsetNs != (offset ?? 160) * 1_000_000L || plan.VentricularMechanicalOffsetNs - plan.VentricularElectricalOffsetNs != 80_000_000 ||
                            ecgConfig.ResolveTiming().QtIntervalNs != 360_000_000)
                        { throw new InvalidOperationException("Phase changed QT or lost electrical/mechanical alignment."); }
                        var blocks = source.AdvanceTo(2_800_000_000, 700, 14, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, (offset ?? 160) / 4);
                    }
                    else
                    {
                        var blocks = MechanicalUncouplingSmokeChecks.Decode(physiologyConfig);
                        MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks);
                        VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [150, 200]);
                        var normal = MechanicalUncouplingSmokeChecks.Decode(physiologyConfig with { IndependentVentricularOffsetMilliseconds = null });
                        foreach (int row in new[] { 1, 4 })
                        {
                            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
                            { throw new InvalidOperationException("Ventricular phase shifted independent respiration or gas."); }
                        }
                    }
                    Click(window.HoldButton); Click(window.RunButton);
                    var held = window.Trace;
                    var timer = window.ActiveTimer;
                    long before = window.SimulationTimeNs;
                    foreach (string invalid in new[] { "-1", "1020", "2147483647", "1.1", "bad" })
                    {
                        window.IndependentVentricularOffsetInput.Text = invalid;
                        Click(apply);
                        Unchanged();
                    }
                    window.IndependentVentricularOffsetInput.Text = "0";
                    window.IndependentVentricularPeriodInput.Text = "";
                    Click(apply);
                    Unchanged();
                    Click(window.ResetButton);
                    if (window.IndependentVentricularOffsetInput.Text != text || window.IndependentVentricularPeriodInput.Text != "1100" || window.BlockCount != 0 || window.ActiveTimer is not null)
                    { throw new InvalidOperationException("Reset lost the accepted ventricular phase."); }

                    void Unchanged()
                    {
                        if (window.EcgConfiguration != ecgConfig || window.BreathConfiguration != physiologyConfig ||
                            !ReferenceEquals(held, window.Trace) || !ReferenceEquals(timer, window.ActiveTimer) || window.SimulationTimeNs != before)
                        { throw new InvalidOperationException("Invalid initial ventricular phase changed source/view/timer."); }
                    }
                }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: ventricular phase zero/late/subsample/clear preserves QT, mechanical alignment, native pixels and atomic lifecycle in both demos");
    }
}
