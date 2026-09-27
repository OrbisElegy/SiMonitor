// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Application.Measurements;

public enum WaveformMeasurementStatus { WarmingUp, Valid, Stale, NoData, PoorSignal, Uncountable }
public sealed record CapnographyReading(WaveformMeasurementStatus Status, int? Value, long? MeasuredAtNs);
public sealed record CapnographyActivity(WaveformMeasurementStatus Status, long? ContinuousUsableSinceNs, long? LastExpirationNs, long? LastSampleNs);
public sealed record CapnographyResult(CapnographyReading EndTidalCentiMmHg, CapnographyReading RespirationsMilliPerMinute)
{
    public CapnographyActivity? Activity { get; init; }
}
public sealed record MeasuredExpiration(long RiseTimeNs, long PeakTimeNs, long ConfirmedAtNs, int EndTidalCentiMmHg);

// Initial engineering estimator for AcqCO2_100@1, baseline <=2mmHg and
// excursions >=5mmHg. Not validated for artifacts, rebreathing or CPR.
// Input is validated acquisition wire only: no generator plan/event access.
public sealed class CapnographyMeasurement
{
    private const long StepNs = 10_000_000;
    private const long FreshnessNs = 10_000_000_000;
    public const long RateWindowNs = 20_000_000_000;
    public const int MaximumRateIntervals = 4;
    private State _state = new();
    public Guid ChannelId { get; }

    public CapnographyMeasurement(Guid channelId)
    {
        if (channelId == Guid.Empty) { throw new ArgumentException("Capnography.EmptyChannel", nameof(channelId)); }
        ChannelId = channelId;
    }

    public IReadOnlyList<MeasuredExpiration> Consume(ReadOnlySpan<byte> wire)
    {
        var block = WaveformEnvelopeCodec.Decode(wire);
        var plane = block.Planes.SingleOrDefault(p => p.ChannelId == ChannelId)
            ?? throw new ArgumentException("Capnography.ChannelMissing", nameof(wire));
        if (plane.SampleRateNumerator != 100 || plane.SampleRateDenominator != 1 ||
            block.StartSimTimeNs < 0 || plane.Samples.Count == 0 ||
            (long)plane.Samples.Count * StepNs != block.DurationNs)
        { throw new ArgumentException("Capnography.UnsupportedSampling", nameof(wire)); }
        long end = checked(block.StartSimTimeNs + block.DurationNs);
        ulong nextIndex = checked(plane.FirstSampleIndex + (ulong)plane.Samples.Count);
        var identity = new Identity(block.SessionId, block.InstanceId, block.TimebaseEpoch, block.StreamEpoch, block.ConfigurationRevision);
        if (_state.Identity is { } old && old.Session == identity.Session && old.Instance == identity.Instance &&
            (identity.Timebase < old.Timebase || identity.Timebase == old.Timebase &&
            (identity.Stream < old.Stream || identity.Stream == old.Stream && identity.Revision < old.Revision)))
        { throw new ArgumentException("Capnography.EpochRollback", nameof(wire)); }
        // A changed identity starts an independent stream. Same-stream overlaps
        // and duplicate delivery are rejected atomically, never replayed.
        bool same = _state.Identity == identity;
        bool sameClock = _state.Identity is { } previous && previous.Session == identity.Session && previous.Instance == identity.Instance &&
            previous.Timebase == identity.Timebase && previous.Stream == identity.Stream;
        if (sameClock && (block.StartSimTimeNs < _state.NextTime || block.BlockSequence <= _state.Sequence || plane.FirstSampleIndex < _state.NextIndex))
        { throw new ArgumentException("Capnography.OutOfOrder", nameof(wire)); }
        var next = same && block.StartSimTimeNs == _state.NextTime && plane.FirstSampleIndex == _state.NextIndex &&
            _state.Sequence != ulong.MaxValue && block.BlockSequence == _state.Sequence + 1 ? _state with { } : new State();
        List<MeasuredExpiration> events = [];
        for (int i = 0; i < plane.Samples.Count; i++)
        {
            Int128 numerator = ((Int128)plane.Samples[i] * plane.ScaleNumerator * plane.OffsetDenominator +
                (Int128)plane.OffsetNumerator * plane.ScaleDenominator) * 100;
            int value = checked((int)FixedPointMath.RoundDivideTiesToEven(numerator,
                (Int128)plane.ScaleDenominator * plane.OffsetDenominator));
            bool usable = plane.Samples[i] is not (short.MinValue or short.MaxValue) && value is >= 0 and <= 15000 &&
                !plane.QualityRanges.Any(r => (uint)i >= r.FirstSampleOffset && (ulong)i < (ulong)r.FirstSampleOffset + r.Count && r.QualityFlags != 0);
            Sample(next, block.StartSimTimeNs + i * StepNs, value, usable, events);
        }
        next.Identity = identity;
        next.NextTime = end;
        next.NextIndex = nextIndex;
        next.Sequence = block.BlockSequence;
        _state = next;
        return events.AsReadOnly();
    }

