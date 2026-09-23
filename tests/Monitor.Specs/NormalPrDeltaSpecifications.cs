// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class NormalPrDeltaSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(NormalPrDeltaPreservesContoursAndDelaysEvents), NormalPrDeltaPreservesContoursAndDelaysEvents),
        new(nameof(ProlongedPrDeltaRetimesVentricularComponentsAndEvents), ProlongedPrDeltaRetimesVentricularComponentsAndEvents),
        new(nameof(NormalPrDeltaRestoresAcrossBoundaries), NormalPrDeltaRestoresAcrossBoundaries),
    ];
    private static void NormalPrDeltaPreservesContoursAndDelaysEvents()
    {
        var plan = NormalPrDeltaReference.CreatePlan();
        var t = NormalPrDeltaReference.Timing;
        Check.That(t.PrIntervalNs == 160_000_000 && t.PDurationNs == 100_000_000 && t.QrsDurationNs == 140_000_000, "normal PR with wide QRS");
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(1_600_000_000, 30);
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 160_000_000, 960_000_000 }), "normal PR event offsets");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 240_000_000, 1_040_000_000 }), "mechanical events follow normal PR");
        var a = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, NormalPrDeltaReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
        var normal = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(timing: t)).GenerateBefore(800_000_000, 200, 100);
        Check.That(a.Take(40).Zip(normal).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "ordinary P and PR baseline retained");
        var wpw = ElectrodeSignalGenerator.Start(WpwReference.CreatePlan(), "AcqECGMonitor250@1", 1, WpwReference.CreateElectrodes(true)).GenerateBefore(800_000_000, 200, 100);
        Check.That(a.Skip(40).Take(100).Zip(wpw.Skip(25)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "delta/QRS/ST/T translated60ms without contour distortion");
        int V(int sample, EcgLead lead) => a[sample].MicrovoltValues[(int)lead];
        Check.That(V(42, EcgLead.V1) < 0 && V(45, EcgLead.V1) < V(42, EcgLead.V1) && V(47, EcgLead.V1) < V(45, EcgLead.V1), "negative V1 delta survives normal PR");
        Check.That(V(55, EcgLead.V1) < -800 && V(120, EcgLead.V1) > 80 && V(55, EcgLead.I) > 600, "negative V1 main/positive secondary T and lateral R");
        Check.That(a.Skip(140).All(s => s.MicrovoltValues.All(v => v == 0)), "QT ends560ms");
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, NormalPrDeltaReference.CreateLeadIIBands()).GenerateBefore(800_000_000, 200, 100);
        Check.That(a.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitorII projection parity");
    }
    private static void ProlongedPrDeltaRetimesVentricularComponentsAndEvents()
    {
        var t = NormalPrDeltaReference.ResolveTiming(true);
        Check.That(t.PrIntervalNs == 240_000_000 && t.QrsDurationNs == 140_000_000 && t.QtIntervalNs == 400_000_000, "long PR retains QRS and QT");
        var plan = NormalPrDeltaReference.CreatePlan(true);
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(1_600_000_000, 30);
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 240_000_000, 1_040_000_000 }), "delayed electrical events");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 320_000_000, 1_120_000_000 }), "mechanical events follow long PR");
        var normal = ElectrodeSignalGenerator.Start(NormalPrDeltaReference.CreatePlan(), "AcqECGMonitor250@1", 1, NormalPrDeltaReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
        var delayed = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, NormalPrDeltaReference.CreateElectrodes(true)).GenerateBefore(800_000_000, 200, 100);
        Check.That(delayed.Take(40).Zip(normal).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "P and early PR baseline unchanged");
        Check.That(delayed.Skip(25).Take(35).All(s => s.MicrovoltValues.All(v => v == 0)), "long PR baseline has no leftover early QRS");
        Check.That(delayed.Skip(60).Take(100).Zip(normal.Skip(40)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "all ventricular contours translated80ms");
        Check.That(delayed.Skip(160).All(s => s.MicrovoltValues.All(v => v == 0)), "QT ends640ms before next P");
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, NormalPrDeltaReference.CreateLeadIIBands(true)).GenerateBefore(800_000_000, 200, 100);
        Check.That(delayed.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "long PR monitorII parity");
    }
    private static void NormalPrDeltaRestoresAcrossBoundaries()
    {
        foreach (bool prolongedPr in new[] { false, true })
            foreach (long boundary in new[] { 98_000_000L, 158_000_000, 188_000_000, 238_000_000, 240_000_000, 298_000_000, 380_000_000, 558_000_000, 638_000_000 })
            {
                var source = ElectrodeSignalGenerator.Start(NormalPrDeltaReference.CreatePlan(prolongedPr), "AcqECGMonitor250@1", 1, NormalPrDeltaReference.CreateElectrodes(prolongedPr));
                source.GenerateBefore(boundary, 200, 100);
                var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
                var a = source.GenerateBefore(1_600_000_000, 400, 100);
                var b = restored.GenerateBefore(1_600_000_000, 400, 100);
                Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "normal PR delta component boundary recovery");
            }
    }
}
