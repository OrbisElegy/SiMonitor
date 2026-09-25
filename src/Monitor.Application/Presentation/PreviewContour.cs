// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

// Bounded display simplification for quantized illustration contours only.
// Every omitted sample stays within tolerance of the displayed segment;
// local extrema, endpoints and the raw acquisition remain unchanged.
public static class PreviewContour
{
    // Causal display-only regression: every point depends solely on a bounded
    // preceding 250ms window. Appending data can never refit a displayed point.
    // Keep steep edges and reversals raw; cap error at 0.75 physical units and
    // the observed local range. No source samples or event times are modified.
    public static IReadOnlyList<(long TimeNs, double Value)> Stable(
        IReadOnlyList<(long TimeNs, double Value)> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count > 10_000) { throw new ArgumentException("PreviewContour.InvalidInput"); }
        List<(long TimeNs, double Value)> result = [];
        int start = 0, direction = 0, preserveThrough = -1;
        for (int i = 0; i < samples.Count; i++)
        {
            var current = samples[i];
            if (current.TimeNs < 0 || !double.IsFinite(current.Value) || (i > 0 && current.TimeNs <= samples[i - 1].TimeNs))
            { throw new ArgumentException("PreviewContour.InvalidSamples"); }
            while (samples[start].TimeNs < current.TimeNs - 250_000_000) { start++; }
            if (i > 0)
            {
                double change = current.Value - samples[i - 1].Value;
                int next = Math.Sign(change);
                if (Math.Abs(change) > 1.5 || (next != 0 && direction != 0 && next != direction)) { preserveThrough = i + 3; }
                if (next != 0) { direction = next; }
            }
            double value = current.Value;
            if (i - start >= 3 && i > preserveThrough)
            {
                double sx = 0, sy = 0, sxx = 0, sxy = 0;
                double minimum = value, maximum = value;
                int count = i - start + 1;
                for (int j = start; j <= i; j++)
                {
                    double x = (samples[j].TimeNs - current.TimeNs) / 1e9, y = samples[j].Value;
                    sx += x; sy += y; sxx += x * x; sxy += x * y;
                    minimum = Math.Min(minimum, y); maximum = Math.Max(maximum, y);
                }
                double denominator = count * sxx - sx * sx;
                if (denominator > 0)
                {
                    double slope = (count * sxy - sx * sy) / denominator;
                    double fitted = (sy - slope * sx) / count;
                    if (double.IsFinite(fitted))
                    { value = Math.Clamp(fitted, Math.Max(minimum, value - .75), Math.Min(maximum, value + .75)); }
                }
            }
            result.Add((current.TimeNs, value));
        }
        return result;
    }

    public static IReadOnlyList<(long TimeNs, double Value)> Simplify(
        IReadOnlyList<(long TimeNs, double Value)> samples, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (!double.IsFinite(tolerance) || tolerance < 0 || samples.Count > 10_000)
        { throw new ArgumentException("PreviewContour.InvalidInput"); }
        var keep = new bool[samples.Count];
        if (samples.Count > 0) { keep[0] = keep[^1] = true; }
        int direction = 0, lastChange = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            if (!double.IsFinite(samples[i].Value) || (i > 0 && samples[i].TimeNs <= samples[i - 1].TimeNs))
            { throw new ArgumentException("PreviewContour.InvalidSamples"); }
            if (i > 0)
            {
                int next = Math.Sign(samples[i].Value - samples[i - 1].Value);
                if (next != 0)
                {
                    if (direction != 0 && next != direction) { keep[lastChange] = true; keep[i - 1] = true; }
                    direction = next; lastChange = i;
                }
            }
        }
        if (samples.Count < 3 || tolerance == 0) { return samples.ToArray(); }
        // Keep the sampled shape around extrema and fast edges. Simplification
        // is intended for small stairs, not for straightening pulse upstrokes.
        var extrema = keep.ToArray();
        for (int i = 1; i < samples.Count - 1; i++)
        {
            if (!extrema[i] && Math.Abs(samples[i].Value - samples[i - 1].Value) <= 2 * tolerance) { continue; }
            for (int neighbor = Math.Max(0, i - 3); neighbor <= Math.Min(samples.Count - 1, i + 3); neighbor++)
            { keep[neighbor] = true; }
        }

        var pending = new Stack<(int Start, int End)>();
        int start = 0;
        for (int i = 1; i < keep.Length; i++)
        { if (keep[i]) { pending.Push((start, i)); start = i; } }
        while (pending.TryPop(out var span))
        {
            var a = samples[span.Start]; var b = samples[span.End];
            double error = tolerance; int selected = -1;
            for (int i = span.Start + 1; i < span.End; i++)
            {
                double expected = a.Value + (b.Value - a.Value) * ((samples[i].TimeNs - a.TimeNs) / (double)(b.TimeNs - a.TimeNs));
                double difference = Math.Abs(samples[i].Value - expected);
                if (difference > error) { error = difference; selected = i; }
            }
            if (selected < 0) { continue; }
            keep[selected] = true; pending.Push((span.Start, selected)); pending.Push((selected, span.End));
        }
        return samples.Where((_, i) => keep[i]).ToArray();
    }
}
