// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Application.Measurements;

public sealed record DetectedEcgBeat(long PeakTimeNs, long ConfirmedAtNs);
public sealed record EcgHeartRateReading(WaveformMeasurementStatus Status, int? MilliBeatsPerMinute, long? LastBeatTimeNs);

// Initial single-channel engineering detector, input physical unit microvolts.
// No source settings/events, rhythm labels or display state enter this class.
public sealed class EcgHeartRateMeasurement
{
    private const long StepNs = 4_000_000;
    public const long RateWindowNs = 20_000_000_000;
    public const int MaximumRateIntervals = 8;
    private const long ExpiryNs = 5_000_000_000;
    private State _state = new();
    public Guid ChannelId { get; }
    public EcgHeartRateMeasurement(Guid channelId)
    {
        if (channelId == Guid.Empty) { throw new ArgumentException("EcgMeasurement.EmptyChannel", nameof(channelId)); }
        ChannelId = channelId;
    }
    public IReadOnlyList<DetectedEcgBeat> Consume(ReadOnlySpan<byte> wire)
    {
        var block = WaveformEnvelopeCodec.Decode(wire);
        var plane = block.Planes.SingleOrDefault(p => p.ChannelId == ChannelId)
            ?? throw new ArgumentException("EcgMeasurement.ChannelMissing", nameof(wire));
        if (plane.SampleRateNumerator != 250 || plane.SampleRateDenominator != 1 ||
            block.StartSimTimeNs < 0 || plane.Samples.Count == 0 ||
            (long)plane.Samples.Count * StepNs != block.DurationNs)
        { throw new ArgumentException("EcgMeasurement.UnsupportedSampling", nameof(wire)); }
        long end = checked(block.StartSimTimeNs + block.DurationNs);
        ulong nextIndex = checked(plane.FirstSampleIndex + (ulong)plane.Samples.Count);
        var identity = new Identity(block.SessionId, block.InstanceId, block.TimebaseEpoch, block.StreamEpoch, block.ConfigurationRevision);
        if (_state.Identity is { } old && old.Session == identity.Session && old.Instance == identity.Instance &&
            (identity.Timebase < old.Timebase || identity.Timebase == old.Timebase &&
            (identity.Stream < old.Stream || identity.Stream == old.Stream && identity.Revision < old.Revision)))
        { throw new ArgumentException("EcgMeasurement.EpochRollback", nameof(wire)); }
        // A changed identity starts an independent stream. Same-stream overlaps
        // and duplicate delivery are rejected atomically, never replayed.
        bool same = _state.Identity == identity;
        bool sameClock = _state.Identity is { } previous && previous.Session == identity.Session && previous.Instance == identity.Instance &&
            previous.Timebase == identity.Timebase && previous.Stream == identity.Stream;
        if (sameClock && (block.StartSimTimeNs < _state.NextTime || block.BlockSequence <= _state.Sequence || plane.FirstSampleIndex < _state.NextIndex))
        { throw new ArgumentException("EcgMeasurement.OutOfOrder", nameof(wire)); }
        var next = same && block.StartSimTimeNs == _state.NextTime && plane.FirstSampleIndex == _state.NextIndex &&
            _state.Sequence != ulong.MaxValue && block.BlockSequence == _state.Sequence + 1 ? Copy(_state) : new State();
        List<DetectedEcgBeat> events = [];
        for (int i = 0; i < plane.Samples.Count; i++)
        {
            Int128 numerator = ((Int128)plane.Samples[i] * plane.ScaleNumerator * plane.OffsetDenominator +
                (Int128)plane.OffsetNumerator * plane.ScaleDenominator);
            int value = checked((int)FixedPointMath.RoundDivideTiesToEven(numerator,
                (Int128)plane.ScaleDenominator * plane.OffsetDenominator));
            bool usable = plane.Samples[i] is not (short.MinValue or short.MaxValue) && value is >= -10000 and <= 10000 &&
                !plane.QualityRanges.Any(r => (uint)i >= r.FirstSampleOffset && (ulong)i < (ulong)r.FirstSampleOffset + r.Count && r.QualityFlags != 0);
            Sample(next, block.StartSimTimeNs + i * StepNs, value, usable, events);
        }
        next.Identity = identity; next.NextTime = end; next.NextIndex = nextIndex; next.Sequence = block.BlockSequence;
        _state = next;
        return events.AsReadOnly();
    }

    public EcgHeartRateReading Read(long asOfSampleTimeNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(asOfSampleTimeNs);
        if (_state.LastSample is { } last && asOfSampleTimeNs < last)
        { throw new ArgumentException("EcgMeasurement.TimeBeforeSamples", nameof(asOfSampleTimeNs)); }
        if (_state.LastSample is null || asOfSampleTimeNs - _state.LastSample > 500_000_000)
        { return new(WaveformMeasurementStatus.NoData, null, _state.LastBeat); }
        if (_state.Poor) { return new(WaveformMeasurementStatus.PoorSignal, null, null); }
        bool expired = asOfSampleTimeNs - (_state.LastBeat ?? _state.FirstSample!.Value) >= ExpiryNs;
        bool activity = _state.Count == 250 && _state.History.Max() - _state.History.Min() >= 100;
        if ((_state.Uncountable || expired) && activity)
        { return new(WaveformMeasurementStatus.Uncountable, null, _state.LastBeat); }
        if (expired) { return new(WaveformMeasurementStatus.Stale, null, _state.LastBeat); }
        var peaks = _state.Peaks.Where(p => p >= asOfSampleTimeNs - RateWindowNs).ToArray();
        int? rate = peaks.Length < 2 ? null : checked((int)FixedPointMath.RoundDivideTiesToEven(
            (Int128)60_000_000_000_000L * (peaks.Length - 1), peaks[^1] - peaks[0]));
        return new(rate.HasValue ? WaveformMeasurementStatus.Valid : WaveformMeasurementStatus.WarmingUp, rate, _state.LastBeat);
    }

