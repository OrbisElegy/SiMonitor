// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed partial class LiveMonitorTrace : Control
{
    internal static readonly string[] Names = ["ECG", "RESP", "PLETH", "ABP", "CO₂", "PA", "CVP"];
    private static readonly string[] UnitSymbols = ["μV", "", "", "mmHg", "mmHg", "mmHg", "mmHg"];
    // RESP and PLETH carry relative units; the others are symbols that need no translation.
    internal static string Unit(Monitor.Application.Localization.ITextLocalizer text, int channel) =>
        channel is 1 or 2 ? text.GetString("display.relative") : UnitSymbols[channel];
    private readonly LocalMonitorPreviewSession _session;
    private readonly DesktopLocalization _localization;

    internal LiveMonitorTrace(LocalMonitorPreviewSession session, DesktopLocalization? localization = null)
    {
        _session = session;
        _localization = localization ?? new DesktopLocalization();
        AttachedToVisualTree += (_, _) => _localization.LocaleChanged += InvalidateVisual;
        DetachedFromVisualTree += (_, _) =>
        {
            _localization.LocaleChanged -= InvalidateVisual;
            ClearWaveformCache();
            ClearPreparedWaveforms();
            _labels.Clear();
        };
    }
    internal static readonly string[] Colors = ["#71E9AF", "#F0D68A", "#8ADAE5", "#F39199", "#E7ECF2", "#CAA7EA", "#F2B67D"];
    private ulong _revision = ulong.MaxValue;
    private long _cycle = -1;
    private StreamGeometry?[,] _paths = new StreamGeometry?[0, 0];
    private Point[]?[,] _contours = new Point[]?[0, 0];
    private sealed record ContourBatch(int StartIndex, int Count, double Begin, double End, StreamGeometry Path);
    private sealed record ContourCache(Rect Plot, Point[] Points, ContourBatch[] Batches);
    private ContourCache?[,] _contourPaths = new ContourCache?[0, 0];
    private readonly record struct RowSweep(int Channel, long DurationNs, long Cycle,
        MonitorAmplitudeRange Range, MonitorAmplitudeRange PreviousRange);
    private RowSweep[] _rowSweeps = [];
    internal LocalMonitorPreviewSession Session => _session;
    internal IMonitorSkin? Skin { get; set; }
    private bool _synchronized;
    private long _synchronizedSinceNs;
    private readonly List<long> _synchronizationPeaks = [];
    internal IReadOnlyList<long> SynchronizationPeaks => _synchronizationPeaks.AsReadOnly();
    internal void RefreshSynchronization(bool enabled)
    {
        enabled &= (MonitoredChannels & MonitorChannels.Ecg) != 0;
        if (_synchronized != enabled)
        {
            _synchronized = enabled;
            _synchronizedSinceNs = _session.SimulationTimeNs;
            _synchronizationPeaks.Clear();
        }
        if (enabled)
        {
            foreach (var beat in _session.DetectedBeats)
            {
                if (beat.PeakTimeNs >= _synchronizedSinceNs && (_synchronizationPeaks.Count == 0 || beat.PeakTimeNs > _synchronizationPeaks[^1]))
                { _synchronizationPeaks.Add(beat.PeakTimeNs); }
            }
            _synchronizationPeaks.RemoveAll(peak => peak < _session.SimulationTimeNs - LocalMonitorPreviewSession.RetainedBlockCount * 200_000_000L);
        }
        InvalidateVisual();
    }
    internal MonitorChannels MonitoredChannels => _session.Display.Slots.Aggregate(Skin?.AdditionalMonitoredChannels ?? MonitorChannels.None,
        (channels, slot) => channels | MonitorChannelMapping.ForChannel(slot.Channel)) & (Skin?.Channels ?? MonitorChannels.All);
    internal IBrush ChannelBrush(int channel) => Skin?.ChannelBrush(channel) ?? Brush.Parse(Colors[channel]);
    internal string ChannelName(int channel) => channel == 0
        ? "ECG · " + ProjectedEcgDemoSource.LeadNames[(int)_session.DisplayEcgLead] : Names[channel];
    internal void RefreshDisplay()
    {
        _revision = ulong.MaxValue;
        InvalidateVisual();
    }

    private void Build()
    {
        if (_revision == _session.DataRevision && _cycle == _session.Ranges.Cycle) { return; }
        _revision = _session.DataRevision; _cycle = _session.Ranges.Cycle;
        Point[]?[,] previousContours = _contours;
        RowSweep[] previousSweeps = _rowSweeps;
        _paths = new StreamGeometry?[_session.Display.Slots.Count, 2];
        _ecgPoints = new Point[]?[_session.Display.Slots.Count, 2];
        _contours = new Point[]?[_session.Display.Slots.Count, 2];
        _rowSweeps = new RowSweep[_session.Display.Slots.Count];
        PrepareWaveformSnapshotRows(_session.Display.Slots.Count);
        for (int row = 0; row < _session.Display.Slots.Count; row++)
        {
            MonitorDisplaySlot slot = _session.Display.Slots[row];
            ReleaseUnusedWaveformSnapshots(row, slot.Channel);
            var sweep = new RowSweep(slot.Channel, slot.DurationNs, _session.Ranges.RowCycle(row),
                _session.Ranges.Range(row), _session.Ranges.PreviousRange(row));
            _rowSweeps[row] = sweep;
            bool sameRow = previousSweeps.Length == _rowSweeps.Length &&
                previousSweeps[row].Channel == sweep.Channel && previousSweeps[row].DurationNs == sweep.DurationNs;
            if (sameRow && sweep.Channel != 0 && sweep.Cycle == previousSweeps[row].Cycle + 1 &&
                sweep.PreviousRange == previousSweeps[row].Range)
            { PromoteSweepCache(row); }
            for (int age = 0; age < 2; age++)
            {
                long cycle = sweep.Cycle - age;
                long duration = sweep.DurationNs;
                if (cycle < 0 || (age == 1 && !_session.Ranges.ShowPrevious(row))) { continue; }
                long from = cycle * duration;
                int channel = sweep.Channel;
                MonitorAmplitudeRange range = age == 0 ? sweep.Range : sweep.PreviousRange;
                // Completed non-ECG samples are immutable and retention covers
                // both sweeps. At wrap the last interval is rebuilt before reuse,
                // including samples acquired since the last rendered frame.
                if (channel != 0 && age == 1 && sameRow && previousSweeps[row].Cycle == sweep.Cycle &&
                    previousSweeps[row].PreviousRange == range && previousContours[row, age] is { } completed)
                {
                    _contours[row, age] = completed;
                    continue;
                }
                var samples = _session.DisplaySamples(channel, from, from + duration).ToArray();
                Point Normalize((long TimeNs, double Value) sample) => new((sample.TimeNs - from) / (double)duration,
                    1 - range.Normalize(sample.Value));
                if (channel != 0)
                {
                    if (age == 0)
                    {
                        _contours[row, age] = BuildCurrentContour(row, channel, from, duration, range, samples);
                        continue;
                    }
                    var contour = PreviewContour.Interpolate(samples);
                    var points = new Point[contour.Count];
                    for (int index = 0; index < points.Length; index++) { points[index] = Normalize(contour[index]); }
                    _contours[row, age] = points;
                    continue;
                }
                BuildEcgGeometry(row, age, from, duration, range, samples);
            }
        }
    }

    private void PromoteSweepCache(int row)
    {
        _contourPaths[row, 1] = _contourPaths[row, 0];
        _contourPaths[row, 0] = null;
        if (_tileCaches.GetLength(0) != _session.Display.Slots.Count) { return; }
        _tileCaches[row, 1]?.Dispose();
        _tileCaches[row, 1] = _tileCaches[row, 0];
        _tileCaches[row, 0] = null;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Skin?.Background ?? Brush.Parse("#101B25"), new Rect(Bounds.Size));
        if (Bounds.Width < 200 || Bounds.Height < 60)
        {
            ClearWaveformCache();
            return;
        }
        Build();
        int rows = _session.Display.Slots.Count;
        if ((_tileCaches.Length > 0 && _tileCaches.GetLength(0) != rows) ||
            (_ecgRasterCaches.Length > 0 && _ecgRasterCaches.Length != rows))
        { ClearWaveformCache(); }
        double rowHeight = Bounds.Height / rows;
        bool compact = Skin?.CompactWaveformLabels == true;
        double left = compact ? 12 : 132, width = Math.Max(1, Bounds.Width - left - 18);
        for (int row = 0; row < rows; row++)
        {
            long duration = _session.Display.Slots[row].DurationNs;
            int channel = _session.Display.Slots[row].Channel;
            long frontierNs = _session.SimulationTimeNs;
            double phase = (frontierNs % duration) / (double)duration;
            if ((MonitoredChannels & MonitorChannelMapping.ForChannel(channel)) == 0)
            {
                ReleaseTileCache(row, 0);
                ReleaseTileCache(row, 1);
                ReleaseEcgCache(row);
                continue;
            }
            var color = ChannelBrush(channel);
            if (channel == 0 || color is not ISolidColorBrush)
            {
                ReleaseTileCache(row, 0);
                ReleaseTileCache(row, 1);
            }
            if (channel != 0 || color is not ISolidColorBrush) { ReleaseEcgCache(row); }
            var range = _session.Ranges.Range(row);
            double top = row * rowHeight;
            if (Skin is null)
            { context.DrawLine(new Pen(Brush.Parse("#607080"), 1), new(0, top + rowHeight - 1), new(Bounds.Width, top + rowHeight - 1)); }
            string label = compact ? channel switch
            {
                0 => ProjectedEcgDemoSource.LeadNames[(int)_session.DisplayEcgLead],
                1 => "Resp",
                2 => "Pleth",
                _ => ChannelName(channel)
            } : ChannelName(channel);
            Label(context, label, 12, top + 8, color, compact ? 13 : 14);
            if (compact && channel == 0) { Label(context, _localization.Get("bedside.monitoring"), 160, top + 8, color, 12); }
            if (!compact)
            { Label(context, string.Create(CultureInfo.InvariantCulture, $"{range.Minimum:0.##} – {range.Maximum:0.##}"), 12, top + 32, color, 11); }
            string unit = Unit(_localization.Current, channel);
            if (!compact && (Skin is null || channel != 0))
            { Label(context, _localization.Format(_session.Display.Slots[row].Automatic ? "monitor.rangeAutomatic" : "monitor.rangeFixed", unit), 12, top + 49, color, 11); }
            Rect plot = new(left, top + (compact ? 34 : 9), width, Math.Max(1, rowHeight - (compact ? 46 : 20)));
            if (channel == 0)
            {
                if (rowHeight >= (Skin is null ? 100 : 80) && _session.ShockArtifacts.Any(artifact => artifact.Contains(frontierNs)))
                { Label(context, _localization.Get("defib.ecgRecovering"), 12, top + (Skin is null ? 83 : 66), color, 11); }
                if (range.Minimum <= -500 && range.Maximum >= 500)
                {
                    double x = left + width * 200_000_000 / duration;
                    context.DrawLine(new Pen(color, 1.2), new(x, plot.Bottom - range.Normalize(-500) * plot.Height),
                        new(x, plot.Bottom - range.Normalize(500) * plot.Height));
                    Label(context, "1 mV", compact ? x + 4 : 12, compact ? plot.Bottom - 14 : top + (Skin is null ? 66 : 49), color, 11);
                }
                else { Label(context, _localization.Get("monitor.calibrationOffScale"), 12, top + (Skin is null ? 66 : 49), color, 11); }
            }
            for (int age = 1; age >= 0; age--)
            {
                var path = _paths[row, age];
                var contour = _contours[row, age];
                if (path is null && contour is null)
                {
                    ReleaseTileCache(row, age);
                    if (age == 1) { ReleaseEcgCache(row); }
                    continue;
                }
                double begin = age == 0 ? 0 : Math.Min(1, phase + .012);
                double end = age == 0 ? phase : 1;
                if (end <= begin)
                {
                    ReleaseTileCache(row, age);
                    if (age == 1) { ReleaseEcgCache(row); }
                    continue;
                }
                using var clip = context.PushClip(new Rect(plot.X + begin * plot.Width, plot.Y, (end - begin) * plot.Width, plot.Height));
                if (contour is not null)
                {
                    ContourBatch[] batches = ContourPaths(row, age, contour, plot);
                    if (CacheWaveformTiles && color is ISolidColorBrush solid)
                    {
                        var operation = new ContourTileOperation(GetTileCache(row, age), plot, contour, batches,
                            solid.Color, solid.Opacity, begin, end);
                        try
                        {
                            context.Custom(operation);
                            continue;
                        }
                        catch (NotSupportedException) { operation.Dispose(); }
                    }
                    var pen = new Pen(color, 1.2);
                    foreach (ContourBatch batch in batches)
                    {
                        if (batch.End < begin || batch.Begin > end) { continue; }
                        context.DrawGeometry(null, pen, batch.Path);
                    }
                    continue;
                }
                // Geometry is cached in normalized units. Scale with the window;
                // draw a device-space stroke so resize does not thicken the trace.
                var matrix = new Matrix(plot.Width, 0, 0, plot.Height, plot.X, plot.Y);
                if (path!.Transform is null || !path.Transform.Value.Equals(matrix)) { path.Transform = new MatrixTransform(matrix); }
                if (age != 1 || _ecgPoints[row, age] is not { } ecgPoints ||
                    !DrawCachedPreviousEcg(context, row, ecgPoints, plot, color))
                { context.DrawGeometry(null, new Pen(color, 1.2), path); }
                if (channel == 0)
                {
                    long cycleStart = (_session.Ranges.RowCycle(row) - age) * duration;
                    foreach (long peak in _synchronizationPeaks)
                    {
                        double position = (peak - cycleStart) / (double)duration;
                        if (position < begin || position >= end) { continue; }
                        double x = plot.X + position * plot.Width;
                        var marker = new StreamGeometry();
                        using (var triangle = marker.Open())
                        {
                            triangle.BeginFigure(new(x - 4, plot.Y + 2), true);
                            triangle.LineTo(new(x + 4, plot.Y + 2));
                            triangle.LineTo(new(x, plot.Y + 10));
                            triangle.EndFigure(true);
                        }
                        context.DrawGeometry(color, null, marker);
                    }
                    foreach (var artifact in _session.ShockArtifacts)
                    {
                        double position = (artifact.DeliveredAtSimTimeNs - cycleStart) / (double)duration;
                        if (position < begin || position >= end) { continue; }
                        double x = plot.X + position * plot.Width;
                        Label(context, _localization.Get("defib.shockMarker"), x + 3, plot.Y + 2, color, 10);
                    }
                }
            }

        }
    }

    private ContourBatch[] ContourPaths(int row, int age, Point[] contour, Rect plot)
    {
        ContourCache? cached = _contourPaths[row, age];
        if (cached is not null && cached.Plot == plot)
        {
            if (ReferenceEquals(cached.Points, contour)) { return cached.Batches; }
            if (cached.Points.AsSpan().SequenceEqual(contour))
            {
                _contourPaths[row, age] = new(plot, contour, cached.Batches);
                return cached.Batches;
            }
        }
        else { cached = null; }
        List<ContourBatch> batches = [];
        Point ToDevice(Point point) => new(plot.X + point.X * plot.Width, plot.Y + point.Y * plot.Height);
        int index = 1;
        while (index < contour.Length)
        {
            int start = index - 1;
            double column = Math.Floor(contour[start].X * plot.Width);
            do { index++; }
            while (index < contour.Length && Math.Floor(contour[index - 1].X * plot.Width) == column);
            int count = index - start;
            if (cached is not null && batches.Count < cached.Batches.Length &&
                cached.Batches[batches.Count] is { } previous && previous.StartIndex == start && previous.Count == count &&
                cached.Points.AsSpan(start, count).SequenceEqual(contour.AsSpan(start, count)))
            {
                batches.Add(previous);
                continue;
            }
            var path = new StreamGeometry();
            using (var geometry = path.Open())
            {
                // A growing Skia path can rerasterize earlier slopes differently.
                // Fixed one-logical-pixel batches keep completed paths unchanged and bound
                // the changing final batch to the sweep frontier. Separate figures
                // preserve segment ends; device coordinates preserve stroke width.
                for (int point = start + 1; point < index; point++)
                {
                    geometry.BeginFigure(ToDevice(contour[point - 1]), false);
                    geometry.LineTo(ToDevice(contour[point]));
                    geometry.EndFigure(false);
                }
            }
            batches.Add(new(start, count, contour[start].X, contour[index - 1].X, path));
        }
        ContourBatch[] result = batches.ToArray();
        _contourPaths[row, age] = new(plot, contour, result);
        return result;
    }

    private readonly record struct LabelKey(string Value, double Size, Color Color, double Opacity);
    private readonly Dictionary<LabelKey, FormattedText> _labels = [];

    private void Label(DrawingContext context, string value, double x, double y, IBrush color, double size)
    {
        FormattedText Create(IBrush brush) => new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(DesignPreviewWindow.PreviewFont), size, brush);
        if (color is not ISolidColorBrush solid)
        {
            context.DrawText(Create(color), new(x, y));
            return;
        }
        var key = new LabelKey(value, size, solid.Color, solid.Opacity);
        if (!_labels.TryGetValue(key, out FormattedText? label))
        {
            if (_labels.Count >= 64) { _labels.Clear(); }
            label = Create(new ImmutableSolidColorBrush(solid.Color, solid.Opacity));
            _labels.Add(key, label);
        }
        context.DrawText(label, new(x, y));
    }
}
