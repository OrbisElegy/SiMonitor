// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Authored smooth variation of a reservoir's per-beat ejection strength, used to
// make teaching pressures drift around their level. Preparation is deterministic
// and evaluation is indexed by beat time; it is not a baroreflex or circulation model.
public sealed class SeededVascularVariation : IEquatable<SeededVascularVariation>
{
    public const string PatternId = "SeededVascularDrift@1";
    public const long KnotPeriodNs = 15_000_000_000;
    public const int KnotCount = 64;
    public const int MaximumAmplitudePermille = 400;
    private readonly int[] _offsets = new int[KnotCount];
    public int AmplitudePermille { get; }
    public string SeedHex { get; }
    public string StreamName { get; }
    public DeterministicStreamState PreparedState { get; }

    public SeededVascularVariation(int amplitudePermille, string seedHex, string streamName)
    {
        if (amplitudePermille is < 0 or > MaximumAmplitudePermille || string.IsNullOrWhiteSpace(streamName))
        { throw new ArgumentException("VascularVariation.InvalidRange"); }
        using var factory = DeterministicStreamFactory.FromLowercaseHex(seedHex);
        var random = factory.CreateStream(streamName);
        AmplitudePermille = amplitudePermille;
        SeedHex = seedHex;
        StreamName = streamName;
        // Alternate excursions of 60..100% of the amplitude so that the drift is
        // visible within a minute; the first knot and the wrap stay at the level.
        int minimum = (amplitudePermille * 3 + 4) / 5;
        for (int i = 1; i < KnotCount - 1; i += 2)
        {
            int sign = random.UniformBelow(2) == 0 ? -1 : 1;
            _offsets[i] = sign * (minimum + (int)random.UniformBelow((ulong)(amplitudePermille - minimum + 1)));
            _offsets[i + 1] = -sign * (minimum + (int)random.UniformBelow((ulong)(amplitudePermille - minimum + 1)));
        }
        PreparedState = random.CaptureState();
    }

    // Reprepared schedules have the same value even though their private arrays differ.
    public bool Equals(SeededVascularVariation? other) => other is not null &&
        AmplitudePermille == other.AmplitudePermille && SeedHex == other.SeedHex && StreamName == other.StreamName;
    public override bool Equals(object? obj) => obj is SeededVascularVariation other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(AmplitudePermille, SeedHex, StreamName);

    // Gain in permille at a beat's mechanical time, smoothstep between knots.
    public int GainPermille(long simTimeNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(simTimeNs);
        int slot = (int)(simTimeNs / KnotPeriodNs % KnotCount);
        long t = simTimeNs % KnotPeriodNs;
        Int128 duration = KnotPeriodNs;
        Int128 weight = (Int128)t * t * (3 * duration - 2 * t);
        return 1000 + _offsets[slot] + (int)FixedPointMath.RoundDivideTiesToEven(
            (_offsets[(slot + 1) % KnotCount] - _offsets[slot]) * weight, duration * duration * duration);
    }
}
