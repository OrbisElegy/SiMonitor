// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.ObjectModel;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public sealed class PhysiologyWaveformGroupException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record PhysiologyWaveformChannelPlan(RegularPhysiologyPlan Physiology,
    WaveformBlockPlaneConfiguration Plane, IReadOnlyList<EventWaveformBand> Bands, int DelayCapacity, uint QualityFlags,
    VascularPressurePlan? VascularPressure = null, PlethRunoffPlan? PlethRunoff = null, int PressureZeroOffsetCentiMmHg = 0, int PressureBaselineCentiMmHg = 0);
public sealed record PhysiologyWaveformChannelState(Guid ChannelId, PhysiologySignalState Generator,
    SignalAcquisitionDelayState Delay, uint QualityFlags, int PressureZeroOffsetCentiMmHg = 0);
public sealed record PhysiologyWaveformGroupState(IReadOnlyList<PhysiologyWaveformChannelState> Channels,
    WaveformBlockAssemblerState Assembler);
public readonly record struct PhysiologyWaveformSample(Guid ChannelId, long SourceSimTimeNs, short NormalizedValue, uint QualityFlags);

// Serialized multi-rate generation with one shared, bounded block assembler.
public sealed class PhysiologyWaveformGroup
{
    private readonly record struct Channel(Guid Id, PhysiologySignalGenerator Generator,
        SignalAcquisitionDelayLine Delay, uint QualityFlags, int PressureZeroOffsetCentiMmHg);
    private Channel[] _channels;
    private WaveformBlockAssembler _assembler;

    private PhysiologyWaveformGroup(PhysiologyWaveformGroup source)
    {
        _channels = source._channels.Select(channel => new Channel(channel.Id, channel.Generator.Fork(),
            SignalAcquisitionDelayLine.Restore(channel.Delay.CaptureState()), channel.QualityFlags, channel.PressureZeroOffsetCentiMmHg)).ToArray();
        _assembler = WaveformBlockAssembler.Restore(source._assembler.CaptureState());
    }

