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

// Local source activity, not a detected apnea classification or device fault.
public enum RespiratoryActivity { Breathing, EffortOnly, Absent }

// Optional count starts with normal breathing and applies the target activity
// after that many complete source cycles. Null applies the target from epoch.
// Optional duration restores normal activity on the original cycle grid.
// HeartPeriodNs is the atrial period; ventricular events use its conduction
// multiple while preserving their explicit electrical/mechanical offsets.
public sealed record RegularPhysiologyPlan(long EpochAnchorSimTimeNs, long HeartPeriodNs,
    long VentricularElectricalOffsetNs, long AtrialMechanicalOffsetNs,
    long VentricularMechanicalOffsetNs, long BreathPeriodNs, long InspirationDurationNs,
    long InspiratoryPauseNs = 0, long ExpiratoryPauseNs = 0,
    RespiratoryActivity RespiratoryActivity = RespiratoryActivity.Breathing, ulong? ActivityAfterBreaths = null, ulong? ActivityDurationBreaths = null,
    int VentricularConductionRatio = 1);
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
            plan.VentricularConductionRatio < 1 || (Int128)plan.HeartPeriodNs * plan.VentricularConductionRatio > long.MaxValue ||
            plan.VentricularElectricalOffsetNs <= 0 || plan.VentricularElectricalOffsetNs >= plan.HeartPeriodNs ||
            plan.AtrialMechanicalOffsetNs < 0 || plan.AtrialMechanicalOffsetNs >= plan.HeartPeriodNs ||
            plan.VentricularMechanicalOffsetNs < plan.VentricularElectricalOffsetNs ||
            plan.VentricularMechanicalOffsetNs >= plan.HeartPeriodNs ||
            plan.InspirationDurationNs <= 0 || plan.InspirationDurationNs >= plan.BreathPeriodNs ||
            plan.InspiratoryPauseNs < 0 || plan.InspiratoryPauseNs >= plan.InspirationDurationNs ||
            plan.ExpiratoryPauseNs < 0 || plan.ExpiratoryPauseNs >= plan.BreathPeriodNs - plan.InspirationDurationNs ||
            !Enum.IsDefined(plan.RespiratoryActivity) ||
            (plan.ActivityAfterBreaths is { } breaths && (breaths == 0 || plan.RespiratoryActivity == RespiratoryActivity.Breathing ||
                (Int128)plan.EpochAnchorSimTimeNs + (Int128)breaths * plan.BreathPeriodNs > long.MaxValue)) ||
            (plan.ActivityDurationBreaths is { } duration && (duration == 0 || plan.ActivityAfterBreaths is null ||
                (Int128)plan.EpochAnchorSimTimeNs + ((Int128)plan.ActivityAfterBreaths.Value + duration) * plan.BreathPeriodNs > long.MaxValue)) ||
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
        long ventricularPeriod = _plan.HeartPeriodNs * _plan.VentricularConductionRatio;
        Add(PhysiologyCycleEventKind.VentricularElectrical, ventricularPeriod, _plan.VentricularElectricalOffsetNs);
        Add(PhysiologyCycleEventKind.AtrialMechanical, _plan.HeartPeriodNs, _plan.AtrialMechanicalOffsetNs);
        Add(PhysiologyCycleEventKind.VentricularMechanical, ventricularPeriod, _plan.VentricularMechanicalOffsetNs);
        if (_plan.RespiratoryActivity != RespiratoryActivity.Absent || _plan.ActivityAfterBreaths is not null)
        {
            ulong? limit = _plan.RespiratoryActivity == RespiratoryActivity.Absent ? _plan.ActivityAfterBreaths : null;
            ulong? resume = limit is { } first && _plan.ActivityDurationBreaths is { } duration ? first + duration : null;
            Add(PhysiologyCycleEventKind.InspirationStart, _plan.BreathPeriodNs, 0, limit, resume);
            Add(PhysiologyCycleEventKind.ExpirationStart, _plan.BreathPeriodNs, _plan.InspirationDurationNs, limit, resume);
        }
        events.Sort((left, right) => left.SimTimeNs != right.SimTimeNs
            ? left.SimTimeNs.CompareTo(right.SimTimeNs) : left.Kind.CompareTo(right.Kind));
        cancellationToken.ThrowIfCancellationRequested();
        _cursor = exclusiveSimTimeNs;
        return events.AsReadOnly();

        void Add(PhysiologyCycleEventKind kind, long period, long offset, ulong? cycleLimit = null, ulong? cycleResume = null)
        {
            Int128 start = (Int128)_plan.EpochAnchorSimTimeNs + offset;
            Int128 first = _cursor <= start ? 0 : ((Int128)_cursor - start + period - 1) / period;
            Int128 time = start + first * period;
            Int128 count = time >= exclusiveSimTimeNs ? 0 : ((Int128)exclusiveSimTimeNs - 1 - time) / period + 1;
            Int128 end = first + count;
            if (cycleLimit is { } limit)
            {
                Append(first, Int128.Min(end, limit));
                if (cycleResume is { } resume) { Append(Int128.Max(first, resume), end); }
            }
            else { Append(first, end); }

            void Append(Int128 begin, Int128 finish)
            {
                if (finish - begin > maximumEvents - events.Count)
                { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
                for (Int128 index = begin; index < finish; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    events.Add(new((long)(start + index * period), kind, (ulong)index));
                }
            }
        }
    }
}
