// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public sealed class ElectrodeWaveformGroupException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record ElectrodeChannelPlan(EcgLead Lead, Guid ChannelId, int DelayCapacity, uint QualityFlags);
public sealed record ElectrodeChannelState(EcgLead Lead, Guid ChannelId,
    SignalAcquisitionDelayState Delay, uint QualityFlags);
public sealed record ElectrodeWaveformGroupState(ElectrodeSignalState Generator,
    IReadOnlyList<ElectrodeChannelState> Channels, WaveformBlockAssemblerState Assembler);

// Synchronous projected monitoring leads, with one shared bounded block assembler.
public sealed class ElectrodeWaveformGroup
{
    private readonly record struct Channel(EcgLead Lead, Guid Id,
        SignalAcquisitionDelayLine Delay, uint QualityFlags);
    private Channel[] _channels;
    private ElectrodeSignalGenerator _generator;
    private WaveformBlockAssembler _assembler;

    private ElectrodeWaveformGroup(ElectrodeWaveformGroup source)
    {
        _generator = source._generator.Fork();
        _channels = source._channels.Select(channel => new Channel(channel.Lead, channel.Id,
            SignalAcquisitionDelayLine.Restore(channel.Delay.CaptureState()), channel.QualityFlags)).ToArray();
        _assembler = WaveformBlockAssembler.Restore(source._assembler.CaptureState());
    }

    // Owner-only transaction copy; external checkpoints still require full
    // Restore validation, including reconstruction of pending lead samples.
    public ElectrodeWaveformGroup Fork() => new(this);

    private ElectrodeWaveformGroup(ElectrodeWaveformGroupState state)
    {
        if (state.Channels is null || state.Channels.Count != 12)
        { throw InvalidCheckpoint(); }
        _generator = ElectrodeSignalGenerator.Restore(state.Generator);
        ElectrodeSignalState source = _generator.CaptureState();
        _assembler = WaveformBlockAssembler.Restore(state.Assembler);
        WaveformBlockAssemblerState assembly = _assembler.CaptureState();
        if (assembly.Planes.Count != state.Channels.Count) { throw InvalidCheckpoint(); }
        _channels = new Channel[state.Channels.Count];
        HashSet<EcgLead> leads = [];
        Dictionary<long, EcgLeadProjection> projections = [];
        string? previous = null;
        for (int index = 0; index < _channels.Length; index++)
        {
            ElectrodeChannelState item = state.Channels[index];
            if (item is null || !Enum.IsDefined(item.Lead) || !leads.Add(item.Lead)) { throw InvalidCheckpoint(); }
            string key = item.ChannelId.ToString("N");
            if (previous is not null && StringComparer.Ordinal.Compare(previous, key) >= 0) { throw InvalidCheckpoint(); }
            previous = key;
            var delay = SignalAcquisitionDelayLine.Restore(item.Delay);
            SignalAcquisitionDelayState waiting = delay.CaptureState();
            WaveformBlockPlaneState? plane = assembly.Planes.SingleOrDefault(candidate => candidate.Configuration.ChannelId == item.ChannelId);
            if (plane is null || plane.Configuration.ScaleNumerator != 1 || plane.Configuration.ScaleDenominator != 1 ||
                plane.Configuration.OffsetNumerator != 0 || plane.Configuration.OffsetDenominator != 1 || plane.Configuration.ProfileId != source.Clock.ProfileId ||
                assembly.StreamEpoch != source.Clock.StreamEpoch || assembly.EpochAnchorSimTimeNs != source.Clock.EpochAnchorSimTimeNs ||
                waiting.ProfileId != source.Clock.ProfileId || waiting.StreamEpoch != source.Clock.StreamEpoch ||
                waiting.EpochAnchorSimTimeNs != source.Clock.EpochAnchorSimTimeNs ||
                waiting.ReleaseCursorSimTimeNs != source.Clock.CursorSimTimeNs ||
                waiting.NextInputSampleIndex != source.Clock.NextSampleIndex ||
                plane.NextInputSampleIndex != waiting.NextInputSampleIndex - (ulong)waiting.PendingSamples.Count)
            { throw InvalidCheckpoint(); }
            DelayedSignalSample[] pending = waiting.PendingSamples.Concat(plane.PendingSamples).ToArray();
            ElectrodeWaveformComposition? composition = null;
            if (pending.Any(sample => !projections.ContainsKey(sample.SourceSimTimeNs)))
            {
                long lookback = source.Electrodes.SelectMany(electrode => electrode.Bands).Max(band => checked(band.DelayNs + band.DurationNs));
                long start = Math.Max(source.Clock.EpochAnchorSimTimeNs, pending.Min(sample => sample.SourceSimTimeNs) - lookback);
                var timeline = RegularPhysiologyTimeline.Restore(source.Timeline with { CursorSimTimeNs = start });
                composition = ElectrodeWaveformComposition.Restore(new(source.Electrodes, timeline.AdvanceBefore(checked(pending.Max(sample => sample.SourceSimTimeNs) + 1), EventWaveformComposition.MaximumEventCount), source.Placement));
            }
            foreach (DelayedSignalSample sample in pending)
            {
                if (!projections.TryGetValue(sample.SourceSimTimeNs, out var projection))
                {
                    projection = composition!.EvaluateAt(sample.SourceSimTimeNs).Leads;
                    projections.Add(sample.SourceSimTimeNs, projection);
                }
                short expected = checked((short)FixedPointMath.RoundDivideTiesToEven(projection[item.Lead].Numerator, 6 * (Int128)FixedPointMath.Q32One));
                if (sample.NormalizedValue != expected || sample.QualityFlags != item.QualityFlags) { throw InvalidCheckpoint(); }
            }
            _channels[index] = new(item.Lead, item.ChannelId, delay, item.QualityFlags);
        }
    }

