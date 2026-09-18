// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class PrematureAtrialSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PrematureAtrialTimingResetsSinusAndSharesMechanicalEvents), PrematureAtrialTimingResetsSinusAndSharesMechanicalEvents),
        new(nameof(PrematureAtrialShapeDiffersAndRecoversAcrossTheEarlyBeat), PrematureAtrialShapeDiffersAndRecoversAcrossTheEarlyBeat),
        new(nameof(PrematureAtrialIndexingAndPressureRespectBoundaries), PrematureAtrialIndexingAndPressureRespectBoundaries),
    ];

    private static void PrematureAtrialTimingResetsSinusAndSharesMechanicalEvents()
    {
        var plan = PrematureAtrialReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_200_000_000, 100);
        long[] atria = [0, 800_000_000, 1_600_000_000, 2_100_000_000, 3_100_000_000, 3_900_000_000, 4_700_000_000, 5_200_000_000];
        var electrical = events.Where(e => e.Kind is PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.PrematureAtrialElectrical).ToArray();
        Check.That(electrical.Select(e => e.SimTimeNs).SequenceEqual(atria), "true early P-prime and noncompensatory sinus reset");
        Check.That(electrical.Where(e => e.Kind == PhysiologyCycleEventKind.PrematureAtrialElectrical).Select(e => e.CycleIndex).SequenceEqual(new ulong[] { 3, 7 }), "distinct ectopic trigger with stable beat ordinal");
        Check.That(atria[3] - atria[2] == 500_000_000 && atria[4] - atria[3] == 1_000_000_000 && atria[4] - atria[2] < 1_600_000_000, "early coupling plus long but incomplete compensatory pause");
        foreach (var (kind, delay) in new[] { (PhysiologyCycleEventKind.AtrialMechanical, 80_000_000L), (PhysiologyCycleEventKind.VentricularElectrical, 160_000_000L), (PhysiologyCycleEventKind.VentricularMechanical, 240_000_000L) })
        { Check.That(events.Where(e => e.Kind == kind).Select(e => e.SimTimeNs).SequenceEqual(atria.Select(t => t + delay)), "normal and premature beats share AV and electromechanical delay"); }
        var silent = RegularPhysiologyTimeline.Start(plan with { VentricularMechanicalEnabled = false }).AdvanceBefore(6_200_000_000, 100);
        Check.That(silent.All(e => e.Kind != PhysiologyCycleEventKind.VentricularMechanical) && silent.Count(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical) == 8, "electrical PAC does not force perfusion");
    }

    private static void PrematureAtrialShapeDiffersAndRecoversAcrossTheEarlyBeat()
    {
        var plan = PrematureAtrialReference.CreatePlan();
        var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureAtrialReference.CreateElectrodes());
        var samples = source.GenerateBefore(3_100_000_000, 775, 100);
        int II(int i) => samples[i].MicrovoltValues[(int)EcgLead.II];
        Check.That(Enumerable.Range(0, 25).Max(II) > 100 && Enumerable.Range(525, 20).Min(II) < -150, "normal upright P and different negative P-prime");
        Check.That(Enumerable.Range(525, 20).Max(i => samples[i].MicrovoltValues[(int)EcgLead.AVR]) > 100, "ectopic P-prime vector projects coherently");
        Check.That(Enumerable.Range(0, 80).All(i => samples[40 + i].MicrovoltValues.SequenceEqual(samples[565 + i].MicrovoltValues)), "PAC conducts the same narrow QRS and T after160ms");
        var atrialOnly = PrematureAtrialReference.CreateElectrodes().Select(e => e with
        { Bands = e.Bands.Where(b => b.Trigger is PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.PrematureAtrialElectrical).ToArray() }).ToArray();
        var atrialSamples = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, atrialOnly).GenerateBefore(3_100_000_000, 775, 100);
        Check.That(atrialSamples.Skip(600).Take(25).All(s => s.MicrovoltValues.All(v => v == 0)), "obsolete sinus P at2400ms is not emitted while ventricular T remains independent");
        Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identities including P-prime");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureAtrialReference.CreateLeadIIBands()).GenerateBefore(3_100_000_000, 775, 100);
        Check.That(samples.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor II shares ectopic P and QRS/T");
        foreach (long boundary in new[] { 2_100_000_000L, 2_132_000_000, 2_260_000_000, 3_096_000_000 })
        {
            source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureAtrialReference.CreateElectrodes());
            source.GenerateBefore(boundary, 800, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            Check.That(source.GenerateBefore(3_600_000_000, 900, 100).Zip(restored.GenerateBefore(3_600_000_000, 900, 100))
                .All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore before/inside P-prime and across reset");
        }
    }

    private static void PrematureAtrialIndexingAndPressureRespectBoundaries()
    {
        var plan = PrematureAtrialReference.CreatePlan();
        var all = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(9_300_000_000, 100);
        for (long begin = 0; begin < 9_000_000_000; begin += 37_000_000)
        {
            long end = begin + 59_000_000;
            var expected = all.Where(e => e.SimTimeNs >= begin && e.SimTimeNs < end).ToArray();
            var source = RegularPhysiologyTimeline.Restore(new(plan, begin));
            Check.That(expected.SequenceEqual(source.AdvanceBefore(end, Math.Max(1, expected.Length))), "exact half-open sparse event budget");
        }
        var budget = RegularPhysiologyTimeline.Restore(new(plan, 2_100_000_000));
        var before = budget.CaptureState();
        try { budget.AdvanceBefore(2_340_000_001, 3); throw new InvalidOperationException("Event budget accepted."); }
        catch (PhysiologyTimelineException) { Check.That(budget.CaptureState() == before, "budget failure atomic"); }
        Check.That(budget.AdvanceBefore(2_340_000_001, 4).Count == 4, "P-prime/A-mechanical/QRS/V-mechanical boundary exact");
        var late = RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - 2_000_000_000)).AdvanceBefore(long.MaxValue, 30);
        Check.That(late.Count > 0 && late.All(e => e.SimTimeNs >= long.MaxValue - 2_000_000_000), "bounded indexed query near maximum time");
        var shiftedPlan = plan with { EpochAnchorSimTimeNs = 1_234_000_000 };
        Check.That(RegularPhysiologyTimeline.Start(shiftedPlan).AdvanceBefore(10_534_000_000, 100)
            .Select(e => e with { SimTimeNs = e.SimTimeNs - shiftedPlan.EpochAnchorSimTimeNs }).SequenceEqual(all), "epoch shift preserves schedule");
        var pressure = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000);
        var vascular = VascularPressureSource.Create(plan, pressure);
        Check.That(vascular.EvaluateAt(long.MaxValue) is > 0 and < short.MaxValue * FixedPointMath.Q32One, "indexed pressure remains bounded near maximum time");
        var cvp = new CentralVenousPressurePlan(600, new(0, 600_000_000, 200), new(0, 120_000_000, 80),
            new(60_000_000, 240_000_000, 100), new(160_000_000, 320_000_000, 250), new(400_000_000, 160_000_000, 120), -100);
        bool rejected = false;
        try { cvp.CreateChannel(plan, Guid.NewGuid(), 0); }
        catch (EventWaveformException e) when (e.ReasonCode == "Cvp.InvalidPlan") { rejected = true; }
        Check.That(rejected, "CVP a-wave support is bounded by shortest actual PP, not nominal sinus period");
        long[] mechanical = [240_000_000, 1_040_000_000, 1_840_000_000, 2_340_000_000, 3_340_000_000, 4_140_000_000, 4_940_000_000, 5_440_000_000];
        for (long time = 0; time < 6_200_000_000; time += 13_000_000)
        {
            double sourceTime = Math.Max(0, time - pressure.TransitDelayNs), tau = pressure.TimeConstantNs;
            double expected = 1000 + 7000 * Math.Exp(-sourceTime / tau);
            foreach (long beat in mechanical.Where(t => t <= sourceTime))
            {
                double age = sourceTime - beat;
                expected += 30000 * (1 - Math.Exp(-Math.Min(age, 240_000_000) / tau)) * Math.Exp(-Math.Max(0, age - 240_000_000) / tau);
            }
            Check.That(Math.Abs((double)vascular.EvaluateAt(time) / FixedPointMath.Q32One - expected) < 0.01, "pressure RC uses actual premature and resumed ejections, independent analytic reference");
        }
        foreach (var invalid in new[] { plan with { HeartPeriodNs = 700_000_000 }, plan with { VentricularConductionRatio = 2 }, plan with { MechanicalEveryCycles = 2 }, plan with { IndependentVentricularPeriodNs = 1_200_000_000 }, plan with { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 1 }, plan with { CardiacActivity = CardiacActivity.AtrialOnly }, plan with { VentricularElectricalOffsetNs = 80_000_000 } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Conflicting PAC plan accepted.");
        }
    }
}
