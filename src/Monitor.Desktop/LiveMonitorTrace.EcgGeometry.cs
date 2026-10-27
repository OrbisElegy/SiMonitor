// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed partial class LiveMonitorTrace
{
    private sealed record EcgGeometrySnapshot(long FromNs, long DurationNs, MonitorAmplitudeRange Range,
        (long TimeNs, double Value)[] Samples, Point[] Points, StreamGeometry Path);
    private EcgGeometrySnapshot?[,] _ecgGeometrySnapshots = new EcgGeometrySnapshot?[0, 0];
    private Point[]?[,] _ecgPoints = new Point[]?[0, 0];

    private void BuildEcgGeometry(int row, int age, long fromNs, long durationNs,
        MonitorAmplitudeRange range, (long TimeNs, double Value)[] samples)
    {
        if (_ecgGeometrySnapshots.GetLength(0) != _session.Display.Slots.Count)
        { _ecgGeometrySnapshots = new EcgGeometrySnapshot?[_session.Display.Slots.Count, 2]; }
        EcgGeometrySnapshot? previous = _ecgGeometrySnapshots[row, age];
        if (previous is not null && previous.FromNs == fromNs && previous.DurationNs == durationNs &&
            previous.Range == range && previous.Samples.Length == samples.Length &&
            HasUnchangedSamplePrefix(previous.Samples, samples))
        {
            _paths[row, age] = previous.Path;
            _ecgPoints[row, age] = previous.Points;
            return;
        }
        Point[] points = BuildEcgPoints(fromNs, durationNs, range, samples);
        var path = new StreamGeometry();
        using (var geometry = path.Open())
        {
            for (int index = 0; index < points.Length; index++)
            {
                Point point = points[index];
                if (index == 0) { geometry.BeginFigure(point, false); }
                else { geometry.LineTo(point); }
            }
            if (points.Length > 0) { geometry.EndFigure(false); }
        }
        _ecgGeometrySnapshots[row, age] = new(fromNs, durationNs, range, samples, points, path);
        _paths[row, age] = path;
        _ecgPoints[row, age] = points;
    }

    internal static Point[] BuildEcgPoints(long fromNs, long durationNs,
        MonitorAmplitudeRange range, (long TimeNs, double Value)[] samples)
    {
        var points = new Point[samples.Length];
        int count = 0;
        foreach (var sample in samples)
        {
            Point point = new((sample.TimeNs - fromNs) / (double)durationNs, 1 - range.Normalize(sample.Value));
            // Keep only the endpoints of an exactly horizontal run. Dense collinear
            // vertices can produce uneven GPU stroke coverage as a flat ECG grows.
            // No tolerance or resampling: all non-flat details and raw samples remain intact.
            if (count >= 2 && points[count - 2].Y == point.Y && points[count - 1].Y == point.Y &&
                points[count - 2].X < points[count - 1].X && points[count - 1].X < point.X)
            { points[count - 1] = point; }
            else { points[count++] = point; }
        }
        if (count != points.Length) { Array.Resize(ref points, count); }
        return points;
    }
}
