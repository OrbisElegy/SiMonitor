// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AcceleratedVentricularChecks
{
    internal static void VerifySharedEventGrid()
    {
        var events = RegularPhysiologyTimeline.Start(AcceleratedVentricularReference.CreatePlan()).AdvanceBefore(18_000_000_000, 200);
        var selected = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical && e.CycleIndex % 16 == 0).ToArray();
        Check.That(selected.Select(e => e.SimTimeNs).SequenceEqual([120_000_000L, 12_120_000_000]),
            "one selected capture or fusion slot per 16 ventricular beats");
        Check.That(selected.All(e => events.Any(p => p.Kind == PhysiologyCycleEventKind.AtrialElectrical && p.SimTimeNs == e.SimTimeNs - 120_000_000)),
            "the selected slot aligns with independent P conduction");
        Check.That(events.Count(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical) == events.Count(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical),
            "capture and fusion reuse one ventricular event and ejection per beat");
    }

    internal static void VerifyMorphology(bool capture)
    {
        string scenario = capture ? "AIVR capture" : "AIVR fusion";
        var events = RegularPhysiologyTimeline.Start(AcceleratedVentricularReference.CreatePlan()).AdvanceBefore(18_000_000_000, 200);
        var selected = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical && e.CycleIndex % 16 == 0).ToArray();
        var electrodes = AcceleratedVentricularReference.CreateElectrodes(fusion: !capture, capture: capture);
        var ventricular = AcceleratedVentricularReference.CreateElectrodes();
        var conducted = TextbookElectrodeReference.CreateElectrodes(timing: AcceleratedVentricularReference.Timing with { QrsDurationNs = 80_000_000 });
        bool differsFromVentricular = false;
        bool differsFromConducted = false;
        for (int i = 0; i < electrodes.Count; i++)
        {
            var selectedSource = EventWaveformComposition.Restore(new(electrodes[i].Bands, events));
            var ventricularSource = EventWaveformComposition.Restore(new(ventricular[i].Bands, events));
            var conductedSource = EventWaveformComposition.Restore(new(conducted[i].Bands, events));
            Check.That(electrodes[i].Bands.Count(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical) == 1,
                $"{scenario}: one unchanged independent P source");
            for (long simTimeNs = 0; simTimeNs < 18_000_000_000; simTimeNs += 4_000_000)
            {
                bool isSelectedWindow = selected.Any(e => simTimeNs >= e.SimTimeNs && simTimeNs < e.SimTimeNs + 400_000_000);
                long value = selectedSource.EvaluateAt(simTimeNs);
                long ventricularValue = ventricularSource.EvaluateAt(simTimeNs);
                long conductedValue = conductedSource.EvaluateAt(simTimeNs);
                if (!isSelectedWindow)
                {
                    Check.That(value == ventricularValue, $"{scenario}: unselected beats and P remain unchanged");
                    continue;
                }
                if (capture)
                {
                    Check.That(value == conductedValue, "AIVR capture: the selected beat is fully conducted without ventricular residue");
                }
                else
                {
                    Check.That(Math.Abs(2 * value - ventricularValue - conductedValue) <= 8,
                        "AIVR fusion: half conducted plus half ventricular, without a full ventricular beat underneath");
                }
                differsFromVentricular |= Math.Abs(value - ventricularValue) > 1_000_000;
                differsFromConducted |= Math.Abs(value - conductedValue) > 1_000_000;
            }
        }
        Check.That(differsFromVentricular && differsFromConducted == !capture,
            $"{scenario}: the selected morphology has the expected relationship to pure ventricular and conducted beats");
    }

    internal static void VerifyProjectionAndRecovery(bool capture)
    {
        string scenario = capture ? "AIVR capture" : "AIVR fusion";
        var plan = AcceleratedVentricularReference.CreatePlan();
        var electrodes = AcceleratedVentricularReference.CreateElectrodes(fusion: !capture, capture: capture);
        var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(5_600_000_000, 1400, 200);
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1,
            AcceleratedVentricularReference.CreateLeadIIBands(fusion: !capture, capture: capture)).GenerateBefore(5_600_000_000, 1400, 200);
        EcgProjectionChecks.RequireMatchingLeadII(full, monitor, 1400, scenario);
        Check.That(full.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1),
            $"{scenario}: limb identities are retained");
        foreach (long boundaryNs in new long[] { 118_000_000, 120_000_000, 160_000_000, 240_000_000, 400_000_000, 520_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes);
            source.GenerateBefore(boundaryNs, 1400, 200);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var expected = full.Where(sample => sample.Tick.SimTimeNs >= boundaryNs).ToArray();
            var actual = source.GenerateBefore(5_600_000_000, 1400, 200);
            var recovered = restored.GenerateBefore(5_600_000_000, 1400, 200);
            EcgProjectionChecks.RequireMatchingSamples(expected, actual, scenario);
            EcgProjectionChecks.RequireMatchingSamples(expected, recovered, scenario);
        }
    }
}
