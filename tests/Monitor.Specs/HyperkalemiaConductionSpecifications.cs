// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class HyperkalemiaConductionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(HighKConductionChangesSharedTimingAndMorphology), HighKConductionChangesSharedTimingAndMorphology),
        new(nameof(HighKQrsVoltageChangesProjectedRAndS), HighKQrsVoltageChangesProjectedRAndS),
        new(nameof(HighKAbsentPRemovesAtrialEventsOnly), HighKAbsentPRemovesAtrialEventsOnly),
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
            Check.That(electrodes[i].Bands[1].DelayNs == 0, "QRS begins at the same shared event");
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
    private static void HighKQrsVoltageChangesProjectedRAndS()
    {
        var plan = HyperkalemiaConductionReference.CreatePlan();
        var changed = HyperkalemiaConductionReference.CreateElectrodes();
        var reference = TextbookElectrodeReference.CreateElectrodes(timing: HyperkalemiaConductionReference.Timing);
        IReadOnlyList<ElectrodeSignalSample> Qrs(IReadOnlyList<ElectrodeWaveformPlan> electrodes) =>
            ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
                electrodes.Select(e => e with { Bands = [e.Bands[1]] }).ToArray()).GenerateBefore(1_000_000_000, 250, 100);
        var actual = Qrs(changed); var normal = Qrs(reference);
        foreach (int lead in new[] { 0, 1, 2, 6, 7, 8, 9, 10, 11 })
        {
            long r = actual.Max(s => s.MicrovoltValues[lead]), oldR = normal.Max(s => s.MicrovoltValues[lead]);
            long s = -actual.Min(v => v.MicrovoltValues[lead]), oldS = -normal.Min(v => v.MicrovoltValues[lead]);
            Check.That(r > oldR * 60 / 100 && r < oldR * 70 / 100, "projected R attenuated with no time shift");
            Check.That(s > oldS * 125 / 100 && s < oldS * 135 / 100, "projected S deeper");
        }
        Check.That(actual.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identities preserved after offline authoring");
        // Normal and modified Q are identical before main ventricular activation.
        Check.That(actual.Where(s => s.Tick.SimTimeNs < 257_000_000).Zip(normal).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "early Q unchanged");
        var absent = HyperkalemiaConductionReference.CreateElectrodes(true);
        for (int i = 0; i < 10; i++)
            Check.That(changed[i].Bands[1].TableQ32.SequenceEqual(absent[i].Bands[0].TableQ32), "same voltage morphology with and without P");
    }
    private static void HighKAbsentPRemovesAtrialEventsOnly()
    {
        var plan = HyperkalemiaConductionReference.CreatePlan(true);
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(3_000_000_000, 50);
        var ordinary = RegularPhysiologyTimeline.Start(HyperkalemiaConductionReference.CreatePlan()).AdvanceBefore(3_000_000_000, 50);
        Check.That(events.All(e => e.Kind is not (PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.AtrialMechanical)), "no effective atrial excitation or mechanical event");
        Check.That(events.SequenceEqual(ordinary.Where(e => e.Kind is not (PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.AtrialMechanical))), "ventricular and respiratory timing remains identical");
        var electrodes = HyperkalemiaConductionReference.CreateElectrodes(true);
        Check.That(electrodes.All(e => e.Bands.All(b => b.Trigger != PhysiologyCycleEventKind.AtrialElectrical)), "no latent P band can reappear on an injected event");
        var a = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(3_000_000_000, 750, 100);
        var b = ElectrodeSignalGenerator.Start(HyperkalemiaConductionReference.CreatePlan(), "AcqECGMonitor250@1", 1, HyperkalemiaConductionReference.CreateElectrodes()).GenerateBefore(3_000_000_000, 750, 100);
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Tick.SimTimeNs % 1_000_000_000 < 240_000_000)
                Check.That(a[i].MicrovoltValues.All(v => v == 0), "no P signal before each broad QRS");
            else Check.That(a[i].MicrovoltValues.SequenceEqual(b[i].MicrovoltValues), "QRS/ST/T remain exact");
        }
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, HyperkalemiaConductionReference.CreateLeadIIBands(true)).GenerateBefore(3_000_000_000, 750, 100);
        Check.That(a.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "shared absentP monitorII parity");
    }
    private static void HighKConductionRestoresAcrossDelayedQrsAndT()
    {
        foreach (bool absentP in new[] { false, true })
            foreach (long boundary in new[] { 138_000_000L, 238_000_000, 350_000_000, 602_000_000 })
            {
                var source = ElectrodeSignalGenerator.Start(HyperkalemiaConductionReference.CreatePlan(absentP), "AcqECGMonitor250@1", 1, HyperkalemiaConductionReference.CreateElectrodes(absentP));
                source.GenerateBefore(boundary, 250, 100);
                var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
                var a = source.GenerateBefore(2_000_000_000, 500, 100);
                var b = restored.GenerateBefore(2_000_000_000, 500, 100);
                Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "long PR/QRS/T recovery exact");
            }
    }
}
