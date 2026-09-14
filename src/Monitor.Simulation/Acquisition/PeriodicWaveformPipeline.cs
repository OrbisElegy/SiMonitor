// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Acquisition;

public sealed class PeriodicWaveformPipelineException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record PeriodicWaveformPipelineState(PeriodicSignalState Generator,
    SignalAcquisitionDelayState Delay, WaveformBlockAssemblerState Assembler, uint QualityFlags);

// Serialized single-channel source. All clocks below are data-simulation clocks.
public sealed class PeriodicWaveformPipeline
{
    private PeriodicSignalGenerator _generator;
    private SignalAcquisitionDelayLine _delay;
    private WaveformBlockAssembler _assembler;
    private readonly uint _qualityFlags;
    private readonly Guid _channelId;

    private PeriodicWaveformPipeline(PeriodicSignalGenerator generator, SignalAcquisitionDelayLine delay,
        WaveformBlockAssembler assembler, uint qualityFlags)
    {
        _generator = generator;
        _delay = delay;
        _assembler = assembler;
        _qualityFlags = qualityFlags;
        PeriodicSignalState source = generator.CaptureState();
        SignalAcquisitionDelayState waiting = delay.CaptureState();
        WaveformBlockAssemblerState blocks = assembler.CaptureState();
        if (blocks.Planes.Count != 1 || blocks.StreamEpoch != source.Plan.StreamEpoch ||
            blocks.EpochAnchorSimTimeNs != source.Plan.EpochAnchorSimTimeNs ||
            waiting.ProfileId != source.Plan.ProfileId || waiting.StreamEpoch != source.Plan.StreamEpoch ||
            waiting.EpochAnchorSimTimeNs != source.Plan.EpochAnchorSimTimeNs ||
            waiting.ReleaseCursorSimTimeNs != source.Clock.CursorSimTimeNs ||
            waiting.NextInputSampleIndex != source.Clock.NextSampleIndex)
        { throw InvalidCheckpoint(); }
        WaveformBlockPlaneState plane = blocks.Planes[0];
        if (plane.Configuration.ProfileId != source.Plan.ProfileId ||
            plane.NextInputSampleIndex != waiting.NextInputSampleIndex - (ulong)waiting.PendingSamples.Count)
        { throw InvalidCheckpoint(); }
        _channelId = plane.Configuration.ChannelId;
        long[] table = source.Plan.TableQ32.ToArray();
        foreach (DelayedSignalSample sample in waiting.PendingSamples.Concat(plane.PendingSamples))
        {
            ulong phase = unchecked(source.Plan.InitialPhaseU64 + sample.SampleIndex * source.Plan.PhaseIncrementU64);
            long value = PeriodicLutLinear.Interpolate(table, phase).Value;
            short expected = checked((short)FixedPointMath.RoundDivideTiesToEven(value, FixedPointMath.Q32One));
            if (sample.NormalizedValue != expected || sample.QualityFlags != qualityFlags)
            { throw InvalidCheckpoint(); }
        }
    }

    public static PeriodicWaveformPipeline Start(PeriodicSignalPlan source, Guid sessionId, Guid instanceId,
        ulong timebaseEpoch, ulong configurationRevision, ulong firstBlockSequence,
        WaveformBlockPlaneConfiguration channel, int delayCapacity, uint qualityFlags)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(channel);
        return new(PeriodicSignalGenerator.Start(source),
            SignalAcquisitionDelayLine.Start(source.ProfileId, source.StreamEpoch, source.EpochAnchorSimTimeNs, delayCapacity),
            WaveformBlockAssembler.Start(sessionId, instanceId, timebaseEpoch, source.StreamEpoch,
                configurationRevision, firstBlockSequence, source.EpochAnchorSimTimeNs, 1, [channel]), qualityFlags);
    }

    public PeriodicWaveformPipelineState CaptureState() =>
        new(_generator.CaptureState(), _delay.CaptureState(), _assembler.CaptureState(), _qualityFlags);

    public static PeriodicWaveformPipeline Restore(PeriodicWaveformPipelineState state)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(state);
            return new(PeriodicSignalGenerator.Restore(state.Generator), SignalAcquisitionDelayLine.Restore(state.Delay),
                WaveformBlockAssembler.Restore(state.Assembler), state.QualityFlags);
        }
        catch (ArgumentException) { throw InvalidCheckpoint(); }
        catch (OverflowException) { throw InvalidCheckpoint(); }
    }

    public IReadOnlyList<byte[]> AdvanceTo(long simTimeNs, int maximumSamples, int maximumBlocks,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumBlocks is < 1 or > WaveformBlockAssembler.MaximumBufferedBlockCount)
        { throw new PeriodicWaveformPipelineException("PeriodicPipeline.InvalidBlockLimit", nameof(maximumBlocks)); }
        PeriodicWaveformPipeline trial = Restore(CaptureState());
        IReadOnlyList<GeneratedSignalSample> generated = trial._generator.GenerateBefore(simTimeNs, maximumSamples, cancellationToken);
        List<byte[]> output = [];
        foreach (GeneratedSignalSample sample in generated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Release(sample.Tick.SimTimeNs);
            trial._delay.Enqueue(sample.Tick, sample.NormalizedValue, _qualityFlags);
        }
        Release(simTimeNs);
        cancellationToken.ThrowIfCancellationRequested();
        _generator = trial._generator;
        _delay = trial._delay;
        _assembler = trial._assembler;
        return output.AsReadOnly();

        void Release(long cursor)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (WaveformEnvelope block in trial._assembler.Push(_channelId, trial._delay.DrainAvailable(cursor)))
            {
                if (output.Count == maximumBlocks)
                { throw new PeriodicWaveformPipelineException("PeriodicPipeline.BlockLimitExceeded", nameof(maximumBlocks)); }
                output.Add(WaveformEnvelopeCodec.EncodeRaw(block));
            }
        }
    }

    private static PeriodicWaveformPipelineException InvalidCheckpoint() =>
        new("PeriodicPipeline.InvalidCheckpoint", "state");
}
