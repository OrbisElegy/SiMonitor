// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// An authored conducted beat replaces slot11,45ms before its VT deadline.
// The next ectopic deadline remains on the original grid; no second ejection.
internal static class VtCaptureSchedule
{
    internal const int CaptureSlot = 11;
    private const long PeriodNs = 375_000_000;
    private const long AdvanceNs = 45_000_000;

    internal static long AdvanceForCycleNs(ulong index) => index % 32 == CaptureSlot ? AdvanceNs : 0;

    internal static void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind,
        long offset, long inclusive, Int128 exclusive, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, ulong? cycleLimit, ulong? cycleResume,
        int cycleStride, CancellationToken cancellationToken)
    {
        Int128 start = (Int128)plan.EpochAnchorSimTimeNs + offset;
        Int128 Time(Int128 index) => start + index * PeriodNs - AdvanceForCycleNs(checked((ulong)index));
        // Invert the nominal grid with one bounded correction at either end.
        Int128 first = inclusive <= start ? 0 : ((Int128)inclusive - start + PeriodNs - 1) / PeriodNs;
        if (Time(first) < inclusive) { first++; }
        Int128 end = exclusive + AdvanceNs <= start ? 0 : (exclusive + AdvanceNs - start + PeriodNs - 1) / PeriodNs;
        if (end > first && Time(end - 1) >= exclusive) { end--; }
        int remaining = maximumEvents;
        if (cycleLimit is { } limit)
        {
            Append(first, Int128.Min(end, limit));
            if (cycleResume is { } resume) { Append(Int128.Max(first, resume), end); }
        }
        else { Append(first, end); }

        void Append(Int128 begin, Int128 finish)
        {
            begin = (begin + cycleStride - 1) / cycleStride * cycleStride;
            if (begin >= finish) { return; }
            Int128 count = (finish - 1 - begin) / cycleStride + 1;
            if (count > remaining)
            { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
            remaining -= (int)count;
            for (Int128 index = begin; index < finish; index += cycleStride)
            {
                cancellationToken.ThrowIfCancellationRequested();
                visitor(new(checked((long)Time(index)), kind, checked((ulong)index)));
            }
        }
    }
}
