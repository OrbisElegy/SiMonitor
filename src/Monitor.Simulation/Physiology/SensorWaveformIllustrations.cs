// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public enum PressureMeasurementIllustration
{
    Reference,
    Overdamped
}

public enum PlethIllustration
{
    Reference,
    LowPerfusion,
    MotionArtifact
}

// Acquisition-time modes, independent of ventricular mechanics and gas transport.
public sealed record SensorWaveformIllustration(long FromSimTimeNs,
    PressureMeasurementIllustration PressureMeasurement = PressureMeasurementIllustration.Reference,
    PlethIllustration PlethShape = PlethIllustration.Reference)
{
    public SeededEcgInterference? EcgInterference { get; init; }
    // A continued source starts from its effective weights at the switch.
    // Null keeps initial/restarted templates at their selected steady state.
    public int? PlethTransitionFromGainPpm { get; init; }
    public int PlethTransitionFromMotionPpm { get; init; }
}

internal static class SensorWaveformIllustrations
{
    private const int UnityPpm = 1_000_000;
    private const long PlethTransitionDurationNs = 1_000_000_000;
    private static readonly SensorWaveformIllustration Reference = new(0);

    internal static SensorWaveformIllustration At(IReadOnlyList<SensorWaveformIllustration> history, long timeNs)
    {
        for (int index = history.Count - 1; index >= 0; index--)
        { if (history[index].FromSimTimeNs <= timeNs) { return history[index]; } }
        return Reference;
    }

    internal static void Validate(IReadOnlyList<SensorWaveformIllustration> history, string profileId,
        long epochAnchorSimTimeNs, long cursorSimTimeNs)
    {
        if (history is null || history.Count > 512) { throw Invalid(); }
        long previous = -1;
        foreach (var item in history)
        {
            if (item is null || item.FromSimTimeNs < epochAnchorSimTimeNs || item.FromSimTimeNs > cursorSimTimeNs ||
                item.FromSimTimeNs <= previous || !Enum.IsDefined(item.PressureMeasurement) || !Enum.IsDefined(item.PlethShape) ||
                item.PressureMeasurement != PressureMeasurementIllustration.Reference && profileId != "AcqPressure125@1" ||
                item.PlethShape != PlethIllustration.Reference && profileId != "AcqPleth125@1" ||
                item.EcgInterference is not null && profileId != "AcqECGMonitor250@1" ||
                item.PlethTransitionFromGainPpm is { } gain && (gain is < 80_000 or > UnityPpm || profileId != "AcqPleth125@1") ||
                item.PlethTransitionFromMotionPpm is < 0 or > UnityPpm ||
                item.PlethTransitionFromGainPpm is null && item.PlethTransitionFromMotionPpm != 0)
            { throw Invalid(); }
            previous = item.FromSimTimeNs;
        }
    }

    internal static IReadOnlyList<SensorWaveformIllustration> ContinueWith(IReadOnlyList<SensorWaveformIllustration> history,
        SensorWaveformIllustration next, long boundarySimTimeNs, long pendingFromSimTimeNs)
    {
        var current = At(history, boundarySimTimeNs);
        if (current.PressureMeasurement == next.PressureMeasurement && current.PlethShape == next.PlethShape &&
            Equals(current.EcgInterference, next.EcgInterference))
        { return history; }
        if (current.PlethShape != next.PlethShape)
        {
            var weights = PlethWeights(current, boundarySimTimeNs);
            next = next with
            {
                PlethTransitionFromGainPpm = weights.GainPpm,
                PlethTransitionFromMotionPpm = weights.MotionPpm
            };
        }
        var retained = history.Where(item => item.FromSimTimeNs < boundarySimTimeNs).ToList();
        int keepFrom = retained.FindLastIndex(item => item.FromSimTimeNs <= pendingFromSimTimeNs);
        if (keepFrom > 0) { retained.RemoveRange(0, keepFrom); }
        retained.Add(next with { FromSimTimeNs = boundarySimTimeNs });
        if (retained.Count > 512) { throw Invalid(); }
        return Array.AsReadOnly(retained.ToArray());
    }

