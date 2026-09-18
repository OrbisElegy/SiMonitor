// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AberrantPrematureAtrialSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(AberrantPacChangesOnlySelectedVentricularMorphology), AberrantPacChangesOnlySelectedVentricularMorphology),
        new(nameof(AberrantPacRecoveryAndMechanicsPreserveOriginalBeats), AberrantPacRecoveryAndMechanicsPreserveOriginalBeats),
        new(nameof(VentricularMorphologyMaskIsBoundedAndComposesWithGates), VentricularMorphologyMaskIsBoundedAndComposesWithGates),
    ];

    private static void AberrantPacChangesOnlySelectedVentricularMorphology()
    {
        var plan = PrematureAtrialReference.CreateAberrantPlan();
        var mixed = Generate(PrematureAtrialReference.CreateAberrantElectrodes());
        var normal = Generate(PrematureAtrialReference.CreateElectrodes());
        var right = Generate(RightBundleBlockReference.CreateElectrodes());
        for (int i = 0; i < mixed.Count; i++)
        {
            long phase = i * 4_000_000L % 3_100_000_000;
            var expected = phase is >= 2_260_000_000 and < 2_660_000_000 ? right[i] : normal[i];
            Check.That(mixed[i].MicrovoltValues.SequenceEqual(expected.MicrovoltValues), "only premature ventricular QRS/ST/T uses RBBB; P-prime and sinus beats unchanged");
        }
        Check.That(Enumerable.Range(585, 15).Any(i => Math.Abs(mixed[i].MicrovoltValues[6] - normal[i].MicrovoltValues[6]) > 300), "V1 late R-prime differs after normal QRS has finished");
        Check.That(mixed.Skip(630).Take(30).Min(s => s.MicrovoltValues[6]) < -100, "aberrant V1 retains secondary negative T");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureAtrialReference.CreateAberrantLeadIIBands()).GenerateBefore(6_200_000_000, 1550, 100);
        Check.That(mixed.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor and projected II share both morphologies");
        Check.That(mixed.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity preserved through morphology change");
        IReadOnlyList<ElectrodeSignalSample> Generate(IReadOnlyList<ElectrodeWaveformPlan> electrodes) =>
            ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(6_200_000_000, 1550, 100);
    }

    private static void AberrantPacRecoveryAndMechanicsPreserveOriginalBeats()
    {
        var plan = PrematureAtrialReference.CreateAberrantPlan();
        var normal = PrematureAtrialReference.CreatePlan();
        Check.That(RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_200_000_000, 100).SequenceEqual(
            RegularPhysiologyTimeline.Start(normal).AdvanceBefore(6_200_000_000, 100)), "no new event or mechanical timing introduced by aberrancy");
        var pressure = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000);
        var a = VascularPressureSource.Create(plan, pressure); var b = VascularPressureSource.Create(normal, pressure);
        for (long time = 0; time < 6_200_000_000; time += 13_000_000)
        { Check.That(a.EvaluateAt(time) == b.EvaluateAt(time), "no uncalibrated change in ejection or pressure"); }
        foreach (long boundary in new[] { 2_288_000_000L, 2_348_000_000, 2_592_000_000, 3_096_000_000 })
        {
            var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, PrematureAtrialReference.CreateAberrantElectrodes());
            source.GenerateBefore(boundary, 800, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            Check.That(source.GenerateBefore(6_200_000_000, 1550, 100).Zip(restored.GenerateBefore(6_200_000_000, 1550, 100))
                .All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore keeps original beat mask through wide QRS/T and group boundary");
        }
        var late = RegularPhysiologyTimeline.Restore(new(plan, long.MaxValue - 2_000_000_000)).AdvanceBefore(long.MaxValue, 30);
        Check.That(late.Any(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical) && a.EvaluateAt(long.MaxValue) == b.EvaluateAt(long.MaxValue), "late indexed query remains bounded and preserves pressure");
    }

    private static void VentricularMorphologyMaskIsBoundedAndComposesWithGates()
    {
        long q = FixedPointMath.Q32One;
        EventWaveformBand narrow = new(PhysiologyCycleEventKind.VentricularElectrical, 0, 40, [0, q, q, 0], VentricularCycles: new(4, 7));
        var wide = narrow with { TableQ32 = new long[] { 0, 2 * q, 2 * q, 0 }, VentricularCycles = new(4, 8) };
        foreach (ulong slot in new ulong[] { 0, 1, 2, 3, 4, 7, ulong.MaxValue })
        {
            var source = EventWaveformComposition.Restore(new([narrow, wide], [new(0, PhysiologyCycleEventKind.VentricularElectrical, slot)]));
            Check.That(source.EvaluateAt(10) == (slot % 4 == 3 ? 2 * q : q), "mutually exclusive bands use original ordinal, including ulong boundary");
        }
        Check.That(Value(narrow with { VentricularCycles = new(64, 1UL << 63) }, ulong.MaxValue) == q &&
            Value(narrow with { VentricularCycles = new(1, 1) }, ulong.MaxValue) == q, "one and64 slot masks accepted");
        var gated = wide with { TriggerCycleLimit = 4, TriggerCycleResume = 8 };
        Check.That(Value(gated, 3) == 2 * q && Value(gated, 7) == 0 && Value(gated, 11) == 2 * q && Value(gated, 8) == 0, "mask composes with limit and resume");
        foreach (var invalid in new[] { narrow with { VentricularCycles = new(0, 1) }, narrow with { VentricularCycles = new(65, 1) }, narrow with { VentricularCycles = new(4, 0) }, narrow with { VentricularCycles = new(4, 16) }, narrow with { Trigger = PhysiologyCycleEventKind.AtrialElectrical } })
        {
            try { Value(invalid, 0); }
            catch (EventWaveformException) { continue; }
            throw new InvalidOperationException("Invalid ventricular mask accepted.");
        }
        static long Value(EventWaveformBand band, ulong slot) => EventWaveformComposition.Restore(new([band], [new(0, band.Trigger, slot)])).EvaluateAt(10);
    }
}
