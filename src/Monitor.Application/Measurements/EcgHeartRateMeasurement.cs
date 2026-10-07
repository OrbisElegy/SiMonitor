// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Application.Measurements;

public sealed record DetectedEcgBeat(long PeakTimeNs, long ConfirmedAtNs);
public sealed record EcgHeartRateReading(WaveformMeasurementStatus Status, int? MilliBeatsPerMinute, long? LastBeatTimeNs);

// Single-channel teaching detector, input physical unit microvolts.
// No source settings/events, rhythm labels or display state enter this class.
public sealed class EcgHeartRateMeasurement
{
    private const long StepNs = 4_000_000;
    public const long RateWindowNs = 20_000_000_000;
    public const int MaximumRateIntervals = 8;
    private const long ExpiryNs = 5_000_000_000;
    private State _state;
    public EcgMonitoringSettings MonitoringSettings { get; }
    public Guid ChannelId { get; }
    public EcgHeartRateMeasurement(Guid channelId) : this(channelId, new EcgMonitoringSettings()) { }
    public EcgHeartRateMeasurement(Guid channelId, EcgMonitoringSettings monitoringSettings)
    {
        if (channelId == Guid.Empty) { throw new ArgumentException("EcgMeasurement.EmptyChannel", nameof(channelId)); }
        ArgumentNullException.ThrowIfNull(monitoringSettings);
        monitoringSettings.Validate();
        MonitoringSettings = monitoringSettings;
        _state = new(monitoringSettings);
        ChannelId = channelId;
    }
    public IReadOnlyList<DetectedEcgBeat> Consume(ReadOnlySpan<byte> wire)
        => Consume(wire, out _);

    public IReadOnlyList<DetectedEcgBeat> Consume(ReadOnlySpan<byte> wire, out IReadOnlyList<DetectedEcgRhythmEvent> rhythmEvents)
        => Consume(wire, out rhythmEvents, out _);

    public IReadOnlyList<DetectedEcgBeat> Consume(ReadOnlySpan<byte> wire,
        out IReadOnlyList<DetectedEcgRhythmEvent> rhythmEvents, out IReadOnlyList<DetectedEcgMonitoringEvent> monitoringEvents,
        EcgPacingEvidence? pacingEvidence = null)
    {
        rhythmEvents = [];
        monitoringEvents = [];
        var block = WaveformEnvelopeCodec.Decode(wire);
        var plane = block.Planes.SingleOrDefault(p => p.ChannelId == ChannelId)
            ?? throw new ArgumentException("EcgMeasurement.ChannelMissing", nameof(wire));
        if (plane.SampleRateNumerator != 250 || plane.SampleRateDenominator != 1 ||
            block.StartSimTimeNs < 0 || plane.Samples.Count == 0 ||
            (long)plane.Samples.Count * StepNs != block.DurationNs)
        { throw new ArgumentException("EcgMeasurement.UnsupportedSampling", nameof(wire)); }
        long end = checked(block.StartSimTimeNs + block.DurationNs);
        long[]? pacingPulses = pacingEvidence?.PulseTimesNs?.ToArray();
        if (pacingEvidence is not null && (pacingPulses is null || pacingPulses.Any(t => t < block.StartSimTimeNs || t >= end ||
            (t - block.StartSimTimeNs) % StepNs != 0) || pacingPulses.Zip(pacingPulses.Skip(1), (a, b) => a >= b).Any(v => v)))
        { throw new ArgumentException("EcgMeasurement.InvalidPacingEvidence", nameof(pacingEvidence)); }
        int pulseIndex = 0;
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
            _state.Sequence != ulong.MaxValue && block.BlockSequence == _state.Sequence + 1 ? Copy(_state) : new State(MonitoringSettings);
        List<DetectedEcgBeat> events = [];
        List<DetectedEcgRhythmEvent> transitions = [];
        List<DetectedEcgMonitoringEvent> monitoring = [];
        if (next.Identity is null && _state.Identity is not null)
        {
            var interrupted = _state.Rhythm.Copy();
            interrupted.Interrupt(_state.NextTime, EcgRhythmInterruption.StreamDiscontinuity, transitions);
            _state.Monitoring.Copy().Interrupt(_state.NextTime, EcgRhythmInterruption.StreamDiscontinuity, monitoring);
        }
        for (int i = 0; i < plane.Samples.Count; i++)
        {
            Int128 numerator = ((Int128)plane.Samples[i] * plane.ScaleNumerator * plane.OffsetDenominator +
                (Int128)plane.OffsetNumerator * plane.ScaleDenominator);
            int value = checked((int)FixedPointMath.RoundDivideTiesToEven(numerator,
                (Int128)plane.ScaleDenominator * plane.OffsetDenominator));
            bool usable = plane.Samples[i] is not (short.MinValue or short.MaxValue) && value is >= -10000 and <= 10000 &&
                !plane.QualityRanges.Any(r => (uint)i >= r.FirstSampleOffset && (ulong)i < (ulong)r.FirstSampleOffset + r.Count && r.QualityFlags != 0);
            long timeNs = block.StartSimTimeNs + i * StepNs;
            int before = events.Count;
            Sample(next, timeNs, value, usable, events);
            bool pulse = pacingPulses is not null && pulseIndex < pacingPulses.Length && pacingPulses[pulseIndex] == timeNs;
            if (pulse) { pulseIndex++; }
            next.Monitoring.Sample(timeNs, value, usable && !next.Poor,
                events.Count > before ? events[^1] : null, next.Start, next.LastActive, next.Active, pacingPulses is not null, pulse, monitoring);
            bool analyzable = usable && !next.Poor && !next.Uncountable &&
                timeNs - (next.LastBeat ?? next.FirstSample!.Value) < ExpiryNs;
            next.Rhythm.Sample(timeNs, value, analyzable, transitions);
            if (analyzable && events.Count > before) { next.Rhythm.Beat(events[^1], transitions); }
        }
        next.Identity = identity; next.NextTime = end; next.NextIndex = nextIndex; next.Sequence = block.BlockSequence;
        _state = next;
        rhythmEvents = transitions.AsReadOnly();
        monitoringEvents = monitoring.AsReadOnly();
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
        long[] peaks = _state.Peaks.Where(p => p >= asOfSampleTimeNs - RateWindowNs).ToArray();
        int? rate = peaks.Length < 2 ? null : checked((int)FixedPointMath.RoundDivideTiesToEven(
            (Int128)60_000_000_000_000L * (peaks.Length - 1), peaks[^1] - peaks[0]));
        return new(rate.HasValue ? WaveformMeasurementStatus.Valid : WaveformMeasurementStatus.WarmingUp, rate, _state.LastBeat);
    }

