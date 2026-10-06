// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Internal authored schedules have sorted, distinct slots within a positive
// group duration. Count the requested interval before visiting any event.
internal static class IndexedCardiacSchedule
{
    internal static void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind,
        long offset, long inclusive, Int128 exclusive, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, long groupDuration, ReadOnlySpan<long> slots,
        CancellationToken cancellationToken)
    {
        Int128 origin = (Int128)plan.EpochAnchorSimTimeNs + offset;
        Int128 first = LowerBound((Int128)inclusive - origin, groupDuration, slots);
        Int128 last = LowerBound(exclusive - origin, groupDuration, slots);
        if (last - first > maximumEvents)
        { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
        for (Int128 index = first; index < last; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Int128 time = origin + index / slots.Length * groupDuration + slots[(int)(index % slots.Length)];
            visitor(new((long)time, kind, (ulong)index));
        }
    }

    private static Int128 LowerBound(Int128 relativeNs, long groupDuration, ReadOnlySpan<long> slots)
    {
        if (relativeNs <= 0) { return 0; }
        Int128 group = relativeNs / groupDuration;
        long phase = (long)(relativeNs % groupDuration);
        int left = 0, right = slots.Length;
        while (left < right)
        {
            int middle = (left + right) / 2;
            if (slots[middle] < phase) { left = middle + 1; }
            else { right = middle; }
        }
        return group * slots.Length + left;
    }
}
