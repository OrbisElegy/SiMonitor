// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Integrates rate into a monotonically increasing reference phase. Original
// event ordinals and waveform widths survive a rate ramp; acquisition time is
// never warped. The inverse selects the first physical nanosecond at a phase.
public sealed class PhysiologyRateClock
{
    private sealed record InverseEntry(long ReferenceTimeNs, long SourceTimeNs);
    private readonly InverseEntry?[] _inverseCache = new InverseEntry[8192];
    private readonly long _initialRateCorrection;
    private readonly Int128 _minimumRateProduct;
    private readonly Int128 _maximumRateProduct;
    private const long MinuteNs = 60_000_000_000;
    public long ReferencePeriodNs { get; }
    public VitalValueCurve RatePerMinute { get; }

    public PhysiologyRateClock(long referencePeriodNs, VitalValueCurve ratePerMinute)
    {
        ArgumentNullException.ThrowIfNull(ratePerMinute);
        if (referencePeriodNs is < 100_000_000 or > MinuteNs || ratePerMinute.MinimumValue < 1 || ratePerMinute.MaximumValue > 600)
        { throw new ArgumentException("PhysiologyRateClock.InvalidRate"); }
        ReferencePeriodNs = referencePeriodNs;
        RatePerMinute = ratePerMinute;
        long correction = MinuteNs - (long)ratePerMinute.InitialValue * referencePeriodNs;
        _initialRateCorrection = Math.Abs(correction) <= referencePeriodNs / 2 ? correction : 0;
        _minimumRateProduct = (Int128)ReferencePeriodNs * RatePerMinute.MinimumValue;
        _maximumRateProduct = (Int128)ReferencePeriodNs * RatePerMinute.MaximumValue;
        if (_initialRateCorrection != 0)
        {
            _minimumRateProduct = Int128.Min(MinuteNs, _minimumRateProduct);
            _maximumRateProduct = Int128.Max(MinuteNs, _maximumRateProduct);
        }
    }

    public long ReferenceAt(long sourceTimeNs) => checked((long)ReferenceAtWide(sourceTimeNs));

    internal Int128 ReferenceAtWide(long sourceTimeNs) =>
        (RatePerMinute.IntegralAt(sourceTimeNs) * ReferencePeriodNs +
            _initialRateCorrection * RatePerMinute.InitialInfluenceAt(sourceTimeNs)) / MinuteNs;

    public long SourceAt(long referenceTimeNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(referenceTimeNs);
        if (referenceTimeNs == 0) { return 0; }
        int slot = (int)(((ulong)referenceTimeNs ^ (ulong)referenceTimeNs >> 23 ^ (ulong)referenceTimeNs >> 43) & 8191);
        var cached = Volatile.Read(ref _inverseCache[slot]);
        if (cached?.ReferenceTimeNs == referenceTimeNs) { return cached.SourceTimeNs; }
        Int128 bound = ((Int128)referenceTimeNs * MinuteNs + _minimumRateProduct - 1) / _minimumRateProduct;
        long low = 0, high = (long)Int128.Min(long.MaxValue, bound + 1);
        if (ReferenceAtWide(high) < referenceTimeNs)
        { throw new ArgumentOutOfRangeException(nameof(referenceTimeNs), "PhysiologyRateClock.TimeOutOfRange"); }
        while (low + 1 < high)
        {
            long middle = low + (high - low) / 2;
            if (ReferenceAtWide(middle) >= referenceTimeNs) { high = middle; }
            else { low = middle; }
        }
        // Derived cache only: collision/eviction changes cost, never results.
        Volatile.Write(ref _inverseCache[slot], new(referenceTimeNs, high));
        return high;
    }

    internal long MaximumDuration(long referenceDurationNs) => checked((long)(
        ((Int128)referenceDurationNs * MinuteNs + _minimumRateProduct - 1) / _minimumRateProduct));

    internal Int128 MinimumDuration(Int128 referenceDurationNs) =>
        referenceDurationNs * MinuteNs / _maximumRateProduct;

    internal void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind, long offsetNs,
        long inclusiveSimTimeNs, Int128 exclusiveSimTimeNs, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, ulong? cycleLimit, ulong? cycleResume,
        int cycleStride, bool respiratory, CancellationToken cancellationToken)
    {
        // Cardiac conduction/mechanical offsets stay in real time. Respiratory
        // inspiration/expiration share the same integrated cycle phase.
        long fixedOffsetNs = respiratory ? 0 : offsetNs;
        long from = checked((long)Int128.Min(long.MaxValue, ReferenceAtWide(Math.Max(0, inclusiveSimTimeNs - fixedOffsetNs))));
        long physicalTo = checked((long)Int128.Min(long.MaxValue, Int128.Max(0, exclusiveSimTimeNs - fixedOffsetNs)));
        var to = Int128.Min(long.MaxValue, ReferenceAtWide(physicalTo) + 1);
        var reference = respiratory ? plan with { RespiratoryRateClock = null } : plan with { CardiacRateClock = null };
        long period = respiratory ? reference.BreathPeriodNs :
            kind is PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical
                ? (long)reference.VentricularPeriodNs : reference.HeartPeriodNs;
        RegularPhysiologyTimeline.VisitCycles(reference, kind, period, offsetNs,
            checked(from + fixedOffsetNs), Int128.Min(long.MaxValue, to + fixedOffsetNs), maximumEvents + 2,
            item =>
            {
                long mapped = checked(SourceAt(item.SimTimeNs - fixedOffsetNs) + fixedOffsetNs);
                if (mapped < inclusiveSimTimeNs || mapped >= exclusiveSimTimeNs) { return; }
                if (maximumEvents-- <= 0)
                { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
                visitor(item with { SimTimeNs = mapped });
            }, cancellationToken, cycleLimit, cycleResume, cycleStride);
    }
}
