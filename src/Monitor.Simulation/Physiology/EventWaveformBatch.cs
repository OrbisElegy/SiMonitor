// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public sealed partial class EventWaveformComposition
{
    // A batch owns no integration state: quotient/remainder phase stepping is
    // initialized from absolute time for each contribution and each phase span.
    // Changing batch boundaries therefore cannot accumulate rounding drift.
    internal void EvaluateUniform(long firstSimTimeNs, long samplePeriodNs, Span<long> output,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(firstSimTimeNs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samplePeriodNs);
        if (output.Length > PeriodicSignalGenerator.MaximumBatchSampleCount)
        { throw new ArgumentOutOfRangeException(nameof(output)); }
        if (output.IsEmpty) { return; }
        long lastSimTimeNs = checked(firstSimTimeNs + (output.Length - 1L) * samplePeriodNs);
        Int128[] rented = ArrayPool<Int128>.Shared.Rent(output.Length);
        Span<Int128> sums = rented.AsSpan(0, output.Length);
        sums.Clear();
        try
        {
            foreach (var item in _events)
            {
                if (item.SimTimeNs > lastSimTimeNs) { break; }
                for (int bandIndex = 0; bandIndex < _bands.Length; bandIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var band = _bands[bandIndex];
                    if (!Accepts(band, item)) { continue; }
                    int slot = (int)(item.CycleIndex % SpontaneousRespirationSchedule.Length);
                    var shape = band.RespiratoryCycleShapes?[slot];
                    long delay = shape?.DelayNs ?? band.DelayNs;
                    long duration = shape?.DurationNs ?? (band.CycleDurationRate is { } rate
                        ? rate.FollowingIntervalNs(item.CycleIndex)
                        : band.EjectionIllustration is { } mode
                            ? PrematureBeatPerfusion.DurationNs(mode, item.CycleIndex, band.DurationNs) : band.DurationNs);
                    var points = shape is null ? band.PhasePoints : shape.PhasePoints;
                    long[] table = _cycleTables[bandIndex]?[slot] ?? _tables[bandIndex];
                    int gain = band.EjectionIllustration is { } perfusion
                        ? PrematureBeatPerfusion.GainPermille(perfusion, item.CycleIndex)
                        : band.ExpirationCycleGainsPermille is { } cycleGains
                            ? cycleGains[(int)(item.CycleIndex % (ulong)cycleGains.Count)]
                            : RespiratoryPatternDepth.At(band.DepthPattern, item.CycleIndex);
                    int inspirationGain = band.InspirationCycleGainsPermille is { } inspirationGains
                        ? inspirationGains[(int)(item.CycleIndex % (ulong)inspirationGains.Count)] : 1000;
                    int activationGain = band.ExpirationActivationGainsPermille is { } activationGains
                        ? activationGains[(int)(item.CycleIndex - band.ExpirationCycleOrigin)] : 1000;
                    long referenceDuration = shape?.DurationNs ?? band.DurationNs;
                    Int128 begin = (Int128)item.SimTimeNs + delay;
                    if (band.PhaseClock is null && band.CardiogenicRippleDurationNs == 0 &&
                        (points is null || referenceDuration == duration))
                    {
                        if (points is null)
                        {
                            AccumulateLinearPhase(table, begin, duration, 0, (UInt128)1 << 64,
                                firstSimTimeNs, samplePeriodNs, sums, gain, inspirationGain, activationGain, cancellationToken);
                        }
                        else
                        {
                            for (int point = 1; point < points.Count; point++)
                            {
                                var left = points[point - 1];
                                var right = points[point];
                                UInt128 phaseStart = ((UInt128)(uint)left.TableIndex << 64) / (uint)table.Length;
                                UInt128 phaseEnd = ((UInt128)(uint)right.TableIndex << 64) / (uint)table.Length;
                                AccumulateLinearPhase(table, begin + left.OffsetNs, right.OffsetNs - left.OffsetNs,
                                    phaseStart, phaseEnd - phaseStart, firstSimTimeNs, samplePeriodNs, sums,
                                    gain, inspirationGain, activationGain, cancellationToken);
                            }
                        }
                        continue;
                    }

                    // A changing respiratory clock and cardiogenic attenuation
                    // retain their exact time-dependent definition. Shape,
                    // selection, duration and gains are still prepared once.
                    var clock = band.PhaseClock;
                    int first = FirstSampleAtOrAfter(clock is null ? begin : (Int128)item.SimTimeNs + band.PhaseClockFixedDelayNs,
                        firstSimTimeNs, samplePeriodNs, sums.Length);
                    int last = FirstSampleAtOrAfter(clock is null ? begin + duration : (Int128)item.SimTimeNs + band.MaximumSupportNs,
                        firstSimTimeNs, samplePeriodNs, sums.Length);
                    if (first >= last) { continue; }
                    long eventReference = clock?.ReferenceAt(item.SimTimeNs) ?? 0;
                    for (int sample = first; sample < last; sample++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        long time = firstSimTimeNs + sample * samplePeriodNs;
                        long elapsed = clock is null ? (long)(time - begin) : checked(
                            clock.ReferenceAt(time - band.PhaseClockFixedDelayNs) - eventReference -
                            (delay - band.PhaseClockFixedDelayNs));
                        if (elapsed < 0 || elapsed >= duration) { continue; }
                        ulong phase = PhaseAt(points, table.Length, referenceDuration, elapsed, duration);
                        long value = PeriodicLutLinear.Interpolate(table, phase).Value;
                        value = ApplyGain(value, inspirationGain);
                        value = ApplyGain(value, activationGain);
                        if (band.CardiogenicRippleDurationNs > 0 && value > 0 &&
                            (points is not { Count: > 2 } || elapsed >= points[2].OffsetNs))
                        { value = ApplyGain(value, 1000 - CardiogenicAttenuation(band, time - delay)); }
                        sums[sample] += ApplyGain(value, gain);
                    }
                }
            }
            for (int sample = 0; sample < sums.Length; sample++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (sums[sample] < short.MinValue * FixedPointMath.Q32One ||
                    sums[sample] > short.MaxValue * FixedPointMath.Q32One)
                { throw new EventWaveformException("EventWaveform.AmplitudeOverflow", nameof(firstSimTimeNs)); }
                output[sample] = (long)sums[sample];
            }
        }
        finally { ArrayPool<Int128>.Shared.Return(rented); }
    }

    private static void AccumulateLinearPhase(long[] table, Int128 beginSimTimeNs, long durationNs,
        UInt128 phaseStart, UInt128 phaseSpan, long firstSimTimeNs, long samplePeriodNs, Span<Int128> sums,
        int gain, int inspirationGain, int activationGain, CancellationToken cancellationToken)
    {
        int first = FirstSampleAtOrAfter(beginSimTimeNs, firstSimTimeNs, samplePeriodNs, sums.Length);
        int last = FirstSampleAtOrAfter(beginSimTimeNs + durationNs, firstSimTimeNs, samplePeriodNs, sums.Length);
        if (first >= last) { return; }
        ulong elapsed = (ulong)((Int128)firstSimTimeNs + first * (Int128)samplePeriodNs - beginSimTimeNs);
        UInt128 initial = phaseSpan * elapsed;
        ulong phase = (ulong)(phaseStart + initial / (ulong)durationNs);
        ulong remainder = (ulong)(initial % (ulong)durationNs);
        UInt128 step = phaseSpan * (ulong)samplePeriodNs;
        ulong phaseStep = (ulong)(step / (ulong)durationNs);
        ulong remainderStep = (ulong)(step % (ulong)durationNs);
        for (int sample = first; sample < last; sample++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long value = PeriodicLutLinear.Interpolate(table, phase).Value;
            value = ApplyGain(value, inspirationGain);
            value = ApplyGain(value, activationGain);
            sums[sample] += ApplyGain(value, gain);
            remainder += remainderStep;
            ulong carry = remainder >= (ulong)durationNs ? 1UL : 0;
            if (carry != 0) { remainder -= (ulong)durationNs; }
            phase = unchecked(phase + phaseStep + carry);
        }
    }

    private static long ApplyGain(long value, int gain) => gain == 1000 ? value :
        checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)value * gain, 1000));

    internal static int FirstSampleAtOrAfter(Int128 timeNs, long firstSimTimeNs, long samplePeriodNs, int count)
    {
        Int128 difference = timeNs - firstSimTimeNs;
        return difference <= 0 ? 0 : (int)Int128.Min(count, (difference + samplePeriodNs - 1) / samplePeriodNs);
    }
}
