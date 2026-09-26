// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Application.Measurements;

public sealed record DetectedPlethPulse(long PeakTimeNs, long ConfirmedAtNs);
public sealed record PlethPulseRateReading(WaveformMeasurementStatus Status, int? MilliBeatsPerMinute, long? LastPulseTimeNs);

// Initial pulse detector for AcqPleth125@1 relative amplitude counts.
// No source settings/events, rhythm labels or display state enter this class.
public sealed class PlethPulseRateMeasurement
{
    private const long StepNs = 8_000_000;
    public const long RateWindowNs = 20_000_000_000;
    public const int MaximumRateIntervals = 8;
    private const long ExpiryNs = 5_000_000_000;
    private State _state = new();
    public Guid ChannelId { get; }
    public PlethPulseRateMeasurement(Guid channelId)
    {
        if (channelId == Guid.Empty) { throw new ArgumentException("PlethMeasurement.EmptyChannel", nameof(channelId)); }
        ChannelId = channelId;
    }
    public IReadOnlyList<DetectedPlethPulse> Consume(ReadOnlySpan<byte> wire)
    {
        var block = WaveformEnvelopeCodec.Decode(wire);
        var plane = block.Planes.SingleOrDefault(p => p.ChannelId == ChannelId)
            ?? throw new ArgumentException("PlethMeasurement.ChannelMissing", nameof(wire));
        if (plane.SampleRateNumerator != 125 || plane.SampleRateDenominator != 1 ||
            block.StartSimTimeNs < 0 || plane.Samples.Count == 0 ||
            (long)plane.Samples.Count * StepNs != block.DurationNs)
        { throw new ArgumentException("PlethMeasurement.UnsupportedSampling", nameof(wire)); }
        long end = checked(block.StartSimTimeNs + block.DurationNs);
        ulong nextIndex = checked(plane.FirstSampleIndex + (ulong)plane.Samples.Count);
        var identity = new Identity(block.SessionId, block.InstanceId, block.TimebaseEpoch, block.StreamEpoch, block.ConfigurationRevision);
        if (_state.Identity is { } old && old.Session == identity.Session && old.Instance == identity.Instance &&
            (identity.Timebase < old.Timebase || identity.Timebase == old.Timebase &&
            (identity.Stream < old.Stream || identity.Stream == old.Stream && identity.Revision < old.Revision)))
        { throw new ArgumentException("PlethMeasurement.EpochRollback", nameof(wire)); }
        // A changed identity starts an independent stream. Same-stream overlaps
        // and duplicate delivery are rejected atomically, never replayed.
        bool same = _state.Identity == identity;
        bool sameClock = _state.Identity is { } previous && previous.Session == identity.Session && previous.Instance == identity.Instance &&
            previous.Timebase == identity.Timebase && previous.Stream == identity.Stream;
        if (sameClock && (block.StartSimTimeNs < _state.NextTime || block.BlockSequence <= _state.Sequence || plane.FirstSampleIndex < _state.NextIndex))
        { throw new ArgumentException("PlethMeasurement.OutOfOrder", nameof(wire)); }
        var next = same && block.StartSimTimeNs == _state.NextTime && plane.FirstSampleIndex == _state.NextIndex &&
            _state.Sequence != ulong.MaxValue && block.BlockSequence == _state.Sequence + 1 ? Copy(_state) : new State();
        List<DetectedPlethPulse> events = [];
        for (int i = 0; i < plane.Samples.Count; i++)
        {
            Int128 numerator = ((Int128)plane.Samples[i] * plane.ScaleNumerator * plane.OffsetDenominator +
                (Int128)plane.OffsetNumerator * plane.ScaleDenominator);
            int value = checked((int)FixedPointMath.RoundDivideTiesToEven(numerator,
                (Int128)plane.ScaleDenominator * plane.OffsetDenominator));
            bool usable = plane.Samples[i] is not (short.MinValue or short.MaxValue) && value is >= -1000000 and <= 1000000 &&
                !plane.QualityRanges.Any(r => (uint)i >= r.FirstSampleOffset && (ulong)i < (ulong)r.FirstSampleOffset + r.Count && r.QualityFlags != 0);
            Sample(next, block.StartSimTimeNs + i * StepNs, value, usable, events);
        }
        next.Identity = identity; next.NextTime = end; next.NextIndex = nextIndex; next.Sequence = block.BlockSequence;
        _state = next;
        return events.AsReadOnly();
    }

