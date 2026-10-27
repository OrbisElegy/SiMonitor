// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Monitor.Desktop;

internal sealed class PatientTimelineGraphSurface : Control
{
    private static readonly IBrush BackgroundBrush = Brush.Parse("#F6F8FB");
    private static readonly IBrush SequenceBrush = Brush.Parse("#98A6B7");
    private static readonly IBrush[] RouteBrushes = [Brush.Parse("#167C62"), Brush.Parse("#AE6220"), Brush.Parse("#7955B5")];
    private readonly List<RenderedEdge> _edges = [];
    private IReadOnlyList<PatientTimelineGraphNode> _nodes = [];
    private string? _selectedLine;
    private object? _selectedKey;

    internal static IBrush RouteBrush(PatientTimelineGraphRouteKind kind) => RouteBrushes[(int)kind];

    internal void SetLayout(PatientTimelineGraphLayout layout)
    {
        _nodes = layout.Nodes;
        _edges.Clear();
        var routes = layout.Edges.Where(edge => edge.Kind is not null).ToArray();
        var outgoing = routes.GroupBy(edge => (edge.Source.Row, Upward: edge.Target.Row < edge.Source.Row && !edge.IsReturn))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var incoming = routes.GroupBy(edge => (edge.Target, edge.IsReturn))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var returns = routes.Where(edge => edge.IsReturn).ToArray();
        double returnWidth = layout.Nodes.Count == 0 ? 0 : layout.Nodes.Min(node => node.Bounds.Left) - 32;
        foreach (var edge in layout.Edges)
        {
            if (edge.Kind is null) { _edges.Add(CreateEdge(edge, 0, 0, 0, true)); continue; }
            var sourceGroup = outgoing[(edge.Source.Row, edge.Target.Row < edge.Source.Row && !edge.IsReturn)];
            var targetGroup = incoming[(edge.Target, edge.IsReturn)];
            double sourceLane = 1 - Fraction(sourceGroup, edge);
            double targetPort = 1 - Fraction(targetGroup, edge);
            double returnLane = edge.IsReturn ? 16 + returnWidth * Fraction(returns, edge) : 0;
            _edges.Add(CreateEdge(edge, sourceLane, targetPort, returnLane, targetGroup.Length == 1));
        }
        InvalidateVisual();
    }

    internal RenderedEdge GeometryFor(PatientTimelineGraphEdge edge) => _edges.Single(item => ReferenceEquals(item.Model, edge));

    private static double Fraction(PatientTimelineGraphEdge[] group, PatientTimelineGraphEdge edge) =>
        (Array.IndexOf(group, edge) + 1d) / (group.Length + 1d);

    internal void Select(string lineId, object? eventKey)
    {
        _selectedLine = lineId;
        _selectedKey = eventKey;
        InvalidateVisual();
    }

