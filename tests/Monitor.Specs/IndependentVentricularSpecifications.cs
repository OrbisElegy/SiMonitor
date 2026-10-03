// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class IndependentVentricularSpecifications
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000, IndependentVentricularPeriodNs: 1_100_000_000);
    public static Specification[] All =>
    [
        new(nameof(IndependentVentriclesRetainAtrialAndBreathClocks), IndependentVentriclesRetainAtrialAndBreathClocks),
        new(nameof(IndependentNativeGroupsRestoreByteExactly), IndependentNativeGroupsRestoreByteExactly),
        new(nameof(IndependentPressureUsesOriginalMechanicalSchedule), IndependentPressureUsesOriginalMechanicalSchedule),
        new(nameof(IndependentPeriodDefaultsBoundsAndAtomicFailure), IndependentPeriodDefaultsBoundsAndAtomicFailure),
    ];

    private static void IndependentVentriclesRetainAtrialAndBreathClocks()
    {
        var normal = RegularPhysiologyTimeline.Start(Plan with { IndependentVentricularPeriodNs = null }).AdvanceBefore(8_000_000_000, 100);
        var actual = RegularPhysiologyTimeline.Start(Plan).AdvanceBefore(8_000_000_000, 100);
        bool Other(PhysiologyCycleEvent e) => e.Kind is not (PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical);
        Check.That(actual.Where(Other).SequenceEqual(normal.Where(Other)), "independent ventricular clock does not rebase atrial or respiratory events");
        foreach (var kind in new[] { PhysiologyCycleEventKind.VentricularElectrical, PhysiologyCycleEventKind.VentricularMechanical })
        {
            long offset = kind == PhysiologyCycleEventKind.VentricularElectrical ? 160_000_000 : 240_000_000;
            var expected = Enumerable.Range(0, 8).Select(i => new PhysiologyCycleEvent(i * 1_100_000_000L + offset, kind, (ulong)i));
            Check.That(actual.Where(e => e.Kind == kind).SequenceEqual(expected), "noninteger ventricular periods retain explicit offsets and original indices");
        }
        var phases = actual.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).Select(e => e.SimTimeNs % Plan.HeartPeriodNs);
        Check.That(phases.Distinct().Count() == 8, "P and QRS phase drifts instead of being rounded to a conduction multiple");
    }

    private static PhysiologyWaveformGroup Group() => PhysiologyWaveformGroup.Start(Id, Id, 1, 1, 1, 0, 50,
        [new(Plan, new(Id, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 10, 0)]);

    private static void IndependentNativeGroupsRestoreByteExactly()
    {
        var expected = Group().AdvanceTo(8_000_000_000, 2000, 40, 100);
        var group = Group();
        List<byte[]> actual = [];
        for (int step = 1; step <= 500; step++)
        {
            actual.AddRange(group.AdvanceTo(step * 16_000_000L, 4, 1, 100));
            if (step is 1 or 9 or 10 or 11 or 12 or 13 or 24 or 25 or 26 or 74 or 75 or 76 or 78 or 79 or 124 or 125 or 126 or 499)
            { group = PhysiologyWaveformGroup.Restore(group.CaptureState()); }
        }
        Check.That(expected.Count > 0 && expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            "nonaligned independent cycles preserve native ECG and pending samples across event and publication boundary recovery");
    }

    private static void IndependentPressureUsesOriginalMechanicalSchedule()
    {
        var plan = Plan with
        {
            VentricularMechanicalEnabled = false,
            MechanicalAfterCycles = 2,
            MechanicalDurationCycles = 3,
            MechanicalEveryCycles = 2,
        };
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(9_000_000_000, 100);
        Check.That(events.Where(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).Select(e => e.CycleIndex).SequenceEqual(new ulong[] { 0, 6 }),
            "mechanical recovery and stride count independent ventricular periods");
        var pressure = new VascularPressurePlan(80_000_000, 240_000_000, 1_500_000_000, 10_000, 1200, 26_000);
        var actual = VascularPressureSource.Create(plan, pressure);
        var expected = VascularPressureSource.Create(plan with { IndependentVentricularPeriodNs = null, HeartPeriodNs = 1_100_000_000 }, pressure);
        for (long time = 0; time <= 9_000_000_000; time += 8_000_000)
        { Check.That(actual.EvaluateAt(time) == expected.EvaluateAt(time), "indexed RC pressure follows the same independent mechanical clock"); }
    }

    private static void IndependentPeriodDefaultsBoundsAndAtomicFailure()
    {
        var legacy = Plan with { IndependentVentricularPeriodNs = null };
        var json = JsonSerializer.SerializeToNode(legacy)!.AsObject();
        json.Remove(nameof(RegularPhysiologyPlan.IndependentVentricularPeriodNs));
        Check.That(json.Deserialize<RegularPhysiologyPlan>() == legacy, "old checkpoints retain integer-ratio conduction");
        foreach (var invalid in new[] { Plan with { IndependentVentricularPeriodNs = 0 }, Plan with { IndependentVentricularPeriodNs = 799_999_999 },
            Plan with { VentricularConductionRatio = 2 }, Plan with { IndependentVentricularPeriodNs = long.MaxValue, VentricularMechanicalEnabled = false, MechanicalAfterCycles = 2 } })
        {
            bool rejected = false;
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException e) { rejected = e.ReasonCode == "PhysiologyTimeline.InvalidState"; }
            Check.That(rejected, "invalid, ambiguous or overflowing independent schedules reject");
        }
        _ = RegularPhysiologyTimeline.Start(Plan with { IndependentVentricularPeriodNs = 800_000_000 });
        _ = RegularPhysiologyTimeline.Start(Plan with { IndependentVentricularPeriodNs = long.MaxValue });
        var group = Group();
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(8_000_000_000, 2000, 1, 100); }
        catch (PhysiologyWaveformGroupException) { limited = true; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "failed independent publication retains source and pending state");
    }
}