    public CapnographyResult Read(long asOfSampleTimeNs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(asOfSampleTimeNs);
        if (_state.LastSample is { } last && asOfSampleTimeNs < last)
        { throw new ArgumentException("Capnography.TimeBeforeSamples", nameof(asOfSampleTimeNs)); }
        WaveformMeasurementStatus? unavailable = _state.LastSample is null || asOfSampleTimeNs - _state.LastSample > 500_000_000
            ? WaveformMeasurementStatus.NoData : _state.Poor ? WaveformMeasurementStatus.PoorSignal : null;
        CapnographyReading Reading(int? value, bool expiredWindow = false)
        {
            var status = unavailable ?? (expiredWindow || asOfSampleTimeNs - (_state.CompletedAt ?? _state.FirstSample!.Value) >= FreshnessNs
                ? WaveformMeasurementStatus.Stale : value.HasValue ? WaveformMeasurementStatus.Valid : WaveformMeasurementStatus.WarmingUp);
            return new(status, status == WaveformMeasurementStatus.Valid ? value : null, _state.CompletedAt);
        }
        // Both endpoints must be in the sampling-time window. Pool durations,
        // not instantaneous rates; long intervals caused by pauses are retained.
        var intervals = _state.Intervals.Where(i => i.StartNs >= asOfSampleTimeNs - RateWindowNs).ToArray();
        int? rate = intervals.Length == 0 ? null : checked((int)FixedPointMath.RoundDivideTiesToEven(
            (Int128)60_000_000_000_000L * intervals.Length, intervals.Sum(i => i.EndNs - i.StartNs)));
        return new(Reading(_state.EndTidal), Reading(rate, _state.Intervals.Length > 0 && intervals.Length == 0))
        { Activity = new(unavailable ?? WaveformMeasurementStatus.Valid, _state.UsableSince, _state.CompletedAt, _state.LastSample) };
    }

    // Opaque, immutable in-process continuation; no externally editable state
    // or unbounded replay history. Durable checkpoint serialization is deferred.
    public Checkpoint Capture() => new(ChannelId, _state with { });
    public static CapnographyMeasurement Restore(Checkpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new(checkpoint.Channel) { _state = checkpoint.Value with { } };
    }
    public sealed class Checkpoint
    {
        internal Guid Channel { get; }
        internal State Value { get; }
        internal Checkpoint(Guid channel, State value)
        {
            Channel = channel;
            Value = value;
        }
    }

    private static void Sample(State s, long time, int raw, bool usable, List<MeasuredExpiration> events)
    {
        s.FirstSample ??= time;
        s.LastSample = time;
        if (!usable)
        {
            ClearCycle(s);
            s.Poor = true;
            s.EndTidal = null;
            s.Intervals = [];
            s.CompletedAt = s.PreviousStart = null;
            s.FilterCount = 0;
            s.UsableSince = null;
            return;
        }
        if (!s.Poor) { s.UsableSince ??= time; }
        // A causal three-sample median rejects isolated one-sample spikes;
        // source samples are untouched. Detection time includes confirmation.
        int value = raw;
        if (s.FilterCount >= 2) { value = Math.Max(Math.Min(raw, s.Previous1), Math.Min(Math.Max(raw, s.Previous1), s.Previous2)); }
        s.Previous2 = s.Previous1;
        s.Previous1 = raw;
        s.FilterCount = Math.Min(2, s.FilterCount + 1);
        if (s.FilterCount < 2) { return; }
        if (!s.Active)
        {
            if (value <= 200)
            {
                s.LowCount = Math.Min(10, s.LowCount + 1);
                if (s.LowCount >= 10)
                {
                    s.Armed = true;
                    s.Poor = false;
                    s.UsableSince ??= time;
                }
                s.HighCount = 0;
            }
            else if (s.Armed && value >= 500)
            {
                if (s.HighCount++ == 0)
                {
                    s.Start = time;
                    s.Peak = value;
                }
                if (value >= s.Peak)
                {
                    s.Peak = value;
                    s.PeakTime = time;
                }
                if (s.HighCount >= 10)
                {
                    s.Active = true;
                    s.LowCount = 0;
                }
            }
            else { s.LowCount = s.HighCount = 0; }
            return;
        }
        if (value >= s.Peak)
        {
            s.Peak = value;
            s.PeakTime = time;
        }
        if (value <= 200) { s.LowCount++; } else { s.LowCount = 0; }
        if (time - s.Start >= FreshnessNs)
        {
            ClearCycle(s);
            s.Poor = true;
            s.UsableSince = null;
            s.EndTidal = null;
            s.Intervals = [];
            s.PreviousStart = s.CompletedAt = null;
            return;
        }
        if (s.LowCount < 10) { return; }
        if (time - s.Start >= 300_000_000)
        {
            long? interval = s.PreviousStart is { } previous ? s.Start - previous : null;
            s.Intervals = interval is >= 500_000_000 and <= RateWindowNs
                ? s.Intervals.Append(new Interval(s.PreviousStart!.Value, s.Start))
                    .Where(i => i.StartNs >= time - RateWindowNs).TakeLast(MaximumRateIntervals).ToArray() : [];
            s.EndTidal = s.Peak;
            s.CompletedAt = time;
            s.PreviousStart = s.Start;
            events.Add(new(s.Start, s.PeakTime, time, s.Peak));
        }
        ClearCycle(s);
        s.Armed = true;
    }

    private static void ClearCycle(State s)
    {
        s.Active = s.Armed = false;
        s.HighCount = s.LowCount = 0;
    }
    internal sealed record Identity(Guid Session, Guid Instance, ulong Timebase, ulong Stream, ulong Revision);
    internal sealed record Interval(long StartNs, long EndNs);
    internal sealed record State
    {
        internal Identity? Identity;
        internal long NextTime, Start, PeakTime;
        internal ulong NextIndex, Sequence;
        internal long? FirstSample, LastSample, PreviousStart, CompletedAt, UsableSince;
        internal int? EndTidal;
        // Replace rather than mutate arrays, preserving checkpoint ownership.
        internal Interval[] Intervals = [];
        internal bool Poor, Armed, Active;
        internal int LowCount, HighCount, Peak, Previous1, Previous2, FilterCount;
    }
}
