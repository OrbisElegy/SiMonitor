// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Application.Measurements;

public sealed record MeanPressureReading(WaveformMeasurementStatus Status, int? MeanCentiMmHg,
    long? WindowStartTimeNs, long? MeasuredAtNs)
{
    public PulsePressureReading? Pulse { get; init; }
}

// Mean of the latest four seconds of uniform125Hz pressure samples in mmHg.
// Optional pulse extrema use a separate bounded detector; no configured pressure.
public sealed class MeanPressureMeasurement
{
    private const long StepNs = 8_000_000;
    public const int WindowSamples = 500;
    private State _state = new();
    public Guid ChannelId { get; }
    public bool DetectPulse { get; }
    public MeanPressureMeasurement(Guid channelId, bool detectPulse = false)
    {
        if (channelId == Guid.Empty) { throw new ArgumentException("MeanPressure.EmptyChannel", nameof(channelId)); }
        ChannelId = channelId; DetectPulse = detectPulse;
    }
    public MeanPressureReading Consume(ReadOnlySpan<byte> wire)
    {
        var block = WaveformEnvelopeCodec.Decode(wire);
        var plane = block.Planes.SingleOrDefault(p => p.ChannelId == ChannelId)
            ?? throw new ArgumentException("MeanPressure.ChannelMissing", nameof(wire));
        if (plane.SampleRateNumerator != 125 || plane.SampleRateDenominator != 1 ||
            block.StartSimTimeNs < 0 || plane.Samples.Count is < 1 or > WindowSamples ||
            (long)plane.Samples.Count * StepNs != block.DurationNs)
        { throw new ArgumentException("MeanPressure.UnsupportedSampling", nameof(wire)); }
        long end = checked(block.StartSimTimeNs + block.DurationNs);
        ulong nextIndex = checked(plane.FirstSampleIndex + (ulong)plane.Samples.Count);
        var identity = new Identity(block.SessionId, block.InstanceId, block.TimebaseEpoch, block.StreamEpoch, block.ConfigurationRevision);
        if (_state.Identity is { } old && old.Session == identity.Session && old.Instance == identity.Instance &&
            (identity.Timebase < old.Timebase || identity.Timebase == old.Timebase &&
            (identity.Stream < old.Stream || identity.Stream == old.Stream && identity.Revision < old.Revision)))
        { throw new ArgumentException("MeanPressure.EpochRollback", nameof(wire)); }
        // A changed identity starts an independent stream. Same-stream overlaps
        // and duplicate delivery are rejected atomically, never replayed.
        bool same = _state.Identity == identity;
        bool sameClock = _state.Identity is { } previous && previous.Session == identity.Session && previous.Instance == identity.Instance &&
            previous.Timebase == identity.Timebase && previous.Stream == identity.Stream;
        if (sameClock && (block.StartSimTimeNs < _state.NextTime || block.BlockSequence <= _state.Sequence || plane.FirstSampleIndex < _state.NextIndex))
        { throw new ArgumentException("MeanPressure.OutOfOrder", nameof(wire)); }
        var next = same && block.StartSimTimeNs == _state.NextTime && plane.FirstSampleIndex == _state.NextIndex &&
            _state.Sequence != ulong.MaxValue && block.BlockSequence == _state.Sequence + 1 ? Copy(_state) : new State();
        for (int i = 0; i < plane.Samples.Count; i++)
        {
            int value = checked((int)FixedPointMath.RoundDivideTiesToEven(
                ((Int128)plane.Samples[i] * plane.ScaleNumerator * plane.OffsetDenominator +
                (Int128)plane.OffsetNumerator * plane.ScaleDenominator) * 100,
                (Int128)plane.ScaleDenominator * plane.OffsetDenominator));
            bool usable = plane.Samples[i] is not (short.MinValue or short.MaxValue) && value is >= -10000 and <= 40000 &&
                !plane.QualityRanges.Any(r => (uint)i >= r.FirstSampleOffset && (ulong)i < (ulong)r.FirstSampleOffset + r.Count && r.QualityFlags != 0);
            next.LastSample = block.StartSimTimeNs + i * StepNs;
            if (!usable)
            { next.Count = next.Cursor = 0; next.Sum = 0; next.Poor = true; next.Pulse = new(); continue; }
            if (DetectPulse) { next.Pulse.Sample(next.LastSample.Value, value); }
            if (next.Count == WindowSamples) { next.Sum -= next.Values[next.Cursor]; }
            next.Values[next.Cursor] = value;
            next.Sum += value; next.Cursor = (next.Cursor + 1) % WindowSamples;
            next.Count = Math.Min(WindowSamples, next.Count + 1);
            if (next.Count == WindowSamples) { next.Poor = false; }
        }
        next.Identity = identity; next.NextTime = end; next.NextIndex = nextIndex; next.Sequence = block.BlockSequence;
        _state = next;
        return Read(next.LastSample!.Value);
    }
    public MeanPressureReading Read(long asOfSampleTimeNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(asOfSampleTimeNs);
        if (_state.LastSample is { } last && asOfSampleTimeNs < last)
        { throw new ArgumentException("MeanPressure.TimeBeforeSamples", nameof(asOfSampleTimeNs)); }
        var status = _state.LastSample is null || asOfSampleTimeNs - _state.LastSample > 500_000_000 ? WaveformMeasurementStatus.NoData :
            _state.Poor ? WaveformMeasurementStatus.PoorSignal : _state.Count < WindowSamples ? WaveformMeasurementStatus.WarmingUp : WaveformMeasurementStatus.Valid;
        return new(status, status == WaveformMeasurementStatus.Valid ? (int)FixedPointMath.RoundDivideTiesToEven(_state.Sum, WindowSamples) : null,
            status == WaveformMeasurementStatus.Valid ? _state.LastSample - (WindowSamples - 1) * StepNs : null, _state.LastSample)
        { Pulse = DetectPulse ? _state.Pulse.Read(asOfSampleTimeNs, status) : null };
    }
    public Checkpoint Capture() => new(ChannelId, DetectPulse, Copy(_state));
    public static MeanPressureMeasurement Restore(Checkpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new(checkpoint.Channel, checkpoint.DetectPulse) { _state = Copy(checkpoint.Value) };
    }
    public sealed class Checkpoint
    {
        internal Guid Channel { get; }
        internal bool DetectPulse { get; }
        internal State Value { get; }
        internal Checkpoint(Guid channel, bool detectPulse, State value) { Channel = channel; DetectPulse = detectPulse; Value = value; }
    }
    private static State Copy(State state) => state with { Values = (int[])state.Values.Clone(), Pulse = state.Pulse.Copy() };
    internal sealed record Identity(Guid Session, Guid Instance, ulong Timebase, ulong Stream, ulong Revision);
    internal sealed record State
    {
        internal Identity? Identity;
        internal long NextTime, Sum;
        internal ulong NextIndex, Sequence;
        internal long? LastSample;
        internal int[] Values = new int[WindowSamples];
        internal int Count, Cursor;
        internal bool Poor;
        internal PressurePulseTracker Pulse = new();
    }
}
