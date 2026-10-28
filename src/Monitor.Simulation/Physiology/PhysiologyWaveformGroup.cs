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
    WaveformBlockPlaneConfiguration Plane, IReadOnlyList<EventWaveformBand> Bands, uint QualityFlags,
    VascularPressurePlan? VascularPressure = null, PlethRunoffPlan? PlethRunoff = null, int PressureZeroOffsetCentiMmHg = 0, int PressureBaselineCentiMmHg = 0)
{
    public WaveformAmplitudeCurve? AmplitudeCurve { get; init; }
    public SeededEcgInterference? EcgInterference { get; init; }
    public PressureMeasurementIllustration PressureMeasurement { get; init; }
    public PlethIllustration PlethShape { get; init; }
}
public sealed record PhysiologyWaveformChannelState(Guid ChannelId, PhysiologySignalState Generator,
    uint QualityFlags, int PressureZeroOffsetCentiMmHg = 0)
{
    public IReadOnlyList<SensorWaveformIllustration> SensorIllustrations { get; init; } = [];
    public IReadOnlyList<WaveformAmplitudeCurveSegment> AmplitudeCurves { get; init; } = [];
}
public sealed record PhysiologyWaveformGroupState(IReadOnlyList<PhysiologyWaveformChannelState> Channels,
    WaveformBlockAssemblerState Assembler);
public readonly record struct PhysiologyWaveformSample(Guid ChannelId, long SourceSimTimeNs, short NormalizedValue, uint QualityFlags);

// Serialized multi-rate generation with one shared, bounded block assembler.
public sealed class PhysiologyWaveformGroup
{
    private readonly record struct Channel(Guid Id, PhysiologySignalGenerator Generator,
        uint QualityFlags, int PressureZeroOffsetCentiMmHg, IReadOnlyList<SensorWaveformIllustration> SensorIllustrations,
        IReadOnlyList<WaveformAmplitudeCurveSegment> AmplitudeCurves);
    private Channel[] _channels;
    private WaveformBlockAssembler _assembler;