    // Serialized owner-only transaction copy. External checkpoints still use
    // Restore, including full replay validation of pending waveform samples.
    public PhysiologyWaveformGroup Fork() => new(this);

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
            var generator = PhysiologySignalGenerator.Restore(item.Generator);
            var delay = SignalAcquisitionDelayLine.Restore(item.Delay);
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
            if (item.PressureZeroOffsetCentiMmHg is < short.MinValue or > short.MaxValue) { throw InvalidCheckpoint(); }
            if (item.PressureZeroOffsetCentiMmHg != 0 &&
                (plane.Configuration.ProfileId != "AcqPressure125@1" ||
                 plane.Configuration.ScaleNumerator != 1 || plane.Configuration.ScaleDenominator != 100))
            { throw InvalidCheckpoint(); }
            if (generator.VascularPressure is not null &&
                (plane.Configuration.ProfileId != "AcqPressure125@1" ||
                 plane.Configuration.ScaleNumerator != 1 || plane.Configuration.ScaleDenominator != 100 ||
                 plane.Configuration.OffsetNumerator != 0 || plane.Configuration.OffsetDenominator != 1))
            { throw InvalidCheckpoint(); }
            if (generator.PlethRunoff is not null && (plane.Configuration.ProfileId != "AcqPleth125@1" ||
                plane.Configuration.ScaleNumerator != 1 || plane.Configuration.ScaleDenominator != 1 ||
                plane.Configuration.OffsetNumerator != 0 || plane.Configuration.OffsetDenominator != 1)) { throw InvalidCheckpoint(); }
            if (sharedPlan is not null && sharedPlan != source.Timeline.Plan) { throw InvalidCheckpoint(); }
            sharedPlan = source.Timeline.Plan;
            cursor = source.Clock.CursorSimTimeNs;
            DelayedSignalSample[] pending = waiting.PendingSamples.Concat(plane.PendingSamples).ToArray();
            EventWaveformComposition? composition = null;
            if (pending.Length > 0 && generator.VascularPressure is null && generator.PlethRunoff is null)
            {
                long lookback = source.Bands.Max(band => checked(band.DelayNs + band.DurationNs));
                long start = Math.Max(source.Clock.EpochAnchorSimTimeNs, pending.Min(sample => sample.SourceSimTimeNs) - lookback);
                var timeline = RegularPhysiologyTimeline.Restore(source.Timeline with { CursorSimTimeNs = start });
                composition = EventWaveformComposition.Restore(new(source.Bands, timeline.AdvanceBefore(checked(pending.Max(sample => sample.SourceSimTimeNs) + 1), EventWaveformComposition.MaximumEventCount)));
            }
            foreach (DelayedSignalSample sample in pending)
            {
                long value = source.ActiveFromEventTimeNs is not null ? generator.EvaluateAt(sample.SourceSimTimeNs) : generator.VascularPressure is { } pressure
                    ? pressure.EvaluateAt(sample.SourceSimTimeNs)
                    : generator.PlethRunoff is { } runoff ? runoff.EvaluateAt(sample.SourceSimTimeNs)
                    : checked(composition!.EvaluateAt(sample.SourceSimTimeNs) +
                        (long)source.PressureBaselineCentiMmHg * FixedPointMath.Q32One);
                short expected = checked((short)FixedPointMath.RoundDivideTiesToEven(checked(value + (long)item.PressureZeroOffsetCentiMmHg * FixedPointMath.Q32One), FixedPointMath.Q32One));
                if (sample.NormalizedValue != expected || sample.QualityFlags != item.QualityFlags) { throw InvalidCheckpoint(); }
            }
            _channels[index] = new(item.ChannelId, generator, delay, item.QualityFlags, item.PressureZeroOffsetCentiMmHg);
        }
    }

    public static PhysiologyWaveformGroup Start(Guid sessionId, Guid instanceId, ulong timebaseEpoch,
        ulong streamEpoch, ulong configurationRevision, ulong firstBlockSequence, int maximumBufferedBlocks,
        IReadOnlyList<PhysiologyWaveformChannelPlan> channels)
    {
        if (channels is null || channels.Count is < 1 or > WaveformEnvelopeCodec.MaximumPlaneCount)
        { throw new PhysiologyWaveformGroupException("PhysiologyGroup.InvalidChannels", nameof(channels)); }
        var plans = new PhysiologyWaveformChannelPlan[channels.Count];
        for (int index = 0; index < plans.Length; index++)
        {
            PhysiologyWaveformChannelPlan plan = channels[index];
            if (plan is null || plan.Physiology is null || plan.Plane is null || plan.Bands is null)
            { throw new PhysiologyWaveformGroupException("PhysiologyGroup.InvalidChannels", nameof(channels)); }
            plans[index] = plan;
        }
        plans = plans.OrderBy(plan => plan.Plane.ChannelId.ToString("N"), StringComparer.Ordinal).ToArray();
        PhysiologyWaveformChannelState[] sources = plans.Select(plan => new PhysiologyWaveformChannelState(plan.Plane.ChannelId,
            PhysiologySignalGenerator.Start(plan.Physiology, plan.Plane.ProfileId, streamEpoch, plan.Bands, plan.VascularPressure, plan.PlethRunoff, plan.PressureBaselineCentiMmHg).CaptureState(),
            SignalAcquisitionDelayLine.Start(plan.Plane.ProfileId, streamEpoch,
                plan.Physiology.EpochAnchorSimTimeNs, plan.DelayCapacity).CaptureState(), plan.QualityFlags, plan.PressureZeroOffsetCentiMmHg)).ToArray();
        var assembler = WaveformBlockAssembler.Start(sessionId, instanceId, timebaseEpoch,
            streamEpoch, configurationRevision, firstBlockSequence, plans[0].Physiology.EpochAnchorSimTimeNs,
            maximumBufferedBlocks, plans.Select(plan => plan.Plane).ToArray());
        return Restore(new(sources, assembler.CaptureState()));
    }

    public void ContinueWith(PhysiologyWaveformGroup definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_channels.Select(c => (c.Id, c.QualityFlags, c.PressureZeroOffsetCentiMmHg))
                .SequenceEqual(definition._channels.Select(c => (c.Id, c.QualityFlags, c.PressureZeroOffsetCentiMmHg))) ||
            !_assembler.CaptureState().Planes.Select(p => p.Configuration)
                .SequenceEqual(definition._assembler.CaptureState().Planes.Select(p => p.Configuration)))
        { throw new ArgumentException("PhysiologyGroup.ChannelMismatch", nameof(definition)); }
        // Validate every new generator before publishing. Delays, assembler,
        // sample indices and already acquired samples stay on the same stream.
        var channels = _channels.Select((channel, index) => channel with
        {
            Generator = channel.Generator.ContinueWith(definition._channels[index].Generator.CaptureState())
        }).ToArray();
        _channels = channels;
    }

    public PhysiologyWaveformGroupState CaptureState() => new(Array.AsReadOnly(_channels.Select(channel =>
        new PhysiologyWaveformChannelState(channel.Id, channel.Generator.CaptureState(), channel.Delay.CaptureState(), channel.QualityFlags, channel.PressureZeroOffsetCentiMmHg)).ToArray()),
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
        CancellationToken cancellationToken = default) =>
        AdvanceToCore(simTimeNs, maximumSamplesPerChannel, maximumBlocks, maximumEvents, false, out _, cancellationToken);

    // Live traces share one source-time frontier across all native sample rates.
    // Publish the taps only after the complete acquisition transaction commits.
    public IReadOnlyList<byte[]> AdvanceTo(long simTimeNs, int maximumSamplesPerChannel, int maximumBlocks, int maximumEvents,
        out IReadOnlyList<PhysiologyWaveformSample> immediateSamples, CancellationToken cancellationToken = default) =>
        AdvanceToCore(simTimeNs, maximumSamplesPerChannel, maximumBlocks, maximumEvents, true, out immediateSamples, cancellationToken);

    private ReadOnlyCollection<byte[]> AdvanceToCore(long simTimeNs, int maximumSamplesPerChannel, int maximumBlocks, int maximumEvents,
        bool captureImmediateSamples, out IReadOnlyList<PhysiologyWaveformSample> immediateSamples, CancellationToken cancellationToken)
    {
        immediateSamples = [];
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumBlocks is < 1 or > WaveformBlockAssembler.MaximumBufferedBlockCount)
        { throw new PhysiologyWaveformGroupException("PhysiologyGroup.InvalidBlockLimit", nameof(maximumBlocks)); }
        PhysiologyWaveformGroup trial = Fork();
        List<byte[]> output = [];
        List<PhysiologyWaveformSample> immediate = [];
        foreach (Channel channel in trial._channels)
        {
            IReadOnlyList<PhysiologySignalSample> samples = channel.Generator.GenerateBefore(simTimeNs, maximumSamplesPerChannel, maximumEvents, cancellationToken);
            foreach (PhysiologySignalSample sample in samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Release(sample.Tick.SimTimeNs);
                // Apply sensor zero error after the true source, before wire quantization.
                // Overflow rejects the whole trial; never silently clip pressure.
                long measuredValue = channel.PressureZeroOffsetCentiMmHg == 0 ? sample.NormalizedValue
                    : (long)FixedPointMath.RoundDivideTiesToEven(
                        checked(sample.ValueQ32 + (long)channel.PressureZeroOffsetCentiMmHg * FixedPointMath.Q32One), FixedPointMath.Q32One);
                if (measuredValue is < short.MinValue or > short.MaxValue)
                { throw new PhysiologyWaveformGroupException("PhysiologyGroup.MeasuredPressureOutOfRange", nameof(simTimeNs)); }
                short measured = (short)measuredValue;
                channel.Delay.Enqueue(sample.Tick, measured, channel.QualityFlags);
                if (captureImmediateSamples)
                { immediate.Add(new(channel.Id, sample.Tick.SimTimeNs, measured, channel.QualityFlags)); }
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
        immediateSamples = immediate.AsReadOnly();
        return output.AsReadOnly();
    }

    private static PhysiologyWaveformGroupException InvalidCheckpoint() => new("PhysiologyGroup.InvalidCheckpoint", "state");
}
