// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Authored finite sequence, not a sinus-node/autonomic model. 256 beats repeat.
// PRNG is consumed only while preparing the immutable schedule; its terminal
// state is retained. Runtime indexed visits never consume random draws.
public sealed record SeededCardiacRate
{
    public const string PatternId = "SeededCardiacSlowVariation@2";
    public int HeartRateBpm { get; }
    public string SeedHex { get; }
    public int VariationPermille { get; }
    public DeterministicStreamState PreparedState { get; }
    internal long[] Slots { get; }
    public long PeriodNs { get; }
    public long MinimumPeriodNs { get; }
    public SeededCardiacRate(int heartRateBpm, string seedHex, int variationPermille)
        : this(heartRateBpm, seedHex, variationPermille, false) { }

    internal SeededCardiacRate(int heartRateBpm, string seedHex, int variationPermille, bool extendedRange)
    {
        if (heartRateBpm < (extendedRange ? 20 : 30) || heartRateBpm > (extendedRange ? 600 : 180) || variationPermille is < 0 or > 50)
        { throw new ArgumentException("SeededRate.InvalidRange"); }
        using var factory = DeterministicStreamFactory.FromLowercaseHex(seedHex);
        var random = factory.CreateStream("physiology.cardiac.rate");
        HeartRateBpm = heartRateBpm; SeedHex = seedHex; VariationPermille = variationPermille;
        PeriodNs = (long)FixedPointMath.RoundDivideTiesToEven(60_000_000_000, heartRateBpm);
        long maximumOffset = PeriodNs * variationPermille / 1000;
        MinimumPeriodNs = PeriodNs - maximumOffset;
        Slots = new long[256];
        long cursor = 0;
        // Correlated16-interval excursions survive the8-interval measurement
        // window. Pair each with its opposite to preserve the nominal cycle
        // mean exactly; the seed selects direction and75..100% of the bound.
        for (int pair = 0; pair < 8; pair++)
        {
            long minimum = (maximumOffset * 3 + 3) / 4;
            long magnitude = minimum + (long)random.UniformBelow((ulong)(maximumOffset - minimum + 1));
            int sign = random.UniformBelow(2) == 0 ? -1 : 1;
            for (int beat = 0; beat < 32; beat++)
            {
                int phase = beat % 16;
                int weight = Math.Min(phase + 1, 16 - phase);
                long offset = magnitude * weight / 8 * sign * (beat < 16 ? 1 : -1);
                Slots[pair * 32 + beat] = cursor;
                cursor += PeriodNs + offset;
            }
        }
        PreparedState = random.CaptureState();
    }
    // Indexed preceding RR, including the established schedule across wrap.
    // Reuses the teaching filling curve; not a calibrated stroke volume.
    public int EjectionGainPermille(ulong ordinal)
    {
        long intervalNs = PrecedingIntervalNs(ordinal);
        long leadNs = Timing.PrIntervalNs;
        long effectiveAtrialNs = Math.Max(0, Math.Min(leadNs, CardiacFillingPerfusion.AtrialContractionDurationNs) -
            Math.Max(0, leadNs - (intervalNs - FillingLimitedEjection.NonFillingDurationNs)));
        return CardiacFillingPerfusion.StrokeVolumePermille(intervalNs, effectiveAtrialNs, FillingLimitedEjection.NonFillingDurationNs);
    }
    internal long PrecedingIntervalNs(ulong ordinal)
    {
        int index = (int)(ordinal % (ulong)Slots.Length);
        return index == 0 ? PeriodNs * Slots.Length - Slots[^1] : Slots[index] - Slots[index - 1];
    }
    internal long FollowingIntervalNs(ulong ordinal) => PrecedingIntervalNs(ordinal % (ulong)Slots.Length + 1);
    // Teaching morphology support, not patient-specific QT adaptation.
    public EcgCycleTiming Timing
    {
        get
        {
            var normal = TextbookEcgReference.Timing;
            if (MinimumPeriodNs >= normal.PrIntervalNs + normal.QtIntervalNs)
            { return normal with { RrIntervalNs = MinimumPeriodNs }; }
            long qt = MinimumPeriodNs - 100_000_000;
            return new(MinimumPeriodNs, 70_000_000, 100_000_000, 80_000_000, qt, Math.Min(180_000_000, qt - 100_000_000));
        }
    }
    internal void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind, long offset, long inclusive, Int128 exclusive,
        int maximumEvents, Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        Int128 origin = (Int128)plan.EpochAnchorSimTimeNs + offset;
        Int128 duration = (Int128)PeriodNs * Slots.Length;
        Int128 LowerBound(Int128 time)
        {
            Int128 relative = time - origin;
            if (relative <= 0) { return 0; }
            Int128 group = relative / duration;
            long phase = (long)(relative % duration);
            int left = 0, right = Slots.Length;
            while (left < right)
            {
                int middle = (left + right) / 2;
                if (Slots[middle] < phase) { left = middle + 1; } else { right = middle; }
            }
            return group * Slots.Length + left;
        }
        Int128 first = LowerBound(inclusive), last = LowerBound(exclusive);
        if (last - first > maximumEvents)
        { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
        for (Int128 i = first; i < last; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Int128 time = origin + i / Slots.Length * duration + Slots[(int)(i % Slots.Length)];
            visitor(new((long)time, kind, (ulong)i));
        }
    }
}
