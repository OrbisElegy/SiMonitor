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
    // A tiled source segment, not one coordinated atrial depolarization.
    AtrialFibrillationSegment,
    VentricularDisorganizationSegment,
    PrematureAtrialElectrical,
}

// Local source activity, not a detected apnea classification or device fault.
public enum RespiratoryActivity { Breathing, EffortOnly, Absent }

// Source event availability; no inference of perfusion or detected arrest.
public enum CardiacActivity { AtrialAndVentricular, AtrialOnly, Absent, VentricularOnly }

public enum AvConductionPattern { FixedPr, WenckebachFourToThreeIllustration, CompleteAvBlockJunctionalIllustration, CompleteAvBlockVentricularIllustration, AtrialFlutterIllustration, AtrialFibrillationCoarseIllustration, AtrialFibrillationFineIllustration, VentricularFlutterIllustration, VentricularFibrillationCoarseIllustration, VentricularFibrillationFineIllustration, WenckebachThreeToTwoIllustration, WenckebachFiveToFourIllustration, MobitzTwoThreeToTwoIllustration, MobitzTwoFourToThreeIllustration, MobitzTwoRbbbFourToThreeIllustration, MobitzTwoLbbbFourToThreeIllustration, PrematureAtrialIllustration }

// Optional count starts with normal breathing and applies the target activity
// after that many complete source cycles. Null applies the target from epoch.
// Optional duration restores normal activity on the original cycle grid.
// HeartPeriodNs is the atrial period; ventricular events use its conduction
// multiple or an explicit independent period, preserving epoch offsets.
// The named AF illustration instead uses HeartPeriodNs as its authored
// ventricular timing grid; its atrial source is an irregular waveform segment.
// Independent periods are at least the atrial period to retain existing band
// support bounds; require ratio1 to avoid two conflicting ventricular clocks.
public sealed record RegularPhysiologyPlan(long EpochAnchorSimTimeNs, long HeartPeriodNs,
    long VentricularElectricalOffsetNs, long AtrialMechanicalOffsetNs,
    long VentricularMechanicalOffsetNs, long BreathPeriodNs, long InspirationDurationNs,
    long InspiratoryPauseNs = 0, long ExpiratoryPauseNs = 0,
    RespiratoryActivity RespiratoryActivity = RespiratoryActivity.Breathing, ulong? ActivityAfterBreaths = null, ulong? ActivityDurationBreaths = null,
    int VentricularConductionRatio = 1, CardiacActivity CardiacActivity = CardiacActivity.AtrialAndVentricular,
    bool VentricularMechanicalEnabled = true, ulong? MechanicalAfterCycles = null, ulong? MechanicalDurationCycles = null,
    int MechanicalEveryCycles = 1, long? IndependentVentricularPeriodNs = null, RespiratoryPattern RespiratoryPattern = RespiratoryPattern.Regular, int ConductedBeatsPerGroup = 1,
    AvConductionPattern ConductionPattern = AvConductionPattern.FixedPr)
{
    // Irregular sources expose a conservative interval for pulse support and
    // indexed pressure bounds. Their event times come from their own visitor.
    internal Int128 AtrialPeriodNs => ConductionPattern == AvConductionPattern.PrematureAtrialIllustration ? PrematureAtrialReference.MinimumRrNs : HeartPeriodNs;
    internal Int128 VentricularPeriodNs => ConductionPattern == AvConductionPattern.PrematureAtrialIllustration ? PrematureAtrialReference.MinimumRrNs : AtrialFibrillationReference.IsPattern(ConductionPattern)
        ? AtrialFibrillationReference.MinimumRrNs : IndependentVentricularPeriodNs ?? (Int128)HeartPeriodNs * (ConductedBeatsPerGroup > 1 ? 1 : VentricularConductionRatio);
}
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
            plan.MechanicalEveryCycles < 1 ||
            plan.ConductedBeatsPerGroup < 1 ||
            !Enum.IsDefined(plan.ConductionPattern) ||
            (VentricularDisorganizationReference.IsPattern(plan.ConductionPattern) &&
                (plan.HeartPeriodNs != 800_000_000 || plan.VentricularConductionRatio != 1 || plan.ConductedBeatsPerGroup != 1 ||
                 plan.CardiacActivity != CardiacActivity.VentricularOnly || plan.VentricularMechanicalEnabled ||
                 plan.IndependentVentricularPeriodNs is not null || plan.MechanicalEveryCycles != 1 ||
                 plan.MechanicalAfterCycles is not null || plan.MechanicalDurationCycles is not null)) ||
            (plan.ConductionPattern == AvConductionPattern.PrematureAtrialIllustration &&
                (plan.HeartPeriodNs != 800_000_000 || plan.VentricularConductionRatio != 1 || plan.ConductedBeatsPerGroup != 1 ||
                 plan.IndependentVentricularPeriodNs is not null || plan.CardiacActivity != CardiacActivity.AtrialAndVentricular ||
                 plan.VentricularElectricalOffsetNs != 160_000_000 || plan.VentricularMechanicalOffsetNs != 240_000_000 ||
                 plan.AtrialMechanicalOffsetNs != 80_000_000 || plan.MechanicalEveryCycles != 1 ||
                 plan.MechanicalAfterCycles is not null || plan.MechanicalDurationCycles is not null)) ||
            (AtrialFibrillationReference.IsPattern(plan.ConductionPattern) &&
                (plan.HeartPeriodNs != AtrialFibrillationReference.GridNs || plan.VentricularConductionRatio != 1 ||
                 plan.ConductedBeatsPerGroup != 1 || plan.IndependentVentricularPeriodNs is not null ||
                 plan.CardiacActivity != CardiacActivity.AtrialAndVentricular ||
                 plan.VentricularElectricalOffsetNs != 80_000_000 || plan.VentricularMechanicalOffsetNs != 160_000_000 ||
                 plan.MechanicalEveryCycles != 1 || plan.MechanicalAfterCycles is not null || plan.MechanicalDurationCycles is not null)) ||
            (plan.ConductionPattern == AvConductionPattern.AtrialFlutterIllustration &&
                (plan.HeartPeriodNs != 200_000_000 || plan.VentricularConductionRatio is not (2 or 4) ||
                 plan.ConductedBeatsPerGroup != 1 || plan.IndependentVentricularPeriodNs is not null ||
                 plan.CardiacActivity != CardiacActivity.AtrialAndVentricular ||
                 plan.VentricularElectricalOffsetNs != 80_000_000 || plan.VentricularMechanicalOffsetNs != 160_000_000)) ||
            (plan.ConductionPattern == AvConductionPattern.CompleteAvBlockVentricularIllustration &&
                (plan.IndependentVentricularPeriodNs is not (>= 1_500_000_000 and <= 3_000_000_000) ||
                 plan.HeartPeriodNs >= plan.IndependentVentricularPeriodNs ||
                 plan.CardiacActivity != CardiacActivity.AtrialAndVentricular ||
                 plan.VentricularConductionRatio != 1 || plan.ConductedBeatsPerGroup != 1)) ||
            (plan.ConductionPattern == AvConductionPattern.CompleteAvBlockJunctionalIllustration &&
                (plan.IndependentVentricularPeriodNs is not (>= 1_000_000_000 and <= 1_500_000_000) ||
                 plan.HeartPeriodNs >= plan.IndependentVentricularPeriodNs ||
                 plan.CardiacActivity != CardiacActivity.AtrialAndVentricular ||
                 plan.VentricularConductionRatio != 1 || plan.ConductedBeatsPerGroup != 1)) ||
            (plan.ConductionPattern is AvConductionPattern.MobitzTwoThreeToTwoIllustration or AvConductionPattern.MobitzTwoFourToThreeIllustration or AvConductionPattern.MobitzTwoRbbbFourToThreeIllustration or AvConductionPattern.MobitzTwoLbbbFourToThreeIllustration &&
                (plan.VentricularConductionRatio != (plan.ConductionPattern == AvConductionPattern.MobitzTwoThreeToTwoIllustration ? 3 : 4) ||
                 plan.ConductedBeatsPerGroup != plan.VentricularConductionRatio - 1 ||
                 plan.VentricularElectricalOffsetNs != 160_000_000 || plan.VentricularMechanicalOffsetNs != 240_000_000)) ||
            (WenckebachIllustration.GroupSize(plan.ConductionPattern) is > 0 and var groupSize &&
                (plan.VentricularConductionRatio != groupSize || plan.ConductedBeatsPerGroup != groupSize - 1 ||
                 (Int128)plan.VentricularMechanicalOffsetNs + WenckebachIllustration.ExtraDelayNs(groupSize - 2) >= plan.HeartPeriodNs)) ||
            (plan.ConductedBeatsPerGroup > 1 && (plan.ConductedBeatsPerGroup >= plan.VentricularConductionRatio ||
                plan.IndependentVentricularPeriodNs is not null || plan.CardiacActivity != CardiacActivity.AtrialAndVentricular ||
                plan.MechanicalEveryCycles != 1 || plan.MechanicalAfterCycles is not null || plan.MechanicalDurationCycles is not null)) ||
            plan.VentricularConductionRatio < 1 || plan.VentricularPeriodNs > long.MaxValue ||
            (plan.IndependentVentricularPeriodNs is { } independent &&
                (independent < plan.HeartPeriodNs || plan.VentricularConductionRatio != 1)) ||
            plan.VentricularElectricalOffsetNs < (plan.IndependentVentricularPeriodNs is null ? 1 : 0) ||
            plan.VentricularElectricalOffsetNs >= (plan.IndependentVentricularPeriodNs ?? plan.HeartPeriodNs) ||
            plan.AtrialMechanicalOffsetNs < 0 || plan.AtrialMechanicalOffsetNs >= plan.HeartPeriodNs ||
            plan.VentricularMechanicalOffsetNs < plan.VentricularElectricalOffsetNs ||
            plan.VentricularMechanicalOffsetNs >= (plan.IndependentVentricularPeriodNs ?? plan.HeartPeriodNs) ||
            plan.InspirationDurationNs <= 0 || plan.InspirationDurationNs >= plan.BreathPeriodNs ||
            plan.InspiratoryPauseNs < 0 || plan.InspiratoryPauseNs >= plan.InspirationDurationNs ||
            plan.ExpiratoryPauseNs < 0 || plan.ExpiratoryPauseNs >= plan.BreathPeriodNs - plan.InspirationDurationNs ||
            !Enum.IsDefined(plan.RespiratoryPattern) ||
            (plan.RespiratoryPattern != RespiratoryPattern.Regular && (plan.RespiratoryActivity != RespiratoryActivity.Breathing || plan.ActivityAfterBreaths is not null || plan.ActivityDurationBreaths is not null)) ||
            !Enum.IsDefined(plan.RespiratoryActivity) || !Enum.IsDefined(plan.CardiacActivity) ||
            (plan.MechanicalAfterCycles is { } cycles && (cycles == 0 || plan.VentricularMechanicalEnabled ||
                plan.CardiacActivity is not (CardiacActivity.AtrialAndVentricular or CardiacActivity.VentricularOnly) ||
                (Int128)cycles * plan.VentricularPeriodNs > long.MaxValue - plan.EpochAnchorSimTimeNs)) ||
            (plan.MechanicalDurationCycles is { } mechanicalDuration && (mechanicalDuration == 0 || plan.MechanicalAfterCycles is null ||
                (Int128)plan.MechanicalAfterCycles.Value + mechanicalDuration >
                    ((Int128)long.MaxValue - plan.EpochAnchorSimTimeNs) / plan.VentricularPeriodNs)) ||
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
        if (_plan.CardiacActivity is CardiacActivity.AtrialAndVentricular or CardiacActivity.AtrialOnly)
        {
            if (AtrialFibrillationReference.IsPattern(_plan.ConductionPattern))
            { Add(PhysiologyCycleEventKind.AtrialFibrillationSegment, AtrialFibrillationReference.SegmentDurationNs, 0); }
            else { Add(PhysiologyCycleEventKind.AtrialElectrical, _plan.HeartPeriodNs, 0); }
            if (_plan.ConductionPattern == AvConductionPattern.PrematureAtrialIllustration)
            { Add(PhysiologyCycleEventKind.PrematureAtrialElectrical, _plan.HeartPeriodNs, 0); }
            // Flutter mechanics are not modelled; do not emit normal atrial
            // contractions at the electrical flutter frequency.
            if (_plan.ConductionPattern != AvConductionPattern.AtrialFlutterIllustration && !AtrialFibrillationReference.IsPattern(_plan.ConductionPattern))
            { Add(PhysiologyCycleEventKind.AtrialMechanical, _plan.HeartPeriodNs, _plan.AtrialMechanicalOffsetNs); }
        }
        if (VentricularDisorganizationReference.IsPattern(_plan.ConductionPattern))
        { Add(PhysiologyCycleEventKind.VentricularDisorganizationSegment, VentricularDisorganizationReference.SegmentDurationNs(_plan.ConductionPattern), 0); }
        else if (_plan.CardiacActivity is CardiacActivity.AtrialAndVentricular or CardiacActivity.VentricularOnly)
        {
            long ventricularPeriod = (long)_plan.VentricularPeriodNs;
            Add(PhysiologyCycleEventKind.VentricularElectrical, ventricularPeriod, _plan.VentricularElectricalOffsetNs);
            VisitVentricularMechanical(_plan, _cursor, exclusiveSimTimeNs,
                maximumEvents - events.Count, events.Add, cancellationToken);
        }
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

        void Add(PhysiologyCycleEventKind kind, long period, long offset, ulong? cycleLimit = null, ulong? cycleResume = null, int cycleStride = 1)
        {
            VisitCycles(_plan, kind, period, offset, _cursor, exclusiveSimTimeNs,
                maximumEvents - events.Count, events.Add, cancellationToken, cycleLimit, cycleResume, cycleStride);
        }
    }

    // Shared source-event selection for timeline consumers and indexed pressure
    // reconstruction. The latter must not consume the unrelated all-event budget.
    // Callers own validated plans and bounds; Int128 permits an inclusive final
    // representable timestamp by expressing its exclusive endpoint as MaxValue+1.
    internal static void VisitVentricularMechanical(RegularPhysiologyPlan plan,
        long inclusiveSimTimeNs, Int128 exclusiveSimTimeNs, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        if (plan.CardiacActivity is not (CardiacActivity.AtrialAndVentricular or CardiacActivity.VentricularOnly) ||
            !plan.VentricularMechanicalEnabled && plan.MechanicalAfterCycles is null) { return; }
        ulong? resume = plan.MechanicalAfterCycles is { } first && plan.MechanicalDurationCycles is { } duration ? first + duration : null;
        VisitCycles(plan, PhysiologyCycleEventKind.VentricularMechanical,
            (long)plan.VentricularPeriodNs, plan.VentricularMechanicalOffsetNs,
            inclusiveSimTimeNs, exclusiveSimTimeNs, maximumEvents, visitor, cancellationToken,
            plan.MechanicalAfterCycles, resume, plan.MechanicalEveryCycles);
    }

    private static void VisitCycles(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind,
        long period, long offset, long inclusiveSimTimeNs, Int128 exclusiveSimTimeNs, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken,
        ulong? cycleLimit = null, ulong? cycleResume = null, int cycleStride = 1)
    {
        if (plan.ConductionPattern == AvConductionPattern.PrematureAtrialIllustration &&
            kind is PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.PrematureAtrialElectrical or
                PhysiologyCycleEventKind.AtrialMechanical or PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical)
        {
            PrematureAtrialReference.Visit(plan, kind, offset, inclusiveSimTimeNs, exclusiveSimTimeNs, maximumEvents, visitor, cancellationToken);
            return;
        }
        if (AtrialFibrillationReference.IsPattern(plan.ConductionPattern) &&
            kind is PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical)
        {
            AtrialFibrillationReference.Visit(plan, kind, offset, inclusiveSimTimeNs, exclusiveSimTimeNs, maximumEvents, visitor, cancellationToken);
            return;
        }
        if (WenckebachIllustration.GroupSize(plan.ConductionPattern) > 0 &&
            kind is PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical)
        {
            VisitWenckebach(plan, kind, offset, inclusiveSimTimeNs, exclusiveSimTimeNs, maximumEvents, visitor, cancellationToken);
            return;
        }
        Int128 start = (Int128)plan.EpochAnchorSimTimeNs + offset;
        Int128 first = inclusiveSimTimeNs <= start ? 0 : ((Int128)inclusiveSimTimeNs - start + period - 1) / period;
        Int128 time = start + first * period;
        Int128 count = time >= exclusiveSimTimeNs ? 0 : (exclusiveSimTimeNs - 1 - time) / period + 1;
        Int128 end = first + count;
        int remaining = maximumEvents;
        if (cycleLimit is { } limit)
        {
            Append(first, Int128.Min(end, limit));
            if (cycleResume is { } resume) { Append(Int128.Max(first, resume), end); }
        }
        else { Append(first, end); }

        void Append(Int128 begin, Int128 finish)
        {
            // Retain original cycle indices, including after a skipped range.
            begin = (begin + cycleStride - 1) / cycleStride * cycleStride;
            if (begin >= finish) { return; }
            bool patternedBreath = plan.RespiratoryPattern == RespiratoryPattern.CheyneStokesIllustration &&
                kind is PhysiologyCycleEventKind.InspirationStart or PhysiologyCycleEventKind.ExpirationStart;
            bool groupedConduction = plan.ConductedBeatsPerGroup > 1 &&
                kind is PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical;
            Int128 ConductedBefore(Int128 slot) => slot / plan.VentricularConductionRatio * plan.ConductedBeatsPerGroup +
                Int128.Min(slot % plan.VentricularConductionRatio, plan.ConductedBeatsPerGroup);
            Int128 selected = groupedConduction ? ConductedBefore(finish) - ConductedBefore(begin) : patternedBreath ? RespiratoryPatternDepth.ActiveBefore(finish) - RespiratoryPatternDepth.ActiveBefore(begin) : (finish - 1 - begin) / cycleStride + 1;
            if (selected > remaining)
            { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
            remaining -= (int)selected;
            for (Int128 index = begin; index < finish; index += cycleStride)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (groupedConduction && index % plan.VentricularConductionRatio >= plan.ConductedBeatsPerGroup)
                {
                    index += plan.VentricularConductionRatio - index % plan.VentricularConductionRatio - 1;
                    continue;
                }
                if (patternedBreath && RespiratoryPatternDepth.At(plan.RespiratoryPattern, (ulong)index) == 0) { continue; }
                visitor(new((long)(start + index * period), kind, (ulong)index));
            }
        }
    }

    // Conducted periodic streams share an atrial group. Count exact half-open
    // intersections before visiting, then emit in time order for pressure RC.
    // Authored successive PR increments: 80/40/20 ms, then one dropped P.
    private static void VisitWenckebach(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind,
        long offset, long inclusive, Int128 exclusive, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        int groupSize = WenckebachIllustration.GroupSize(plan.ConductionPattern);
        Int128 period = (Int128)plan.HeartPeriodNs * groupSize;
        Int128 Start(int slot) => (Int128)plan.EpochAnchorSimTimeNs + offset +
            (Int128)slot * plan.HeartPeriodNs + WenckebachIllustration.ExtraDelayNs(slot);
        Int128 firstGroup = Int128.MaxValue, lastGroup = -1, count = 0;
        for (int slot = 0; slot < groupSize - 1; slot++)
        {
            Int128 start = Start(slot);
            Int128 first = inclusive <= start ? 0 : ((Int128)inclusive - start + period - 1) / period;
            if (exclusive <= start + first * period) { continue; }
            Int128 last = (exclusive - 1 - start) / period;
            count += last - first + 1;
            firstGroup = Int128.Min(firstGroup, first);
            lastGroup = Int128.Max(lastGroup, last);
        }
        if (count > maximumEvents)
        { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
        for (Int128 group = firstGroup; group <= lastGroup; group++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int slot = 0; slot < groupSize - 1; slot++)
            {
                Int128 time = Start(slot) + group * period;
                if (time >= inclusive && time < exclusive)
                { visitor(new((long)time, kind, (ulong)(group * groupSize + slot))); }
            }
        }
    }
}
