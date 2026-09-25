// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

// Display-only, local monotone Hermite interpolation. Original samples remain
// exact knots; flat segments remain flat and interpolation cannot overshoot.
public static class PreviewContour
{
    public static IReadOnlyList<(long TimeNs, double Value)> Interpolate(
        IReadOnlyList<(long TimeNs, double Value)> samples, int subdivisions = 4)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count > 10_000 || subdivisions is < 1 or > 8)
        { throw new ArgumentException("PreviewContour.InvalidInput"); }
        for (int i = 0; i < samples.Count; i++)
            if (!double.IsFinite(samples[i].Value) || samples[i].TimeNs < 0 ||
                (i > 0 && samples[i].TimeNs <= samples[i - 1].TimeNs))
            { throw new ArgumentException("PreviewContour.InvalidSamples"); }
        if (samples.Count < 2) { return samples.ToArray(); }
        double[] slopes = new double[samples.Count - 1];
        double[] widths = new double[slopes.Length];
        for (int i = 0; i < slopes.Length; i++)
        {
            widths[i] = samples[i + 1].TimeNs - samples[i].TimeNs;
            slopes[i] = (samples[i + 1].Value - samples[i].Value) / widths[i];
        }
        double[] tangents = new double[samples.Count];
        tangents[0] = slopes[0]; tangents[^1] = slopes[^1];
        for (int i = 1; i < tangents.Length - 1; i++)
        {
            double a = slopes[i - 1], b = slopes[i];
            if (a == 0 || b == 0 || Math.Sign(a) != Math.Sign(b)) { continue; }
            double w1 = 2 * widths[i] + widths[i - 1], w2 = widths[i] + 2 * widths[i - 1];
            tangents[i] = (w1 + w2) / (w1 / a + w2 / b);
        }
        List<(long, double)> result = [];
        for (int i = 0; i < slopes.Length; i++)
        {
            var a = samples[i]; var b = samples[i + 1];
            result.Add(a);
            for (int part = 1; part < subdivisions; part++)
            {
                long time = a.TimeNs + (long)((Int128)(b.TimeNs - a.TimeNs) * part / subdivisions);
                if (time <= result[^1].Item1 || time >= b.TimeNs) { continue; }
                double t = (time - a.TimeNs) / widths[i], t2 = t * t, t3 = t2 * t;
                double value = (2 * t3 - 3 * t2 + 1) * a.Value + (t3 - 2 * t2 + t) * widths[i] * tangents[i]
                    + (-2 * t3 + 3 * t2) * b.Value + (t3 - t2) * widths[i] * tangents[i + 1];
                result.Add((time, Math.Clamp(value, Math.Min(a.Value, b.Value), Math.Max(a.Value, b.Value))));
            }
        }
        result.Add(samples[^1]); return result;
    }
}
