// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AtrialFlutterSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(FlutterThreeToOnePreservesVentricularPerfusionAndRecovery), FlutterThreeToOnePreservesVentricularPerfusionAndRecovery),
        new(nameof(FlutterHasContinuousFAndConductedVentricles), FlutterHasContinuousFAndConductedVentricles),
        new(nameof(FlutterRestoresProjectionAndRejectsConflicts), FlutterRestoresProjectionAndRejectsConflicts),
    ];
    private static void FlutterThreeToOnePreservesVentricularPerfusionAndRecovery()
    {
        var plan = AtrialFlutterReference.CreatePlan(3);
        var regular = plan with { HeartPeriodNs = 600_000_000, VentricularConductionRatio = 1, ConductionPattern = AvConductionPattern.FixedPr };
        var pressure = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000,
            Morphology: new(VascularPressureMorphologyKind.Arterial, 520_000_000, 4000));
        var flutterPressure = VascularPressureSource.Create(plan, pressure);
        var regularPressure = VascularPressureSource.Create(regular, pressure);
        for (long time = 0; time < 4_800_000_000; time += 8_000_000)
        { Check.That(flutterPressure.EvaluateAt(time) == regularPressure.EvaluateAt(time), "3:1 perfusion follows100/min ventricles, not300/min F waves"); }
        var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, AtrialFlutterReference.CreateElectrodes(3));
        source.GenerateBefore(680_000_000, 170, 100);
        var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
        var expected = source.GenerateBefore(2_400_000_000, 430, 100);
        Check.That(expected.Zip(restored.GenerateBefore(2_400_000_000, 430, 100)).All(pair => pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)),
            "3:1 restore exactly at the next conducted QRS retains all leads");
        var timeline = RegularPhysiologyTimeline.Start(plan);
        var before = timeline.CaptureState();
        try { timeline.AdvanceBefore(600_000_000, 1); throw new InvalidOperationException("Insufficient event budget accepted."); }
        catch (PhysiologyTimelineException) { Check.That(timeline.CaptureState() == before, "3:1 event rejection remains atomic"); }
        var events = timeline.AdvanceBefore(600_000_000, 100);
        Check.That(events.Count(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical) == 3 &&
            events.Count(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical) == 1, "three F activations produce one mechanical beat");
        foreach (int invalid in new[] { 0, 1, 5, int.MaxValue })
        {
            try { AtrialFlutterReference.CreateElectrodes(invalid); throw new InvalidOperationException("Unsupported flutter ratio accepted."); }
            catch (ArgumentOutOfRangeException) { }
        }
    }

    private static void FlutterHasContinuousFAndConductedVentricles()
    {
        foreach (int ratio in new[] { 2, 3, 4 })
        {
            var plan = AtrialFlutterReference.CreatePlan(ratio);
            var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(2_400_000_000, 100);
            Check.That(events.Count(e => e.Kind == PhysiologyCycleEventKind.AtrialElectrical) == 12 &&
                events.All(e => e.Kind != PhysiologyCycleEventKind.AtrialMechanical), "300/min F without invented normal atrial contractions");
            var qrs = events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
            Check.That(qrs.Length == 12 / ratio && qrs.Select(e => e.SimTimeNs).SequenceEqual(Enumerable.Range(0, 12 / ratio).Select(i => 80_000_000L + i * ratio * 200_000_000L)), "fixed 2:1/3:1/4:1 conduction");
            Check.That(qrs.Zip(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical)).All(p => p.Second.SimTimeNs - p.First.SimTimeNs == 80_000_000), "ventricular mechanics follows QRS, not every F");
            var electrodes = AtrialFlutterReference.CreateElectrodes(ratio);
            Check.That(electrodes.All(e => e.Bands.Count(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical) == 1 && e.Bands[0].DurationNs == 200_000_000 && e.Bands[1].DurationNs == 80_000_000), "replace P with full-cycle F and retain narrow QRS");
            var atrial = electrodes.Select(e => e with { Bands = e.Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical).ToArray() }).ToArray();
            var f = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, atrial).GenerateBefore(800_000_000, 200, 100);
            var exact = ElectrodeWaveformComposition.Restore(new(atrial, events));
            foreach (var lead in new[] { EcgLead.II, EcgLead.III, EcgLead.AVF })
            {
                var values = f.Select(s => s.MicrovoltValues[(int)lead]).ToArray();
                Check.That(values.Take(50).SequenceEqual(values.Skip(50).Take(50)) && values.Min() < -150 && values.Max() > 50, "equal F amplitude and period in inferior leads");
                // Check before acquisition rounding: a sub-microvolt crossing
                // can legitimately quantize two neighbouring samples to zero.
                var potentials = Enumerable.Range(0, 200).Select(i => exact.EvaluateAt(i * 4_000_000L).Leads[lead].Numerator).ToArray();
                Check.That(!potentials.Zip(potentials.Skip(1)).Any(p => p.First == 0 && p.Second == 0), "no flat isoelectric interval between F cycles");
            }
            var full = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(800_000_000, 200, 100);
            var ventricular = electrodes.Select(e => e with { Bands = e.Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical).ToArray() }).ToArray();
            var v = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, ventricular).GenerateBefore(800_000_000, 200, 100);
            Check.That(Enumerable.Range(0, 200).All(i => Math.Abs(full[i].MicrovoltValues[1] - f[i].MicrovoltValues[1] - v[i].MicrovoltValues[1]) <= 1), "continuous F is also present underneath QRS and T");
        }
    }
    private static void FlutterRestoresProjectionAndRejectsConflicts()
    {
        var plan = AtrialFlutterReference.CreatePlan(2);
        var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, AtrialFlutterReference.CreateElectrodes(2));
        source.GenerateBefore(196_000_000, 49, 100);
        var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
        var tail = source.GenerateBefore(1_600_000_000, 351, 100);
        Check.That(tail.Zip(restored.GenerateBefore(1_600_000_000, 351, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore across F boundary preserves all leads");
        Check.That(tail.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb lead identity preserved");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, AtrialFlutterReference.CreateLeadIIBands(2)).GenerateBefore(1_600_000_000, 400, 100);
        Check.That(tail.Zip(monitor.Skip(49)).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor matches projected lead II");
        // The mechanical pulse lasts longer than F-F but less than the actual
        // ventricular RR. Its validation must use the trigger's own clock.
        _ = new ArterialPulsePlan(80_000_000, 320_000_000, 80, 40).CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        _ = new PulmonaryArteryPulsePlan(40_000_000, 320_000_000, 10, 15).CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0);
        foreach (bool arterial in new[] { false, true })
        {
            try
            {
                if (arterial) { _ = new ArterialPulsePlan(0, 400_000_001, 80, 40).CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0); }
                else { _ = new PulmonaryArteryPulsePlan(0, 400_000_001, 10, 15).CreateChannel(plan, Guid.Parse("11111111-1111-4111-8111-111111111111"), 0); }
            }
            catch (EventWaveformException) { continue; }
            throw new InvalidOperationException("Pulse longer than ventricular RR accepted.");
        }
        foreach (var invalid in new[] { plan with { HeartPeriodNs = 800_000_000 }, plan with { VentricularConductionRatio = 1 }, plan with { VentricularConductionRatio = 5 }, plan with { IndependentVentricularPeriodNs = 800_000_000 }, plan with { CardiacActivity = CardiacActivity.AtrialOnly }, plan with { VentricularElectricalOffsetNs = 100_000_000 } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Invalid flutter accepted.");
        }
        var timeline = RegularPhysiologyTimeline.Start(plan);
        var before = timeline.CaptureState();
        try { timeline.AdvanceBefore(200_000_000, 3); throw new InvalidOperationException("Budget accepted."); }
        catch (PhysiologyTimelineException) { Check.That(timeline.CaptureState() == before, "budget rejection atomic"); }
        Check.That(timeline.AdvanceBefore(200_000_000, 4).Count == 4, "exact first-cycle budget excludes normal atrial mechanics");
    }
}