    public static ElectrodeWaveformGroup Start(Guid sessionId, Guid instanceId, ulong timebaseEpoch,
        ulong streamEpoch, ulong configurationRevision, ulong firstBlockSequence, int maximumBufferedBlocks,
        RegularPhysiologyPlan physiology, IReadOnlyList<ElectrodeWaveformPlan> electrodes,
        IReadOnlyList<ElectrodeChannelPlan> channels, EcgLimbPlacement placement = EcgLimbPlacement.Standard)
    {
        if (channels is null || channels.Count != 12 || channels.Any(channel => channel is null))
        { throw new ElectrodeWaveformGroupException("ElectrodeGroup.InvalidChannels", nameof(channels)); }
        var plans = channels.OrderBy(plan => plan.ChannelId.ToString("N"), StringComparer.Ordinal).ToArray();
        var source = ElectrodeSignalGenerator.Start(physiology, "AcqECGMonitor250@1", streamEpoch, electrodes, placement);
        var sources = plans.Select(plan => new ElectrodeChannelState(plan.Lead, plan.ChannelId,
            SignalAcquisitionDelayLine.Start("AcqECGMonitor250@1", streamEpoch,
                physiology.EpochAnchorSimTimeNs, plan.DelayCapacity).CaptureState(), plan.QualityFlags)).ToArray();
        var assembler = WaveformBlockAssembler.Start(sessionId, instanceId, timebaseEpoch,
            streamEpoch, configurationRevision, firstBlockSequence, physiology.EpochAnchorSimTimeNs,
            maximumBufferedBlocks, plans.Select(plan => new WaveformBlockPlaneConfiguration(
                plan.ChannelId, "AcqECGMonitor250@1", 1, 1, 0, 1)).ToArray());
        return Restore(new(source.CaptureState(), sources, assembler.CaptureState()));
    }

    public ElectrodeWaveformGroupState CaptureState() => new(_generator.CaptureState(),
        Array.AsReadOnly(_channels.Select(channel => new ElectrodeChannelState(channel.Lead,
            channel.Id, channel.Delay.CaptureState(), channel.QualityFlags)).ToArray()), _assembler.CaptureState());

    public static ElectrodeWaveformGroup Restore(ElectrodeWaveformGroupState state)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(state);
            return new(state);
        }
        catch (ArgumentException) { throw InvalidCheckpoint(); }
        catch (OverflowException) { throw InvalidCheckpoint(); }
    }

    public IReadOnlyList<byte[]> AdvanceTo(long simTimeNs, int maximumSamples, int maximumBlocks, int maximumEvents,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumBlocks is < 1 or > WaveformBlockAssembler.MaximumBufferedBlockCount)
        { throw new ElectrodeWaveformGroupException("ElectrodeGroup.InvalidBlockLimit", nameof(maximumBlocks)); }
        ElectrodeWaveformGroup trial = Fork();
        List<byte[]> output = [];
        IReadOnlyList<ElectrodeSignalSample> samples = trial._generator.GenerateBefore(simTimeNs, maximumSamples, maximumEvents, cancellationToken);
        foreach (Channel channel in trial._channels)
        {
            foreach (ElectrodeSignalSample sample in samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Release(sample.Tick.SimTimeNs);
                channel.Delay.Enqueue(sample.Tick, sample.MicrovoltValues[(int)channel.Lead], channel.QualityFlags);
            }
            Release(simTimeNs);

            void Release(long cursor)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (WaveformEnvelope envelope in trial._assembler.Push(channel.Id, channel.Delay.DrainAvailable(cursor)))
                {
                    if (output.Count == maximumBlocks)
                    { throw new ElectrodeWaveformGroupException("ElectrodeGroup.BlockLimitExceeded", nameof(maximumBlocks)); }
                    output.Add(WaveformEnvelopeCodec.EncodeRaw(envelope));
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        _generator = trial._generator;
        _channels = trial._channels;
        _assembler = trial._assembler;
        return output.AsReadOnly();
    }

    private static ElectrodeWaveformGroupException InvalidCheckpoint() => new("ElectrodeGroup.InvalidCheckpoint", "state");
}
