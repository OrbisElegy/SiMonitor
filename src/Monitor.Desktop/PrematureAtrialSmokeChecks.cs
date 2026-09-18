// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class PrematureAtrialSmokeChecks
{
    internal static void Verify()
    {
        foreach (var (selection, projected) in new[] { (22, false), (22, true), (23, false), (23, true), (24, false), (24, true), (25, false), (25, true), (26, false), (26, true), (27, false), (27, true) })
        {
            bool blocked = selection == 23, aberrant = selection == 24, junctional = selection >= 25;
            int pStart = selection switch { 23 => 500, 25 => 545, 26 => 595, 27 => 565, _ => 525 };
            var expectedEcg = junctional ? ProjectedEcgDemoConfiguration.PrematureJunctional with { ConductionPattern = ConductionSelection.Pattern(selection) } : aberrant ? ProjectedEcgDemoConfiguration.AberrantPrematureAtrial : blocked ? ProjectedEcgDemoConfiguration.BlockedPrematureAtrial : ProjectedEcgDemoConfiguration.PrematureAtrial;
            var expectedPhysiology = junctional ? PhysiologyDemoConfiguration.PrematureJunctional with { ConductionPattern = ConductionSelection.Pattern(selection) } : aberrant ? PhysiologyDemoConfiguration.AberrantPrematureAtrial : blocked ? PhysiologyDemoConfiguration.BlockedPrematureAtrial : PhysiologyDemoConfiguration.PrematureAtrial;
            WaveformDemoWindow window = new(physiology: !projected, projected: projected);
            window.Show();
            void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            try
            {
                foreach (int prior in Enumerable.Range(0, 4))
                {
                    if (prior == 0) { Click(window.VentricularDisorganizationButton); }
                    if (prior == 1) { Click(window.VentricularEscapeButton); }
                    if (prior == 2) { window.BundleBlockInput.SelectedIndex = 1; Click(window.BundleBlockButton); }
                    if (prior == 3) { window.SecondDegreePresetInput.SelectedIndex = 0; Click(window.SecondDegreePresetButton); }
                    Click(window.StepButton); Click(window.RunButton);
                    var stale = window.ActiveTimer;
                    window.ConductionInput.SelectedIndex = selection;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton); window.Pulse(stale);
                    if (window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null || window.BundleBlockInput.SelectedIndex != 0 ||
                        (projected ? window.EcgConfiguration != expectedEcg : window.BreathConfiguration != expectedPhysiology))
                    { throw new InvalidOperationException("PAC transition retained old rhythm, morphology or timer."); }
                }
                Click(junctional ? window.PrematureJunctionalButton : aberrant ? window.AberrantPrematureAtrialButton : blocked ? window.BlockedPrematureAtrialButton : window.PrematureAtrialButton);
                window.ConductionInput.SelectedIndex = selection;
                Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                if (!string.IsNullOrEmpty(projected ? window.EcgConfigurationStatus.Text : window.BreathConfigurationStatus.Text))
                { throw new InvalidOperationException("Loaded PAC cannot be reapplied."); }
                for (int step = 0; step < 30; step++) { Click(window.StepButton); }
                List<byte[]> expected = [], recovered = [];
                if (projected)
                {
                    var source = ProjectedEcgDemoSource.Create(window.EcgConfiguration);
                    var trial = ProjectedEcgDemoSource.Create(window.EcgConfiguration);
                    for (int step = 1; step <= 30; step++)
                    {
                        expected.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                        recovered.AddRange(trial.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                        trial = ElectrodeWaveformGroup.Restore(trial.CaptureState());
                    }
                }
                else
                {
                    var source = PhysiologyDemoSource.Create(window.BreathConfiguration);
                    var trial = PhysiologyDemoSource.Create(window.BreathConfiguration);
                    for (int step = 1; step <= 30; step++)
                    {
                        expected.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                        recovered.AddRange(trial.AdvanceTo(step * 200_000_000L, 50, 1, 100));
                        trial = PhysiologyWaveformGroup.Restore(trial.CaptureState());
                    }
                }
                if (expected.Count != recovered.Count || expected.Zip(recovered).Any(p => !p.First.SequenceEqual(p.Second)))
                { throw new InvalidOperationException("PAC checkpoint changed wire images across P-prime and sinus reset."); }
                var blocks = expected.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
                var id = projected ? ProjectedEcgDemoSource.ChannelId(EcgLead.II) : PhysiologyDemoSource.ChannelId(0);
                var samples = blocks.SelectMany(b => b.Planes.Single(p => p.ChannelId == id).Samples).ToArray();
                if ((blocked ? Enumerable.Range(500, 20).Min(i => samples[i] - samples[i - 400]) > -150 || samples.Skip(520).Take(255).Any(v => v != 0)
                    : Enumerable.Range(pStart, 20).Min(i => selection >= 26 ? samples[i] - samples[i - 525] : samples[i]) > -150 || samples.Skip(565).Take(20).Max() < 500) || samples.Skip(junctional ? 840 : 815).Take(20).Max() < 500)
                { throw new InvalidOperationException("PAC early P-prime or conducted/resumed QRS missing."); }
                if (projected)
                {
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, pStart, [EcgLead.II, EcgLead.AVR, EcgLead.V1]);
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, blocked ? 440 : 565);
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, junctional ? 840 : 815);
                    if (aberrant)
                    {
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 587, [EcgLead.V1, EcgLead.V2, EcgLead.V6]);
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 648, [EcgLead.V1, EcgLead.V2, EcgLead.V6]);
                    }
                }
                else
                {
                    MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, blocked ? 440 : 565);
                    VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [303, 320, 355, 395, 420]);
                    if (aberrant)
                    {
                        var narrow = MechanicalUncouplingSmokeChecks.Decode(PhysiologyDemoConfiguration.PrematureAtrial);
                        foreach (int row in Enumerable.Range(1, 6))
                        {
                            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(narrow, row)))
                            { throw new InvalidOperationException("Aberrant PAC changed non-ECG physiology."); }
                        }
                    }

                }
                VerifyPPrime(window, samples, projected, blocked, pStart, selection >= 26);
                Click(window.HoldButton); Click(window.RunButton);
                var ecg = window.EcgConfiguration; var physiology = window.BreathConfiguration;
                var trace = window.Trace; var timer = window.ActiveTimer; long before = window.SimulationTimeNs;
                window.IndependentVentricularPeriodInput.Text = "1200";
                Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                if (window.EcgConfiguration != ecg || window.BreathConfiguration != physiology || window.Trace != trace || window.ActiveTimer != timer || window.SimulationTimeNs != before ||
                    string.IsNullOrEmpty(projected ? window.EcgConfigurationStatus.Text : window.BreathConfigurationStatus.Text))
                { throw new InvalidOperationException("PAC invalid edit changed accepted state."); }
                Click(window.ResetButton);
                if (window.ConductionInput.SelectedIndex != selection || window.ActiveTimer is not null || window.BlockCount != 0)
                { throw new InvalidOperationException("PAC reset lost the selected source."); }
                window.ConductionInput.SelectedIndex = 6; Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                if (projected ? window.EcgConfiguration != SecondDegreeBlockPreset.Ecg(0) : window.BreathConfiguration != SecondDegreeBlockPreset.Physiology(0))
                { throw new InvalidOperationException("Leaving PAC retained incompatible fields."); }
                foreach (int next in new[] { 22, 25, 26, 27, 24, 23, 26, 25, 22 })
                {
                    Click(window.StepButton); Click(window.RunButton);
                    var stale = window.ActiveTimer;
                    window.ConductionInput.SelectedIndex = next;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton); window.Pulse(stale);
                    if (window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null ||
                        (projected ? window.EcgConfiguration.ConductionPattern : window.BreathConfiguration.ConductionPattern) != ConductionSelection.Pattern(next))
                    { throw new InvalidOperationException("Direct conducted/blocked/aberrant PAC and three PJC variants transition retained old source or timer."); }
                }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: conducted/blocked/aberrant PAC and three PJC variants P-prime and P/T overlap, selected QRS/mechanics, pressure, wire recovery and atomic transitions");
    }

    private static void VerifyPPrime(WaveformDemoWindow window, short[] samples, bool projected, bool blocked, int pStart, bool retrogradeOverlay)
    {
        int height = projected ? 12 * ProjectedEcgPlotLayout.RowHeight : 840;
        window.Trace.Measure(new Size(1044, height)); window.Trace.Arrange(new Rect(0, 0, 1044, height));
        using RenderTargetBitmap image = new(new PixelSize(1044, height), new Vector(96, 96)); image.Render(window.Trace);
        using WriteableBitmap pixels = new(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = pixels.Lock(); image.CopyPixels(buffer);
        int peak = Enumerable.Range(pStart, 20).MinBy(i => blocked ? samples[i] - samples[i - 400] : retrogradeOverlay ? samples[i] - samples[i - 525] : samples[i]);
        int x = (int)Math.Round((projected ? 85 : 0) + peak / 2.0);
        int y = (int)Math.Round((projected ? ProjectedEcgPlotLayout.RowHeight * 1.5 : 60) - samples[peak] * (projected ? 0.04 : 0.05));
        if (!Enumerable.Range(y - 1, 3).Any(row => Enumerable.Range(x - 1, 3).Any(column =>
            Marshal.ReadByte(buffer.Address + row * buffer.RowBytes + column * 4 + 1) > 100)))
        { throw new InvalidOperationException("Native trace lacks P-prime or its deformation of T."); }
    }
}
