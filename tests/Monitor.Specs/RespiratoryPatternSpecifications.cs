// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RespiratoryPatternSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000,
        1_000_000_000, 400_000_000, RespiratoryPattern: RespiratoryPattern.CheyneStokesIllustration);
    public static Specification[] All =>
    [
        new(nameof(IntermittentBreathsSharePausesAndGasRecovery), IntermittentBreathsSharePausesAndGasRecovery),
        new(nameof(PatternDepthAndCentralPauseShareEvents), PatternDepthAndCentralPauseShareEvents),
        new(nameof(PatternRecoveryAndCardiacIndependence), PatternRecoveryAndCardiacIndependence),
        new(nameof(PatternValidationAndEventBudgetsAreAtomic), PatternValidationAndEventBudgetsAreAtomic),
    ];
    private static void IntermittentBreathsSharePausesAndGasRecovery()
    {
        var plan = Plan with { RespiratoryPattern = RespiratoryPattern.IntermittentIllustration };
        int[] depths = [1000, 1000, 1000, 0, 0, 1000, 1000, 0, 0, 0];
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(21_000_000_000, 300);
        var resp = EventWaveformComposition.Restore(new(new RespirationPlan(-1000).CreateChannel(plan, Guid.NewGuid(), 0).Bands, events));
        var channel = new CapnogramPlan(125_000_000, 250_000_000, 200_000_000, 0, 40).CreateChannel(plan, Guid.NewGuid(), 0);
        var gas = EventWaveformComposition.Restore(new(channel.Bands, events));
        for (int slot = 0; slot < 20; slot++)
        {
            Check.That(resp.EvaluateAt(slot * 1_000_000_000L + 400_000_000) == -depths[slot % 10] * FixedPointMath.Q32One, "equal-depth groups and abrupt pauses share original slots");
            Check.That(events.Count(e => e.CycleIndex == (ulong)slot && e.Kind is PhysiologyCycleEventKind.InspirationStart or PhysiologyCycleEventKind.ExpirationStart) == (depths[slot % 10] == 0 ? 0 : 2), "no fictitious expiration during pause");
        }
        Check.That(gas.EvaluateAt(4_000_000_000) == 0 && gas.EvaluateAt(6_000_000_000) > gas.EvaluateAt(3_000_000_000), "gas clears in apnea; first returning expiration has accumulated CO2");
        double x = 1;
        for (int lap = 0; lap < 2000; lap++)
            foreach (int depth in depths) { x = (20 * x + 1) / (20 + depth / 1000.0); }
        for (int slot = 0; slot < 10; slot++)
        {
            x = (20 * x + 1) / (20 + depths[slot] / 1000.0);
            Check.That(Math.Abs(channel.Bands[0].ExpirationCycleGainsPermille![slot] - x * 1000) <= 0.500001, "bounded gas solution matches independent recurrence");
        }
        foreach (bool co2 in new[] { false, true })
        {
            var bands = co2 ? channel.Bands : new RespirationPlan(1000).CreateChannel(plan, Guid.NewGuid(), 0).Bands;
            var source = PhysiologySignalGenerator.Start(plan, co2 ? "AcqCO2_100@1" : "AcqResp125@1", 1, bands);
            source.GenerateBefore(4_600_000_000, 1000, 100);
            var restored = PhysiologySignalGenerator.Restore(source.CaptureState());
            Check.That(source.GenerateBefore(21_000_000_000, 3000, 300).SequenceEqual(restored.GenerateBefore(21_000_000_000, 3000, 300)), "intermittent pause and repeated groups restore exactly");
        }
        var regularEvents = RegularPhysiologyTimeline.Start(plan with { RespiratoryPattern = RespiratoryPattern.Regular }).AdvanceBefore(21_000_000_000, 300);
        Check.That(events.Where(e => e.Kind < PhysiologyCycleEventKind.InspirationStart).SequenceEqual(regularEvents.Where(e => e.Kind < PhysiologyCycleEventKind.InspirationStart)), "intermittent breathing preserves cardiac events");
        var late = EventWaveformComposition.Restore(new(channel.Bands, [new(400_000_000, PhysiologyCycleEventKind.ExpirationStart, ulong.MaxValue)]));
        var phase = EventWaveformComposition.Restore(new(channel.Bands, [new(400_000_000, PhysiologyCycleEventKind.ExpirationStart, ulong.MaxValue % 10)]));
        Check.That(late.EvaluateAt(1_000_000_000) == phase.EvaluateAt(1_000_000_000), "late original ordinals use bounded phase lookup");
        foreach (var invalid in new[] { plan with { RespiratoryActivity = RespiratoryActivity.EffortOnly }, plan with { RespiratoryActivity = RespiratoryActivity.Absent, ActivityAfterBreaths = 2 } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Conflicting intermittent activity accepted");
        }
        var timeline = RegularPhysiologyTimeline.Start(plan with { CardiacActivity = CardiacActivity.Absent });
        var before = timeline.CaptureState();
        try { timeline.AdvanceBefore(10_000_000_000, 9); throw new InvalidOperationException("Budget accepted"); }
        catch (PhysiologyTimelineException e) { Check.That(e.ReasonCode == "PhysiologyTimeline.EventLimitExceeded" && before == timeline.CaptureState(), "intermittent event budget failure is atomic"); }
        Check.That(timeline.AdvanceBefore(10_000_000_000, 10).Count == 10, "silent slots consume no budget");
        Check.That(timeline.AdvanceBefore(20_000_000_000, 10).Count == 10, "prefix budget remains exact across repeats");
    }
    private static void PatternDepthAndCentralPauseShareEvents()
    {
        var plan = Plan;
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(23_000_000_000, 300);
        var resp = EventWaveformComposition.Restore(new(new RespirationPlan(-1000).CreateChannel(plan, Guid.NewGuid(), 0).Bands, events));
        var co2 = EventWaveformComposition.Restore(new(new CapnogramPlan(125_000_000, 250_000_000, 200_000_000, 0, 40, TransportDelayNs: 300_000_000).CreateChannel(plan, Guid.NewGuid(), 0).Bands, events));
        int[] depths = [200, 400, 600, 800, 1000, 800, 600, 400, 200, 0, 0];
        for (int cycle = 0; cycle < 23; cycle++)
        {
            Check.That(resp.EvaluateAt(cycle * 1_000_000_000L + 400_000_000) == -depths[cycle % 11] * FixedPointMath.Q32One, "signed depth grows, falls, pauses and repeats");
            Check.That(events.Count(e => e.CycleIndex == (ulong)cycle && e.Kind is PhysiologyCycleEventKind.InspirationStart or PhysiologyCycleEventKind.ExpirationStart) == (depths[cycle % 11] == 0 ? 0 : 2), "central pause has no breath events");
        }
        Check.That(co2.EvaluateAt(9_100_000_000) > 0 && co2.EvaluateAt(9_600_000_000) == 0 &&
            co2.EvaluateAt(10_900_000_000) == 0 && co2.EvaluateAt(12_100_000_000) > 0, "gas tail completes after pause and gas resumes on original grid");
        var cvp = new CentralVenousPressurePlan(600, new(0, 120_000_000, 200), new(0, 120_000_000, 80),
            new(60_000_000, 240_000_000, 100), new(160_000_000, 320_000_000, 250), new(400_000_000, 160_000_000, 120), -100).CreateChannel(plan, Guid.NewGuid(), 0);
        var respiratoryBand = cvp.Bands.Single(b => b.Trigger == PhysiologyCycleEventKind.InspirationStart);
        var modulation = EventWaveformComposition.Restore(new([respiratoryBand], events));
        Check.That(modulation.EvaluateAt(400_000_000) * 5 == modulation.EvaluateAt(4_400_000_000) && modulation.EvaluateAt(9_400_000_000) == 0, "CVP respiratory component follows depth and pause");
    }
    private static void PatternRecoveryAndCardiacIndependence()
    {
        var plan = Plan;
        var regular = plan with { RespiratoryPattern = RespiratoryPattern.Regular };
        var a = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(23_000_000_000, 300);
        var b = RegularPhysiologyTimeline.Start(regular).AdvanceBefore(23_000_000_000, 300);
        Check.That(a.Where(e => e.Kind < PhysiologyCycleEventKind.InspirationStart).SequenceEqual(b.Where(e => e.Kind < PhysiologyCycleEventKind.InspirationStart)), "heart clocks are unaffected");
        foreach (bool gas in new[] { false, true })
        {
            var channel = gas ? new CapnogramPlan(125_000_000, 250_000_000, 200_000_000, 0, 40).CreateChannel(plan, Guid.NewGuid(), 0) : new RespirationPlan(1000, 160).CreateChannel(plan, Guid.NewGuid(), 0);
            var source = PhysiologySignalGenerator.Start(plan, gas ? "AcqCO2_100@1" : "AcqResp125@1", 1, channel.Bands);
            source.GenerateBefore(9_600_000_000, 1500, 300);
            var restored = PhysiologySignalGenerator.Restore(source.CaptureState());
            Check.That(source.GenerateBefore(23_000_000_000, 1800, 300).SequenceEqual(restored.GenerateBefore(23_000_000_000, 1800, 300)), "pause/resumption and pattern wrap restore exactly");
        }
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(11_000_000_000, 100);
        var artifact = EventWaveformComposition.Restore(new(new RespirationPlan(1000, 160).CreateChannel(plan, Guid.NewGuid(), 0).Bands, events));
        var artifactOnly = EventWaveformComposition.Restore(new(new RespirationPlan(0, 160).CreateChannel(regular, Guid.NewGuid(), 0).Bands, events));
        Check.That(artifact.EvaluateAt(9_640_000_000) == artifactOnly.EvaluateAt(9_640_000_000) && artifact.EvaluateAt(9_640_000_000) != 0, "cardiac artifact survives central pause without a breath event");
    }
    private static void PatternValidationAndEventBudgetsAreAtomic()
    {
        var source = RegularPhysiologyTimeline.Start(Plan with { CardiacActivity = CardiacActivity.Absent });
        var state = source.CaptureState();
        try { source.AdvanceBefore(11_000_000_000, 17); throw new InvalidOperationException("Budget accepted."); }
        catch (PhysiologyTimelineException e) { Check.That(e.ReasonCode == "PhysiologyTimeline.EventLimitExceeded" && source.CaptureState() == state, "budget failure atomic"); }
        Check.That(source.AdvanceBefore(11_000_000_000, 18).Count == 18, "budget counts actual events, not silent slots");
        foreach (var plan in new[] { Plan with { RespiratoryPattern = (RespiratoryPattern)99 }, Plan with { RespiratoryActivity = RespiratoryActivity.EffortOnly }, Plan with { RespiratoryActivity = RespiratoryActivity.Absent, ActivityAfterBreaths = 2 } })
        {
            try { RegularPhysiologyTimeline.Start(plan); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Invalid pattern combination accepted.");
        }
        var band = new RespirationPlan(1000).CreateChannel(Plan, Guid.NewGuid(), 0).Bands[0];
        foreach (var invalid in new[] { band with { DepthPattern = (RespiratoryPattern)99 }, band with { Trigger = PhysiologyCycleEventKind.VentricularMechanical } })
        {
            try { EventWaveformComposition.Restore(new([invalid], [])); }
            catch (EventWaveformException) { continue; }
            throw new InvalidOperationException("Invalid depth band accepted.");
        }
    }
}
