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
        Int128 firstGroup = Int128.MaxValue, lastGroup = -1, count = 0;
        for (int slot = 0; slot < slots.Length; slot++)
        {
            Int128 start = (Int128)plan.EpochAnchorSimTimeNs + offset + slots[slot];
            Int128 first = inclusive <= start ? 0 : ((Int128)inclusive - start + groupDuration - 1) / groupDuration;
            if (exclusive <= start + first * groupDuration) { continue; }
            Int128 last = (exclusive - 1 - start) / groupDuration;
            count += last - first + 1;
            firstGroup = Int128.Min(firstGroup, first); lastGroup = Int128.Max(lastGroup, last);
        }
        if (count > maximumEvents)
        { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
        for (Int128 group = firstGroup; group <= lastGroup; group++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int slot = 0; slot < slots.Length; slot++)
            {
                Int128 time = (Int128)plan.EpochAnchorSimTimeNs + offset + group * groupDuration + slots[slot];
                if (time >= inclusive && time < exclusive) { visitor(new((long)time, kind, (ulong)(group * slots.Length + slot))); }
            }
        }
    }

}
