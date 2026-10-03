// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ShortPrSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ShortPrHasNormalQrsAndEarlySharedEvents), ShortPrHasNormalQrsAndEarlySharedEvents),
    ];
    private static void ShortPrHasNormalQrsAndEarlySharedEvents()
    {
        var plan = ShortPrReference.CreatePlan();
        var t = ShortPrReference.Timing;
        Check.That(t.PrIntervalNs == 100_000_000 && t.PDurationNs == 80_000_000 && t.QrsDurationNs == 80_000_000, "short PR, separate P and narrow QRS");
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(1_600_000_000, 30);
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 100_000_000, 900_000_000 }), "early QRS events");
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.SimTimeNs).SequenceEqual(new long[] { 180_000_000, 980_000_000 }), "shared mechanical delay");
        var a = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, ShortPrReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
        var normalTiming = new EcgCycleTiming(800_000_000, 80_000_000, 160_000_000, 80_000_000, 400_000_000, 180_000_000);
        var normalPlan = new RegularPhysiologyPlan(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
        var normal = ElectrodeSignalGenerator.Start(normalPlan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(timing: normalTiming)).GenerateBefore(800_000_000, 200, 100);
        Check.That(a.Take(25).Zip(normal).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "P and intervening baseline unchanged");
        Check.That(a.Skip(25).Take(100).Zip(normal.Skip(40)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "all QRS/ST/T samples are ordinary reference translated60ms, no inserted delta");
        Check.That(a.Skip(125).All(s => s.MicrovoltValues.All(v => v == 0)), "QT400 ends500ms");
        var wpw = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, WpwReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
        Check.That(a.Skip(25).Take(35).Zip(wpw.Skip(25)).Any(p => !p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "shortPR is not WPW QRS");
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, ShortPrReference.CreateLeadIIBands()).GenerateBefore(800_000_000, 200, 100);
        Check.That(a.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitorII projection parity");
    }
}
