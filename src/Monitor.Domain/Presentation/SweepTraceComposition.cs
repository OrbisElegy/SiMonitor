// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Presentation;

public sealed class SweepTraceCompositionException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public enum SweepTraceRegionKind
{
    RetainSourceTrace,
    NoDataBaseline,
    BackgroundEraseGap,
    PinnedHistory,
}

public sealed record SweepTraceRegion(
    ulong StartOffsetNs,
    ulong EndExclusiveOffsetNs,
    SweepTraceRegionKind Kind);

public sealed record SweepTraceCompositionSnapshot(
    string GroupId,
    ulong SweepEpoch,
    ulong PresentationClockRevision,
    ulong VisibleDurationNs,
    IReadOnlyList<SweepTraceRegion> Regions);

// Coordinates cover only SweepPlotArea. Calibration gutters and safety chrome
// are outside this time domain and cannot be erased by any returned region.
public static class SweepTraceComposition
{
    public static SweepTraceCompositionSnapshot Compose(SweepStateProjectionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var machine = SweepStateProjectionStateMachine.Restore(state);
        SweepStateProjectionState validated = machine.CaptureState();
        NoDataSweepPlan plan = validated.Plan;
        if (plan.EraseGapNs >= plan.VisibleDurationNs)
        {
            throw new SweepTraceCompositionException(
                "SweepComposition.GapCoversWindow", nameof(state));
        }

        if (validated.TemporalViewMode != TemporalViewMode.LiveSweep)
        {
            return Snapshot(plan,
                [new SweepTraceRegion(0, plan.VisibleDurationNs, SweepTraceRegionKind.PinnedHistory)]);
        }

        var elapsed = checked((UInt128)((Int128)validated.LiveSweepClockNs -
            plan.CycleOriginPresentationNs));
        ulong head = (ulong)(elapsed % plan.VisibleDurationNs);
        IReadOnlyList<SweepCoverageInterval> noData =
            machine.CaptureProjection().NoDataCoverage?.CoveredIntervals ??
            Array.Empty<SweepCoverageInterval>();
        SweepCoverageInterval[] gap = Gap(head, plan.EraseGapNs, plan.VisibleDurationNs);
        SortedSet<ulong> edges = [0, plan.VisibleDurationNs];
        foreach (SweepCoverageInterval interval in noData.Concat(gap))
        {
            edges.Add(interval.StartOffsetNs);
            edges.Add(interval.EndOffsetNs);
        }

        ulong[] boundaries = edges.ToArray();
        List<SweepTraceRegion> regions = [];
        for (int index = 0; index + 1 < boundaries.Length; index++)
        {
            ulong start = boundaries[index];
            ulong end = boundaries[index + 1];
            SweepTraceRegionKind kind = Contains(gap, start)
                ? SweepTraceRegionKind.BackgroundEraseGap
                : Contains(noData, start)
                    ? SweepTraceRegionKind.NoDataBaseline
                    : SweepTraceRegionKind.RetainSourceTrace;
            if (regions.Count > 0 && regions[^1].Kind == kind)
            {
                regions[^1] = regions[^1] with { EndExclusiveOffsetNs = end };
            }
            else
            {
                regions.Add(new SweepTraceRegion(start, end, kind));
            }
        }

        return Snapshot(plan, regions.ToArray());
    }

    private static SweepCoverageInterval[] Gap(ulong head, ulong duration, ulong visible)
    {
        if (duration == 0)
        {
            return [];
        }

        UInt128 end = (UInt128)head + duration;
        return end <= visible
            ? [new SweepCoverageInterval(head, (ulong)end)]
            : [new SweepCoverageInterval(0, (ulong)(end - visible)), new SweepCoverageInterval(head, visible)];
    }

    private static bool Contains(IEnumerable<SweepCoverageInterval> intervals, ulong offset) =>
        intervals.Any(interval => interval.StartOffsetNs <= offset && offset < interval.EndOffsetNs);

    private static SweepTraceCompositionSnapshot Snapshot(NoDataSweepPlan plan, SweepTraceRegion[] regions) =>
        new(plan.GroupId, plan.SweepEpoch, plan.PresentationClockRevision,
            plan.VisibleDurationNs, Array.AsReadOnly(regions));
}
