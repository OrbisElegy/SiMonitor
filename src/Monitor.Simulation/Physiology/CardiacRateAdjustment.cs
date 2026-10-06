// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.CompilerServices;

namespace Monitor.Simulation.Physiology;

// Re-times the authored event sequence, preserving its omissions, event kinds,
// cycle identities and mechanical delays. Waveform components keep their widths.
// The target is the underlying rhythm rate (mean ventricular grid for AF), not
// a promise about the detector's displayed HR in rhythms with premature beats.
public sealed record CardiacRateAdjustment
{
    private readonly ConditionalWeakTable<SeededRhythmSchedule, ArrestClock> _arrestClocks = new();
    public SeededCardiacRate Rate { get; }
    public SeededCardiacRate? AtrialRate { get; }

    public CardiacRateAdjustment(int rateBpm, int? atrialRateBpm, string seedHex, int variationPermille)
    {
        Rate = new(rateBpm, seedHex, variationPermille, true);
        AtrialRate = atrialRateBpm is { } atrial ? new(atrial, seedHex, variationPermille, true) : null;
    }

    public static bool Supports(RegularPhysiologyPlan plan) =>
        plan.CardiacActivity != CardiacActivity.Absent && !VentricularDisorganizationReference.IsPattern(plan.ConductionPattern);

    public void Validate(RegularPhysiologyPlan plan)
    {
        if (!Supports(plan) || plan.SeededRate is not null ||
            (plan.IndependentVentricularPeriodNs is not null) != (AtrialRate is not null))
        { throw new ArgumentException("CardiacRate.InvalidMode"); }
        if (plan.ConductionPattern == AvConductionPattern.SinusArrestIllustration && plan.RhythmSchedule is { } rhythm &&
            rhythm.PauseDurationNs <= Rate.PeriodNs * (1000 + Rate.VariationPermille) / 1000)
        { throw new ArgumentException("CardiacRate.PauseMustBeLonger"); }
        if (MinimumPeriodNs(plan, true) <= 0 || MinimumPeriodNs(plan, false) <= 0)
        { throw new ArgumentException("CardiacRate.WaveformSupport"); }
        if (plan.ConductionPattern == AvConductionPattern.CompleteAvBlockVentricularIllustration && Rate.HeartRateBpm is < 20 or > 40)
        { throw new ArgumentException("CardiacRate.VentricularEscapeRange"); }
        if (plan.ConductionPattern == AvConductionPattern.CompleteAvBlockJunctionalIllustration && Rate.HeartRateBpm is < 40 or > 60)
        { throw new ArgumentException("CardiacRate.JunctionalEscapeRange"); }
        if (plan.ConductionPattern is AvConductionPattern.CompleteAvBlockVentricularIllustration or AvConductionPattern.CompleteAvBlockJunctionalIllustration &&
            AtrialRate!.PeriodNs * (1000 + AtrialRate.VariationPermille) >= Rate.PeriodNs * (1000 - Rate.VariationPermille))
        { throw new ArgumentException("CardiacRate.EscapeMustBeSlower"); }
    }

    internal static bool IsTimedEvent(PhysiologyCycleEventKind kind) => kind is
        PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.AtrialMechanical or
        PhysiologyCycleEventKind.PrematureAtrialElectrical or PhysiologyCycleEventKind.RetrogradeAtrialElectrical or
        PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical;

    private SeededCardiacRate Schedule(bool atrial) => atrial && AtrialRate is { } rate ? rate : Rate;
    private static long ReferencePeriodNs(RegularPhysiologyPlan plan, bool atrial) =>
        !atrial && plan.IndependentVentricularPeriodNs is { } period ? period : plan.HeartPeriodNs;

    private static long RonTCouplingNs(RegularPhysiologyPlan plan) => plan.ConductionPattern switch
    {
        AvConductionPattern.RonTLongQtPvcIllustration => 500_000_000,
        AvConductionPattern.ShortCoupledRonTPvcIllustration => 200_000_000,
        _ => 0
    };

