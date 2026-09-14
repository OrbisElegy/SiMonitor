// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public sealed class PhysiologyWaveformGroupException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record PhysiologyWaveformChannelPlan(RegularPhysiologyPlan Physiology,
    WaveformBlockPlaneConfiguration Plane, IReadOnlyList<EventWaveformBand> Bands, int DelayCapacity, uint QualityFlags);
public sealed record PhysiologyWaveformChannelState(Guid ChannelId, PhysiologySignalState Generator,
    SignalAcquisitionDelayState Delay, uint QualityFlags);
public sealed record PhysiologyWaveformGroupState(IReadOnlyList<PhysiologyWaveformChannelState> Channels,
    WaveformBlockAssemblerState Assembler);

// Serialized multi-rate generation with one shared, bounded block assembler.
public sealed class PhysiologyWaveformGroup
{
    private readonly record struct Channel(Guid Id, PhysiologySignalGenerator Generator,
        SignalAcquisitionDelayLine Delay, uint QualityFlags);
    private Channel[] _channels;
    private WaveformBlockAssembler _assembler;

    private PhysiologyWaveformGroup(PhysiologyWaveformGroupState state)
    {
        if (state.Channels is null || state.Channels.Count is < 1 or > WaveformEnvelopeCodec.MaximumPlaneCount)
        { throw InvalidCheckpoint(); }
        _assembler = WaveformBlockAssembler.Restore(state.Assembler);
        WaveformBlockAssemblerState assembly = _assembler.CaptureState();
        if (assembly.Planes.Count != state.Channels.Count) { throw InvalidCheckpoint(); }
        _channels = new Channel[state.Channels.Count];
        long? cursor = null;
        RegularPhysiologyPlan? sharedPlan = null;
        string? previous = null;
        for (int index = 0; index < _channels.Length; index++)
        {
            PhysiologyWaveformChannelState item = state.Channels[index];
            if (item is null) { throw InvalidCheckpoint(); }
            string key = item.ChannelId.ToString("N");
            if (previous is not null && StringComparer.Ordinal.Compare(previous, key) >= 0) { throw InvalidCheckpoint(); }
            previous = key;
            PhysiologySignalGenerator generator = PhysiologySignalGenerator.Restore(item.Generator);
            SignalAcquisitionDelayLine delay = SignalAcquisitionDelayLine.Restore(item.Delay);
            PhysiologySignalState source = generator.CaptureState();
            SignalAcquisitionDelayState waiting = delay.CaptureState();
            WaveformBlockPlaneState? plane = assembly.Planes.SingleOrDefault(candidate => candidate.Configuration.ChannelId == item.ChannelId);
            if (plane is null || plane.Configuration.ProfileId != source.Clock.ProfileId ||
                assembly.StreamEpoch != source.Clock.StreamEpoch || assembly.EpochAnchorSimTimeNs != source.Clock.EpochAnchorSimTimeNs ||
                waiting.ProfileId != source.Clock.ProfileId || waiting.StreamEpoch != source.Clock.StreamEpoch ||
                waiting.EpochAnchorSimTimeNs != source.Clock.EpochAnchorSimTimeNs ||
                waiting.ReleaseCursorSimTimeNs != source.Clock.CursorSimTimeNs ||
                waiting.NextInputSampleIndex != source.Clock.NextSampleIndex ||
                plane.NextInputSampleIndex != waiting.NextInputSampleIndex - (ulong)waiting.PendingSamples.Count ||
                cursor is { } common && common != source.Clock.CursorSimTimeNs)
            { throw InvalidCheckpoint(); }
            if (sharedPlan is not null && sharedPlan != source.Timeline.Plan) { throw InvalidCheckpoint(); }
            sharedPlan = source.Timeline.Plan;
            cursor = source.Clock.CursorSimTimeNs;
            DelayedSignalSample[] pending = waiting.PendingSamples.Concat(plane.PendingSamples).ToArray();
            EventWaveformComposition? composition = null;
            if (pending.Length > 0)
            {
                long lookback = source.Bands.Max(band => checked(band.DelayNs + band.DurationNs));
                long start = Math.Max(source.Clock.EpochAnchorSimTimeNs, pending.Min(sample => sample.SourceSimTimeNs) - lookback);
                var timeline = RegularPhysiologyTimeline.Restore(source.Timeline with { CursorSimTimeNs = start });
                composition = EventWaveformComposition.Restore(new(source.Bands, timeline.AdvanceBefore(checked(pending.Max(sample => sample.SourceSimTimeNs) + 1), EventWaveformComposition.MaximumEventCount)));
            }
            foreach (DelayedSignalSample sample in pending)
            {
                short expected = checked((short)FixedPointMath.RoundDivideTiesToEven(composition!.EvaluateAt(sample.SourceSimTimeNs), FixedPointMath.Q32One));
                if (sample.NormalizedValue != expected || sample.QualityFlags != item.QualityFlags) { throw InvalidCheckpoint(); }
            }
            _channels[index] = new(item.ChannelId, generator, delay, item.QualityFlags);
        }
    }

