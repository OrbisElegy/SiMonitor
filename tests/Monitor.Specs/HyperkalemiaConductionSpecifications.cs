// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class HyperkalemiaConductionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(HighKConductionChangesSharedTimingAndMorphology), HighKConductionChangesSharedTimingAndMorphology),
        new(nameof(HighKConductionRestoresAcrossDelayedQrsAndT), HighKConductionRestoresAcrossDelayedQrsAndT),
    ];
    private static void HighKConductionChangesSharedTimingAndMorphology()
    {
        var plan = HyperkalemiaConductionReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(1_000_000_000, 20);
        Check.That(events.Single(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).SimTimeNs == 240_000_000 &&
            events.Single(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).SimTimeNs == 320_000_000, "electrical and mechanical events follow authored long PR");
        var electrodes = HyperkalemiaConductionReference.CreateElectrodes();
        var ordinary = TextbookElectrodeReference.CreateElectrodes(timing: HyperkalemiaConductionReference.Timing);
        var samples = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(1_000_000_000, 250, 100);
        var normal = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, ordinary).GenerateBefore(1_000_000_000, 250, 100);
        for (int i = 0; i < 10; i++)
        {
            Check.That(electrodes[i].Bands[0].DurationNs == 140_000_000 && electrodes[i].Bands[1].DurationNs == 140_000_000, "P and QRS supports broaden");
            Check.That(electrodes[i].Bands[1].TableQ32.SequenceEqual(ordinary[i].Bands[1].TableQ32), "QRS duration changes without inventing a bundle-block pattern");
        }
        for (int i = 0; i < 35; i++)
            for (int lead = 0; lead < 12; lead++)
                Check.That(Math.Abs(samples[i].MicrovoltValues[lead] * 3 - normal[i].MicrovoltValues[lead]) <= 3, "P amplitude reduced to one third");
        Check.That(samples.Skip(35).Take(25).All(s => s.MicrovoltValues.All(v => v == 0)), "visible PR segment ends at240ms");
        Check.That(samples.Skip(80).Take(7).Any(s => Math.Abs(s.MicrovoltValues[10]) > 20), "QRS persists beyond usual80ms");
        int[] st = [-40, -80, -40, 60, 0, -60, -50, -80, -100, -100, -80, -60];
        for (int lead = 0; lead < 12; lead++)
            Check.That(Math.Abs(samples[110].MicrovoltValues[lead] - st[lead]) <= 1, "projected ST polarity at440ms");
        Check.That(samples[150].MicrovoltValues[9] > 1000 && samples.Skip(170).All(s => s.MicrovoltValues.All(v => v == 0)), "high T remains; QT ends680ms after QRS240ms");
        Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identities hold");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, HyperkalemiaConductionReference.CreateLeadIIBands()).GenerateBefore(1_000_000_000, 250, 100);
        Check.That(samples.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor II shares all delayed components");
    }
    private static void HighKConductionRestoresAcrossDelayedQrsAndT()
    {
        foreach (long boundary in new[] { 138_000_000L, 238_000_000, 350_000_000, 602_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(HyperkalemiaConductionReference.CreatePlan(), "AcqECGMonitor250@1", 1, HyperkalemiaConductionReference.CreateElectrodes());
            source.GenerateBefore(boundary, 250, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(2_000_000_000, 500, 100);
            var b = restored.GenerateBefore(2_000_000_000, 500, 100);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "long PR/QRS/T recovery exact");
        }
    }
}
