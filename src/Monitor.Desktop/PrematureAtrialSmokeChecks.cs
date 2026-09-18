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
        foreach (var (selection, projected) in new[] { (22, false), (22, true), (23, false), (23, true), (24, false), (24, true), (25, false), (25, true), (26, false), (26, true), (27, false), (27, true), (28, false), (28, true), (29, false), (29, true), (30, false), (30, true), (31, false), (31, true), (32, false), (32, true), (33, false), (33, true), (34, false), (34, true), (35, false), (35, true), (36, false), (36, true), (37, false), (37, true) })
        {
            bool blocked = selection == 23, aberrant = selection == 24, junctional = selection is >= 25 and <= 27, ventricular = selection >= 28;
            int steps = selection >= 31 ? 40 : 30; // Include second PVC after the shared Pleth delay.
            int pvcQrs = selection switch { 29 => 165, 30 => 365, 33 => 665, 37 => 490, _ => 565 };
            int resumedQrs = selection switch { 29 => 440, 30 => 640, 33 => 790, 34 or 35 => 1040, _ => junctional || ventricular ? 840 : 815 };
            int pStart = selection switch { 23 => 500, 25 => 545, 26 => 595, 27 => 565, _ => 525 };
            var expectedEcg = ventricular ? ProjectedEcgDemoConfiguration.Pvc(ConductionSelection.Pattern(selection)) : junctional ? ProjectedEcgDemoConfiguration.PrematureJunctional with { ConductionPattern = ConductionSelection.Pattern(selection) } : aberrant ? ProjectedEcgDemoConfiguration.AberrantPrematureAtrial : blocked ? ProjectedEcgDemoConfiguration.BlockedPrematureAtrial : ProjectedEcgDemoConfiguration.PrematureAtrial;
            var expectedPhysiology = ventricular ? PhysiologyDemoConfiguration.PrematureVentricular with { ConductionPattern = ConductionSelection.Pattern(selection) } : junctional ? PhysiologyDemoConfiguration.PrematureJunctional with { ConductionPattern = ConductionSelection.Pattern(selection) } : aberrant ? PhysiologyDemoConfiguration.AberrantPrematureAtrial : blocked ? PhysiologyDemoConfiguration.BlockedPrematureAtrial : PhysiologyDemoConfiguration.PrematureAtrial;
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
                Click(ventricular ? window.PrematureVentricularButton : junctional ? window.PrematureJunctionalButton : aberrant ? window.AberrantPrematureAtrialButton : blocked ? window.BlockedPrematureAtrialButton : window.PrematureAtrialButton);
                window.ConductionInput.SelectedIndex = selection;
                Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton);
                if (!string.IsNullOrEmpty(projected ? window.EcgConfigurationStatus.Text : window.BreathConfigurationStatus.Text))
                { throw new InvalidOperationException("Loaded PAC cannot be reapplied."); }
                for (int step = 0; step < steps; step++) { Click(window.StepButton); }
                List<byte[]> expected = [], recovered = [];
                if (projected)
                {
                    var source = ProjectedEcgDemoSource.Create(window.EcgConfiguration);
                    var trial = ProjectedEcgDemoSource.Create(window.EcgConfiguration);
                    for (int step = 1; step <= steps; step++)
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
                    for (int step = 1; step <= steps; step++)
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
                if ((ventricular ? (selection is not (36 or 37) && samples.Skip(pvcQrs - 40).Take(40).Any(v => v != 0)) || samples.Skip(pvcQrs).Take(40).Max(v => Math.Abs((int)v)) < 600 : blocked ? Enumerable.Range(500, 20).Min(i => samples[i] - samples[i - 400]) > -150 || samples.Skip(520).Take(255).Any(v => v != 0)
                    : Enumerable.Range(pStart, 20).Min(i => selection >= 26 ? samples[i] - samples[i - 525] : samples[i]) > -150 || samples.Skip(565).Take(20).Max() < 500) || samples.Skip(resumedQrs).Take(20).Max() < 500)
                { throw new InvalidOperationException("PAC early P-prime or conducted/resumed QRS missing."); }
                if (selection is 36 or 37)
                {
                    if (samples.Skip(selection == 37 ? 485 : 535).Take(selection == 37 ? 5 : 30).Max(v => Math.Abs((int)v)) < (selection == 37 ? 1 : 100))
                    { throw new InvalidOperationException("R-on-T lost prolonged preceding T before premature QRS."); }
                    if (projected) { EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, selection == 37 ? 515 : 581, [EcgLead.II, EcgLead.V1, EcgLead.V6]); }
                    else { MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, selection == 37 ? 515 : 581); }
                }
                if (selection == 34)
                {
                    if (!samples.Skip(565).Take(120).SequenceEqual(samples.Skip(690).Take(120)))
                    { throw new InvalidOperationException("Couplet lost the second full QRS/T or inserted an intervening sinus wave."); }
                    if (projected)
                    {
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 712, [EcgLead.II, EcgLead.V1, EcgLead.V6]);
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, 773, [EcgLead.II, EcgLead.V1, EcgLead.V6]);
                    }
                    else { MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, 712); }
                }
                if (selection is 31 or 32 or 35)
                {
                    int second = selection == 35 ? 690 : selection == 31 ? 1365 : 1390;
                    if (Math.Abs(samples[second + 18] + samples[581]) > 1 || Math.Abs(samples[581]) < 200)
                    { throw new InvalidOperationException("Alternating PVC second QRS has wrong polarity, duration or coupling."); }
                    if (projected)
                    {
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, second + 18, [EcgLead.II, EcgLead.V1, EcgLead.V6]);
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, second + 100, [EcgLead.II, EcgLead.V1, EcgLead.V6]);
                    }
                    else { MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, second + 18); }
                }
                if (projected)
                {
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, pStart, [EcgLead.II, EcgLead.AVR, EcgLead.V1]);
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, blocked ? 440 : ventricular ? pvcQrs : 565);
                    EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, resumedQrs);
                    if (aberrant || ventricular)
                    {
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, ventricular ? pvcQrs + 22 : 587, [EcgLead.V1, EcgLead.V2, EcgLead.V6]);
                        EcgLimbPlacementSmokeChecks.VerifyPixels(window, blocks, ventricular ? pvcQrs + 83 : 648, [EcgLead.V1, EcgLead.V2, EcgLead.V6]);
                    }
                }
                else
                {
                    var plethSamples = MechanicalUncouplingSmokeChecks.Samples(blocks, 2);
                    var plethBand = new PlethPulsePlan(80_000_000, 512_000_000, 1250).CreateBands()[0] with
                    { EjectionIllustration = expectedPhysiology.ConductionPattern };
                    var plethSource = PhysiologySignalGenerator.Start(expectedPhysiology.ResolvePlan(), "AcqPleth125@1", 1, [plethBand]);
                    if (!plethSamples.SequenceEqual(plethSource.GenerateBefore(plethSamples.Length * 8_000_000L, plethSamples.Length, 200).Select(sample => sample.NormalizedValue)))
                    { throw new InvalidOperationException("Premature demo compressed normal Pleth support or lost overlapping tails."); }
                    MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, plethIndex: 100);
                    MechanicalUncouplingSmokeChecks.VerifyPixels(window, blocks, blocked ? 440 : ventricular ? pvcQrs : 565);
                    VascularPressureSmokeChecks.VerifyPressurePixels(window, blocks, [303, 320, 355, 395, 420]);
                    if (selection == 37)
                    {
                        WaveformDemoSmokeChecks.VerifyCvpSamplePixels(window, blocks, [2_200_000_000L, 2_256_000_000L, 2_312_000_000L]);
                    }
                    if (selection == 28)
                    {
                        var junctionalBlocks = MechanicalUncouplingSmokeChecks.Decode(PhysiologyDemoConfiguration.PrematureJunctional);
                        foreach (int row in new[] { 2, 3, 5 })
                        {
                            if (MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(junctionalBlocks, row)))
                            { throw new InvalidOperationException("PVC and PJC lost their distinct authored ectopic strengths."); }
                        }
                        foreach (int row in new[] { 1, 4 })
                        {
                            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(junctionalBlocks, row)))
                            { throw new InvalidOperationException("PVC changed independent respiratory channels."); }
                        }
                    }
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
                if (!ventricular) { VerifyPPrime(window, samples, projected, blocked, pStart, selection >= 26); }
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
                foreach (int next in new[] { 22, 37, 36, 35, 34, 33, 31, 32, 29, 30, 28, 25, 26, 27, 34, 24, 23, 26, 25, 22 })
                {
                    Click(window.StepButton); Click(window.RunButton);
                    var stale = window.ActiveTimer;
                    window.ConductionInput.SelectedIndex = next;
                    Click(projected ? window.ApplyEcgButton : window.ApplyBreathButton); window.Pulse(stale);
                    if (window.BlockCount != 0 || window.SimulationTimeNs != 0 || window.ActiveTimer is not null ||
                        (projected ? window.EcgConfiguration.ConductionPattern : window.BreathConfiguration.ConductionPattern) != ConductionSelection.Pattern(next))
                    { throw new InvalidOperationException("Direct conducted/blocked/aberrant PAC and three PJC variants/PVC transition retained old source or timer."); }
                }
            }
            finally { window.Close(); }
        }
        Console.WriteLine("ok: conducted/blocked/aberrant PAC and three PJC variants/PVC P-prime and P/T overlap, selected QRS/mechanics, pressure, wire recovery and atomic transitions");
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