    public static PhysiologyWaveformGroup Start(Guid sessionId, Guid instanceId, ulong timebaseEpoch,
        ulong streamEpoch, ulong configurationRevision, ulong firstBlockSequence, int maximumBufferedBlocks,
        IReadOnlyList<PhysiologyWaveformChannelPlan> channels)
    {
        if (channels is null || channels.Count is < 1 or > WaveformEnvelopeCodec.MaximumPlaneCount)
        { throw new PhysiologyWaveformGroupException("PhysiologyGroup.InvalidChannels", nameof(channels)); }
        PhysiologyWaveformChannelPlan[] plans = new PhysiologyWaveformChannelPlan[channels.Count];
        for (int index = 0; index < plans.Length; index++)
        {
            PhysiologyWaveformChannelPlan plan = channels[index];
            if (plan is null || plan.Physiology is null || plan.Plane is null || plan.Bands is null)
            { throw new PhysiologyWaveformGroupException("PhysiologyGroup.InvalidChannels", nameof(channels)); }
            plans[index] = plan;
        }
        plans = plans.OrderBy(plan => plan.Plane.ChannelId.ToString("N"), StringComparer.Ordinal).ToArray();
        PhysiologyWaveformChannelState[] sources = plans.Select(plan => new PhysiologyWaveformChannelState(plan.Plane.ChannelId,
            PhysiologySignalGenerator.Start(plan.Physiology, plan.Plane.ProfileId, streamEpoch, plan.Bands).CaptureState(),
            SignalAcquisitionDelayLine.Start(plan.Plane.ProfileId, streamEpoch,
                plan.Physiology.EpochAnchorSimTimeNs, plan.DelayCapacity).CaptureState(), plan.QualityFlags)).ToArray();
        WaveformBlockAssembler assembler = WaveformBlockAssembler.Start(sessionId, instanceId, timebaseEpoch,
            streamEpoch, configurationRevision, firstBlockSequence, plans[0].Physiology.EpochAnchorSimTimeNs,
            maximumBufferedBlocks, plans.Select(plan => plan.Plane).ToArray());
        return Restore(new(sources, assembler.CaptureState()));
    }

    public PhysiologyWaveformGroupState CaptureState() => new(Array.AsReadOnly(_channels.Select(channel =>
        new PhysiologyWaveformChannelState(channel.Id, channel.Generator.CaptureState(), channel.Delay.CaptureState(), channel.QualityFlags)).ToArray()),
        _assembler.CaptureState());

    public static PhysiologyWaveformGroup Restore(PhysiologyWaveformGroupState state)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(state);
            return new(state);
        }
        catch (ArgumentException) { throw InvalidCheckpoint(); }
        catch (OverflowException) { throw InvalidCheckpoint(); }
    }

    public IReadOnlyList<byte[]> AdvanceTo(long simTimeNs, int maximumSamplesPerChannel, int maximumBlocks, int maximumEvents,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumBlocks is < 1 or > WaveformBlockAssembler.MaximumBufferedBlockCount)
        { throw new PhysiologyWaveformGroupException("PhysiologyGroup.InvalidBlockLimit", nameof(maximumBlocks)); }
        PhysiologyWaveformGroup trial = Restore(CaptureState());
        List<byte[]> output = [];
        foreach (Channel channel in trial._channels)
        {
            IReadOnlyList<PhysiologySignalSample> samples = channel.Generator.GenerateBefore(simTimeNs, maximumSamplesPerChannel, maximumEvents, cancellationToken);
            foreach (PhysiologySignalSample sample in samples)
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
                    { throw new PhysiologyWaveformGroupException("PhysiologyGroup.BlockLimitExceeded", nameof(maximumBlocks)); }
                    output.Add(WaveformEnvelopeCodec.EncodeRaw(envelope));
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        _channels = trial._channels;
        _assembler = trial._assembler;
        return output.AsReadOnly();
    }

    private static PhysiologyWaveformGroupException InvalidCheckpoint() => new("PhysiologyGroup.InvalidCheckpoint", "state");
}
