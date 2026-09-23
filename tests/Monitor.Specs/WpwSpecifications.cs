// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class WpwSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(WpwHasShortPrSlurredOnsetAndSharedEvents), WpwHasShortPrSlurredOnsetAndSharedEvents),
        new(nameof(WpwNegativeV1IsRegional), WpwNegativeV1IsRegional),
        new(nameof(WpwRestoresAndProjectsConsistently), WpwRestoresAndProjectsConsistently),
    ];
    private static void WpwHasShortPrSlurredOnsetAndSharedEvents()
    {
        var t = WpwReference.Timing;
        Check.That(t.PrIntervalNs == 100_000_000 && t.QrsDurationNs == 140_000_000 && t.PrIntervalNs + t.QrsDurationNs == 240_000_000, "short PR, wide QRS and preserved P-J");
        var plan = WpwReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(1_600_000_000, 30);
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 100_000_000, 900_000_000 }), "ventricular onset follows shortened PR");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 180_000_000, 980_000_000 }), "authored mechanical delay follows early ventricular events");
        var a = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, WpwReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
        int V(int i, EcgLead lead) => a[i].MicrovoltValues[(int)lead];
        foreach (var lead in new[] { EcgLead.I, EcgLead.II, EcgLead.V1, EcgLead.V5 })
        {
            Check.That(V(27, lead) > 0 && V(30, lead) > V(27, lead) && V(32, lead) > V(30, lead), "positive slurred delta before main upstroke");
            Check.That(V(36, lead) - V(33, lead) > 2 * (V(32, lead) - V(29, lead)), "initial delta rises more slowly than main QRS");
            Check.That(V(40, lead) > 600 && V(65, lead) < -20 && V(105, lead) < -80, "dominant R with secondary ST depression and inverted T");
        }
        Check.That(a.Skip(125).All(s => s.MicrovoltValues.All(v => v == 0)), "QT400 ends at500ms");
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, WpwReference.CreateLeadIIBands()).GenerateBefore(800_000_000, 200, 100);
        Check.That(a.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "shared monitorII");
        Check.That(a.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity");
    }
    private static void WpwNegativeV1IsRegional()
    {
        var plan = WpwReference.CreatePlan();
        var positive = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, WpwReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
        var negative = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, WpwReference.CreateElectrodes(true)).GenerateBefore(800_000_000, 200, 100);
        int V(int index) => negative[index].MicrovoltValues[(int)EcgLead.V1];
        Check.That(V(27) < 0 && V(30) < V(27) && V(32) < V(30), "negative initial delta");
        Check.That(V(33) - V(36) > 2 * (V(29) - V(32)), "negative delta has slower initial descent");
        Check.That(V(40) < -800 && negative.Skip(25).Take(30).All(s => s.MicrovoltValues[(int)EcgLead.V1] <= 0), "dominant QS before secondary ST transition");
        Check.That(V(65) > 20 && V(105) > 80, "regional secondary positive ST/T");
        Check.That(positive.Zip(negative).All(p => Enumerable.Range(0, 12).Where(i => i != (int)EcgLead.V1).All(i => p.First.MicrovoltValues[i] == p.Second.MicrovoltValues[i])), "all eleven unselected leads unchanged");
        Check.That(positive.Take(25).Zip(negative).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "P and PR baseline unchanged including V1");
        Check.That(negative.Skip(125).All(s => s.MicrovoltValues.All(v => v == 0)), "QT endpoint retained");
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, WpwReference.CreateLeadIIBands(true)).GenerateBefore(800_000_000, 200, 100);
        Check.That(negative.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "negative variant monitor II parity");
    }
    private static void WpwRestoresAndProjectsConsistently()
    {
        foreach (bool negativeV1 in new[] { false, true })
            foreach (long boundary in new[] { 98_000_000L, 128_000_000, 160_000_000, 238_000_000, 320_000_000, 498_000_000 })
            {
                var source = ElectrodeSignalGenerator.Start(WpwReference.CreatePlan(), "AcqECGMonitor250@1", 1, WpwReference.CreateElectrodes(negativeV1));
                source.GenerateBefore(boundary, 200, 100);
                var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
                var a = source.GenerateBefore(1_600_000_000, 400, 100);
                var b = restored.GenerateBefore(1_600_000_000, 400, 100);
                Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "delta/QRS/ST/T recovery exact");
            }
    }
}
