// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Specs;

internal static class PeriodicSignalGeneratorSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PeriodicSamplesUseFrozenInterpolationAndClock), PeriodicSamplesUseFrozenInterpolationAndClock),
        new(nameof(PeriodicBatchesAndRestoreKeepIdenticalPhase), PeriodicBatchesAndRestoreKeepIdenticalPhase),
        new(nameof(PeriodicFailurePreservesGeneratorState), PeriodicFailurePreservesGeneratorState),
        new(nameof(PeriodicPlansAndCheckpointsAreOwnedAndValidated), PeriodicPlansAndCheckpointsAreOwnedAndValidated),
        new(nameof(PeriodicSamplesFeedAcquisitionWithoutClockSubstitution), PeriodicSamplesFeedAcquisitionWithoutClockSubstitution),
    ];

    private static PeriodicSignalPlan Plan() => new("AcqECGMonitor250@1", 7, 1_000_000_000, 0,
        0x2000000000000000, [0, 3 * FixedPointMath.Q32One, 0, -3 * FixedPointMath.Q32One]);

    private static void PeriodicSamplesUseFrozenInterpolationAndClock()
    {
        PeriodicSignalGenerator generator = PeriodicSignalGenerator.Start(Plan());
        IReadOnlyList<GeneratedSignalSample> samples = generator.GenerateBefore(1_032_000_000, 8);
        short[] expected = [0, 2, 3, 2, 0, -2, -3, -2];
        Check.That(samples.Select(sample => sample.NormalizedValue).SequenceEqual(expected) &&
            samples[1].ValueQ32 == 3 * FixedPointMath.Q32One / 2 &&
            samples[^1].Tick.SimTimeNs == 1_028_000_000 && samples[^1].Tick.SampleIndex == 7 &&
            generator.GenerateBefore(1_036_000_000, 1)[0].PhaseU64 == 0,
            "periodic interpolation must use ties-to-even, source sample clocks and exact wrapping phase");
    }

    private static void PeriodicBatchesAndRestoreKeepIdenticalPhase()
    {
        PeriodicSignalGenerator whole = PeriodicSignalGenerator.Start(Plan());
        PeriodicSignalGenerator split = PeriodicSignalGenerator.Start(Plan());
        List<GeneratedSignalSample> collected = [.. split.GenerateBefore(1_005_000_000, 2)];
        PeriodicSignalGenerator restored = PeriodicSignalGenerator.Restore(split.CaptureState());
        collected.AddRange(restored.GenerateBefore(1_032_000_000, 6));
        Check.That(collected.SequenceEqual(whole.GenerateBefore(1_032_000_000, 8)) &&
            restored.GenerateBefore(1_032_000_000, 1).Count == 0,
            "unaligned batch boundaries, repeats and checkpoint recovery cannot change future samples");
    }

    private static void PeriodicFailurePreservesGeneratorState()
    {
        PeriodicSignalGenerator generator = PeriodicSignalGenerator.Start(Plan());
        PeriodicSignalState before = generator.CaptureState();
        Reject(() => generator.GenerateBefore(1_032_000_000, 7), "PeriodicSignal.SampleLimitExceeded");
        Reject(() => generator.GenerateBefore(1_032_000_000, 0), "PeriodicSignal.InvalidSampleLimit");
        bool reversed = false;
        try { generator.GenerateBefore(999_999_999, 1); }
        catch (SignalSampleClockException) { reversed = true; }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { generator.GenerateBefore(1_032_000_000, 8, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(reversed && cancelled && generator.CaptureState() == before && generator.GenerateBefore(1_032_000_000, 8).Count == 8,
            "invalid bounds and cancellation leave source clock and phase unchanged for retry");
    }

    private static void PeriodicPlansAndCheckpointsAreOwnedAndValidated()
    {
        long[] table = [0, FixedPointMath.Q32One, 0, -FixedPointMath.Q32One];
        PeriodicSignalGenerator generator = PeriodicSignalGenerator.Start(Plan() with { TableQ32 = table });
        table[0] = long.MaxValue;
        PeriodicSignalState state = generator.CaptureState();
        bool immutable = false;
        try { ((IList<long>)state.Plan.TableQ32)[0] = 99; }
        catch (NotSupportedException) { immutable = true; }
        Reject(() => PeriodicSignalGenerator.Start(Plan() with { TableQ32 = new long[5] }), "PeriodicSignal.InvalidPlan");
        Reject(() => PeriodicSignalGenerator.Start(Plan() with { TableQ32 = table }), "PeriodicSignal.InvalidAmplitude");
        Reject(() => PeriodicSignalGenerator.Start(Plan() with { PhaseIncrementU64 = 0 }), "PeriodicSignal.InvalidPlan");
        Reject(() => PeriodicSignalGenerator.Restore(state with { Clock = state.Clock with { StreamEpoch = 8 } }), "PeriodicSignal.InvalidCheckpoint");
        Check.That(immutable && generator.GenerateBefore(1_000_000_001, 1)[0].NormalizedValue == 0,
            "caller edits cannot change accepted waveform tables and mismatched checkpoint identities reject");
    }

    private static void PeriodicSamplesFeedAcquisitionWithoutClockSubstitution()
    {
        PeriodicSignalPlan plan = Plan();
        PeriodicSignalGenerator generator = PeriodicSignalGenerator.Start(plan);
        SignalAcquisitionDelayLine delay = SignalAcquisitionDelayLine.Start(plan.ProfileId, plan.StreamEpoch, plan.EpochAnchorSimTimeNs, 50);
        IReadOnlyList<GeneratedSignalSample> samples = generator.GenerateBefore(1_200_000_000, 50);
        foreach (GeneratedSignalSample sample in samples) { delay.Enqueue(sample.Tick, sample.NormalizedValue, 0x80000000); }
        Check.That(delay.DrainAvailable(1_039_999_999).Count == 0, "ECG acquisition keeps its frozen 40ms latency");
        IReadOnlyList<DelayedSignalSample> available = delay.DrainAvailable(1_236_000_000);
        Check.That(available.Count == 50 && available[^1].SourceSimTimeNs == 1_196_000_000 &&
            available[^1].AvailableSimTimeNs == 1_236_000_000 && available.All(sample => sample.QualityFlags == 0x80000000) &&
            available.Select(sample => sample.NormalizedValue).SequenceEqual(samples.Select(sample => sample.NormalizedValue)),
            "generated samples integrate with acquisition while preserving raw values and caller-supplied quality flags");
        Guid channel = Guid.Parse("11111111-1111-4111-8111-111111111111");
        WaveformBlockAssembler assembler = WaveformBlockAssembler.Start(channel,
            Guid.Parse("22222222-2222-4222-8222-222222222222"), 3, plan.StreamEpoch, 11, 100,
            plan.EpochAnchorSimTimeNs, 1, [new(channel, plan.ProfileId, 1, 1, 0, 1)]);
        byte[] wire = WaveformEnvelopeCodec.EncodeRaw(assembler.Push(channel, available).Single());
        WaveformEnvelope decoded = WaveformEnvelopeCodec.Decode(wire);
        Check.That(decoded.StartSimTimeNs == plan.EpochAnchorSimTimeNs && decoded.DurationNs == 200_000_000 &&
            decoded.Planes.Single().Samples.SequenceEqual(samples.Select(sample => sample.NormalizedValue)) &&
            decoded.Planes.Single().QualityRanges.Single() == new WaveformQualityRange(0, 50, 0x80000000),
            "generated samples must survive the real 200ms block assembly and verified binary codec unchanged");
    }

    private static void Reject(Action action, string reason)
    {
        try { action(); throw new InvalidOperationException("expected generator rejection"); }
        catch (PeriodicSignalGeneratorException exception) { Check.That(exception.ReasonCode == reason, reason); }
    }
}