    internal PatientTimelineGraphEdge? HitRoute(Point point, double tolerance)
    {
        // Match visible treatment connections only. Sequence arrows belong to their destination card.
        foreach (var edge in _edges.AsEnumerable().Reverse())
        {
            if (edge.Model.Kind is null) { continue; }
            for (int i = 1; i < edge.Points.Count; i++)
            {
                if (DistanceToSegment(point, edge.Points[i - 1], edge.Points[i]) <= tolerance) { return edge.Model; }
            }
        }
        return null;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));
        foreach (var row in _nodes.GroupBy(node => node.Row))
        {
            var first = row.First();
            var last = row.Last();
            context.DrawRectangle(Brushes.White, null,
                new Rect(first.Bounds.X - 16, first.Bounds.Y - 16, last.Bounds.Right - first.Bounds.X + 32, first.Bounds.Height + 32), 12, 12);
        }
        foreach (var edge in _edges)
        {
            bool selected = edge.Model.Kind is not null && edge.Model.Source.Line.Id == _selectedLine && ReferenceEquals(edge.Model.Source.Key, _selectedKey);
            IBrush brush = edge.Model.Kind is { } kind ? RouteBrush(kind) : SequenceBrush;
            context.DrawGeometry(null, new Pen(brush, selected ? 3.5 : 2), edge.Path);
            context.DrawGeometry(brush, null, edge.Arrow);
        }
    }

    private static RenderedEdge CreateEdge(PatientTimelineGraphEdge edge, double sourceLane, double targetPort, double returnLane, bool allowDirect)
    {
        var path = new StreamGeometry();
        var points = new List<Point>();
        Point end;
        Vector direction;
        int kind = (int)(edge.Kind ?? PatientTimelineGraphRouteKind.ShockSuccess);
        var start = new Point(edge.Source.Bounds.Right,
            edge.Source.Bounds.Center.Y + (edge.Kind is null ? 0 : -51 + kind * 28));
        using (var context = path.Open())
        {
            if (edge.Kind is null || !edge.IsReturn && allowDirect && edge.Target.Bounds.Left - edge.Source.Bounds.Right <= PatientTimelineGraphLayout.ColumnGap)
            {
                end = new Point(edge.Target.Bounds.Left - 3,
                    edge.Kind is null ? edge.Target.Bounds.Center.Y : edge.Target.Bounds.Top + 24 + (edge.Target.Bounds.Height - 48) * targetPort);
                double middle = (start.X + end.X) / 2;
                var first = new Point(middle, start.Y);
                var second = new Point(middle, end.Y);
                context.BeginFigure(start, false);
                context.CubicBezierTo(first, second, end);
                context.EndFigure(false);
                AddBezierPoints(points, start, first, second, end);
                direction = new Vector(1, 0);
            }
            else
            {
                double sourceX = edge.Source.Bounds.Right + 44 - kind * 8;
                bool upward = edge.Target.Row < edge.Source.Row && !edge.IsReturn;
                double sourceY = upward ? edge.Source.Bounds.Top - 20 - 76 * sourceLane : edge.Source.Bounds.Bottom + 20 + 76 * sourceLane;
                if (!edge.IsReturn)
                {
                    // Every row uses the same column grid. These vertical corridors stay
                    // between cards even when a shared destination crosses several rows.
                    double targetX = edge.Target.Bounds.Left - 24 - 76 * targetPort;
                    end = new Point(edge.Target.Bounds.Left - 3,
                        edge.Target.Bounds.Top + 24 + (edge.Target.Bounds.Height - 48) * targetPort);
                    AddRoundedPath(context, points,
                        [start, new(sourceX, start.Y), new(sourceX, sourceY), new(targetX, sourceY), new(targetX, end.Y), end]);
                    direction = new Vector(1, 0);
                }
                else
                {
                    double targetY = edge.Target.Bounds.Top - 20 - 44 * targetPort;
                    end = new Point(edge.Target.Bounds.Left + 24 + (edge.Target.Bounds.Width - 48) * targetPort, edge.Target.Bounds.Top - 3);
                    AddRoundedPath(context, points,
                        [start, new(sourceX, start.Y), new(sourceX, sourceY), new(returnLane, sourceY),
                            new(returnLane, targetY), new(end.X, targetY), end]);
                    direction = new Vector(0, 1);
                }
            }
        }
        var arrow = new StreamGeometry();
        using (var context = arrow.Open())
        {
            var normal = new Vector(-direction.Y, direction.X);
            context.BeginFigure(end, true);
            context.LineTo(end - direction * 10 + normal * 5);
            context.LineTo(end - direction * 10 - normal * 5);
            context.EndFigure(true);
        }
        return new RenderedEdge(edge, path, arrow, points, new Point(start.X + 8, start.Y - 11));
    }

    private static void AddRoundedPath(StreamGeometryContext context, List<Point> samples, Point[] corners)
    {
        context.BeginFigure(corners[0], false);
        samples.Add(corners[0]);
        for (int i = 1; i < corners.Length - 1; i++)
        {
            var incoming = Difference(corners[i], corners[i - 1]);
            var outgoing = Difference(corners[i + 1], corners[i]);
            if (incoming.Length == 0 || outgoing.Length == 0)
            {
                context.LineTo(corners[i]);
                samples.Add(corners[i]);
                continue;
            }
            double radius = Math.Min(16, Math.Min(incoming.Length, outgoing.Length) / 2);
            var before = corners[i] - incoming / incoming.Length * radius;
            var after = corners[i] + outgoing / outgoing.Length * radius;
            context.LineTo(before);
            context.QuadraticBezierTo(corners[i], after);
            samples.Add(before);
            for (int step = 1; step <= 8; step++)
            {
                double t = step / 8d;
                samples.Add(new Point(
                    (1 - t) * (1 - t) * before.X + 2 * (1 - t) * t * corners[i].X + t * t * after.X,
                    (1 - t) * (1 - t) * before.Y + 2 * (1 - t) * t * corners[i].Y + t * t * after.Y));
            }
        }
        context.LineTo(corners[^1]);
        context.EndFigure(false);
        samples.Add(corners[^1]);
    }

    private static void AddBezierPoints(List<Point> points, Point start, Point first, Point second, Point end)
    {
        for (int step = 0; step <= 40; step++)
        {
            double t = step / 40d;
            double u = 1 - t;
            points.Add(new Point(
                u * u * u * start.X + 3 * u * u * t * first.X + 3 * u * t * t * second.X + t * t * t * end.X,
                u * u * u * start.Y + 3 * u * u * t * first.Y + 3 * u * t * t * second.Y + t * t * t * end.Y));
        }
    }

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        var segment = Difference(end, start);
        double lengthSquared = segment.X * segment.X + segment.Y * segment.Y;
        if (lengthSquared == 0) { return Difference(point, start).Length; }
        var relative = Difference(point, start);
        double fraction = Math.Clamp((relative.X * segment.X + relative.Y * segment.Y) / lengthSquared, 0, 1);
        return Difference(point, start + segment * fraction).Length;
    }

    private static Vector Difference(Point first, Point second) => new(first.X - second.X, first.Y - second.Y);

    internal sealed record RenderedEdge(PatientTimelineGraphEdge Model, StreamGeometry Path, StreamGeometry Arrow,
        List<Point> Points, Point CaptionOrigin);
}
