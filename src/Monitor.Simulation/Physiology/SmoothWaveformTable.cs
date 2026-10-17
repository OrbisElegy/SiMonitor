// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Project-authored landmarks become a half-open LUT with zero-slope support
// boundaries. Decimal is used only while constructing the immutable Q32 table;
// runtime sampling retains the existing fixed-point interpolation path.
internal static class SmoothWaveformTable
{
    internal static long[] Create(int sampleCount, IReadOnlyList<(int Index, long ValueQ32)> landmarks,
        IReadOnlyList<EventWaveformPhasePoint>? phases = null)
    {
        if (sampleCount is < 4 or > 65_536 || !BitOperations.IsPow2((uint)sampleCount))
        { throw new ArgumentOutOfRangeException(nameof(sampleCount)); }
        ArgumentNullException.ThrowIfNull(landmarks);
        if (landmarks.Count is < 2 || landmarks.Count > sampleCount + 1 ||
            landmarks[0] != (0, 0L) || landmarks[^1] != (sampleCount, 0L))
        { throw new ArgumentException("Landmarks must span the zero-ended support.", nameof(landmarks)); }
        for (int index = 0; index < landmarks.Count; index++)
        {
            var point = landmarks[index];
            if (point.ValueQ32 < short.MinValue * FixedPointMath.Q32One ||
                point.ValueQ32 > short.MaxValue * FixedPointMath.Q32One ||
                index > 0 && point.Index <= landmarks[index - 1].Index)
            { throw new ArgumentException("Landmarks must have increasing indices and bounded Q32 values.", nameof(landmarks)); }
        }

        decimal[] times = CreateSampleTimes(sampleCount, phases);
        decimal[] widths = new decimal[landmarks.Count - 1];
        decimal[] secantsQ32 = new decimal[widths.Length];
        decimal[] slopesQ32 = new decimal[landmarks.Count];
        for (int index = 0; index < widths.Length; index++)
        {
            widths[index] = times[landmarks[index + 1].Index] - times[landmarks[index].Index];
            secantsQ32[index] = ((decimal)landmarks[index + 1].ValueQ32 - landmarks[index].ValueQ32) / widths[index];
        }
        for (int index = 1; index < slopesQ32.Length - 1; index++)
        {
            decimal left = secantsQ32[index - 1], right = secantsQ32[index];
            if (left == 0 || right == 0 || Math.Sign(left) != Math.Sign(right)) { continue; }
            // Normalize the harmonic weights before division. Multiplying a
            // nanosecond duration by an inverse tiny slope can otherwise exceed
            // decimal range even when all final amplitudes are bounded.
            decimal leftWeight = (2 * widths[index] + widths[index - 1]) /
                (3 * (widths[index] + widths[index - 1]));
            slopesQ32[index] = 1 / (leftWeight / left + (1 - leftWeight) / right);
        }

        long[] values = new long[sampleCount];
        for (int segment = 0; segment < widths.Length; segment++)
        {
            var left = landmarks[segment];
            var right = landmarks[segment + 1];
            decimal deltaQ32 = (decimal)right.ValueQ32 - left.ValueQ32;
            decimal leftTangentQ32 = widths[segment] * slopesQ32[segment];
            decimal rightTangentQ32 = widths[segment] * slopesQ32[segment + 1];
            for (int index = left.Index; index < right.Index; index++)
            {
                decimal position = (times[index] - times[left.Index]) / widths[segment];
                decimal square = position * position, cube = square * position;
                decimal valueQ32 = left.ValueQ32 + (3 * square - 2 * cube) * deltaQ32 +
                    (cube - 2 * square + position) * leftTangentQ32 +
                    (cube - square) * rightTangentQ32;
                // The monotone cubic is bounded analytically. Keep this final
                // clamp so decimal rounding cannot create a one-bit overshoot.
                valueQ32 = Math.Clamp(valueQ32, Math.Min(left.ValueQ32, right.ValueQ32),
                    Math.Max(left.ValueQ32, right.ValueQ32));
                values[index] = decimal.ToInt64(decimal.Round(valueQ32, 0, MidpointRounding.ToEven));
            }
        }
        return values;
    }

    private static decimal[] CreateSampleTimes(int sampleCount, IReadOnlyList<EventWaveformPhasePoint>? phases)
    {
        decimal[] times = new decimal[sampleCount + 1];
        if (phases is null)
        {
            for (int index = 0; index <= sampleCount; index++) { times[index] = index; }
            return times;
        }
        if (phases.Count is < 2 or > 32 || phases[0] != new EventWaveformPhasePoint(0, 0) ||
            phases[^1].TableIndex != sampleCount)
        { throw new ArgumentException("Phases must span the complete table.", nameof(phases)); }
        for (int phase = 1; phase < phases.Count; phase++)
        {
            var left = phases[phase - 1];
            var right = phases[phase];
            if (right.OffsetNs <= left.OffsetNs || right.TableIndex <= left.TableIndex || right.TableIndex > sampleCount)
            { throw new ArgumentException("Phase times and indices must be strictly increasing.", nameof(phases)); }
            for (int index = left.TableIndex; index < right.TableIndex; index++)
            {
                times[index] = left.OffsetNs + (decimal)(right.OffsetNs - left.OffsetNs) *
                    (index - left.TableIndex) / (right.TableIndex - left.TableIndex);
            }
        }
        times[sampleCount] = phases[^1].OffsetNs;
        return times;
    }
}
