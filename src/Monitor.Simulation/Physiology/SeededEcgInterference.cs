// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Acquisition interference, indexed by source time rather than draw/call order.
// Electrode amplitudes are half the configured lead-to-lead maximum excursion.
public sealed class SeededEcgInterference : IEquatable<SeededEcgInterference>
{
    public const string PatternId = "SeededEcgInterference@1";
    private readonly ulong[] _keys;
    public string SeedHex { get; }
    public int SpikeAmplitudeMicrovolts { get; }
    public int SpikeProbabilityPermille { get; }
    public int BaselineAmplitudeMicrovolts { get; }
    public int BaselineKnotMilliseconds { get; }

    public SeededEcgInterference(string seedHex, int spikeAmplitudeMicrovolts,
        int spikeProbabilityPermille, int baselineAmplitudeMicrovolts, int baselineKnotMilliseconds)
    {
        if (spikeAmplitudeMicrovolts is < 0 or > 5000 || spikeProbabilityPermille is < 0 or > 1000 ||
            baselineAmplitudeMicrovolts is < 0 or > 2000 || baselineKnotMilliseconds is < 200 or > 10000)
        { throw new ArgumentException("EcgInterference.InvalidConfiguration"); }
        using var factory = DeterministicStreamFactory.FromLowercaseHex(seedHex);
        var random = factory.CreateStream("physiology.ecg.interference.v1");
        _keys = Enumerable.Range(0, 20).Select(_ => random.NextUInt64()).ToArray();
        SeedHex = seedHex;
        SpikeAmplitudeMicrovolts = spikeAmplitudeMicrovolts;
        SpikeProbabilityPermille = spikeProbabilityPermille;
        BaselineAmplitudeMicrovolts = baselineAmplitudeMicrovolts;
        BaselineKnotMilliseconds = baselineKnotMilliseconds;
    }

    public long ElectrodeMicrovoltsQ32(EcgElectrode electrode, long simTimeNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(simTimeNs);
        if (!Enum.IsDefined(electrode)) { throw new ArgumentOutOfRangeException(nameof(electrode)); }
        if (electrode == EcgElectrode.RL) { return 0; }
        int index = (int)electrode;
        long durationNs = BaselineKnotMilliseconds * 1_000_000L;
        ulong knot = (ulong)(simTimeNs / durationNs);
        long t = simTimeNs % durationNs;
        long from = SignedAmplitude(_keys[index], knot, BaselineAmplitudeMicrovolts);
        long to = SignedAmplitude(_keys[index], knot + 1, BaselineAmplitudeMicrovolts);
        long weightPpm = (long)FixedPointMath.RoundDivideTiesToEven(
            (Int128)t * t * (3 * durationNs - 2 * t) * 1_000_000, (Int128)durationNs * durationNs * durationNs);
        long baseline = from + (long)FixedPointMath.RoundDivideTiesToEven(
            (Int128)(to - from) * weightPpm, 1_000_000);

        // Each electrode has at most one 4..12 ms spike in each 100 ms window.
        ulong slot = (ulong)(simTimeNs / 100_000_000);
        ulong draw = Mix(unchecked(_keys[index + 10] + slot));
        int sample = (int)(simTimeNs % 100_000_000 / 4_000_000);
        int start = (int)((draw >> 10) % 23);
        int width = 1 + (int)((draw >> 20) % 3);
        long spike = draw % 1000 < (ulong)SpikeProbabilityPermille && sample >= start && sample < start + width
            ? SignedAmplitude(_keys[index + 10], slot, SpikeAmplitudeMicrovolts) : 0;
        return checked(baseline + spike);
    }

    public long LeadIIMicrovoltsQ32(long simTimeNs) =>
        ElectrodeMicrovoltsQ32(EcgElectrode.LL, simTimeNs) - ElectrodeMicrovoltsQ32(EcgElectrode.RA, simTimeNs);

    private static long SignedAmplitude(ulong key, ulong index, int amplitudeMicrovolts) =>
        checked(((long)(Mix(unchecked(key + index)) >> 32) - 2_147_483_648L) * amplitudeMicrovolts);

    // SplitMix64 finalizer: explicit wrapping arithmetic makes indexed draws portable.
    private static ulong Mix(ulong value)
    {
        value = unchecked((value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94d049bb133111ebUL);
        return value ^ (value >> 31);
    }

    public bool Equals(SeededEcgInterference? other) => other is not null && SeedHex == other.SeedHex &&
        SpikeAmplitudeMicrovolts == other.SpikeAmplitudeMicrovolts && SpikeProbabilityPermille == other.SpikeProbabilityPermille &&
        BaselineAmplitudeMicrovolts == other.BaselineAmplitudeMicrovolts && BaselineKnotMilliseconds == other.BaselineKnotMilliseconds;
    public override bool Equals(object? obj) => obj is SeededEcgInterference other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(SeedHex, SpikeAmplitudeMicrovolts,
        SpikeProbabilityPermille, BaselineAmplitudeMicrovolts, BaselineKnotMilliseconds);
}
