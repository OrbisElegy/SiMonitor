// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

public enum PhysiologyCycleEventKind
{
    AtrialElectrical,
    VentricularElectrical,
    AtrialMechanical,
    VentricularMechanical,
    InspirationStart,
    ExpirationStart,
}

public sealed record RegularPhysiologyPlan(long EpochAnchorSimTimeNs, long HeartPeriodNs,
    long VentricularElectricalOffsetNs, long AtrialMechanicalOffsetNs,
    long VentricularMechanicalOffsetNs, long BreathPeriodNs, long InspirationDurationNs,
    long InspiratoryPauseNs = 0, long ExpiratoryPauseNs = 0);
public sealed record RegularPhysiologyState(RegularPhysiologyPlan Plan, long CursorSimTimeNs);
public readonly record struct PhysiologyCycleEvent(long SimTimeNs, PhysiologyCycleEventKind Kind, ulong CycleIndex);
public sealed class PhysiologyTimelineException(string reason, string parameter)
    : ArgumentException(reason, parameter)
{
    public string ReasonCode { get; } = reason;
}

// Local regular-cycle source schedule, not detected QRS or a wire event batch.
public sealed class RegularPhysiologyTimeline
{
    public const int MaximumEventCount = 1_000_000;
    private readonly RegularPhysiologyPlan _plan;
    private long _cursor;

    private RegularPhysiologyTimeline(RegularPhysiologyState state)
    {
        if (state is null || state.Plan is not { } plan || plan.EpochAnchorSimTimeNs < 0 ||
            plan.HeartPeriodNs <= 0 || plan.BreathPeriodNs <= 0 ||
            plan.VentricularElectricalOffsetNs <= 0 || plan.VentricularElectricalOffsetNs >= plan.HeartPeriodNs ||
            plan.AtrialMechanicalOffsetNs < 0 || plan.AtrialMechanicalOffsetNs >= plan.HeartPeriodNs ||
            plan.VentricularMechanicalOffsetNs < plan.VentricularElectricalOffsetNs ||
            plan.VentricularMechanicalOffsetNs >= plan.HeartPeriodNs ||
            plan.InspirationDurationNs <= 0 || plan.InspirationDurationNs >= plan.BreathPeriodNs ||
            plan.InspiratoryPauseNs < 0 || plan.InspiratoryPauseNs >= plan.InspirationDurationNs ||
            plan.ExpiratoryPauseNs < 0 || plan.ExpiratoryPauseNs >= plan.BreathPeriodNs - plan.InspirationDurationNs ||
            state.CursorSimTimeNs < plan.EpochAnchorSimTimeNs)
        { throw new PhysiologyTimelineException("PhysiologyTimeline.InvalidState", nameof(state)); }
        _plan = plan;
        _cursor = state.CursorSimTimeNs;
    }

    public static RegularPhysiologyTimeline Start(RegularPhysiologyPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new(new(plan, plan.EpochAnchorSimTimeNs));
    }
    public static RegularPhysiologyTimeline Restore(RegularPhysiologyState state) => new(state);
    public RegularPhysiologyState CaptureState() => new(_plan, _cursor);

    public IReadOnlyList<PhysiologyCycleEvent> AdvanceBefore(long exclusiveSimTimeNs, int maximumEvents,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (exclusiveSimTimeNs < _cursor)
        { throw new PhysiologyTimelineException("PhysiologyTimeline.TimeRegression", nameof(exclusiveSimTimeNs)); }
        if (maximumEvents is <= 0 or > MaximumEventCount)
        { throw new PhysiologyTimelineException("PhysiologyTimeline.InvalidLimit", nameof(maximumEvents)); }
        List<PhysiologyCycleEvent> events = [];
        Add(PhysiologyCycleEventKind.AtrialElectrical, _plan.HeartPeriodNs, 0);
        Add(PhysiologyCycleEventKind.VentricularElectrical, _plan.HeartPeriodNs, _plan.VentricularElectricalOffsetNs);
        Add(PhysiologyCycleEventKind.AtrialMechanical, _plan.HeartPeriodNs, _plan.AtrialMechanicalOffsetNs);
        Add(PhysiologyCycleEventKind.VentricularMechanical, _plan.HeartPeriodNs, _plan.VentricularMechanicalOffsetNs);
        Add(PhysiologyCycleEventKind.InspirationStart, _plan.BreathPeriodNs, 0);
        Add(PhysiologyCycleEventKind.ExpirationStart, _plan.BreathPeriodNs, _plan.InspirationDurationNs);
        events.Sort((left, right) => left.SimTimeNs != right.SimTimeNs
            ? left.SimTimeNs.CompareTo(right.SimTimeNs) : left.Kind.CompareTo(right.Kind));
        cancellationToken.ThrowIfCancellationRequested();
        _cursor = exclusiveSimTimeNs;
        return events.AsReadOnly();

        void Add(PhysiologyCycleEventKind kind, long period, long offset)
        {
            Int128 start = (Int128)_plan.EpochAnchorSimTimeNs + offset;
            Int128 first = _cursor <= start ? 0 : ((Int128)_cursor - start + period - 1) / period;
            Int128 time = start + first * period;
            Int128 count = time >= exclusiveSimTimeNs ? 0 : ((Int128)exclusiveSimTimeNs - 1 - time) / period + 1;
            if (count > maximumEvents - events.Count)
            { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
            for (Int128 index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                events.Add(new((long)(time + index * period), kind, (ulong)(first + index)));
            }
        }
    }
}
