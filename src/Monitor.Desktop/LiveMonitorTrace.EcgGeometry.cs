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
        var points = new Point[samples.Length];
        var path = new StreamGeometry();
        using (var geometry = path.Open())
        {
            for (int index = 0; index < samples.Length; index++)
            {
                var sample = samples[index];
                Point point = new((sample.TimeNs - fromNs) / (double)durationNs, 1 - range.Normalize(sample.Value));
                points[index] = point;
                if (index == 0) { geometry.BeginFigure(point, false); }
                else { geometry.LineTo(point); }
            }
            if (samples.Length > 0) { geometry.EndFigure(false); }
        }
        _ecgGeometrySnapshots[row, age] = new(fromNs, durationNs, range, samples, points, path);
        _paths[row, age] = path;
        _ecgPoints[row, age] = points;
    }
}
