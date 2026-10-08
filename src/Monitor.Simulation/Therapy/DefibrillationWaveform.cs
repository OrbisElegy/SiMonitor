// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Therapy;

public enum DefibrillationWaveformKind
{
    MonophasicDampedSine,
    MonophasicTruncatedExponential,
    BiphasicTruncatedExponential,
    RectilinearBiphasic,
}

// Current at the discharge circuit, NOT ECG microvolts or an energy prescription.
// Explicit authored phase durations and amplitudes do not model impedance compensation.
public sealed record DefibrillationWaveformPlan(DefibrillationWaveformKind Kind,
    int PeakCurrentMilliamps, long FirstPhaseDurationNs, long SecondPhaseDurationNs,
    int SecondPhaseAmplitudePermille = 500, long InterphaseGapNs = 0);
public readonly record struct DefibrillationCurrentSample(long SimTimeNs, int CurrentMilliamps);

// Immutable one-shot source. The caller supplies the actual delivered-shock source
// timestamp after the therapy authority accepts delivery; charging creates no source.
public sealed class DefibrillationWaveform
{
    public const string EvidenceId = "DefibrillationDischargeIllustration@1";
    private const int Scale = 1_000_000;
    // Authored normalized damped half-sine samples, including both zero endpoints.
    private static readonly int[] DampedSine =
        [0, 81731, 160233, 235411, 307184, 375483, 440252, 501443,
         559024, 612971, 663271, 709924, 752935, 792324, 828116, 860347,
         889061, 914309, 936151, 954652, 969886, 981932, 990874, 996801,
         999811, 1000000, 997473, 992337, 984701, 974678, 962383, 947933,
         931447, 913044, 892845, 870969, 847540, 822676, 796498, 769126,
         740678, 711270, 681017, 650032, 618427, 586309, 553785, 520958,
         487927, 454790, 421641, 388569, 355661, 323002, 290669, 258738,
         227282, 196368, 166060, 136417, 107495, 79347, 52020, 25558,
         0];
    private static readonly int[] Decay = CreateDecay();
    public DefibrillationWaveformPlan Plan { get; }
    public long DeliveredAtSimTimeNs { get; }
    public long EndSimTimeNs { get; }

    private DefibrillationWaveform(DefibrillationWaveformPlan plan, long deliveredAtSimTimeNs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        bool monophasic = plan.Kind is DefibrillationWaveformKind.MonophasicDampedSine or DefibrillationWaveformKind.MonophasicTruncatedExponential;
        if (!Enum.IsDefined(plan.Kind) || plan.PeakCurrentMilliamps is < 1 or > 100_000 ||
            plan.FirstPhaseDurationNs is < 100_000 or > 20_000_000 ||
            (monophasic ? plan.SecondPhaseDurationNs != 0 || plan.InterphaseGapNs != 0 : plan.SecondPhaseDurationNs is < 100_000 or > 20_000_000) ||
            plan.InterphaseGapNs is < 0 or > 1_000_000 || plan.SecondPhaseAmplitudePermille is < 1 or > 1000 || deliveredAtSimTimeNs < 0)
        { throw new ArgumentException("Defibrillation.InvalidPlan", nameof(plan)); }
        long duration = plan.FirstPhaseDurationNs + plan.InterphaseGapNs + plan.SecondPhaseDurationNs;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(deliveredAtSimTimeNs, long.MaxValue - duration);
        Plan = plan;
        DeliveredAtSimTimeNs = deliveredAtSimTimeNs;
        EndSimTimeNs = deliveredAtSimTimeNs + duration;
    }

    public static DefibrillationWaveform Create(DefibrillationWaveformPlan plan, long deliveredAtSimTimeNs) => new(plan, deliveredAtSimTimeNs);

    public int EvaluateCurrentMilliamps(long simTimeNs)
    {
        if (simTimeNs < DeliveredAtSimTimeNs || simTimeNs >= EndSimTimeNs) { return 0; }
        long elapsed = simTimeNs - DeliveredAtSimTimeNs;
        int normalized;
        if (elapsed < Plan.FirstPhaseDurationNs)
        {
            normalized = Plan.Kind switch
            {
                DefibrillationWaveformKind.MonophasicDampedSine => Interpolate(DampedSine, elapsed, Plan.FirstPhaseDurationNs),
                DefibrillationWaveformKind.RectilinearBiphasic => Scale,
                _ => Interpolate(Decay, elapsed, Plan.FirstPhaseDurationNs),
            };
            return (int)FixedPointMath.RoundDivideTiesToEven((Int128)Plan.PeakCurrentMilliamps * normalized, Scale);
        }
        elapsed -= Plan.FirstPhaseDurationNs + Plan.InterphaseGapNs;
        if (elapsed < 0) { return 0; }
        normalized = Interpolate(Decay, elapsed, Plan.SecondPhaseDurationNs);
        return (int)FixedPointMath.RoundDivideTiesToEven(-(Int128)Plan.PeakCurrentMilliamps * Plan.SecondPhaseAmplitudePermille * normalized, Scale * 1000L);
    }

    public IReadOnlyList<DefibrillationCurrentSample> Sample(long fromSimTimeNs, long toExclusiveSimTimeNs,
        long samplePeriodNs, int maximumSamples, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (fromSimTimeNs < 0 || toExclusiveSimTimeNs < fromSimTimeNs || samplePeriodNs <= 0 || maximumSamples is < 1 or > 1_000_000)
        { throw new ArgumentException("Defibrillation.InvalidSampleRange"); }
        Int128 count = ((Int128)toExclusiveSimTimeNs - fromSimTimeNs + samplePeriodNs - 1) / samplePeriodNs;
        if (count > maximumSamples) { throw new ArgumentException("Defibrillation.SampleLimitExceeded", nameof(maximumSamples)); }
        var samples = new DefibrillationCurrentSample[(int)count];
        for (int i = 0; i < samples.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long time = (long)((Int128)fromSimTimeNs + (Int128)i * samplePeriodNs);
            samples[i] = new(time, EvaluateCurrentMilliamps(time));
        }
        return Array.AsReadOnly(samples);
    }

    private static int Interpolate(int[] table, long elapsedNs, long durationNs)
    {
        Int128 position = (Int128)elapsedNs * (table.Length - 1);
        int index = (int)(position / durationNs);
        return table[index] + (int)FixedPointMath.RoundDivideTiesToEven(
            (Int128)(table[index + 1] - table[index]) * (position % durationNs), durationNs);
    }

    private static int[] CreateDecay()
    {
        int[] values = new int[65];
        values[0] = Scale;
        for (int i = 1; i < values.Length; i++)
        { values[i] = (int)FixedPointMath.RoundDivideTiesToEven((Int128)values[i - 1] * 98, 100); }
        return values;
    }
}
