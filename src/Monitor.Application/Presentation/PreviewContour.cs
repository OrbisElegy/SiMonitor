// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

// Bounded display simplification for quantized illustration contours only.
// Every omitted sample stays within tolerance of the displayed segment;
// local extrema, endpoints and the raw acquisition remain unchanged.
public static class PreviewContour
{
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
