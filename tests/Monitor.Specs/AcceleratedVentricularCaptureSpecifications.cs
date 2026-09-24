// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AcceleratedVentricularCaptureSpecifications
{
    private static readonly long[] FusionTimes = [120_000_000, 12_120_000_000];
    public static Specification[] All =>
    [
        new(nameof(CaptureAndFusionCannotOccupyTheSameSlot), CaptureAndFusionCannotOccupyTheSameSlot),
        new(nameof(AcceleratedVentricularCaptureSelectsOnlyOneAtrialAlignedBeatPerGroup), AcceleratedVentricularCaptureSelectsOnlyOneAtrialAlignedBeatPerGroup),
        new(nameof(AcceleratedVentricularCapturePreservesProjectionAndRestoresWithinSelectedBeat), AcceleratedVentricularCapturePreservesProjectionAndRestoresWithinSelectedBeat),
    ];
    private static void AcceleratedVentricularCaptureSelectsOnlyOneAtrialAlignedBeatPerGroup()
    {
        var plan = AcceleratedVentricularReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(18_000_000_000, 200);
        var selected = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical && e.CycleIndex % 16 == 0).ToArray();
        Check.That(selected.Select(e => e.SimTimeNs).SequenceEqual(FusionTimes), "one fusion per16 ventricular beats");
        Check.That(selected.All(e => events.Any(p => p.Kind == PhysiologyCycleEventKind.AtrialElectrical && p.SimTimeNs == e.SimTimeNs - 120_000_000)), "fusion aligns with independent P conduction example");
        Check.That(events.Count(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical) == events.Count(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical), "fusion does not create a second ventricular event or double ejection");
        var electrodes = AcceleratedVentricularReference.CreateElectrodes(capture: true);
        var ventricular = AcceleratedVentricularReference.CreateElectrodes();
        var conducted = TextbookElectrodeReference.CreateElectrodes(timing: AcceleratedVentricularReference.Timing with { QrsDurationNs = 80_000_000 });
        bool differsFromVt = false, differsFromConducted = false;
        for (int i = 0; i < electrodes.Count; i++)
        {
            var mixed = EventWaveformComposition.Restore(new(electrodes[i].Bands, events));
            var vt = EventWaveformComposition.Restore(new(ventricular[i].Bands, events));
            var normal = EventWaveformComposition.Restore(new(conducted[i].Bands, events));
            Check.That(electrodes[i].Bands.Count(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical) == 1, "one unchanged independent P source");
            for (long time = 0; time < 18_000_000_000; time += 4_000_000)
            {
                bool fusion = selected.Any(e => time >= e.SimTimeNs && time < e.SimTimeNs + 400_000_000);
                long value = mixed.EvaluateAt(time), v = vt.EvaluateAt(time), n = normal.EvaluateAt(time);
                if (!fusion) { Check.That(value == v, "unselected beats and P unchanged"); continue; }
                Check.That(value == n, "selected beat is fully conducted, no ventricular residue");
                differsFromVt |= Math.Abs(value - v) > 1_000_000;
                differsFromConducted |= Math.Abs(value - n) > 1_000_000;
            }
        }
        Check.That(differsFromVt && !differsFromConducted, "capture equals conducted waveform and differs from ventricular shape");
    }
    private static void AcceleratedVentricularCapturePreservesProjectionAndRestoresWithinSelectedBeat()
    {
        var plan = AcceleratedVentricularReference.CreatePlan();
        var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, AcceleratedVentricularReference.CreateElectrodes(capture: true)).GenerateBefore(5_600_000_000, 1400, 200);
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, AcceleratedVentricularReference.CreateLeadIIBands(capture: true)).GenerateBefore(5_600_000_000, 1400, 200);
        Check.That(full.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitorII shares fusion projection");
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "fusion limb identity retained");
        foreach (long boundary in new long[] { 118_000_000, 120_000_000, 160_000_000, 240_000_000, 400_000_000, 520_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, AcceleratedVentricularReference.CreateElectrodes(capture: true));
            source.GenerateBefore(boundary, 1400, 200);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(5_600_000_000, 1400, 200); var b = restored.GenerateBefore(5_600_000_000, 1400, 200);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore during partial depolarization and T mixture");
        }
    }
    private static void CaptureAndFusionCannotOccupyTheSameSlot()
    {
        foreach (Action create in new Action[] {
            () => AcceleratedVentricularReference.CreateElectrodes(true,true),
            () => AcceleratedVentricularReference.CreateLeadIIBands(true,true) })
        {
            try { create(); }
            catch (EventWaveformException e) when (e.ReasonCode == "Aivr.ConflictingModes") { continue; }
            throw new InvalidOperationException("Conflicting capture/fusion accepted.");
        }
    }

}
