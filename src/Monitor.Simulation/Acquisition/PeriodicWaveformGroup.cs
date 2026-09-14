// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Acquisition;

public sealed class PeriodicWaveformGroupException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record PeriodicWaveformChannelPlan(PeriodicSignalPlan Source,
    WaveformBlockPlaneConfiguration Plane, int DelayCapacity, uint QualityFlags);
public sealed record PeriodicWaveformChannelState(Guid ChannelId, PeriodicSignalState Generator,
    SignalAcquisitionDelayState Delay, uint QualityFlags);
public sealed record PeriodicWaveformGroupState(IReadOnlyList<PeriodicWaveformChannelState> Channels,
    WaveformBlockAssemblerState Assembler);

// Serialized multi-rate generation with one shared, bounded block assembler.
public sealed class PeriodicWaveformGroup
{
    private readonly record struct Channel(Guid Id, PeriodicSignalGenerator Generator,
        SignalAcquisitionDelayLine Delay, uint QualityFlags);
    private Channel[] _channels;
    private WaveformBlockAssembler _assembler;

    private PeriodicWaveformGroup(PeriodicWaveformGroupState state)
    {
        if (state.Channels is null || state.Channels.Count is < 1 or > WaveformEnvelopeCodec.MaximumPlaneCount)
        { throw InvalidCheckpoint(); }
        _assembler = WaveformBlockAssembler.Restore(state.Assembler);
        WaveformBlockAssemblerState assembly = _assembler.CaptureState();
        if (assembly.Planes.Count != state.Channels.Count) { throw InvalidCheckpoint(); }
        _channels = new Channel[state.Channels.Count];
        long? cursor = null;
        string? previous = null;
        for (int index = 0; index < _channels.Length; index++)
        {
            PeriodicWaveformChannelState item = state.Channels[index];
            if (item is null) { throw InvalidCheckpoint(); }
            string key = item.ChannelId.ToString("N");
            if (previous is not null && StringComparer.Ordinal.Compare(previous, key) >= 0) { throw InvalidCheckpoint(); }
            previous = key;
            var generator = PeriodicSignalGenerator.Restore(item.Generator);
            var delay = SignalAcquisitionDelayLine.Restore(item.Delay);
            PeriodicSignalState source = generator.CaptureState();
            SignalAcquisitionDelayState waiting = delay.CaptureState();
            WaveformBlockPlaneState? plane = assembly.Planes.SingleOrDefault(candidate => candidate.Configuration.ChannelId == item.ChannelId);
            if (plane is null || plane.Configuration.ProfileId != source.Plan.ProfileId ||
                assembly.StreamEpoch != source.Plan.StreamEpoch || assembly.EpochAnchorSimTimeNs != source.Plan.EpochAnchorSimTimeNs ||
                waiting.ProfileId != source.Plan.ProfileId || waiting.StreamEpoch != source.Plan.StreamEpoch ||
                waiting.EpochAnchorSimTimeNs != source.Plan.EpochAnchorSimTimeNs ||
                waiting.ReleaseCursorSimTimeNs != source.Clock.CursorSimTimeNs ||
                waiting.NextInputSampleIndex != source.Clock.NextSampleIndex ||
                plane.NextInputSampleIndex != waiting.NextInputSampleIndex - (ulong)waiting.PendingSamples.Count ||
                cursor is { } common && common != source.Clock.CursorSimTimeNs)
            { throw InvalidCheckpoint(); }
            cursor = source.Clock.CursorSimTimeNs;
            foreach (DelayedSignalSample sample in waiting.PendingSamples.Concat(plane.PendingSamples))
            {
                short expected = generator.EvaluateAt(sample.SampleIndex).NormalizedValue;
                if (sample.NormalizedValue != expected || sample.QualityFlags != item.QualityFlags) { throw InvalidCheckpoint(); }
            }
            _channels[index] = new(item.ChannelId, generator, delay, item.QualityFlags);
        }
    }

