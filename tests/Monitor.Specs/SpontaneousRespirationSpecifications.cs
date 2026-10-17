// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class SpontaneousRespirationSpecifications
{
    private const long Q = FixedPointMath.Q32One;
    private static readonly Guid Channel = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000,
        240_000_000, 3_750_000_000, 1_500_000_000, RespiratoryPattern: RespiratoryPattern.SpontaneousIllustration);
    public static Specification[] All =>
    [
        new(nameof(SpontaneousSchedulePreservesNominalMeanAndIndexedBoundaries), SpontaneousSchedulePreservesNominalMeanAndIndexedBoundaries),
        new(nameof(SpontaneousRespirationAndVenousEffortShareExpirationEvents), SpontaneousRespirationAndVenousEffortShareExpirationEvents),
        new(nameof(SpontaneousShapesOwnInputsAndRejectUnboundedSlots), SpontaneousShapesOwnInputsAndRejectUnboundedSlots),
        new(nameof(SpontaneousGeneratorRestoresInsideAVariableBreath), SpontaneousGeneratorRestoresInsideAVariableBreath),
        new(nameof(SpontaneousActivationUsesTheContinuedEpoch), SpontaneousActivationUsesTheContinuedEpoch),
    ];

    private static void SpontaneousSchedulePreservesNominalMeanAndIndexedBoundaries()
    {
        var plan = Plan with { CardiacActivity = CardiacActivity.Absent };
        long endNs = plan.BreathPeriodNs * SpontaneousRespirationSchedule.Length;
        var timeline = RegularPhysiologyTimeline.Start(plan);
        var before = timeline.CaptureState();
        bool rejected = false;
        try { timeline.AdvanceBefore(endNs, 63); }
        catch (PhysiologyTimelineException) { rejected = true; }
        Check.That(rejected && timeline.CaptureState() == before, "event budget failure does not consume the natural schedule");
        var events = timeline.AdvanceBefore(endNs, 64);
        Check.That(events.Count == 64 && SpontaneousRespirationSchedule.StartOffsetNs(plan, 32) == endNs,
            "32 complete breaths retain the exact nominal mean and omit no inspiration or expiration");
        var periods = new HashSet<long>();
        var depths = new HashSet<int>();
        for (ulong cycle = 0; cycle < 32; cycle++)
        {
            long start = (long)SpontaneousRespirationSchedule.StartOffsetNs(plan, cycle);
            long period = SpontaneousRespirationSchedule.PeriodNs(plan, cycle);
            long inspiration = SpontaneousRespirationSchedule.InspirationDurationNs(plan, cycle);
            periods.Add(period);
            depths.Add(SpontaneousRespirationSchedule.DepthPermille(cycle));
            Check.That(period >= plan.BreathPeriodNs * 93 / 100 && period <= plan.BreathPeriodNs * 107 / 100 &&
                events.Contains(new(start, PhysiologyCycleEventKind.InspirationStart, cycle)) &&
                events.Contains(new(start + inspiration, PhysiologyCycleEventKind.ExpirationStart, cycle)),
                "bounded correlated duration changes share the original breath ordinal");
            Check.That(SpontaneousRespirationSchedule.FindCycle(plan, start) == cycle &&
                SpontaneousRespirationSchedule.FindCycle(plan, start + period - 1) == cycle &&
                SpontaneousRespirationSchedule.FindCycle(plan, start + period) == cycle + 1,
                "indexed lookups own the exact half-open cycle boundary");
        }
        Check.That(periods.Count > 5 && depths.SetEquals([920, 960, 1000, 1040, 1080]),
            "natural breaths vary in both duration and depth without pathologic pause slots");
        long late = 1_000_000_000_000_000;
        ulong lateCycle = SpontaneousRespirationSchedule.FindCycle(plan, late);
        Check.That(SpontaneousRespirationSchedule.StartOffsetNs(plan, lateCycle) <= late &&
            SpontaneousRespirationSchedule.StartOffsetNs(plan, lateCycle + 1) > late,
            "far-future indexed lookup needs no replay from the epoch");
    }

    private static void SpontaneousRespirationAndVenousEffortShareExpirationEvents()
    {
        var plan = Plan with { InspiratoryPauseNs = 100_000_000, ExpiratoryPauseNs = 120_000_000 };
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(120_000_000_000, 1000);
        var respiration = EventWaveformComposition.Restore(new(new RespirationPlan(-1000).CreateChannel(plan, Channel, 0).Bands, events));
        var venous = new CentralVenousPressurePlan(0, new(0, 100_000_000, 0), new(0, 100_000_000, 0),
            new(0, 100_000_000, 0), new(0, 100_000_000, 0), new(0, 100_000_000, 0), -100).CreateChannel(plan, Channel, 0);
        var cvp = EventWaveformComposition.Restore(new(venous.Bands, events));
        foreach (var expiration in events.Where(item => item.Kind == PhysiologyCycleEventKind.ExpirationStart))
        {
            int depth = SpontaneousRespirationSchedule.DepthPermille(expiration.CycleIndex);
            Check.That(respiration.EvaluateAt(expiration.SimTimeNs) == -depth * Q &&
                cvp.EvaluateAt(expiration.SimTimeNs) == -depth * Q / 10,
                "signed RESP and CVP excursion peaks follow the shared variable expiration event");
            long nextStart = (long)SpontaneousRespirationSchedule.StartOffsetNs(plan, expiration.CycleIndex + 1);
            Check.That(respiration.EvaluateAt(nextStart - 1) == 0,
                "each scaled expiratory pause returns to baseline before the following breath");
        }
    }

    private static void SpontaneousShapesOwnInputsAndRejectUnboundedSlots()
    {
        var band = new RespirationPlan(1000).CreateChannel(Plan, Channel, 0).Bands[0];
        var shapes = band.RespiratoryCycleShapes!.ToArray();
        long[] table = shapes[1].TableQ32.ToArray();
        var phases = shapes[1].PhasePoints!.ToArray();
        shapes[1] = shapes[1] with { TableQ32 = table, PhasePoints = phases };
        var originalShape = band.RespiratoryCycleShapes![1];
        PhysiologyCycleEvent[] events = [new(0, PhysiologyCycleEventKind.InspirationStart, 1)];
        var owned = EventWaveformComposition.Restore(new([band with { RespiratoryCycleShapes = shapes }], events));
        long peakAt = SpontaneousRespirationSchedule.InspirationDurationNs(Plan, 1);
        long expected = owned.EvaluateAt(peakAt);
        Array.Clear(table);
        phases[1] = new(1, 1);
        shapes[1] = shapes[0];
        Check.That(owned.EvaluateAt(peakAt) == expected && expected == 1040 * Q,
            "accepted cycle tables and phase maps own every nested input");
        foreach (var invalid in new[]
        {
            band with { RespiratoryCycleShapes = band.RespiratoryCycleShapes!.Take(31).ToArray() },
            band with { RespiratoryCycleShapes = Enumerable.Repeat(originalShape with { DurationNs = band.DurationNs + 1 }, 32).ToArray() },
            band with { RespiratoryCycleShapes = Enumerable.Repeat(originalShape with { DelayNs = -1 }, 32).ToArray() },
        })
        {
            bool rejected = false;
            try { EventWaveformComposition.Restore(new([invalid], [])); }
            catch (EventWaveformException) { rejected = true; }
            Check.That(rejected, "invalid slot count, support or delay is rejected before publication");
        }
        bool amplitudeRejected = false;
        try { new RespirationPlan(short.MaxValue).CreateChannel(Plan, Channel, 0); }
        catch (EventWaveformException) { amplitudeRejected = true; }
        Check.That(amplitudeRejected, "1080-permille spontaneous depth is included in the signed amplitude budget");

        var uniform = new RespiratoryCycleShape(0, 80, [0, Q, 0, 0]);
        var mapped = new EventWaveformBand(PhysiologyCycleEventKind.InspirationStart, 0, 100, [0, Q, 0, 0],
            [new(0, 0), new(20, 2), new(100, 4)])
        { RespiratoryCycleShapes = Enumerable.Repeat(uniform, 32).ToArray() };
        var independentPhase = EventWaveformComposition.Restore(new([mapped], [new(0, PhysiologyCycleEventKind.InspirationStart, 0)]));
        var restoredPhase = EventWaveformComposition.Restore(independentPhase.CaptureState());
        Check.That(independentPhase.EvaluateAt(20) == Q && restoredPhase.EvaluateAt(20) == Q,
            "a complete cycle with uniform phase does not inherit the outer band's nonuniform phase map");
    }

    private static void SpontaneousGeneratorRestoresInsideAVariableBreath()
    {
        var channel = new RespirationPlan(1000, 100).CreateChannel(Plan, Channel, 0);
        var source = PhysiologySignalGenerator.Start(Plan, "AcqResp125@1", 1, channel.Bands);
        source.GenerateBefore(5_113_000_000, 1000, 100);
        string serialized = JsonSerializer.Serialize(source.CaptureState());
        var restored = PhysiologySignalGenerator.Restore(JsonSerializer.Deserialize<PhysiologySignalState>(serialized)!);
        var expected = source.GenerateBefore(17_123_000_000, 2000, 100);
        var actual = restored.GenerateBefore(17_123_000_000, 2000, 100);
        Check.That(expected.SequenceEqual(actual), "irregular phase, depth and cardiac artifact survive a mid-breath JSON checkpoint");
    }

    private static void SpontaneousActivationUsesTheContinuedEpoch()
    {
        var currentPlan = Plan with { EpochAnchorSimTimeNs = 1_000_000_000, BreathPeriodNs = 1_000_000_000, InspirationDurationNs = 400_000_000 };
        var gas = new CapnogramPlan(50_000_000, 150_000_000, 100_000_000, 0, 40);
        var current = PhysiologySignalGenerator.Start(currentPlan, "AcqCO2_100@1", 1,
            gas.CreateChannel(currentPlan, Channel, 0).Bands);
        current.GenerateBefore(1_200_000_000, 100, 100);
        var definitionPlan = currentPlan with { EpochAnchorSimTimeNs = 0 };
        var washout = gas with { Illustration = CapnogramIllustration.EsophagealIntubation };
        var definition = PhysiologySignalGenerator.Start(definitionPlan, "AcqCO2_100@1", 1,
            washout.CreateChannel(definitionPlan, Channel, 0).Bands);
        var continued = current.ContinueWith(definition.CaptureState());
        Check.That(continued.CaptureState().Bands.All(band => band.ExpirationCycleOrigin == 0) &&
            continued.EvaluateAt(1_800_000_000) > 0,
            "activation selects the first upcoming expiration on the retained epoch rather than the definition's epoch");
    }
}
