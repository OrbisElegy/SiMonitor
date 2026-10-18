// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed partial class LiveMonitorTrace
{
    private const int ContourSubdivisions = 4;
    private sealed record CurrentContourSnapshot(int Channel, long FromNs, long DurationNs,
        MonitorAmplitudeRange Range, (long TimeNs, double Value)[] Samples, Point[] Points);
    private CurrentContourSnapshot?[] _currentContourSnapshots = [];

    private Point[] BuildCurrentContour(int row, int channel, long fromNs, long durationNs,
        MonitorAmplitudeRange range, (long TimeNs, double Value)[] samples)
    {
        if (_currentContourSnapshots.Length != _session.Display.Slots.Count)
        { _currentContourSnapshots = new CurrentContourSnapshot?[_session.Display.Slots.Count]; }
        CurrentContourSnapshot? previous = _currentContourSnapshots[row];
        Point[]? points = null;
        if (previous is not null && previous.Channel == channel && previous.FromNs == fromNs &&
            previous.DurationNs == durationNs && previous.Range == range && samples.Length <= 10_000 &&
            previous.Points.Length == ContourPointCount(previous.Samples.Length) &&
            HasUnchangedSamplePrefix(previous.Samples, samples))
        {
            if (previous.Samples.Length == samples.Length) { return previous.Points; }
            if (previous.Samples.Length >= 3)
            {
                // An appended sample changes the old endpoint tangent, hence only
                // the old final interval. One extra interval provides its left
                // tangent; discard that context interval after interpolation.
                var tailSamples = samples.AsSpan(previous.Samples.Length - 3).ToArray();
                var tail = PreviewContour.Interpolate(tailSamples, ContourSubdivisions);
                if (tail.Count == ContourPointCount(tailSamples.Length))
                {
                    int prefixCount = (previous.Samples.Length - 2) * ContourSubdivisions;
                    int pointCount = prefixCount + tail.Count - ContourSubdivisions;
                    if (pointCount == ContourPointCount(samples.Length))
                    {
                        points = new Point[pointCount];
                        previous.Points.AsSpan(0, prefixCount).CopyTo(points);
                        for (int index = ContourSubdivisions; index < tail.Count; index++)
                        { points[prefixCount + index - ContourSubdivisions] = Normalize(tail[index]); }
                    }
                }
            }
        }
        if (points is null)
        {
            var contour = PreviewContour.Interpolate(samples, ContourSubdivisions);
            points = new Point[contour.Count];
            for (int index = 0; index < contour.Count; index++) { points[index] = Normalize(contour[index]); }
        }
        // Render operations can still hold the previous frame on another thread.
        // Publish a new array; never modify its shared normalized prefix in place.
        _currentContourSnapshots[row] = new(channel, fromNs, durationNs, range, samples, points);
        return points;

        Point Normalize((long TimeNs, double Value) sample) => new((sample.TimeNs - fromNs) / (double)durationNs,
            1 - range.Normalize(sample.Value));
    }

    private static int ContourPointCount(int sampleCount) => sampleCount == 0 ? 0 :
        (sampleCount - 1) * ContourSubdivisions + 1;

    private static bool HasUnchangedSamplePrefix((long TimeNs, double Value)[] previous,
        (long TimeNs, double Value)[] samples)
    {
        if (samples.Length < previous.Length) { return false; }
        for (int index = 0; index < previous.Length; index++)
        {
            if (previous[index].TimeNs != samples[index].TimeNs ||
                BitConverter.DoubleToInt64Bits(previous[index].Value) != BitConverter.DoubleToInt64Bits(samples[index].Value))
            { return false; }
        }
        return true;
    }
}
