// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Desktop;

// Screen-only grid/calipers. Patient waveform drawing is a separate pending layer.
public sealed class RecordStudyControl : Control
{
    private sealed record GridStroke(Point Start, Point End, bool Major);
    private sealed record Marker(Point Position, bool First);
    private readonly GridStroke[] _grid;
    private readonly Marker[] _markers;
    private readonly Rect _plot;
    private readonly double _factor;
    private readonly IBrush _background;
    private readonly Pen _minor;
    private readonly Pen _major;
    private readonly Pen _first;
    private readonly Pen _second;
    private readonly double _radius;
    private Func<Point, RecordCursorHits>? _pointerQuery;

    public RecordStudyControl(CapturedRecordSvgPublication publication, EcgPaperGridSvgStyle gridStyle,
        EcgManualCursorSvgStyle cursorStyle)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (publication.Status != CapturedRecordSvgStatus.Ready || publication.Input is null)
        { throw new ArgumentException("Native record content requires ready publication", nameof(publication)); }
        ArgumentNullException.ThrowIfNull(gridStyle);
        ArgumentNullException.ThrowIfNull(cursorStyle);
        InputSession = publication.Input;
        ZoomedCapturedRecordSvgScreenLayers display = InputSession.Display;
        Ecg12ScreenTransform transform = display.RenderedTransform!;
        _factor = Ratio(transform.Factor.Numerator, transform.Factor.Denominator);
        Width = Ratio(transform.Width.Numerator, transform.Width.Denominator);
        Height = Ratio(transform.Height.Numerator, transform.Height.Denominator);
        CapturedRecordPageDisplay page = display.Content.Content.Display.Content.Content;
        RecordCursorViewport viewport = page.Viewport!;
        EcgVerticalScale vertical = InputSession.Layout.VerticalScale;
        _plot = new(viewport.PlotLeftPixels, vertical.PlotTopPixels, viewport.PlotWidthPixels, vertical.PlotHeightPixels);
        _background = display.Content.Content.Display.Content.Theme!.Theme == Ecg12Theme.PaperGridBlack ? Brushes.White : Brushes.Black;
        _minor = MakePen(gridStyle.MinorColor, gridStyle.MinorStrokeMilliPixels);
        _major = MakePen(gridStyle.MajorColor, gridStyle.MajorStrokeMilliPixels);
        _first = MakePen(cursorStyle.FirstColor, cursorStyle.StrokeMilliPixels);
        _second = MakePen(cursorStyle.SecondColor, cursorStyle.StrokeMilliPixels);
        if (cursorStyle.RadiusMilliPixels == 0) { throw new ArgumentOutOfRangeException(nameof(cursorStyle)); }
        _radius = cursorStyle.RadiusMilliPixels / 1000.0;
        _grid = display.Content.Content.Display.GridLines.Select(line =>
        {
            double p = Ratio(line.Position.Numerator, line.Position.Denominator);
            return line.IsVertical ? new GridStroke(new(p, _plot.Top), new(p, _plot.Bottom), line.IsMajor)
                : new GridStroke(new(_plot.Left, p), new(_plot.Right, p), line.IsMajor);
        }).ToArray();
        List<Marker> markers = [];
        if (page.Study.Measurement is { ReasonCode: "RecordMeasurement.Ready" } measurement)
        {
            AddMarker(markers, measurement.First, true);
            AddMarker(markers, measurement.Second, false);
        }
        _markers = markers.ToArray();
        IsHitTestVisible = false;
    }

    public CapturedRecordSvgInputSession InputSession { get; }
    public RecordCursorHits HoveredCursors { get; private set; }

    internal void BindPointerQuery(Func<Point, RecordCursorHits>? query)
    {
        _pointerQuery = query;
        IsHitTestVisible = query is not null;
        SetHover(RecordCursorHits.None);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Func<Point, RecordCursorHits>? query = _pointerQuery;
        if (query is null) { return; }
        // GetPosition returns rendered control-local logical pixels, including
        // current native scroll translation. Only the paint-scale inverse remains.
        RecordCursorHits hits = query(e.GetPosition(this));
        if (ReferenceEquals(query, _pointerQuery)) { SetHover(hits); }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(RecordCursorHits.None);
    }

    private void SetHover(RecordCursorHits hits)
    {
        HoveredCursors = hits;
        if (hits == RecordCursorHits.None) { ToolTip.SetIsOpen(this, false); }
        ToolTip.SetTip(this, hits switch
        {
            RecordCursorHits.First => "卡尺起点",
            RecordCursorHits.Second => "卡尺终点",
            RecordCursorHits.First | RecordCursorHits.Second => "两条卡尺同时命中",
            _ => null,
        });
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using (context.PushClip(new Rect(Bounds.Size)))
        using (context.PushTransform(Matrix.CreateScale(_factor, _factor)))
        {
            context.DrawRectangle(_background, null, new Rect(0, 0, Width / _factor, Height / _factor));
            using (context.PushClip(_plot))
            {
                foreach (GridStroke line in _grid)
                { if (!line.Major) { context.DrawLine(_minor, line.Start, line.End); } }
                foreach (GridStroke line in _grid)
                { if (line.Major) { context.DrawLine(_major, line.Start, line.End); } }
                foreach (Marker marker in _markers)
                {
                    Pen pen = marker.First ? _first : _second;
                    Point point = marker.Position;
                    context.DrawLine(pen, new(point.X, _plot.Top), new(point.X, _plot.Bottom));
                    context.DrawLine(pen, new(_plot.Left, point.Y), new(_plot.Right, point.Y));
                    context.DrawEllipse(null, pen, point, _radius, _radius);
                }
            }
        }
    }

    private static Pen MakePen(string color, uint width)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        if (color is not { Length: 7 } || color[0] != '#' ||
            color.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF".AsSpan()))
        { throw new ArgumentException("Expected #RRGGBB color", nameof(color)); }
        return new Pen(Brush.Parse(color), width / 1000.0);
    }

    private static double Ratio(BigInteger numerator, BigInteger denominator) => (double)numerator / (double)denominator;

    private static void AddMarker(List<Marker> markers, ProjectedRecordCursor? cursor, bool first)
    {
        if (cursor?.Y.Relation != VerticalPlotRelation.WithinPlot) { return; }
        double x = cursor.X.WholePixels + Ratio(cursor.X.FractionNumerator, cursor.X.FractionDenominator);
        double y = Ratio((BigInteger)cursor.Y.PixelNumerator, (BigInteger)cursor.Y.PixelDenominator);
        markers.Add(new(new(x, y), first));
    }
}
