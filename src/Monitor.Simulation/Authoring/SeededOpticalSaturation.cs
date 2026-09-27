// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Authoring;

// Authored smooth target variation, not oxygen transport or tissue calibration.
// Preparation is deterministic; evaluation has no mutable cursor/random state.
public sealed class SeededOpticalSaturation
{
    public const long KnotPeriodNs = 30_000_000_000;
    public const int KnotCount = 64;
    private readonly int[] _offsets = new int[KnotCount];
    public int TargetMilliPercent { get; }
    public int AmplitudeMilliPercent { get; }
    public DeterministicStreamState PreparedState { get; }

    public SeededOpticalSaturation(int targetMilliPercent, int amplitudeMilliPercent, string seedHex)
    {
        if (targetMilliPercent is < 75000 or > 99000 || amplitudeMilliPercent is < 0 or > 2000 ||
            targetMilliPercent - amplitudeMilliPercent < 75000 || targetMilliPercent + amplitudeMilliPercent > 99000)
        { throw new ArgumentException("OpticalVariation.InvalidRange"); }
        using var factory = DeterministicStreamFactory.FromLowercaseHex(seedHex);
        var random = factory.CreateStream("physiology.optical.saturation");
        TargetMilliPercent = targetMilliPercent; AmplitudeMilliPercent = amplitudeMilliPercent;
        for (int i = 1; i < KnotCount; i++)
        { _offsets[i] = (int)random.UniformBelow((ulong)(2 * amplitudeMilliPercent + 1)) - amplitudeMilliPercent; }
        PreparedState = random.CaptureState();
    }

    public int At(long sampleTimeNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleTimeNs);
        int slot = (int)(sampleTimeNs / KnotPeriodNs % KnotCount);
        long t = sampleTimeNs % KnotPeriodNs;
        // Smoothstep 3u^2-2u^3, computed as an exact rational. Zero slope at
        // every knot, including the 32-minute wrap back to the nominal target.
        Int128 duration = KnotPeriodNs;
        Int128 weight = (Int128)t * t * (3 * duration - 2 * t);
        return TargetMilliPercent + _offsets[slot] + (int)FixedPointMath.RoundDivideTiesToEven(
            (_offsets[(slot + 1) % KnotCount] - _offsets[slot]) * weight, duration * duration * duration);
    }
}
