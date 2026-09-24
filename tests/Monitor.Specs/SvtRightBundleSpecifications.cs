// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class SvtRightBundleSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(SvtRightBundleWidensVentriclesWithoutChangingAtria), SvtRightBundleWidensVentriclesWithoutChangingAtria),
        new(nameof(SvtRightBundlePreservesProjectionAndPhase), SvtRightBundlePreservesProjectionAndPhase),
    ];
    private static void SvtRightBundleWidensVentriclesWithoutChangingAtria()
    {
        var timing = SupraventricularTachycardiaReference.ResolveTiming(true);
        timing.Validate();
        Check.That(timing.QrsDurationNs == 140_000_000 && timing.StDurationNs == 20_000_000 && timing.QtIntervalNs == 260_000_000, "authored broad QRS and secondary ST/T fit300ms RR");
        var broad = SupraventricularTachycardiaReference.CreateElectrodes(true);
        var narrow = SupraventricularTachycardiaReference.CreateElectrodes();
        var rbbb = RightBundleBlockReference.CreateElectrodes();
        for (int i = 0; i < broad.Count; i++)
        {
            Check.That(broad[i].Bands[0].TableQ32.SequenceEqual(narrow[i].Bands[0].TableQ32) && broad[i].Bands[0].DurationNs == 40_000_000, "overlapping retrograde P remains unchanged");
            Check.That(broad[i].Bands[1].DurationNs == 140_000_000 && broad[i].Bands[1].TableQ32.SequenceEqual(rbbb[i].Bands[1].TableQ32), "reuse complete RBBB QRS contour");
            Check.That(broad[i].Bands[2].TableQ32.SequenceEqual(rbbb[i].Bands[2].TableQ32) && broad[i].Bands[2].DelayNs == 160_000_000, "secondary T uses shared polarity and authored shorter support");
        }
        Check.That(!broad[6].Bands[1].TableQ32.SequenceEqual(narrow[6].Bands[1].TableQ32), "precordial QRS is actually changed");
        var samples = ElectrodeSignalGenerator.Start(SupraventricularTachycardiaReference.CreatePlan(), "AcqECGMonitor250@1", 1, broad).GenerateBefore(1_200_000_000, 300, 100);
        for (int beat = 0; beat < 4; beat++)
            Check.That(samples.Skip(beat * 75 + 65).Take(10).All(s => s.MicrovoltValues.All(v => v == 0)), "QT ends before next beat, no stale ST/T tail");
    }
    private static void SvtRightBundlePreservesProjectionAndPhase()
    {
        var plan = SupraventricularTachycardiaReference.CreatePlan();
        var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, SupraventricularTachycardiaReference.CreateElectrodes(true)).GenerateBefore(1_200_000_000, 300, 100);
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, SupraventricularTachycardiaReference.CreateLeadIIBands(true)).GenerateBefore(1_200_000_000, 300, 100);
        Check.That(full.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "broad SVT monitorII shares electrode source");
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identities preserved");
        foreach (long boundary in new long[] { 38_000_000, 138_000_000, 150_000_000, 160_000_000, 258_000_000, 300_000_000, 602_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, SupraventricularTachycardiaReference.CreateElectrodes(true));
            source.GenerateBefore(boundary, 300, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(1_200_000_000, 300, 100); var b = restored.GenerateBefore(1_200_000_000, 300, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "recovery retains wide QRS/ST/T phase");
        }
    }
}