    public static PeriodicWaveformGroup Start(Guid sessionId, Guid instanceId, ulong timebaseEpoch,
        ulong configurationRevision, ulong firstBlockSequence, int maximumBufferedBlocks,
        IReadOnlyList<PeriodicWaveformChannelPlan> channels)
    {
        if (channels is null || channels.Count is < 1 or > WaveformEnvelopeCodec.MaximumPlaneCount)
        { throw new PeriodicWaveformGroupException("PeriodicGroup.InvalidChannels", nameof(channels)); }
        var plans = new PeriodicWaveformChannelPlan[channels.Count];
        for (int index = 0; index < plans.Length; index++)
        {
            PeriodicWaveformChannelPlan plan = channels[index];
            if (plan is null || plan.Source is null || plan.Plane is null)
            { throw new PeriodicWaveformGroupException("PeriodicGroup.InvalidChannels", nameof(channels)); }
            plans[index] = plan;
        }
        plans = plans.OrderBy(plan => plan.Plane.ChannelId.ToString("N"), StringComparer.Ordinal).ToArray();
        PeriodicWaveformChannelState[] sources = plans.Select(plan => new PeriodicWaveformChannelState(plan.Plane.ChannelId,
            PeriodicSignalGenerator.Start(plan.Source).CaptureState(),
            SignalAcquisitionDelayLine.Start(plan.Source.ProfileId, plan.Source.StreamEpoch,
                plan.Source.EpochAnchorSimTimeNs, plan.DelayCapacity).CaptureState(), plan.QualityFlags)).ToArray();
        var assembler = WaveformBlockAssembler.Start(sessionId, instanceId, timebaseEpoch,
            plans[0].Source.StreamEpoch, configurationRevision, firstBlockSequence, plans[0].Source.EpochAnchorSimTimeNs,
            maximumBufferedBlocks, plans.Select(plan => plan.Plane).ToArray());
        return Restore(new(sources, assembler.CaptureState()));
    }

    public PeriodicWaveformGroupState CaptureState() => new(Array.AsReadOnly(_channels.Select(channel =>
        new PeriodicWaveformChannelState(channel.Id, channel.Generator.CaptureState(), channel.Delay.CaptureState(), channel.QualityFlags)).ToArray()),
        _assembler.CaptureState());

    public static PeriodicWaveformGroup Restore(PeriodicWaveformGroupState state)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(state);
            return new(state);
        }
        catch (ArgumentException) { throw InvalidCheckpoint(); }
        catch (OverflowException) { throw InvalidCheckpoint(); }
    }

    public IReadOnlyList<byte[]> AdvanceTo(long simTimeNs, int maximumSamplesPerChannel, int maximumBlocks,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumBlocks is < 1 or > WaveformBlockAssembler.MaximumBufferedBlockCount)
        { throw new PeriodicWaveformGroupException("PeriodicGroup.InvalidBlockLimit", nameof(maximumBlocks)); }
        PeriodicWaveformGroup trial = Restore(CaptureState());
        List<byte[]> output = [];
        foreach (Channel channel in trial._channels)
        {
            IReadOnlyList<GeneratedSignalSample> samples = channel.Generator.GenerateBefore(simTimeNs, maximumSamplesPerChannel, cancellationToken);
            foreach (GeneratedSignalSample sample in samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Release(sample.Tick.SimTimeNs);
                channel.Delay.Enqueue(sample.Tick, sample.NormalizedValue, channel.QualityFlags);
            }
            Release(simTimeNs);

            void Release(long cursor)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (WaveformEnvelope envelope in trial._assembler.Push(channel.Id, channel.Delay.DrainAvailable(cursor)))
                {
                    if (output.Count == maximumBlocks)
                    { throw new PeriodicWaveformGroupException("PeriodicGroup.BlockLimitExceeded", nameof(maximumBlocks)); }
                    output.Add(WaveformEnvelopeCodec.EncodeRaw(envelope));
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        _channels = trial._channels;
        _assembler = trial._assembler;
        return output.AsReadOnly();
    }

    private static PeriodicWaveformGroupException InvalidCheckpoint() => new("PeriodicGroup.InvalidCheckpoint", "state");
}
