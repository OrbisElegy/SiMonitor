// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class HyperkalemiaFusionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(HighKFusionHasOneContinuousCompoundAndSharedProjection), HighKFusionHasOneContinuousCompoundAndSharedProjection),
        new(nameof(HighKFusionRestoresAtExtremaAndSupportBoundaries), HighKFusionRestoresAtExtremaAndSupportBoundaries),
    ];
    private static void HighKFusionHasOneContinuousCompoundAndSharedProjection()
    {
        var plan = HyperkalemiaFusionReference.CreatePlan();
        var electrodes = HyperkalemiaFusionReference.CreateElectrodes();
        Check.That(electrodes.All(e => e.Bands.Count == 1 && e.Bands[0].Trigger == PhysiologyCycleEventKind.VentricularElectrical && e.Bands[0].DurationNs == 720_000_000), "no independent P, ST or T component");
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(2_000_000_000, 50);
        Check.That(events.SequenceEqual(RegularPhysiologyTimeline.Start(HyperkalemiaConductionReference.CreatePlan(true)).AdvanceBefore(2_000_000_000, 50)), "fusion changes morphology only, not presumed pumping");
        var a = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(2_000_000_000, 500, 100);
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, HyperkalemiaFusionReference.CreateLeadIIBands()).GenerateBefore(2_000_000_000, 500, 100);
        Check.That(a.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitorII parity");
        Check.That(a.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "Einthoven identity");
        Check.That(a.Where(s => s.Tick.SimTimeNs % 1_000_000_000 < 240_000_000 || s.Tick.SimTimeNs % 1_000_000_000 >= 960_000_000).All(s => s.MicrovoltValues.All(v => v == 0)), "no P and exact compound support");
        short[] lead = a.Take(250).Select(s => s.MicrovoltValues[1]).ToArray();
        Check.That(lead[88] > 620 && lead[119] < -450 && lead[175] > 690, "broad positive-negative-positive fused contour");
        Check.That(Enumerable.Range(95, 65).All(i => lead[i] != 0 || lead[i + 1] != 0), "no intervening isoelectric ST plateau");
        Check.That(Enumerable.Range(1, 249).Max(i => Math.Abs(lead[i] - lead[i - 1])) < 70, "bounded per-sample slope including support joins");
    }
    private static void HighKFusionRestoresAtExtremaAndSupportBoundaries()
    {
        foreach (long boundary in new[] { 238_000_000L, 352_000_000, 476_000_000, 702_000_000, 958_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(HyperkalemiaFusionReference.CreatePlan(), "AcqECGMonitor250@1", 1, HyperkalemiaFusionReference.CreateElectrodes());
            source.GenerateBefore(boundary, 250, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(3_000_000_000, 750, 100);
            var b = restored.GenerateBefore(3_000_000_000, 750, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "fusion restore through joins and subsequent beats");
        }
    }
}
