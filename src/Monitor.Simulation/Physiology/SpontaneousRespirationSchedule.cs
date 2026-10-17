// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Fixed authored variation, not a stochastic patient model. Opposite paired
// offsets preserve exactly 32 nominal periods, including integer rounding.
internal static class SpontaneousRespirationSchedule
{
    internal const int Length = 32;
    private static readonly int[] PeriodOffsets = [0, 20, 50, 70, 40, 10, -20, -50, -70, -40, -10, 30, 60, 20, -30, -60];
    private static readonly int[] DepthOffsets = [0, 40, 80, 40, 0, -40, -80, -40, 0, 40, 0, -40, 0, 80, 40, 0];

    internal static int DepthPermille(ulong cycle) => 1000 + DepthOffsets[(int)(cycle % 16)] * (cycle % Length < 16 ? 1 : -1);
    internal static long PeriodNs(RegularPhysiologyPlan plan, ulong cycle) => checked(plan.BreathPeriodNs +
        (long)FixedPointMath.RoundDivideTiesToEven((Int128)plan.BreathPeriodNs * PeriodOffsets[(int)(cycle % 16)] *
            (cycle % Length < 16 ? 1 : -1), 1000));
    internal static long Scale(long valueNs, long actualDurationNs, long referenceDurationNs) =>
        checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)valueNs * actualDurationNs, referenceDurationNs));
    internal static long InspirationDurationNs(RegularPhysiologyPlan plan, ulong cycle) =>
        Math.Clamp(Scale(plan.InspirationDurationNs, PeriodNs(plan, cycle), plan.BreathPeriodNs), 1, PeriodNs(plan, cycle) - 1);
    internal static long InspiratoryPauseNs(RegularPhysiologyPlan plan, ulong cycle) =>
        Math.Min(Scale(plan.InspiratoryPauseNs, PeriodNs(plan, cycle), plan.BreathPeriodNs), InspirationDurationNs(plan, cycle) - 1);
    internal static long ExpiratoryPauseNs(RegularPhysiologyPlan plan, ulong cycle) =>
        Math.Min(Scale(plan.ExpiratoryPauseNs, PeriodNs(plan, cycle), plan.BreathPeriodNs), PeriodNs(plan, cycle) - InspirationDurationNs(plan, cycle) - 1);
    internal static Int128 StartOffsetNs(RegularPhysiologyPlan plan, ulong cycle)
    {
        Int128 result = (Int128)(cycle / Length) * Length * plan.BreathPeriodNs;
        for (ulong slot = 0; slot < cycle % Length; slot++) { result += PeriodNs(plan, slot); }
        return result;
    }
    internal static ulong FindCycle(RegularPhysiologyPlan plan, long simTimeNs)
    {
        Int128 elapsed = (Int128)simTimeNs - plan.EpochAnchorSimTimeNs;
        if (elapsed <= 0) { return 0; }
        Int128 groupDuration = (Int128)Length * plan.BreathPeriodNs;
        ulong cycle = checked((ulong)(elapsed / groupDuration) * Length);
        Int128 remainder = elapsed % groupDuration;
        while (remainder >= PeriodNs(plan, cycle)) { remainder -= PeriodNs(plan, cycle); cycle++; }
        return cycle;
    }
    internal static ulong FirstExpirationAtOrAfter(RegularPhysiologyPlan plan, long simTimeNs)
    {
        ulong cycle = FindCycle(plan, simTimeNs);
        return (Int128)plan.EpochAnchorSimTimeNs + StartOffsetNs(plan, cycle) + InspirationDurationNs(plan, cycle) < simTimeNs ? cycle + 1 : cycle;
    }
    internal static RegularPhysiologyPlan CyclePlan(RegularPhysiologyPlan plan, ulong cycle) => plan with
    {
        BreathPeriodNs = PeriodNs(plan, cycle),
        InspirationDurationNs = InspirationDurationNs(plan, cycle),
        InspiratoryPauseNs = InspiratoryPauseNs(plan, cycle),
        ExpiratoryPauseNs = ExpiratoryPauseNs(plan, cycle),
        RespiratoryPattern = RespiratoryPattern.Regular
    };
    internal static void Validate(RegularPhysiologyPlan plan)
    {
        try
        {
            for (ulong cycle = 0; cycle < Length; cycle++)
            {
                if (PeriodNs(plan, cycle) < 2) { throw new OverflowException(); }
                _ = CyclePlan(plan, cycle);
            }
        }
        catch (OverflowException) { throw new PhysiologyTimelineException("PhysiologyTimeline.InvalidSpontaneousTiming", nameof(plan)); }
    }
    internal static void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind, long inclusive, Int128 exclusive,
        int maximumEvents, Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        ulong cycle = FindCycle(plan, inclusive);
        int count = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Int128 time = (Int128)plan.EpochAnchorSimTimeNs + StartOffsetNs(plan, cycle) +
                (kind == PhysiologyCycleEventKind.ExpirationStart ? InspirationDurationNs(plan, cycle) : 0);
            if (time >= exclusive) { return; }
            if (time >= inclusive)
            {
                if (count++ >= maximumEvents)
                { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
                visitor(new((long)time, kind, cycle));
            }
            cycle++;
        }
    }
    internal static EventWaveformBand WithCycleShapes(IReadOnlyList<EventWaveformBand> cycles)
    {
        var first = cycles[0];
        long delayNs = cycles.Min(band => band.DelayNs);
        long supportNs = cycles.Max(band => checked(band.DelayNs + band.DurationNs));
        return first with
        {
            DelayNs = delayNs,
            DurationNs = supportNs - delayNs,
            PhasePoints = null,
            RespiratoryCycleShapes = Array.AsReadOnly(cycles.Select(band => new RespiratoryCycleShape(
                band.DelayNs, band.DurationNs, band.TableQ32, band.PhasePoints)).ToArray())
        };
    }
}
