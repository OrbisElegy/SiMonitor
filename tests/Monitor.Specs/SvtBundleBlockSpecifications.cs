// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class SvtBundleBlockSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(SvtRightBundleWidensVentriclesWithoutChangingAtria), SvtRightBundleWidensVentriclesWithoutChangingAtria),
        new(nameof(SvtRightBundlePreservesProjectionAndPhase), SvtRightBundlePreservesProjectionAndPhase),
        new(nameof(SvtLeftBundlePreservesMorphologyAndPhase), SvtLeftBundlePreservesMorphologyAndPhase),
        new(nameof(SvtRejectsSimultaneousBundleShapes), SvtRejectsSimultaneousBundleShapes),
    ];
    private static void SvtRightBundleWidensVentriclesWithoutChangingAtria() => VerifyMorphology(false);
    private static void SvtLeftBundlePreservesMorphologyAndPhase()
    {
        VerifyMorphology(true);
        VerifyPhase(true);
    }
    private static void VerifyMorphology(bool left)
    {
        var timing = SupraventricularTachycardiaReference.ResolveTiming(!left, left);
        timing.Validate();
        Check.That(timing.QrsDurationNs == 140_000_000 && timing.StDurationNs == 20_000_000 && timing.QtIntervalNs == 260_000_000, "authored broad QRS and secondary ST/T fit300ms RR");
        var broad = SupraventricularTachycardiaReference.CreateElectrodes(!left, left);
        var narrow = SupraventricularTachycardiaReference.CreateElectrodes();
        var bundleBlockReference = left ? LeftBundleBlockReference.CreateElectrodes() : RightBundleBlockReference.CreateElectrodes();
        for (int i = 0; i < broad.Count; i++)
        {
            Check.That(broad[i].Bands[0].TableQ32.SequenceEqual(narrow[i].Bands[0].TableQ32) && broad[i].Bands[0].DurationNs == 40_000_000, "overlapping retrograde P remains unchanged");
            Check.That(broad[i].Bands[1].DurationNs == 140_000_000 && broad[i].Bands[1].TableQ32.SequenceEqual(bundleBlockReference[i].Bands[1].TableQ32), "reuse complete bundle-block QRS contour");
            Check.That(broad[i].Bands[2].TableQ32.SequenceEqual(bundleBlockReference[i].Bands[2].TableQ32) && broad[i].Bands[2].DelayNs == 160_000_000, "secondary T uses shared polarity and authored shorter support");
        }
        Check.That(!broad[6].Bands[1].TableQ32.SequenceEqual(narrow[6].Bands[1].TableQ32), "precordial QRS is actually changed");
        var samples = ElectrodeSignalGenerator.Start(SupraventricularTachycardiaReference.CreatePlan(), "AcqECGMonitor250@1", 1, broad).GenerateBefore(1_200_000_000, 300, 100);
        Check.That(samples.Count == 300, "broad SVT produces every sample across four complete beats");
        for (int beat = 0; beat < 4; beat++)
            Check.That(samples.Skip(beat * 75 + 65).Take(10).All(s => s.MicrovoltValues.All(v => v == 0)), "QT ends before next beat, no stale ST/T tail");
    }
    private static void SvtRightBundlePreservesProjectionAndPhase() => VerifyPhase(false);
    private static void VerifyPhase(bool left)
    {
        var plan = SupraventricularTachycardiaReference.CreatePlan();
        var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, SupraventricularTachycardiaReference.CreateElectrodes(!left, left)).GenerateBefore(1_200_000_000, 300, 100);
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, SupraventricularTachycardiaReference.CreateLeadIIBands(!left, left)).GenerateBefore(1_200_000_000, 300, 100);
        string scenario = left ? "SVT left bundle block" : "SVT right bundle block";
        EcgProjectionChecks.RequireMatchingLeadII(full, ii, 300, scenario);
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identities preserved");
        foreach (long boundary in new long[] { 38_000_000, 138_000_000, 150_000_000, 160_000_000, 258_000_000, 300_000_000, 602_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, SupraventricularTachycardiaReference.CreateElectrodes(!left, left));
            source.GenerateBefore(boundary, 300, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var expected = full.Where(sample => sample.Tick.SimTimeNs >= boundary).ToArray();
            var actual = source.GenerateBefore(1_200_000_000, 300, 100);
            var recovered = restored.GenerateBefore(1_200_000_000, 300, 100);
            EcgProjectionChecks.RequireMatchingSamples(expected, actual, scenario);
            EcgProjectionChecks.RequireMatchingSamples(expected, recovered, scenario);
        }
    }
    private static void SvtRejectsSimultaneousBundleShapes()
    {
        foreach (Action invalid in new Action[] {
            () => SupraventricularTachycardiaReference.ResolveTiming(true, true),
            () => SupraventricularTachycardiaReference.CreateElectrodes(true, true),
            () => SupraventricularTachycardiaReference.CreateLeadIIBands(true, true) })
        {
            try { invalid(); }
            catch (EventWaveformException e) when (e.ReasonCode == "Svt.ConflictingModes") { continue; }
            throw new InvalidOperationException("Simultaneous left/right bundle morphology accepted.");
        }
    }

}
