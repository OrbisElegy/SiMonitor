// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Application.Measurements;

public enum WaveformMeasurementStatus { WarmingUp, Valid, Stale, NoData, PoorSignal, Uncountable, OutOfRange }
public sealed record CapnographyReading(WaveformMeasurementStatus Status, int? Value, long? MeasuredAtNs);
public sealed record CapnographyActivity(WaveformMeasurementStatus Status, long? ContinuousUsableSinceNs, long? LastExpirationNs, long? LastSampleNs);
public sealed record CapnographyResult(CapnographyReading EndTidalCentiMmHg, CapnographyReading RespirationsMilliPerMinute)
{
    public CapnographyActivity? Activity { get; init; }
}
public sealed record MeasuredExpiration(long RiseTimeNs, long PeakTimeNs, long ConfirmedAtNs, int EndTidalCentiMmHg)
{
    public long EndTidalTimeNs { get; init; } = PeakTimeNs;
}

// Engineering estimator for AcqCO2_100@1 with an acquired inspiratory baseline
// and excursions >=5mmHg. Terminal descent is inferred from samples alone;
// this is not a validated device algorithm or a diagnostic classifier.
// Input is validated acquisition wire only: no generator plan/event access.
public sealed class CapnographyMeasurement
{
    private const long StepNs = 10_000_000;
    private const long FreshnessNs = 10_000_000_000;
    private const int HistorySamples = 1000;
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
            _state.Sequence != ulong.MaxValue && block.BlockSequence == _state.Sequence + 1 ? Copy(_state) : new State();
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
    public Checkpoint Capture() => new(ChannelId, Copy(_state));
    public static CapnographyMeasurement Restore(Checkpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new(checkpoint.Channel) { _state = Copy(checkpoint.Value) };
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
            s.Baseline = null;
            s.BaselineCount = 0;
            s.HistoryCount = s.HistoryCursor = 0;
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
        s.History[s.HistoryCursor] = value;
        s.HistoryCursor = (s.HistoryCursor + 1) % HistorySamples;
        s.HistoryCount = Math.Min(HistorySamples, s.HistoryCount + 1);
        if (!s.Active)
        {
            if (s.Baseline is null)
            {
                // A stable segment establishes an observed baseline even when
                // inspired CO2 is nonzero. A constant elevated trace can arm,
                // but cannot complete a breath without a subsequent excursion.
                if (s.BaselineCount == 0 || value < s.BaselineMinimum - 100 || value > s.BaselineMaximum + 100)
                {
                    s.BaselineMinimum = s.BaselineMaximum = value;
                    s.BaselineCount = 1;
                }
                else
                {
                    s.BaselineMinimum = Math.Min(s.BaselineMinimum, value);
                    s.BaselineMaximum = Math.Max(s.BaselineMaximum, value);
                    s.BaselineCount++;
                    if (s.BaselineMaximum - s.BaselineMinimum > 100)
                    { s.BaselineMinimum = s.BaselineMaximum = value; s.BaselineCount = 1; }
                }
                if (s.BaselineCount >= 10)
                {
                    s.Baseline = s.BaselineMinimum;
                    s.Armed = true;
                    s.Poor = false;
                    s.UsableSince ??= time;
                }
                return;
            }
            if (value <= s.Baseline.Value + 200)
            {
                s.Baseline = Math.Min(s.Baseline.Value, value);
                s.LowCount = Math.Min(10, s.LowCount + 1);
                if (s.LowCount >= 10)
                {
                    s.Armed = true;
                    s.Poor = false;
                    s.UsableSince ??= time;
                }
                s.HighCount = 0;
            }
            else if (s.Armed && value >= s.Baseline.Value + 500)
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
        int lowThreshold = s.Baseline!.Value + Math.Max(200, (s.Peak - s.Baseline.Value) / 5);
        if (value <= lowThreshold)
        {
            s.LowMinimum = s.LowCount == 0 ? value : Math.Min(s.LowMinimum, value);
            s.LowCount++;
        }
        else { s.LowCount = 0; }
        if (time - s.Start >= FreshnessNs)
        {
            ClearCycle(s);
            s.Poor = true;
            s.UsableSince = null;
            s.EndTidal = null;
            s.Intervals = [];
            s.PreviousStart = s.CompletedAt = null;
            s.Baseline = null;
            s.BaselineCount = 0;
            return;
        }
        // A settled lower segment can establish a changed inspired baseline,
        // even when it no longer crosses the previous cycle's low threshold.
        // Require 180 ms below the known threshold, or a separately settled
        // 200 ms lower segment, so brief near-baseline clefts stay intrabreath.
        bool settledLow = SettledLow(s, out int lowMinimum);
        if (s.LowCount < 18 && !settledLow) { return; }
        if (settledLow) { s.LowMinimum = lowMinimum; }
        if (time - s.Start >= 300_000_000)
        {
            long? interval = s.PreviousStart is { } previous ? s.Start - previous : null;
            s.Intervals = interval is >= 500_000_000 and <= RateWindowNs
                ? s.Intervals.Append(new Interval(s.PreviousStart!.Value, s.Start))
                    .Where(i => i.StartNs >= time - RateWindowNs).TakeLast(MaximumRateIntervals).ToArray() : [];
            // Ordinary flat/rising plateaus retain the established filtered
            // peak. Only an observed reverse-plateau corner replaces it.
            s.EndTidal = s.Peak;
            long endTidalTime = s.PeakTime;
            if (ReversePlateauTerminal(s, time) is { } reverse)
            { s.EndTidal = reverse.Value; endTidalTime = reverse.TimeNs; }
            // Coalesce a terminal shoulder within 1 mmHg of the measured peak.
            // This retains the established ordinary capnogram's quantized value
            // instead of biasing it downward by the descent detector's delay.
            if (s.Peak - s.EndTidal.Value is >= 0 and <= 100)
            { s.EndTidal = s.Peak; endTidalTime = s.PeakTime; }
            s.CompletedAt = time;
            s.PreviousStart = s.Start;
            events.Add(new(s.Start, s.PeakTime, time, s.EndTidal.Value) { EndTidalTimeNs = endTidalTime });
        }
        s.Baseline = s.LowMinimum;
        ClearCycle(s);
        s.Armed = true;
    }

    private static bool SettledLow(State s, out int minimum)
    {
        minimum = int.MaxValue;
        if (s.HistoryCount < 20) { return false; }
        int maximum = int.MinValue;
        for (int index = s.HistoryCount - 20; index < s.HistoryCount; index++)
        {
            int value = HistoryAt(s, index);
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }
        return maximum - minimum <= 20 && maximum <= s.Peak - 500;
    }

    private static (int Value, long TimeNs)? ReversePlateauTerminal(State s, long timeNs)
    {
        // Search only the final descent into the acquired low segment. A
        // preceding cleft ends at its rebound and is outside this segment.
        int half = s.LowMinimum + (s.Peak - s.LowMinimum) / 2;
        int high = s.HistoryCount - 1;
        while (high >= 0 && HistoryAt(s, high) <= half) { high--; }
        if (high < 10) { return null; }
        int start = high;
        while (start > 0 && HistoryAt(s, start) <= HistoryAt(s, start - 1) + 10) { start--; }
        (int Value, long TimeNs)? result = null;
        for (int index = start + 5; index <= high && index + 10 < s.HistoryCount; index++)
        {
            int leftDrop = HistoryAt(s, index - 5) - HistoryAt(s, index);
            int rightDrop = HistoryAt(s, index + 5) - HistoryAt(s, index + 10);
            if (leftDrop < 3 || rightDrop * 2 <= leftDrop * 3 ||
                !NearlyLinearDescent(s, index - 5, leftDrop) || !NearlyLinearDescent(s, index + 5, rightDrop))
            { continue; }
            // Intersect the two local descending lines across their 50 ms
            // gap. This recovers a steepening corner without equating an
            // earlier maximum or an arbitrary absolute slope with end-tidal CO2.
            int difference = rightDrop - leftDrop;
            int numerator = HistoryAt(s, index + 5) - HistoryAt(s, index) + rightDrop;
            if (numerator < 0 || numerator > difference) { continue; }
            int value = HistoryAt(s, index) - checked((int)FixedPointMath.RoundDivideTiesToEven((Int128)leftDrop * numerator, difference));
            long at = timeNs - (s.HistoryCount - 1L - index) * StepNs +
                (long)FixedPointMath.RoundDivideTiesToEven((Int128)5 * StepNs * numerator, difference);
            if (value >= half && at > s.Start) { result = (value, at); }
        }
        // A rounded inspiratory shoulder may contain no locally straight
        // corner. Locate its strongest acceleration over 30 ms, then extend
        // that local quadratic back to the preceding acquired plateau slope.
        // A straight-corner estimate remains preferable when it precedes this
        // acceleration; later straight portions belong to the falling limb.
        int strongestAcceleration = 0;
        var corner = result;
        for (int index = start + 9; index <= high && index + 3 < s.HistoryCount; index++)
        {
            int leftDrop = HistoryAt(s, index - 3) - HistoryAt(s, index);
            int rightDrop = HistoryAt(s, index) - HistoryAt(s, index + 3);
            int acceleration = rightDrop - leftDrop;
            int plateauDrop = HistoryAt(s, index - 9) - HistoryAt(s, index - 6);
            int twiceSlope = leftDrop + rightDrop;
            if (acceleration <= Math.Max(5, strongestAcceleration) || plateauDrop < 3 ||
                leftDrop < 0 || twiceSlope <= plateauDrop * 3)
            { continue; }
            strongestAcceleration = acceleration;
            long accelerationTime = timeNs - (s.HistoryCount - 1L - index) * StepNs;
            if (corner is { } straight && straight.TimeNs <= accelerationTime &&
                accelerationTime - straight.TimeNs <= 90_000_000)
            { result = corner; continue; }
            long offsetNs = (long)FixedPointMath.RoundDivideTiesToEven(
                (Int128)(twiceSlope - 2 * plateauDrop) * 3 * StepNs, 2 * acceleration);
            if (offsetNs is < 0 or > 90_000_000) { continue; }
            int value = HistoryAt(s, index) + checked((int)FixedPointMath.RoundDivideTiesToEven(
                (Int128)twiceSlope * twiceSlope - (Int128)4 * plateauDrop * plateauDrop, 8 * acceleration));
            long at = accelerationTime - offsetNs;
            if (value >= half && value <= s.Peak && at > s.Start)
            { result = (value, at); }
        }
        return result;
    }

    private static bool NearlyLinearDescent(State s, int start, int totalDrop)
    {
        int tolerance = Math.Max(10, totalDrop / 5);
        for (int index = start; index < start + 5; index++)
        {
            int drop = HistoryAt(s, index) - HistoryAt(s, index + 1);
            if (drop < 0 || Math.Abs(drop * 5 - totalDrop) > tolerance) { return false; }
        }
        return true;
    }

    private static int HistoryAt(State s, int index) =>
        s.History[(s.HistoryCursor - s.HistoryCount + index + HistorySamples) % HistorySamples];

    private static State Copy(State state) => state with { History = (int[])state.History.Clone() };

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
        internal int? EndTidal, Baseline;
        // Replace rather than mutate arrays, preserving checkpoint ownership.
        internal Interval[] Intervals = [];
        internal bool Poor, Armed, Active;
        internal int LowCount, HighCount, Peak, Previous1, Previous2, FilterCount,
            BaselineCount, BaselineMinimum, BaselineMaximum, LowMinimum;
        internal int[] History = new int[HistorySamples];
        internal int HistoryCount, HistoryCursor;
    }
}