    public PlethPulseRateReading Read(long asOfSampleTimeNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(asOfSampleTimeNs);
        if (_state.LastSample is { } last && asOfSampleTimeNs < last)
        { throw new ArgumentException("PlethMeasurement.TimeBeforeSamples", nameof(asOfSampleTimeNs)); }
        if (_state.LastSample is null || asOfSampleTimeNs - _state.LastSample > 500_000_000)
        { return new(WaveformMeasurementStatus.NoData, null, _state.LastPulse); }
        if (_state.Poor) { return new(WaveformMeasurementStatus.PoorSignal, null, null); }
        bool expired = asOfSampleTimeNs - (_state.LastPulse ?? _state.FirstSample!.Value) >= ExpiryNs;
        if (expired) { return new(WaveformMeasurementStatus.Stale, null, _state.LastPulse); }
        long[] peaks = _state.Peaks.Where(p => p >= asOfSampleTimeNs - RateWindowNs).ToArray();
        int? rate = peaks.Length < 2 ? null : checked((int)FixedPointMath.RoundDivideTiesToEven(
            (Int128)60_000_000_000_000L * (peaks.Length - 1), peaks[^1] - peaks[0]));
        return new(rate.HasValue ? WaveformMeasurementStatus.Valid : WaveformMeasurementStatus.WarmingUp, rate, _state.LastPulse);
    }

    public Checkpoint Capture() => new(ChannelId, Copy(_state));
    public static PlethPulseRateMeasurement Restore(Checkpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new(checkpoint.Channel) { _state = Copy(checkpoint.Value) };
    }
    public sealed class Checkpoint
    {
        internal Guid Channel { get; }
        internal State Value { get; }
        internal Checkpoint(Guid channel, State value) { Channel = channel; Value = value; }
    }
    private static State Copy(State s) => s with { };

    private static void Sample(State s, long time, int raw, bool usable, List<DetectedPlethPulse> events)
    {
        s.FirstSample ??= time; s.LastSample = time;
        if (!usable)
        {
            s.Poor = true; s.Initialized = s.Active = false;
            s.Peaks = []; s.LastPulse = null; s.FilterCount = s.GoodCount = s.LastAmplitude = 0; return;
        }
        s.GoodCount = Math.Min(125, s.GoodCount + 1);
        if (s.Poor && s.GoodCount < 125) { return; }
        s.Poor = false;
        int value = s.FilterCount < 2 ? raw : Math.Max(Math.Min(raw, s.Previous1), Math.Min(Math.Max(raw, s.Previous1), s.Previous2));
        s.Previous2 = s.Previous1; s.Previous1 = raw; s.FilterCount = Math.Min(2, s.FilterCount + 1);
        if (!s.Initialized) { s.Initialized = true; s.Trough = value; s.TroughTime = time; return; }
        if (s.LastPulse is { } last && time - last >= ExpiryNs) { s.LastAmplitude = 0; }
        int threshold = Math.Max(30, s.LastAmplitude / 5);
        if (!s.Active)
        {
            if (value <= s.Trough) { s.Trough = value; s.TroughTime = time; }
            if (value - s.Trough < threshold) { return; }
            s.Active = true; s.Peak = value; s.PeakTime = time;
        }
        if (value > s.Peak) { s.Peak = value; s.PeakTime = time; }
        if (time - s.TroughTime > 2_000_000_000)
        { s.Active = false; s.Trough = value; s.TroughTime = time; return; }
        if (s.Peak - value < threshold) { return; }
        s.Active = false;
        int amplitude = s.Peak - s.Trough;
        long rise = s.PeakTime - s.TroughTime;
        s.Trough = value; s.TroughTime = time;
        long interval = s.LastPulse is { } previous ? s.PeakTime - previous : long.MaxValue;
        if (rise < 40_000_000 || amplitude < 60 || interval < 300_000_000) { return; }
        s.Peaks = interval <= ExpiryNs ? s.Peaks.Append(s.PeakTime).TakeLast(MaximumRateIntervals + 1).ToArray() : [s.PeakTime];
        s.LastPulse = s.PeakTime; s.LastAmplitude = amplitude;
        events.Add(new(s.PeakTime, time));
    }

    internal sealed record Identity(Guid Session, Guid Instance, ulong Timebase, ulong Stream, ulong Revision);
    internal sealed record State
    {
        internal Identity? Identity;
        internal long NextTime, TroughTime, PeakTime;
        internal ulong NextIndex, Sequence;
        internal long? FirstSample, LastSample, LastPulse;
        // Replaced, never mutated: checkpoint copies can safely share this array.
        internal long[] Peaks = [];
        internal int FilterCount, Previous1, Previous2, GoodCount, Trough, Peak, LastAmplitude;
        internal bool Poor, Initialized, Active;
    }
}
