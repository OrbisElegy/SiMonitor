// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VentricularPhaseSpecifications
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static RegularPhysiologyPlan Plan(long phase) => new(0, 800_000_000, phase,
        80_000_000, phase + 80_000_000, 4_000_000_000, 2_000_000_000, IndependentVentricularPeriodNs: 1_100_000_000);
    public static Specification[] All =>
    [
        new(nameof(IndependentPhaseMovesOnlyVentricularEvents), IndependentPhaseMovesOnlyVentricularEvents),
        new(nameof(LatePhasePreservesDelayedWaveTailsAndRecovery), LatePhasePreservesDelayedWaveTailsAndRecovery),
        new(nameof(IndependentPhaseMovesPressureArrivalWithoutInventedInputs), IndependentPhaseMovesPressureArrivalWithoutInventedInputs),
        new(nameof(IndependentPhaseBoundsAndAtomicFailure), IndependentPhaseBoundsAndAtomicFailure),
    ];

    private static void IndependentPhaseMovesOnlyVentricularEvents()
    {
        var reference = RegularPhysiologyTimeline.Start(Plan(0)).AdvanceBefore(3_300_000_000, 100);
        foreach (long phase in new[] { 0L, 900_000_000, 1_019_999_999 })
        {
            var events = RegularPhysiologyTimeline.Start(Plan(phase)).AdvanceBefore(3_300_000_000, 100);
            var expected = reference.Select(e => e.Kind is PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical
                ? e with { SimTimeNs = e.SimTimeNs + phase } : e).OrderBy(e => e.SimTimeNs).ThenBy(e => e.Kind);
            Check.That(events.SequenceEqual(expected), "ventricular events shift together without moving atrial/breath grids or cycle indices");
        }
    }

    private static PhysiologyWaveformGroup Group() => PhysiologyWaveformGroup.Start(Id, Id, 1, 1, 1, 0, 50,
        [new(Plan(900_000_000), new(Id, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 10, 0)]);

    private static void LatePhasePreservesDelayedWaveTailsAndRecovery()
    {
        var group = Group();
        var expected = Group().AdvanceTo(4_400_000_000, 1100, 22, 100);
        List<byte[]> actual = [];
        for (int step = 1; step <= 275; step++)
        {
            actual.AddRange(group.AdvanceTo(step * 16_000_000L, 4, 1, 100));
            group = PhysiologyWaveformGroup.Restore(group.CaptureState());
        }
        Check.That(expected.Count > 0 && expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            "late QRS and repolarization across the nominal cycle boundary survive pending-sample recovery");
        var composition = EventWaveformComposition.Restore(new(TextbookEcgReference.CreateBands(),
            RegularPhysiologyTimeline.Start(Plan(900_000_000)).AdvanceBefore(1_300_000_000, 100)));
        Check.That(composition.EvaluateAt(1_180_000_000) != 0, "the late ventricular T tail is not clipped at its original cycle boundary");
    }

    private static void IndependentPhaseMovesPressureArrivalWithoutInventedInputs()
    {
        var pressure = new VascularPressurePlan(80_000_000, 240_000_000, 1_500_000_000, 1200, 1200, 26_000);
        var reference = VascularPressureSource.Create(Plan(0), pressure);
        var shifted = VascularPressureSource.Create(Plan(900_000_000), pressure);
        for (long time = 900_000_000; time <= 4_000_000_000; time += 8_000_000)
        { Check.That(shifted.EvaluateAt(time) == reference.EvaluateAt(time - 900_000_000), "pressure follows the same shifted ejection and transit with an equilibrium initial state"); }
        Check.That(shifted.EvaluateAt(1_060_000_000) == shifted.EvaluateAt(0) && shifted.EvaluateAt(1_068_000_000) > shifted.EvaluateAt(0),
            "pressure stays at the declared baseline until mechanical onset plus transit");
    }

    private static void IndependentPhaseBoundsAndAtomicFailure()
    {
        foreach (var invalid in new[] { Plan(-1), Plan(1_020_000_000), Plan(0) with { IndependentVentricularPeriodNs = null },
            Plan(900_000_000) with { IndependentVentricularPeriodNs = null }, Plan(900_000_000) with { AtrialMechanicalOffsetNs = 800_000_000 } })
        {
            bool rejected = false;
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException e) { rejected = e.ReasonCode == "PhysiologyTimeline.InvalidState"; }
            Check.That(rejected, "independent bounds do not weaken legacy or atrial timing validation");
        }
        var timeline = RegularPhysiologyTimeline.Start(Plan(0));
        string before = JsonSerializer.Serialize(timeline.CaptureState());
        bool limited = false;
        try { timeline.AdvanceBefore(3_300_000_000, 1); }
        catch (PhysiologyTimelineException) { limited = true; }
        Check.That(limited && JsonSerializer.Serialize(timeline.CaptureState()) == before, "same-time event budget failure is atomic");
    }
}
