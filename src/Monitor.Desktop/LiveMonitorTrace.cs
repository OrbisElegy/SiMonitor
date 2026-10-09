// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed class LiveMonitorTrace : Control
{
    internal static readonly string[] Names = ["ECG · II", "RESP", "PLETH", "ABP", "CO₂", "PA", "CVP"];
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
        DetachedFromVisualTree += (_, _) => _localization.LocaleChanged -= InvalidateVisual;
    }
    internal static readonly string[] Colors = ["#71E9AF", "#F0D68A", "#8ADAE5", "#F39199", "#E7ECF2", "#CAA7EA", "#F2B67D"];
    private ulong _revision = ulong.MaxValue;
    private long _cycle = -1;
    private StreamGeometry?[,] _paths = new StreamGeometry?[0, 0];
    private Point[]?[,] _contours = new Point[]?[0, 0];
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
            foreach (var beat in _session.SynchronizationBeats)
            {
                if (beat.PeakTimeNs >= _synchronizedSinceNs && (_synchronizationPeaks.Count == 0 || beat.PeakTimeNs > _synchronizationPeaks[^1]))
                { _synchronizationPeaks.Add(beat.PeakTimeNs); }
            }
            _synchronizationPeaks.RemoveAll(peak => peak < _session.SimulationTimeNs - LocalMonitorPreviewSession.RetainedBlockCount * 200_000_000L);
        }
        InvalidateVisual();
    }
    internal MonitorChannels MonitoredChannels => _session.Display.Slots.Aggregate(MonitorChannels.None,
        (channels, slot) => channels | MonitorChannelMapping.ForChannel(slot.Channel)) & (Skin?.Channels ?? MonitorChannels.All);
    internal IBrush ChannelBrush(int channel) => Skin?.ChannelBrush(channel) ?? Brush.Parse(Colors[channel]);
    private void Build()
    {
        if (_revision == _session.DataRevision && _cycle == _session.Ranges.Cycle) { return; }
        _revision = _session.DataRevision; _cycle = _session.Ranges.Cycle;
        _paths = new StreamGeometry?[_session.Display.Slots.Count, 2];
        _contours = new Point[]?[_session.Display.Slots.Count, 2];
        for (int row = 0; row < _session.Display.Slots.Count; row++)
            for (int age = 0; age < 2; age++)
            {
                long cycle = _session.Ranges.RowCycle(row) - age;
                long duration = _session.Display.Slots[row].DurationNs;
                if (cycle < 0 || (age == 1 && !_session.Ranges.ShowPrevious(row))) { continue; }
                long from = cycle * duration;
                List<Point> stablePoints = [];
                var path = new StreamGeometry();
                using (var geometry = path.Open())
                {
                    bool started = false;
                    int channel = _session.Display.Slots[row].Channel;
                    var samples = _session.PresentedSamples(channel, from, from + duration).ToArray();
                    var contour = channel != 0 ? PreviewContour.Interpolate(samples) : samples;
                    foreach (var sample in contour)
                    {
                        Point point = new((sample.TimeNs - from) / (double)duration,
                            1 - (age == 0 ? _session.Ranges.Range(row) : _session.Ranges.PreviousRange(row)).Normalize(sample.Value));
                        if (channel != 0) { stablePoints.Add(point); continue; }
                        if (!started) { geometry.BeginFigure(point, false); started = true; }
                        else { geometry.LineTo(point); }
                    }
                    if (started) { geometry.EndFigure(false); }
                }
                if (stablePoints.Count > 0) { _contours[row, age] = stablePoints.ToArray(); }
                else { _paths[row, age] = path; }
            }
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Skin?.Background ?? Brush.Parse("#101B25"), new Rect(Bounds.Size));
        if (Bounds.Width < 200 || Bounds.Height < 60) { return; }
        Build();
        int rows = _session.Display.Slots.Count;
        double rowHeight = Bounds.Height / rows;
        double left = 132, width = Math.Max(1, Bounds.Width - left - 18);
        for (int row = 0; row < rows; row++)
        {
            long duration = _session.Display.Slots[row].DurationNs;
            int channel = _session.Display.Slots[row].Channel;
            long frontierNs = _session.PresentationFrontierNs(channel);
            double phase = (frontierNs % duration) / (double)duration;
            if ((MonitoredChannels & MonitorChannelMapping.ForChannel(channel)) == 0) { continue; }
            var color = ChannelBrush(channel);
            var range = _session.Ranges.Range(row);
            double top = row * rowHeight;
            context.DrawLine(new Pen(Brush.Parse("#607080"), 1), new(0, top + rowHeight - 1), new(Bounds.Width, top + rowHeight - 1));
            Label(context, Names[channel], 12, top + 10, color, 14);
            Label(context, string.Create(CultureInfo.InvariantCulture, $"{range.Minimum:0.##} – {range.Maximum:0.##}"), 12, top + 32, color, 11);
            string unit = Unit(_localization.Current, channel);
            if (Skin is null || channel != 0)
            { Label(context, _localization.Format(_session.Display.Slots[row].Automatic ? "monitor.rangeAutomatic" : "monitor.rangeFixed", unit), 12, top + 49, color, 11); }
            Rect plot = new(left, top + 9, width, Math.Max(1, rowHeight - 20));
            if (channel == 0)
            {
                if (rowHeight >= (Skin is null ? 100 : 80) && _session.ShockArtifacts.Any(artifact => artifact.Contains(frontierNs)))
                { Label(context, _localization.Get("defib.ecgRecovering"), 12, top + (Skin is null ? 83 : 66), color, 11); }
                if (range.Minimum <= -500 && range.Maximum >= 500)
                {
                    double x = left + width * 200_000_000 / duration;
                    context.DrawLine(new Pen(color, 1.2), new(x, plot.Bottom - range.Normalize(-500) * plot.Height),
                        new(x, plot.Bottom - range.Normalize(500) * plot.Height));
                    Label(context, "1 mV", 12, top + (Skin is null ? 66 : 49), color, 11);
                }
                else { Label(context, _localization.Get("monitor.calibrationOffScale"), 12, top + (Skin is null ? 66 : 49), color, 11); }
            }
            for (int age = 1; age >= 0; age--)
            {
                var path = _paths[row, age];
                var contour = _contours[row, age];
                if (path is null && contour is null) { continue; }
                double begin = age == 0 ? 0 : Math.Min(1, phase + .012);
                double end = age == 0 ? phase : 1;
                if (end <= begin) { continue; }
                using var clip = context.PushClip(new Rect(plot.X + begin * plot.Width, plot.Y, (end - begin) * plot.Width, plot.Height));
                if (contour is not null)
                {
                    var pen = new Pen(color, 1.2);
                    Point ToDevice(Point p) => new(plot.X + p.X * plot.Width, plot.Y + p.Y * plot.Height);
                    for (int i = 1; i < contour.Length; i++)
                    {
                        if (contour[i].X < begin || contour[i - 1].X > end) { continue; }
                        context.DrawLine(pen, ToDevice(contour[i - 1]), ToDevice(contour[i]));
                    }
                    continue;
                }
                // Geometry is cached in normalized units. Scale with the window;
                // draw a device-space stroke so resize does not thicken the trace.
                var matrix = new Matrix(plot.Width, 0, 0, plot.Height, plot.X, plot.Y);
                if (path!.Transform is null || !path.Transform.Value.Equals(matrix)) { path.Transform = new MatrixTransform(matrix); }
                context.DrawGeometry(null, new Pen(color, 1.2), path);
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
    private static void Label(DrawingContext context, string value, double x, double y, IBrush color, double size) =>
        context.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(DesignPreviewWindow.PreviewFont), size, color), new(x, y));
}