    internal long MinimumPeriodNs(RegularPhysiologyPlan plan, bool atrial)
    {
        Int128 minimum = atrial ? plan.ReferenceAtrialPeriodNs : plan.ReferenceVentricularPeriodNs;
        if (!atrial && RonTCouplingNs(plan) is > 0 and var coupling)
        {
            return (long)Int128.Min(coupling, (Int128)2 * Rate.MinimumPeriodNs - coupling);
        }
        return (long)(minimum * Schedule(atrial).MinimumPeriodNs / ReferencePeriodNs(plan, atrial));
    }

    internal long MinimumEjectingIntervalNs(RegularPhysiologyPlan plan) =>
        plan.ConductionPattern == AvConductionPattern.RonTLongQtPvcIllustration ? MinimumPeriodNs(plan, false) :
        (long)((Int128)PrematureBeatPerfusion.MinimumEjectingIntervalNs(plan.ConductionPattern) * Rate.MinimumPeriodNs / plan.HeartPeriodNs);

    internal IReadOnlyList<EventWaveformBand> AdjustBands(RegularPhysiologyPlan plan, IReadOnlyList<EventWaveformBand> bands)
    {
        if (!AtrialFlutterReference.IsPattern(plan.ConductionPattern)) { return bands; }
        return Array.AsReadOnly(bands.Select(band => band.Trigger == PhysiologyCycleEventKind.AtrialElectrical
            ? band with { CycleDurationRate = Rate, DurationNs = Rate.PeriodNs * (1000 + Rate.VariationPermille) / 1000 }
            : band).ToArray());
    }

    internal void ValidateBands(RegularPhysiologyPlan plan, IEnumerable<EventWaveformBand> bands)
    {
        foreach (var band in bands)
        {
            if (band.Trigger is not (PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.VentricularElectrical or
                PhysiologyCycleEventKind.PrematureAtrialElectrical or PhysiologyCycleEventKind.RetrogradeAtrialElectrical)) { continue; }
            if (band.CycleDurationRate is not null) { continue; }
            bool atrial = band.Trigger is not (PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical);
            Int128 reference = atrial ? plan.ReferenceAtrialPeriodNs : plan.ReferenceVentricularPeriodNs;
            Int128 support = (Int128)band.DelayNs + band.DurationNs;
            Int128 allowedOverlap = (support + reference - 1) / reference;
            if (support > MinimumPeriodNs(plan, atrial) * allowedOverlap)
            { throw new ArgumentException("CardiacRate.WaveformSupport"); }
        }
    }

    internal Int128 Map(RegularPhysiologyPlan plan, bool atrial, Int128 relativeNs)
    {
        var rate = Schedule(atrial);
        if (plan.ConductionPattern == AvConductionPattern.SinusArrestIllustration && plan.RhythmSchedule is { } rhythm)
        { return ArrestTiming(rhythm).Map(relativeNs, inverse: false); }
        long reference = ReferencePeriodNs(plan, atrial);
        Int128 cycle = FloorDivide(relativeNs, reference);
        Int128 phase = relativeNs - cycle * reference;
        Int128 group = FloorDivide(cycle, rate.Slots.Length);
        int index = (int)(cycle - group * rate.Slots.Length);
        long start = rate.Slots[index];
        long end = index + 1 == rate.Slots.Length ? rate.PeriodNs * rate.Slots.Length : rate.Slots[index + 1];
        return group * rate.PeriodNs * rate.Slots.Length + start + phase * (end - start) / reference;
    }

    // First reference nanosecond whose mapped time is >= the supplied bound.
    internal Int128 Unmap(RegularPhysiologyPlan plan, bool atrial, Int128 relativeNs)
    {
        var rate = Schedule(atrial);
        if (plan.ConductionPattern == AvConductionPattern.SinusArrestIllustration && plan.RhythmSchedule is { } rhythm)
        { return ArrestTiming(rhythm).Map(relativeNs, inverse: true); }
        long reference = ReferencePeriodNs(plan, atrial);
        long duration = rate.PeriodNs * rate.Slots.Length;
        Int128 group = FloorDivide(relativeNs, duration);
        long phase = (long)(relativeNs - group * duration);
        int index = Array.BinarySearch(rate.Slots, phase);
        if (index < 0) { index = ~index - 1; }
        long start = rate.Slots[index];
        long end = index + 1 == rate.Slots.Length ? duration : rate.Slots[index + 1];
        return (group * rate.Slots.Length + index) * reference +
            ((Int128)(phase - start) * reference + end - start - 1) / (end - start);
    }