    internal static long Apply(long valueQ32, long timeNs, long epochAnchorSimTimeNs,
        SensorWaveformIllustration illustration, PhysiologySignalGenerator generator, CancellationToken cancellationToken = default)
    {
        if (illustration.PressureMeasurement == PressureMeasurementIllustration.Overdamped)
        {
            // Positive triangular FIR: two cascaded 320 ms moving averages.
            // A unit DC gain preserves steady mean pressure while suppressing
            // narrow peaks/notches. This is an authored transducer response,
            // not a fitted catheter model or a change in ejected blood volume.
            Int128 weighted = 0;
            for (int index = 0; index < 39; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int weight = index < 20 ? index + 1 : 39 - index;
                long sampleTimeNs = Math.Max(epochAnchorSimTimeNs, timeNs - index * 16_000_000L);
                long value = index == 0 ? valueQ32 : generator.EvaluateAt(sampleTimeNs, cancellationToken);
                weighted += (Int128)value * weight;
            }
            valueQ32 = (long)FixedPointMath.RoundDivideTiesToEven(weighted, 400);
        }
        var weights = PlethWeights(illustration, timeNs);
        if (weights.GainPpm != UnityPpm)
        { valueQ32 = (long)FixedPointMath.RoundDivideTiesToEven((Int128)valueQ32 * weights.GainPpm, UnityPpm); }
        if (weights.MotionPpm != 0)
        {
            valueQ32 = checked(valueQ32 + (long)FixedPointMath.RoundDivideTiesToEven(
                (Int128)MotionCounts(timeNs - epochAnchorSimTimeNs) * FixedPointMath.Q32One * weights.MotionPpm, UnityPpm));
        }
        return checked(valueQ32 + (illustration.EcgInterference?.LeadIIMicrovoltsQ32(timeNs) ?? 0));
    }

    internal static uint Quality(uint qualityFlags, SensorWaveformIllustration illustration, long timeNs) =>
        illustration.PlethShape == PlethIllustration.MotionArtifact || PlethWeights(illustration, timeNs).MotionPpm != 0
            ? qualityFlags | 1u : qualityFlags;

    private static (int GainPpm, int MotionPpm) PlethWeights(SensorWaveformIllustration illustration, long timeNs)
    {
        int targetGain = illustration.PlethShape == PlethIllustration.LowPerfusion ? 80_000 : UnityPpm;
        int targetMotion = illustration.PlethShape == PlethIllustration.MotionArtifact ? UnityPpm : 0;
        long elapsedNs = timeNs - illustration.FromSimTimeNs;
        if (illustration.PlethTransitionFromGainPpm is not { } initialGain || elapsedNs >= PlethTransitionDurationNs)
        { return (targetGain, targetMotion); }
        if (elapsedNs <= 0) { return (initialGain, illustration.PlethTransitionFromMotionPpm); }
        // Fixed-time smoothstep changes the envelope, leaving the acquired
        // pulse phase intact. Zero endpoint slopes avoid a cut in mid-upstroke.
        // Capturing these effective weights also makes rapid reversals continuous.
        int progressPpm = (int)FixedPointMath.RoundDivideTiesToEven(
            (Int128)elapsedNs * elapsedNs * (3 * PlethTransitionDurationNs - 2 * elapsedNs) * UnityPpm,
            (Int128)PlethTransitionDurationNs * PlethTransitionDurationNs * PlethTransitionDurationNs);
        int Interpolate(int from, int to) => from + (int)FixedPointMath.RoundDivideTiesToEven(
            (Int128)(to - from) * progressPpm, UnityPpm);
        return (Interpolate(initialGain, targetGain), Interpolate(illustration.PlethTransitionFromMotionPpm, targetMotion));
    }

    private static long MotionCounts(long elapsedNs)
    {
        // Incommensurate bounded triangle components and a slow envelope give
        // reproducible sensor movement without inventing physiological beats.
        long envelope = 450 + (Triangle(elapsedNs, 3_700_000_000) + 1000) * 550 / 2000;
        long drift = Triangle(elapsedNs, 1_710_000_000) * 360 / 1000;
        long flutter = Triangle(elapsedNs, 263_000_000) * 580 / 1000 +
            Triangle(elapsedNs, 137_000_000) * 220 / 1000;
        return drift + flutter * envelope / 1000;
    }

    private static long Triangle(long elapsedNs, long periodNs)
    {
        long phase = elapsedNs % periodNs;
        return phase < periodNs / 2 ? -1000 + 4000 * phase / periodNs : 3000 - 4000 * phase / periodNs;
    }

    private static ArgumentException Invalid() => new("PhysiologyGroup.InvalidSensorIllustration");
}
