// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RegularPhysiologyTimelineSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 3_750_000_000, 1_250_000_000);
    public static Specification[] All =>
    [
        new(nameof(RegularCyclesShareTimingAndIdentity), RegularCyclesShareTimingAndIdentity),
        new(nameof(TimelinePartitionsAndRecoveryAgree), TimelinePartitionsAndRecoveryAgree),
        new(nameof(TimelineFailuresPreserveCursor), TimelineFailuresPreserveCursor),
    ];

    private static void RegularCyclesShareTimingAndIdentity()
    {
        var source = RegularPhysiologyTimeline.Start(Plan);
        var events = source.AdvanceBefore(60_000_000_000, 400);
        Check.That(events.Count(item => item.Kind == PhysiologyCycleEventKind.VentricularElectrical) == 75 &&
            events.Count(item => item.Kind == PhysiologyCycleEventKind.InspirationStart) == 16,
            "75/min and 16/min must arise from exact source periods");
        foreach (var electrical in events.Where(item => item.Kind == PhysiologyCycleEventKind.VentricularElectrical))
        {
            var mechanical = events.Single(item => item.Kind == PhysiologyCycleEventKind.VentricularMechanical && item.CycleIndex == electrical.CycleIndex);
            Check.That(mechanical.SimTimeNs - electrical.SimTimeNs == 80_000_000, "shared beat identity retains explicit electromechanical delay");
        }
        Check.That(events[0].Kind == PhysiologyCycleEventKind.AtrialElectrical && events[1].Kind == PhysiologyCycleEventKind.InspirationStart,
            "simultaneous local markers have stable ordering");
    }

    private static void TimelinePartitionsAndRecoveryAgree()
    {
        var whole = RegularPhysiologyTimeline.Start(Plan);
        var expected = whole.AdvanceBefore(8_000_000_000, 100);
        var split = RegularPhysiologyTimeline.Start(Plan);
        var prefix = split.AdvanceBefore(160_000_000, 10);
        Check.That(prefix.All(item => item.SimTimeNs < 160_000_000), "deadline is exclusive");
        var restored = RegularPhysiologyTimeline.Restore(split.CaptureState());
        var suffix = restored.AdvanceBefore(8_000_000_000, 100);
        Check.That(expected.SequenceEqual(prefix.Concat(suffix)) && whole.CaptureState() == restored.CaptureState() &&
            restored.AdvanceBefore(8_000_000_000, 1).Count == 0, "partitions and recovery preserve events without replay");
    }

    private static void TimelineFailuresPreserveCursor()
    {
        var source = RegularPhysiologyTimeline.Start(Plan);
        var before = source.CaptureState();
        bool full = false;
        try { source.AdvanceBefore(60_000_000_000, 10); }
        catch (PhysiologyTimelineException exception) { full = exception.ReasonCode == "PhysiologyTimeline.EventLimitExceeded"; }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { source.AdvanceBefore(1, 10, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        bool invalid = false;
        try { RegularPhysiologyTimeline.Restore(before with { Plan = Plan with { InspirationDurationNs = Plan.BreathPeriodNs } }); }
        catch (PhysiologyTimelineException) { invalid = true; }
        Check.That(full && cancelled && invalid && source.CaptureState() == before, "failed batches and malformed plans publish no cursor");
        var edge = RegularPhysiologyTimeline.Start(Plan with { EpochAnchorSimTimeNs = long.MaxValue - 1 });
        Check.That(edge.AdvanceBefore(long.MaxValue, 10).Count == 2, "future offsets beyond int64 must not wrap into past events");
        _ = source.AdvanceBefore(1, 10);
        var advanced = source.CaptureState();
        bool regressed = false;
        try { source.AdvanceBefore(0, 10); }
        catch (PhysiologyTimelineException exception) { regressed = exception.ReasonCode == "PhysiologyTimeline.TimeRegression"; }
        bool limit = false;
        try { source.AdvanceBefore(1, 0); }
        catch (PhysiologyTimelineException exception) { limit = exception.ReasonCode == "PhysiologyTimeline.InvalidLimit"; }
        Check.That(regressed && limit && source.CaptureState() == advanced, "invalid time and capacity must preserve accepted progress");
    }
}
