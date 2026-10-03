// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RespiratoryTransitionSpecifications
{
    private static readonly Guid Resp = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Co2 = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static RegularPhysiologyPlan Plan(RespiratoryActivity activity = RespiratoryActivity.Absent, ulong? after = 1) =>
        new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000, 800_000_000, 800_000_000, activity, after);
    public static Specification[] All =>
    [
        new(nameof(CycleLimitRetainsPreviouslyTriggeredTails), CycleLimitRetainsPreviouslyTriggeredTails),
        new(nameof(LastCompleteBreathKeepsStableEventIdentity), LastCompleteBreathKeepsStableEventIdentity),
        new(nameof(TransitionPreservesPrefixAndCo2Response), TransitionPreservesPrefixAndCo2Response),
        new(nameof(InvalidSchedulesAndLateFailureReject), InvalidSchedulesAndLateFailureReject),
    ];

    private static void CycleLimitRetainsPreviouslyTriggeredTails()
    {
        EventWaveformBand band = new(PhysiologyCycleEventKind.ExpirationStart, 100, 400,
            [0, FixedPointMath.Q32One, 0, -FixedPointMath.Q32One], TriggerCycleLimit: 1);
        PhysiologyCycleEvent[] events = [new(0, PhysiologyCycleEventKind.ExpirationStart, 0), new(200, PhysiologyCycleEventKind.ExpirationStart, 1)];
        var source = EventWaveformComposition.Restore(new([band], events));
        Check.That(source.EvaluateAt(200) == FixedPointMath.Q32One && source.EvaluateAt(400) == -FixedPointMath.Q32One &&
            source.EvaluateAt(600) == 0, "exclusive trigger-cycle limit suppresses future contributions, not an already active delayed tail");
        Check.That(EventWaveformComposition.Restore(source.CaptureState()).EvaluateAt(400) == source.EvaluateAt(400),
            "band cycle bound survives owned checkpoint state");
        bool rejected = false;
        try { EventWaveformComposition.Restore(new([band with { TriggerCycleLimit = 0 }], events)); }
        catch (EventWaveformException exception) { rejected = exception.ReasonCode == "EventWaveform.InvalidState"; }
        Check.That(rejected, "zero trigger-cycle limit rejects rather than silently disabling a band");
    }

    private static void LastCompleteBreathKeepsStableEventIdentity()
    {
        var normal = RegularPhysiologyTimeline.Start(Plan(RespiratoryActivity.Breathing, null)).AdvanceBefore(12_000_000_000, 100);
        foreach (var activity in new[] { RespiratoryActivity.Absent, RespiratoryActivity.EffortOnly })
        {
            var plan = Plan(activity, 2) with { EpochAnchorSimTimeNs = 100_000_000 };
            var whole = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(12_100_000_000, 100);
            var timeline = RegularPhysiologyTimeline.Start(plan);
            List<PhysiologyCycleEvent> parts = [];
            foreach (long end in new[] { 8_100_000_000L, 8_100_000_001, 12_100_000_000 })
            {
                parts.AddRange(timeline.AdvanceBefore(end, 100));
                timeline = RegularPhysiologyTimeline.Restore(timeline.CaptureState());
            }
            Check.That(whole.SequenceEqual(parts), "exact transition boundary, epoch and split recovery preserve event identity");
            Check.That(whole.Where(e => e.Kind is PhysiologyCycleEventKind.InspirationStart or PhysiologyCycleEventKind.ExpirationStart)
                .All(e => activity == RespiratoryActivity.EffortOnly || e.CycleIndex < 2), "only absent mode stops future chest cycles");
            Check.That(whole.Where(e => e.Kind < PhysiologyCycleEventKind.InspirationStart).Select(e => e with { SimTimeNs = e.SimTimeNs - 100_000_000 })
                .SequenceEqual(normal.Where(e => e.Kind < PhysiologyCycleEventKind.InspirationStart)), "cardiac events do not move at the respiratory transition");
        }
    }

    private static PhysiologyWaveformGroup Group(RespiratoryActivity activity, ulong? after) => PhysiologyWaveformGroup.Start(Resp, Co2, 1, 1, 1, 0, 60,
        [new RespirationPlan(800).CreateChannel(Plan(activity, after), Resp, 0),
         new CapnogramPlan(125_000_000, 250_000_000, 200_000_000, 0, 40, 4000, 600_000_000, 100_000_000)
             .CreateChannel(Plan(activity, after), Co2, 0)]);
    private static short[] Samples(IReadOnlyList<byte[]> blocks, Guid id) => blocks.Select(bytes => WaveformEnvelopeCodec.Decode(bytes)
        .Planes.Single(plane => plane.ChannelId == id)).SelectMany(plane => plane.Samples).ToArray();

    private static void TransitionPreservesPrefixAndCo2Response()
    {
        var normal = Group(RespiratoryActivity.Breathing, null).AdvanceTo(10_000_000_000, 1250, 40, 100);
        foreach (var activity in new[] { RespiratoryActivity.Absent, RespiratoryActivity.EffortOnly })
        {
            var changed = NativeRecoveryChecks.Verify(() => Group(activity, 1),
                [1_200_000_000, 2_190_000_000, 3_200_000_000, 3_999_999_999, 4_000_000_000,
                 4_000_000_001, 4_600_000_000, 4_800_000_000, 5_000_000_000, 8_000_000_000, 10_000_000_000],
                1250, 40, 100, 40, $"scheduled {activity}");
            Check.That(changed.Take(20).Zip(normal.Take(20)).All(pair => pair.First.SequenceEqual(pair.Second)),
                "all wire samples before the completed-breath boundary remain identical");
            short[] co2 = Samples(changed, Co2);
            Check.That(co2[490] > 0 && co2.Skip(500).All(value => value == 0),
                "last gas cycle retains pure delay and dispersion tail after4s and closes at5s");
            short[] resp = Samples(changed, Resp);
            Check.That(activity == RespiratoryActivity.Absent ? resp.Skip(500).All(value => value == 0)
                : resp.SequenceEqual(Samples(normal, Resp)), "thoracic effort either stops at the next cycle or continues independently of gas");
        }
    }

    private static void InvalidSchedulesAndLateFailureReject()
    {
        foreach (var plan in new[] { Plan(after: 0), Plan(after: ulong.MaxValue), Plan(RespiratoryActivity.Breathing),
            Plan() with { EpochAnchorSimTimeNs = long.MaxValue - 1 } })
        {
            bool rejected = false;
            try { RegularPhysiologyTimeline.Start(plan); }
            catch (PhysiologyTimelineException exception) { rejected = exception.ReasonCode == "PhysiologyTimeline.InvalidState"; }
            Check.That(rejected, "zero/overflowing schedule and a normal target reject before publication");
        }
        var json = JsonSerializer.SerializeToNode(Plan(after: null))!.AsObject();
        json.Remove(nameof(RegularPhysiologyPlan.ActivityAfterBreaths));
        Check.That(json.Deserialize<RegularPhysiologyPlan>() == Plan(after: null), "old source plans retain immediate activity");
        var group = Group(RespiratoryActivity.Absent, 1);
        string before = JsonSerializer.Serialize(group.CaptureState());
        bool limited = false;
        try { group.AdvanceTo(10_000_000_000, 1250, 1, 100); }
        catch (PhysiologyWaveformGroupException exception) { limited = exception.ReasonCode == "PhysiologyGroup.BlockLimitExceeded"; }
        Check.That(limited && JsonSerializer.Serialize(group.CaptureState()) == before, "failed publication cannot partially cross the transition");
    }
}
