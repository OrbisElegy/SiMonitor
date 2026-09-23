// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VtFusionSpecifications
{
    private static readonly long[] FusionTimes = [4_995_000_000, 16_995_000_000];
    public static Specification[] All =>
    [
        new(nameof(VtFusionSelectsOnlyOneAtrialAlignedBeatPerGroup), VtFusionSelectsOnlyOneAtrialAlignedBeatPerGroup),
        new(nameof(VtFusionPreservesProjectionAndRestoresWithinSelectedBeat), VtFusionPreservesProjectionAndRestoresWithinSelectedBeat),
    ];
    private static void VtFusionSelectsOnlyOneAtrialAlignedBeatPerGroup()
    {
        var plan = VentricularTachycardiaReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(18_000_000_000, 200);
        var selected = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical && e.CycleIndex % 32 == 13).ToArray();
        Check.That(selected.Select(e => e.SimTimeNs).SequenceEqual(FusionTimes), "one fusion per32 ventricular beats");
        Check.That(selected.All(e => events.Any(p => p.Kind == PhysiologyCycleEventKind.AtrialElectrical && p.SimTimeNs == e.SimTimeNs - 195_000_000)), "fusion aligns with independent P conduction example");
        Check.That(events.Count(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical) == events.Count(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical), "fusion does not create a second ventricular event or double ejection");
        var electrodes = VentricularTachycardiaReference.CreateElectrodes(true);
        var ventricular = VentricularTachycardiaReference.CreateElectrodes();
        var conducted = TextbookElectrodeReference.CreateElectrodes(timing: VentricularTachycardiaReference.Timing with { QrsDurationNs = 80_000_000 });
        bool differsFromVt = false, differsFromConducted = false;
        for (int i = 0; i < electrodes.Count; i++)
        {
            var mixed = EventWaveformComposition.Restore(new(electrodes[i].Bands, events));
            var vt = EventWaveformComposition.Restore(new(ventricular[i].Bands, events));
            var normal = EventWaveformComposition.Restore(new(conducted[i].Bands, events));
            Check.That(electrodes[i].Bands.Count(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical) == 1, "one unchanged independent P source");
            for (long time = 0; time < 18_000_000_000; time += 4_000_000)
            {
                bool fusion = selected.Any(e => time >= e.SimTimeNs && time < e.SimTimeNs + 275_000_000);
                long value = mixed.EvaluateAt(time), v = vt.EvaluateAt(time), n = normal.EvaluateAt(time);
                if (!fusion) { Check.That(value == v, "unselected beats and P unchanged"); continue; }
                Check.That(Math.Abs(2 * value - v - n) <= 8, "half normal plus half ventricular, no full VT left underneath");
                differsFromVt |= Math.Abs(value - v) > 1_000_000;
                differsFromConducted |= Math.Abs(value - n) > 1_000_000;
            }
        }
        Check.That(differsFromVt && differsFromConducted, "fusion is distinct from pure capture and unchanged VT");
    }
    private static void VtFusionPreservesProjectionAndRestoresWithinSelectedBeat()
    {
        var plan = VentricularTachycardiaReference.CreatePlan();
        var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, VentricularTachycardiaReference.CreateElectrodes(true)).GenerateBefore(5_600_000_000, 1400, 200);
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, VentricularTachycardiaReference.CreateLeadIIBands(true)).GenerateBefore(5_600_000_000, 1400, 200);
        Check.That(full.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitorII shares fusion projection");
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "fusion limb identity retained");
        foreach (long boundary in new long[] { 4_994_000_000, 4_995_000_000, 5_040_000_000, 5_100_000_000, 5_200_000_000, 5_270_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, VentricularTachycardiaReference.CreateElectrodes(true));
            source.GenerateBefore(boundary, 1400, 200);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var a = source.GenerateBefore(5_600_000_000, 1400, 200); var b = restored.GenerateBefore(5_600_000_000, 1400, 200);
            Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore during partial depolarization and T mixture");
        }
    }
}