    private ArrestClock ArrestTiming(SeededRhythmSchedule rhythm) => _arrestClocks.TryGetValue(rhythm, out var clock)
        ? clock : _arrestClocks.GetValue(rhythm, key => new(key, Rate));

    // Four 256-beat arrest groups contain 768 normal intervals: both the
    // 256-slot normal-rate clock and pause placement repeat at this boundary.
    // This immutable lookup replaces a whole-Int64-domain inverse search on
    // every sample. It is derived data, never authoritative checkpoint state.
    private sealed class ArrestClock
    {
        private readonly long[] _reference = new long[1025];
        private readonly long[] _adjusted = new long[1025];

        internal ArrestClock(SeededRhythmSchedule rhythm, SeededCardiacRate rate)
        {
            for (int i = 0; i < _reference.Length; i++)
            {
                _reference[i] = checked((long)rhythm.CycleStartNs(AvConductionPattern.SinusArrestIllustration, (ulong)i));
                _adjusted[i] = checked((long)rhythm.MapArrest(rate, _reference[i]));
            }
        }

        internal Int128 Map(Int128 timeNs, bool inverse)
        {
            long[] input = inverse ? _adjusted : _reference;
            long[] output = inverse ? _reference : _adjusted;
            Int128 group = FloorDivide(timeNs, input[^1]);
            long phase = (long)(timeNs - group * input[^1]);
            int index = Array.BinarySearch(input, phase);
            if (index < 0) { index = ~index - 1; }
            long interval = input[index + 1] - input[index];
            Int128 numerator = (Int128)(phase - input[index]) * (output[index + 1] - output[index]);
            return group * output[^1] + output[index] + (numerator + (inverse ? interval - 1 : 0)) / interval;
        }
    }

    private static Int128 FloorDivide(Int128 value, long divisor) =>
        value >= 0 ? value / divisor : (value + 1) / divisor - 1;

    internal void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind, long offsetNs,
        long inclusiveSimTimeNs, Int128 exclusiveSimTimeNs, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, ulong? cycleLimit, ulong? cycleResume, int cycleStride,
        CancellationToken cancellationToken)
    {
        bool atrial = kind is not (PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical);
        Int128 origin = (Int128)plan.EpochAnchorSimTimeNs + offsetNs;
        long coupling = atrial ? 0 : RonTCouplingNs(plan);
        Int128 padding = coupling == 0 ? 0 : coupling + Map(plan, false, coupling);
        Int128 from = Unmap(plan, atrial, (Int128)inclusiveSimTimeNs - origin - padding) + origin;
        Int128 to = Unmap(plan, atrial, exclusiveSimTimeNs - origin + padding) + origin;
        if (to > (Int128)long.MaxValue + 1)
        { throw new PhysiologyTimelineException("PhysiologyTimeline.TimeOutOfRange", nameof(exclusiveSimTimeNs)); }
        if (to <= 0) { return; }
        var reference = plan with { RateAdjustment = null };
        long period = atrial ? reference.HeartPeriodNs : (long)reference.VentricularPeriodNs;
        int emitted = 0;
        RegularPhysiologyTimeline.VisitCycles(reference, kind, period, offsetNs,
            (long)Int128.Max(0, from), to, maximumEvents + (coupling == 0 ? 0 : 16),
            item =>
            {
                long fixedCoupling = coupling > 0 && item.CycleIndex % 4 == 3 ? coupling : 0;
                Int128 mapped = origin + Map(plan, atrial, (Int128)item.SimTimeNs - origin - fixedCoupling) + fixedCoupling;
                if (mapped < inclusiveSimTimeNs || mapped >= exclusiveSimTimeNs) { return; }
                if (++emitted > maximumEvents)
                { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
                visitor(item with { SimTimeNs = checked((long)mapped) });
            }, cancellationToken, cycleLimit, cycleResume, cycleStride);
    }
}
