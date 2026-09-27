// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Authored finite sequence, not a sinus-node/autonomic model. 256 beats repeat.
// PRNG is consumed only while preparing the immutable schedule; its terminal
// state is retained. Runtime indexed visits never consume random draws.
public sealed record SeededCardiacRate
{
    public int HeartRateBpm { get; }
    public string SeedHex { get; }
    public int VariationPermille { get; }
    public DeterministicStreamState PreparedState { get; }
    internal long[] Slots { get; }
    public long PeriodNs { get; }
    public long MinimumPeriodNs { get; }
    public SeededCardiacRate(int heartRateBpm, string seedHex, int variationPermille)
    {
        if (heartRateBpm is < 30 or > 180 || variationPermille is < 0 or > 50)
        { throw new ArgumentException("SeededRate.InvalidRange"); }
        using var factory = DeterministicStreamFactory.FromLowercaseHex(seedHex);
        var random = factory.CreateStream("physiology.cardiac.rate");
        HeartRateBpm = heartRateBpm; SeedHex = seedHex; VariationPermille = variationPermille;
        PeriodNs = (long)FixedPointMath.RoundDivideTiesToEven(60_000_000_000, heartRateBpm);
        long displacement = PeriodNs * variationPermille / 2000;
        MinimumPeriodNs = PeriodNs - 2 * displacement;
        Slots = new long[256];
        for (int i = 1; i < Slots.Length; i++)
        { Slots[i] = i * PeriodNs + (long)random.UniformBelow((ulong)(2 * displacement + 1)) - displacement; }
        PreparedState = random.CaptureState();
    }
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
        Int128 first = Int128.Max(0, ((Int128)inclusive - origin) / PeriodNs - 1);
        Int128 last = (exclusive - origin) / PeriodNs + 1;
        if (last - first > (Int128)maximumEvents + 4)
        { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
        Int128 At(Int128 cycle) => origin + cycle / Slots.Length * (PeriodNs * Slots.Length) + Slots[(int)(cycle % Slots.Length)];
        int count = 0;
        for (Int128 i = first; i <= last; i++)
        { cancellationToken.ThrowIfCancellationRequested(); if (At(i) >= inclusive && At(i) < exclusive) { count++; } }
        if (count > maximumEvents) { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
        for (Int128 i = first; i <= last; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); Int128 time = At(i);
            if (time >= inclusive && time < exclusive) { visitor(new((long)time, kind, (ulong)i)); }
        }
    }
}