    public EcgRhythmReading ReadRhythm(long asOfSampleTimeNs) => _state.Rhythm.Read(Read(asOfSampleTimeNs).Status);

    public EcgMonitoringReading ReadMonitoring(long asOfSampleTimeNs) => _state.Monitoring.Read(Read(asOfSampleTimeNs).Status, asOfSampleTimeNs);

    public IReadOnlyList<DetectedEcgMonitoringEvent> Relearn(out IReadOnlyList<DetectedEcgRhythmEvent> rhythmEvents)
    {
        var next = Copy(_state);
        List<DetectedEcgMonitoringEvent> monitoring = [];
        List<DetectedEcgRhythmEvent> rhythms = [];
        long timeNs = next.LastSample ?? 0;
        next.Monitoring.Interrupt(timeNs, EcgRhythmInterruption.Relearning, monitoring);
        next.Rhythm.Interrupt(timeNs, EcgRhythmInterruption.Relearning, rhythms);
        _state = next;
        rhythmEvents = rhythms.AsReadOnly();
        return monitoring.AsReadOnly();
    }

    public Checkpoint Capture() => new(ChannelId, MonitoringSettings, Copy(_state));
    public static EcgHeartRateMeasurement Restore(Checkpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new(checkpoint.Channel, checkpoint.Settings) { _state = Copy(checkpoint.Value) };
    }
    public sealed class Checkpoint
    {
        internal Guid Channel { get; }
        internal State Value { get; }
        internal EcgMonitoringSettings Settings { get; }
        internal Checkpoint(Guid channel, EcgMonitoringSettings settings, State value) { Channel = channel; Settings = settings; Value = value; }
    }
    private static State Copy(State s) => s with { History = (int[])s.History.Clone(), Rhythm = s.Rhythm.Copy(), Monitoring = s.Monitoring.Copy() };

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
        if (s.Active && s.Max - s.Min < 250 && !IsCompactLowAmplitudeQrs(s) && time - s.LastActive >= 24_000_000 &&
            slope < Math.Max(threshold / 2, s.MaxSlope / 4)) { s.Active = false; }
        // A markedly steeper new deflection can follow a broad atrial prelude
        // before the quiet confirmation gap has elapsed. Restart at that edge
        // instead of rejecting the merged P/QRS envelope as an overlong complex.
        if (s.Active && time - s.Start >= 100_000_000 && (long)slope > (long)s.MaxSlope * 3)
        { s.Active = false; }
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
        // After recent fast, discrete beats, follow the steeper QRS contour so
        // a slower following wave cannot prolong it into a rejected complex.
        // Without that evidence retain the conservative activity threshold:
        // continuous disorganization must not be split into apparent beats.
        int activeThreshold = Math.Max(threshold / 2, s.MaxSlope / (HasRecentFastRhythm(s, time) ? 2 : 4));
        if (slope >= activeThreshold) { s.LastActive = time; }
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
        if (width is < 12_000_000 or > 180_000_000 || s.Max - s.Min < 200) { return; }
        if (s.Max - s.Min < 250 && !IsCompactLowAmplitudeQrs(s)) { return; }
        long interval = s.LastBeat is { } previous ? s.PeakTime - previous : long.MaxValue;
        // Suppress a secondary lobe and slower T-like slopes near a prior QRS.
        // This is a detector heuristic, not a physiological refractory model.
        // Compare candidate starts for T-like slope suppression: a changing
        // dominant lobe can move the peak within a wide QRS without moving the
        // next QRS onset. Peak spacing alone would discard that weaker beat.
        // A newly wide, high-amplitude complex after a learned narrow rhythm
        // can be an early PVC. Do not discard it solely because its broad slope
        // is slower than the preceding narrow QRS (needed for R-on-T evidence).
        bool widePremature = s.Monitoring.HasNarrowTemplate && width >= 100_000_000 &&
            s.Max - s.Min >= Math.Max(750, s.LastQrsAmplitude * 3 / 4) && s.MaxSlope >= 180;
        if (interval < 200_000_000 || s.Start - s.LastQrsStartNs < 360_000_000 &&
            (long)s.MaxSlope * 3 < (long)s.LastSlope * 2 && !widePremature) { return; }
        s.Peaks = interval <= ExpiryNs ? s.Peaks.Append(s.PeakTime).TakeLast(MaximumRateIntervals + 1).ToArray() : [s.PeakTime];
        s.LastBeat = s.PeakTime;
        s.LastSlope = s.MaxSlope;
        s.LastQrsStartNs = s.Start;
        s.LastQrsAmplitude = s.Max - s.Min;
        if (s.Peaks.Length >= 2) { s.Uncountable = false; }
        events.Add(new(s.PeakTime, time));
    }

    // Low-amplitude acceptance stays restricted to a compact, steep contour.
    // The same rule keeps that candidate alive across the confirmation gap.
    private static bool IsCompactLowAmplitudeQrs(State s) => s.Max - s.Min >= 200 &&
        s.LastActive - s.Start <= 80_000_000 && (long)s.MaxSlope * 4 >= (long)(s.Max - s.Min) * 3;

    // Teaching segmentation context, not a rhythm classification or predicted RR.
    private static bool HasRecentFastRhythm(State s, long time) => s.Peaks.Length >= 3 &&
        time - s.Peaks[^1] < 800_000_000 &&
        s.Peaks[^1] - s.Peaks[^2] <= 600_000_000 && s.Peaks[^2] - s.Peaks[^3] <= 600_000_000;

    internal sealed record Identity(Guid Session, Guid Instance, ulong Timebase, ulong Stream, ulong Revision);
    internal sealed record State(EcgMonitoringSettings Settings)
    {
        internal EcgRhythmAnalysis Rhythm = new(Settings.RhythmEndDelayMilliseconds);
        internal EcgMonitoringAnalysis Monitoring = new(Settings);
        internal Identity? Identity;
        internal long NextTime, Start, LastActive, PeakTime, LastQrsStartNs;
        internal ulong NextIndex, Sequence;
        internal long? FirstSample, LastSample, LastBeat;
        internal long[] Peaks = [];
        internal int[] History = new int[250];
        internal int Cursor, Count, FilterCount, Previous1, Previous2, GoodCount;
        internal int Baseline, Min, Max, MaxSlope, LastSlope, PeakDistance, LastQrsAmplitude;
        internal bool Poor, Uncountable, Active;
    }
}