    private PhysiologyWaveformGroup(PhysiologyWaveformGroup source)
    {
        _channels = source._channels.Select(channel => new Channel(channel.Id, channel.Generator.Fork(),
            channel.QualityFlags, channel.PressureZeroOffsetCentiMmHg, channel.SensorIllustrations, channel.AmplitudeCurves)).ToArray();
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
            PhysiologySignalState source = generator.CaptureState();
            SensorWaveformIllustrations.Validate(item.SensorIllustrations, source.Clock.ProfileId,
                source.Clock.EpochAnchorSimTimeNs, source.Clock.CursorSimTimeNs);
            var sensorIllustrations = Array.AsReadOnly(item.SensorIllustrations.ToArray());
            WaveformAmplitudeCurves.Validate(item.AmplitudeCurves, source.Clock.ProfileId,
                source.Clock.EpochAnchorSimTimeNs, source.Clock.CursorSimTimeNs);
            var amplitudeCurves = Array.AsReadOnly(item.AmplitudeCurves.ToArray());
            WaveformBlockPlaneState? plane = assembly.Planes.SingleOrDefault(candidate => candidate.Configuration.ChannelId == item.ChannelId);
            if (plane is null || plane.Configuration.ProfileId != source.Clock.ProfileId ||
                assembly.StreamEpoch != source.Clock.StreamEpoch || assembly.EpochAnchorSimTimeNs != source.Clock.EpochAnchorSimTimeNs ||
                plane.NextInputSampleIndex != source.Clock.NextSampleIndex ||
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
            AcquiredSignalSample[] pending = plane.PendingSamples.ToArray();
            EventWaveformComposition? composition = null;
            if (pending.Length > 0 && generator.VascularPressure is null && generator.PlethRunoff is null)
            {
                long lookback = source.Bands.Max(band => checked(band.DelayNs + band.DurationNs));
                long start = Math.Max(source.Clock.EpochAnchorSimTimeNs, pending.Min(sample => sample.SourceSimTimeNs) - lookback);
                var timeline = RegularPhysiologyTimeline.Restore(source.Timeline with { CursorSimTimeNs = start });
                composition = EventWaveformComposition.Restore(new(source.Bands, timeline.AdvanceBefore(checked(pending.Max(sample => sample.SourceSimTimeNs) + 1), EventWaveformComposition.MaximumEventCount)));
            }
            foreach (AcquiredSignalSample sample in pending)
            {
                long value = source.ActiveFromEventTimeNs is not null ? generator.EvaluateAt(sample.SourceSimTimeNs) : generator.VascularPressure is { } pressure
                    ? pressure.EvaluateAt(sample.SourceSimTimeNs)
                    : generator.PlethRunoff is { } runoff ? runoff.EvaluateAt(sample.SourceSimTimeNs)
                    : checked(composition!.EvaluateAt(sample.SourceSimTimeNs) +
                        (long)source.PressureBaselineCentiMmHg * FixedPointMath.Q32One);
                value = WaveformAmplitudeCurves.At(amplitudeCurves, sample.SourceSimTimeNs)?.Apply(value, sample.SourceSimTimeNs) ?? value;
                var illustration = SensorWaveformIllustrations.At(sensorIllustrations, sample.SourceSimTimeNs);
                value = SensorWaveformIllustrations.Apply(value, sample.SourceSimTimeNs,
                    source.Clock.EpochAnchorSimTimeNs, illustration, generator);
                short expected = checked((short)FixedPointMath.RoundShiftTiesToEven(checked(value + (long)item.PressureZeroOffsetCentiMmHg * FixedPointMath.Q32One), 32));
                if (sample.NormalizedValue != expected || sample.QualityFlags != SensorWaveformIllustrations.Quality(item.QualityFlags, illustration, sample.SourceSimTimeNs))
                { throw InvalidCheckpoint(); }
            }
            _channels[index] = new(item.ChannelId, generator, item.QualityFlags, item.PressureZeroOffsetCentiMmHg, sensorIllustrations, amplitudeCurves);
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
            plan.QualityFlags, plan.PressureZeroOffsetCentiMmHg)
        {
            AmplitudeCurves = plan.AmplitudeCurve is null ? [] : [new(plan.Physiology.EpochAnchorSimTimeNs, plan.AmplitudeCurve)],
            SensorIllustrations = plan.PressureMeasurement == PressureMeasurementIllustration.Reference && plan.PlethShape == PlethIllustration.Reference && plan.EcgInterference is null
                ? [] : [new(plan.Physiology.EpochAnchorSimTimeNs, plan.PressureMeasurement, plan.PlethShape) { EcgInterference = plan.EcgInterference }]
        }).ToArray();
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
        // Validate every new generator before publishing. The recording assembler,
        // sample indices and already acquired samples stay on the same stream.
        var channels = _channels.Select((channel, index) => channel with
        {
            Generator = channel.Generator.ContinueWith(definition._channels[index].Generator.CaptureState()),
            AmplitudeCurves = WaveformAmplitudeCurves.ContinueWith(channel.AmplitudeCurves,
                WaveformAmplitudeCurves.At(definition._channels[index].AmplitudeCurves, long.MaxValue),
                channel.Generator.CaptureState().Clock.CursorSimTimeNs, _assembler.NextBlockStartSimTimeNs),
            SensorIllustrations = SensorWaveformIllustrations.ContinueWith(channel.SensorIllustrations,
                SensorWaveformIllustrations.At(definition._channels[index].SensorIllustrations, long.MaxValue),
                channel.Generator.CaptureState().Clock.CursorSimTimeNs, _assembler.NextBlockStartSimTimeNs)
        }).ToArray();
        _channels = channels;
    }

    public PhysiologyWaveformGroupState CaptureState() => new(Array.AsReadOnly(_channels.Select(channel =>
        new PhysiologyWaveformChannelState(channel.Id, channel.Generator.CaptureState(), channel.QualityFlags, channel.PressureZeroOffsetCentiMmHg)
        { SensorIllustrations = channel.SensorIllustrations, AmplitudeCurves = channel.AmplitudeCurves }).ToArray()),
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
            long epochAnchorSimTimeNs = channel.Generator.CaptureState().Clock.EpochAnchorSimTimeNs;
            IReadOnlyList<PhysiologySignalSample> samples = channel.Generator.GenerateBefore(simTimeNs, maximumSamplesPerChannel, maximumEvents, cancellationToken);
            foreach (PhysiologySignalSample sample in samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Apply sensor zero error after the true source, before wire quantization.
                // Overflow rejects the whole trial; never silently clip pressure.
                var illustration = SensorWaveformIllustrations.At(channel.SensorIllustrations, sample.Tick.SimTimeNs);
                long sourceValueQ32 = WaveformAmplitudeCurves.At(channel.AmplitudeCurves, sample.Tick.SimTimeNs)?.Apply(
                    sample.ValueQ32, sample.Tick.SimTimeNs) ?? sample.ValueQ32;
                long valueQ32 = SensorWaveformIllustrations.Apply(sourceValueQ32, sample.Tick.SimTimeNs,
                    epochAnchorSimTimeNs, illustration, channel.Generator, cancellationToken);
                long measuredValue = (long)FixedPointMath.RoundShiftTiesToEven(
                    checked(valueQ32 + (long)channel.PressureZeroOffsetCentiMmHg * FixedPointMath.Q32One), 32);
                if (measuredValue is < short.MinValue or > short.MaxValue)
                { throw new PhysiologyWaveformGroupException("PhysiologyGroup.MeasuredPressureOutOfRange", nameof(simTimeNs)); }
                short measured = (short)measuredValue;
                uint qualityFlags = SensorWaveformIllustrations.Quality(channel.QualityFlags, illustration, sample.Tick.SimTimeNs);
                Record(new(sample.Tick.ProfileId, sample.Tick.StreamEpoch, sample.Tick.SampleIndex,
                    sample.Tick.SimTimeNs, measured, qualityFlags));
                if (captureImmediateSamples)
                { immediate.Add(new(channel.Id, sample.Tick.SimTimeNs, measured, qualityFlags)); }
            }

            void Record(AcquiredSignalSample sample)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (WaveformEnvelope envelope in trial._assembler.Push(channel.Id, [sample]))
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
