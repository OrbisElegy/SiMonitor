// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RespiratoryResumptionSpecifications
{
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Co2 = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static RegularPhysiologyPlan Plan(RespiratoryActivity activity = RespiratoryActivity.Absent, ulong? duration = 1) =>
        new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000,
            800_000_000, 800_000_000, activity, 1, duration);
    public static Specification[] All =>
    [
        new(nameof(TriggerResumeUsesOriginalCycleIndices), TriggerResumeUsesOriginalCycleIndices),
        new(nameof(RespirationResumesOnOriginalEventClock), RespirationResumesOnOriginalEventClock),
        new(nameof(ResumedSamplesAndGasResponseSurviveRecovery), ResumedSamplesAndGasResponseSurviveRecovery),
        new(nameof(ResumeValidationDefaultsAndAtomicLimits), ResumeValidationDefaultsAndAtomicLimits),
    ];

    private static void TriggerResumeUsesOriginalCycleIndices()
    {
        EventWaveformBand band = new(PhysiologyCycleEventKind.ExpirationStart, 100, 400,
            [0, FixedPointMath.Q32One, 0, -FixedPointMath.Q32One], TriggerCycleLimit: 1, TriggerCycleResume: 3);
        var events = Enumerable.Range(0, 5).Select(i => new PhysiologyCycleEvent(i * 1000, PhysiologyCycleEventKind.ExpirationStart, (ulong)i)).ToArray();
        var source = EventWaveformComposition.Restore(new([band], events));
        foreach (int cycle in Enumerable.Range(0, 5))
        {
            Check.That(source.EvaluateAt(cycle * 1000 + 200) == (cycle is 1 or 2 ? 0 : FixedPointMath.Q32One),
            "trigger exclusion is half-open and resumed contributions keep their original event indices");
        }
        Check.That(EventWaveformComposition.Restore(source.CaptureState()).EvaluateAt(3200) == FixedPointMath.Q32One,
            "optional resumption survives owned band restore");
        foreach (var invalid in new[] { band with { TriggerCycleLimit = null }, band with { TriggerCycleResume = 0 }, band with { TriggerCycleResume = 1 } })
        {
            bool rejected = false;
            try { EventWaveformComposition.Restore(new([invalid], events)); }
            catch (EventWaveformException exception) { rejected = exception.ReasonCode == "EventWaveform.InvalidState"; }
            Check.That(rejected, "resumption requires a preceding nonempty exclusion interval");
        }
    }

    private static void RespirationResumesOnOriginalEventClock()
    {
        var plan = Plan() with { EpochAnchorSimTimeNs = 100_000_000 };
        var whole = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(12_100_000_000, 100);
        var timeline = RegularPhysiologyTimeline.Start(plan);
        List<PhysiologyCycleEvent> split = [];
        foreach (long end in new[] { 4_100_000_000L, 4_100_000_001, 8_100_000_000, 8_100_000_001, 12_100_000_000 })
        {
            split.AddRange(timeline.AdvanceBefore(end, 100));
            timeline = RegularPhysiologyTimeline.Restore(timeline.CaptureState());
        }
        var normal = RegularPhysiologyTimeline.Start(plan with
        {
            RespiratoryActivity = RespiratoryActivity.Breathing,
            ActivityAfterBreaths = null,
            ActivityDurationBreaths = null
        }).AdvanceBefore(12_100_000_000, 100);
        Check.That(whole.SequenceEqual(split) && whole.SequenceEqual(normal.Where(e =>
            e.Kind < PhysiologyCycleEventKind.InspirationStart || e.CycleIndex != 1)),
            "chest events skip one cycle then resume without moving cardiac events or restarting identities");
        Check.That(whole.Any(e => e == new PhysiologyCycleEvent(8_100_000_000, PhysiologyCycleEventKind.InspirationStart, 2)),
            "the exact recovery boundary is the original epoch plus two breath periods");
    }

    private static PhysiologyWaveformGroup Group(RegularPhysiologyPlan plan) => PhysiologyWaveformGroup.Start(Resp, Co2, 1, 1, 1, 0, 80,
        [new RespirationPlan(800).CreateChannel(plan, Resp, 0),
         new CapnogramPlan(125_000_000, 250_000_000, 200_000_000, 0, 40, 4000, 600_000_000, 100_000_000).CreateChannel(plan, Co2, 0)]);
    private static short[] Samples(IReadOnlyList<byte[]> blocks, Guid id) => blocks.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)
        .Planes.Single(plane => plane.ChannelId == id)).SelectMany(plane => plane.Samples).ToArray();

    private static void ResumedSamplesAndGasResponseSurviveRecovery()
    {
        var normalPlan = Plan() with { RespiratoryActivity = RespiratoryActivity.Breathing, ActivityAfterBreaths = null, ActivityDurationBreaths = null };
        var normal = Group(normalPlan).AdvanceTo(14_000_000_000, 1750, 60, 150);
        foreach (var activity in new[] { RespiratoryActivity.Absent, RespiratoryActivity.EffortOnly })
        {
            var plan = Plan(activity);
            var whole = NativeRecoveryChecks.Verify(() => Group(plan),
                [2_190_000_000, 3_999_999_999, 4_000_000_000, 4_000_000_001,
                 4_800_000_000, 7_999_999_999, 8_000_000_000, 8_000_000_001,
                 10_600_000_000, 10_725_000_000, 10_800_000_000, 12_800_000_000, 14_000_000_000],
                1750, 60, 150, 60, $"resumed {activity}");
            short[] resp = Samples(whole, Resp);
            Check.That(resp.Skip(1000).SequenceEqual(Samples(normal, Resp).Skip(1000)) &&
                (activity == RespiratoryActivity.EffortOnly || resp.Skip(500).Take(500).All(value => value == 0)),
                "Resp resumes the normal phase after8s, with zero excursion during absent activity");
            short[] gas = Samples(whole, Co2);
            Check.That(gas.Skip(500).Take(573).All(value => value == 0) && gas[1080] > 0 &&
                gas.Skip(900).SequenceEqual(Samples(normal, Co2).Skip(900)),
                "gas remains at baseline until the resumed expiration passes its transport delay and dead space");
        }
    }

    private static void ResumeValidationDefaultsAndAtomicLimits()
    {
        foreach (var plan in new[] { Plan(duration: 0), Plan(duration: ulong.MaxValue), Plan() with { ActivityAfterBreaths = null },
            Plan() with { EpochAnchorSimTimeNs = long.MaxValue - 6_000_000_000 } })
        {
            bool rejected = false;
            try { RegularPhysiologyTimeline.Start(plan); }
            catch (PhysiologyTimelineException exception) { rejected = exception.ReasonCode == "PhysiologyTimeline.InvalidState"; }
            Check.That(rejected, "zero duration, missing start and overflowing resumption reject");
        }
        var json = JsonSerializer.SerializeToNode(Plan(duration: null))!.AsObject();
        json.Remove(nameof(RegularPhysiologyPlan.ActivityDurationBreaths));
        Check.That(json.Deserialize<RegularPhysiologyPlan>() == Plan(duration: null), "old schedules remain permanent without a resumption field");
        var timeline = RegularPhysiologyTimeline.Start(Plan());
        var before = timeline.CaptureState();
        bool limited = false;
        try { timeline.AdvanceBefore(12_000_000_000, 61); }
        catch (PhysiologyTimelineException exception) { limited = exception.ReasonCode == "PhysiologyTimeline.EventLimitExceeded"; }
        Check.That(limited && timeline.CaptureState() == before, "event-budget failure across both accepted ranges is atomic");
    }
}