    public Checkpoint Capture() => new(ChannelId, Copy(_state));
    public static EcgHeartRateMeasurement Restore(Checkpoint checkpoint)
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
    private static State Copy(State s) => s with { History = (int[])s.History.Clone() };

    private static void Sample(State s, long time, int raw, bool usable, List<DetectedEcgBeat> events)
    {
        s.FirstSample ??= time; s.LastSample = time;
        if (!usable)
        {
            s.Poor = true; s.Active = false; s.Peaks = []; s.LastBeat = null;
            s.Count = s.FilterCount = s.GoodCount = 0; s.LastSlope = 0; s.Uncountable = false; return;
        }
        s.GoodCount = Math.Min(250, s.GoodCount + 1);
        if (s.Poor && s.GoodCount < 250) { return; }
        s.Poor = false;
        int value = s.FilterCount < 2 ? raw : Math.Max(Math.Min(raw, s.Previous1), Math.Min(Math.Max(raw, s.Previous1), s.Previous2));
        s.Previous2 = s.Previous1; s.Previous1 = raw; s.FilterCount = Math.Min(2, s.FilterCount + 1);
        int delayed = s.History[(s.Cursor + 245) % 250];
        int slope = s.Count < 5 ? 0 : Math.Abs(value - delayed);
        s.History[s.Cursor] = value; s.Cursor = (s.Cursor + 1) % 250; s.Count = Math.Min(250, s.Count + 1);
        if (s.LastBeat is { } lastBeat && time - lastBeat >= ExpiryNs) { s.LastSlope = 0; }
        int threshold = Math.Max(90, s.LastSlope / 4);
        // Discard a subthreshold prelude after a short quiet gap. Otherwise a
        // short-PR P wave can keep the candidate open through the following QRS.
        // Accepted QRS candidates still require the original64ms confirmation.
        if (s.Active && s.Max - s.Min < 250 && time - s.LastActive >= 24_000_000 &&
            slope < Math.Max(threshold / 2, s.MaxSlope / 4)) { s.Active = false; }
        if (!s.Active)
        {
            if (slope < threshold || s.Count < 10) { return; }
            s.Active = true; s.Start = s.LastActive = time; s.PeakTime = time;
            s.Baseline = delayed; s.Min = Math.Min(value, delayed); s.Max = Math.Max(value, delayed);
            s.PeakDistance = Math.Abs(value - delayed); s.MaxSlope = slope;
        }
        s.Min = Math.Min(s.Min, value); s.Max = Math.Max(s.Max, value); s.MaxSlope = Math.Max(s.MaxSlope, slope);
        int distance = Math.Abs(value - s.Baseline);
        if (distance > s.PeakDistance) { s.PeakDistance = distance; s.PeakTime = time; }
        if (slope >= Math.Max(threshold / 2, s.MaxSlope / 4)) { s.LastActive = time; }
        if (time - s.Start > 240_000_000)
        {
            // A merged wide complex is not evidence of an uncountable rhythm.
            // Require sustained activity without a recent discrete confirmation.
            if (time - (s.LastBeat ?? s.FirstSample!.Value) >= 2_000_000_000)
            { s.Uncountable = true; s.Peaks = []; }
            s.Active = false; s.LastSlope = 0; return;
        }
        if (time - s.LastActive < 64_000_000) { return; }
        s.Active = false;
        long width = s.LastActive - s.Start;
        if (width is < 12_000_000 or > 180_000_000 || s.Max - s.Min < 250) { return; }
        long interval = s.LastBeat is { } previous ? s.PeakTime - previous : long.MaxValue;
        // Suppress a secondary lobe and slower T-like slopes near a prior QRS.
        // This is a detector heuristic, not a physiological refractory model.
        if (interval < 200_000_000 || interval < 360_000_000 && (long)s.MaxSlope * 3 < (long)s.LastSlope * 2) { return; }
        s.Peaks = interval <= ExpiryNs ? s.Peaks.Append(s.PeakTime).TakeLast(MaximumRateIntervals + 1).ToArray() : [s.PeakTime];
        s.LastBeat = s.PeakTime; s.LastSlope = s.MaxSlope;
        if (s.Peaks.Length >= 2) { s.Uncountable = false; }
        events.Add(new(s.PeakTime, time));
    }

    internal sealed record Identity(Guid Session, Guid Instance, ulong Timebase, ulong Stream, ulong Revision);
    internal sealed record State
    {
        internal Identity? Identity;
        internal long NextTime, Start, LastActive, PeakTime;
        internal ulong NextIndex, Sequence;
        internal long? FirstSample, LastSample, LastBeat;
        internal long[] Peaks = [];
        internal int[] History = new int[250];
        internal int Cursor, Count, FilterCount, Previous1, Previous2, GoodCount;
        internal int Baseline, Min, Max, MaxSlope, LastSlope, PeakDistance;
        internal bool Poor, Uncountable, Active;
    }
}
