// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;

namespace Monitor.Simulation.Determinism;

public readonly record struct DeterminismStepRecord(
    long SimTimeNs,
    ulong PhaseU64,
    long FilterYQ32,
    long TargetQ32,
    ulong OutcomeU64)
{
    public const int EncodedSize = 40;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < EncodedSize)
        {
            throw new ArgumentException("The destination is shorter than one step record.",
                nameof(destination));
        }

        BinaryPrimitives.WriteInt64LittleEndian(destination, SimTimeNs);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], PhaseU64);
        BinaryPrimitives.WriteInt64LittleEndian(destination[16..], FilterYQ32);
        BinaryPrimitives.WriteInt64LittleEndian(destination[24..], TargetQ32);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[32..], OutcomeU64);
    }
}

public sealed class DeterminismConformanceKernel
{
    public const long StepDurationNs = 4_000_000;

    private const string NoiseStreamId = "sensor.ecg.noise";
    private const string DriftStreamId = "physiology.hr.drift";
    private const string OutcomeStreamId = "therapy.outcome";
    private const ulong InitialPhaseU64 = 0x0123456789abcdef;
    private const long InitialFilterYQ32 = -123_456_789;
    private const ulong BasePhaseIncrement = 0x0ccccccccccccccd;
    private const long FilterAlphaQ32 = 214_748_365;

    private DeterministicRandomSource _noise;
    private DeterministicRandomSource _drift;
    private DeterministicRandomSource _outcome;
    private ulong _stepIndex;

    private DeterminismConformanceKernel(
        long simTimeNs,
        ulong phaseU64,
        long filterYQ32,
        DeterministicRandomSource noise,
        DeterministicRandomSource drift,
        DeterministicRandomSource outcome)
    {
        if (simTimeNs < 0 || simTimeNs % StepDurationNs != 0)
        {
            throw new DeterminismConfigurationException(
                "DeterminismKernel.InvalidSimTime",
                nameof(simTimeNs));
        }

        SimTimeNs = simTimeNs;
        PhaseU64 = phaseU64;
        FilterYQ32 = filterYQ32;
        _stepIndex = checked((ulong)(simTimeNs / StepDurationNs));
        _noise = noise;
        _drift = drift;
        _outcome = outcome;
    }

    public long SimTimeNs { get; private set; }

    public ulong PhaseU64 { get; private set; }

    public long FilterYQ32 { get; private set; }

    public ulong StepIndex => _stepIndex;

    public static DeterminismConformanceKernel Create(ReadOnlySpan<byte> rootSeed)
    {
        using DeterministicStreamFactory factory = new(rootSeed);
        return new DeterminismConformanceKernel(
            0,
            InitialPhaseU64,
            InitialFilterYQ32,
            factory.CreateStream(NoiseStreamId),
            factory.CreateStream(DriftStreamId),
            factory.CreateStream(OutcomeStreamId));
    }

    public static DeterminismConformanceKernel CreateFromLowercaseHex(string rootSeedHex)
    {
        using var factory =
            DeterministicStreamFactory.FromLowercaseHex(rootSeedHex);
        return new DeterminismConformanceKernel(
            0,
            InitialPhaseU64,
            InitialFilterYQ32,
            factory.CreateStream(NoiseStreamId),
            factory.CreateStream(DriftStreamId),
            factory.CreateStream(OutcomeStreamId));
    }

    public static DeterminismConformanceKernel Restore(DeterminismCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(checkpoint.Streams);
        Dictionary<string, DeterministicStreamState> streams = new(StringComparer.Ordinal);
        foreach (DeterministicStreamState stream in checkpoint.Streams)
        {
            if (!streams.TryAdd(stream.StreamId, stream))
            {
                throw InvalidStreamSet();
            }
        }

        if (streams.Count != 3 ||
            !streams.TryGetValue(NoiseStreamId, out DeterministicStreamState noise) ||
            !streams.TryGetValue(DriftStreamId, out DeterministicStreamState drift) ||
            !streams.TryGetValue(OutcomeStreamId, out DeterministicStreamState outcome))
        {
            throw InvalidStreamSet();
        }

        return new DeterminismConformanceKernel(
            checkpoint.SimTimeNs,
            checkpoint.PhaseU64,
            checkpoint.FilterYQ32,
            DeterministicRandomSource.Restore(noise),
            DeterministicRandomSource.Restore(drift),
            DeterministicRandomSource.Restore(outcome));

        static DeterminismConfigurationException InvalidStreamSet() => new(
            "DeterminismKernel.StreamSetMismatch",
            nameof(checkpoint));
    }

    public DeterminismStepRecord Step()
    {
        if (SimTimeNs > long.MaxValue - StepDurationNs)
        {
            throw new DeterminismConfigurationException(
                "DeterminismKernel.TimeOverflow",
                nameof(SimTimeNs));
        }

        DeterministicRandomSource noise = Clone(_noise);
        DeterministicRandomSource drift = Clone(_drift);
        DeterministicRandomSource outcome = Clone(_outcome);
        long noiseQ32 = noise.NextNormal12CenteredQ32();
        long driftRaw = checked((long)drift.UniformBelow(2_001) - 1_000);
        ulong phaseIncrement = unchecked(
            BasePhaseIncrement + ((_stepIndex % 7) << 40));
        ulong nextPhase = unchecked(PhaseU64 + phaseIncrement);
        Int128 rawTarget = FixedPointMath.RoundDivideTiesToEven(noiseQ32, 8) +
            (Int128)driftRaw * 65_536;
        long targetQ32 = FixedPointMath.Saturate(rawTarget).Value;
        long deltaQ32 = FixedPointMath.Subtract(targetQ32, FilterYQ32).Value;
        long correctionQ32 = FixedPointMath.MultiplyQ32(FilterAlphaQ32, deltaQ32).Value;
        long nextFilterYQ32 = FixedPointMath.Add(FilterYQ32, correctionQ32).Value;
        long nextSimTimeNs = SimTimeNs + StepDurationNs;
        ulong outcomeU64 = _stepIndex % 8 == 7 ? outcome.UniformBelow(17) : 0;

        _noise = noise;
        _drift = drift;
        _outcome = outcome;
        _stepIndex++;
        SimTimeNs = nextSimTimeNs;
        PhaseU64 = nextPhase;
        FilterYQ32 = nextFilterYQ32;
        return new DeterminismStepRecord(
            nextSimTimeNs,
            nextPhase,
            nextFilterYQ32,
            targetQ32,
            outcomeU64);
    }

    public byte[] Run(int stepCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stepCount);

        byte[] records = new byte[checked(stepCount * DeterminismStepRecord.EncodedSize)];
        for (int index = 0; index < stepCount; index++)
        {
            Step().WriteTo(records.AsSpan(index * DeterminismStepRecord.EncodedSize));
        }

        return records;
    }

    public DeterminismCheckpoint CaptureCheckpoint() => new(
        SimTimeNs,
        PhaseU64,
        FilterYQ32,
        [_noise.CaptureState(), _drift.CaptureState(), _outcome.CaptureState()]);

    private static DeterministicRandomSource Clone(DeterministicRandomSource source) =>
        DeterministicRandomSource.Restore(source.CaptureState());
}
