// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;

namespace Monitor.Desktop;

internal enum PatientTimelineGraphRouteKind
{
    ShockSuccess,
    ShockFailure,
    Pacing
}

internal sealed record PatientTimelineGraphRoute(string TargetLineId, PatientTimelineGraphRouteKind Kind);

// Keys belong to the editor. Reference identity survives edits without requiring a valid domain event.
internal sealed record PatientTimelineGraphEvent(
    object Key,
    string Name,
    decimal? StartSeconds,
    decimal? DurationSeconds,
    string Summary,
    bool IsValid,
    IReadOnlyList<PatientTimelineGraphRoute> Routes,
    bool IsManual = false,
    bool IsRelativeToPrevious = false);

internal sealed record PatientTimelineGraphLine(
    string Id,
    string Name,
    bool IsInitial,
    IReadOnlyList<PatientTimelineGraphEvent> Events,
    IReadOnlyList<PatientTimelineGraphRoute> EntryRoutes,
    bool IsValid = true,
    string EntrySummary = "");

internal sealed record PatientTimelineGraphNode(
    PatientTimelineGraphLine Line,
    PatientTimelineGraphEvent? Event,
    Rect Bounds,
    int Row,
    int Column)
{
    internal object? Key => Event?.Key;
    internal bool IsValid => Line.IsValid && (Event?.IsValid ?? true);
}

internal sealed record PatientTimelineGraphEdge(
    PatientTimelineGraphNode Source,
    PatientTimelineGraphNode Target,
    PatientTimelineGraphRouteKind? Kind,
    bool IsReturn);

internal sealed record PatientTimelineGraphLayout(
    IReadOnlyList<PatientTimelineGraphNode> Nodes,
    IReadOnlyList<PatientTimelineGraphEdge> Edges,
    Size Size)
{
    internal const double NodeWidth = 230;
    internal const double NodeHeight = 166;
    internal const double ColumnGap = 164;
    internal const double RowGap = 116;
    internal const double Padding = 72;

    internal static PatientTimelineGraphLayout Build(IReadOnlyList<PatientTimelineGraphLine> lines)
    {
        var definitions = new Dictionary<string, PatientTimelineGraphLine>(StringComparer.Ordinal);
        foreach (var line in lines) { definitions.TryAdd(line.Id, line); }
        var placed = new Dictionary<string, PatientTimelineGraphNode[]>(StringComparer.Ordinal);
        var nodes = new List<PatientTimelineGraphNode>();
        int nextRow = 0;
        double width = 0;
        // Return connections use a separate corridor to the left of every line.
        double left = Padding + definitions.Count * 12;

        void Place(PatientTimelineGraphLine line, double x)
        {
            if (placed.ContainsKey(line.Id)) { return; }
            int row = nextRow++;
            double y = Padding + row * (NodeHeight + RowGap);
            var rowNodes = new PatientTimelineGraphNode[line.Events.Count + 1];
            for (int column = 0; column < rowNodes.Length; column++)
            {
                var bounds = new Rect(x + column * (NodeWidth + ColumnGap), y, NodeWidth, NodeHeight);
                var node = new PatientTimelineGraphNode(line, column == 0 ? null : line.Events[column - 1], bounds, row, column);
                rowNodes[column] = node;
                nodes.Add(node);
                width = Math.Max(width, bounds.Right);
            }
            placed.Add(line.Id, rowNodes);
            // Later branch points are placed first. Their descendants stay to the right of
            // the earlier branch corridors, keeping tree arrows clear of intervening cards.
            for (int column = rowNodes.Length - 1; column >= 0; column--)
            {
                var source = rowNodes[column];
                var routes = source.Event?.Routes ?? line.EntryRoutes;
                foreach (var route in routes)
                {
                    if (!definitions.TryGetValue(route.TargetLineId, out var target) || placed.ContainsKey(target.Id)) { continue; }
                    Place(target, source.Bounds.Right + ColumnGap);
                }
            }
        }

        foreach (var line in lines.Where(line => line.IsInitial)) { Place(line, left); }
        foreach (var line in lines) { Place(line, left); }

        var edges = new List<PatientTimelineGraphEdge>();
        foreach (var node in nodes)
        {
            var rowNodes = placed[node.Line.Id];
            if (node.Column + 1 < rowNodes.Length)
            {
                edges.Add(new PatientTimelineGraphEdge(node, rowNodes[node.Column + 1], null, false));
            }
            var routes = node.Event?.Routes ?? node.Line.EntryRoutes;
            if (routes.Count > 0) { width = Math.Max(width, node.Bounds.Right + ColumnGap); }
            foreach (var route in routes)
            {
                if (!placed.TryGetValue(route.TargetLineId, out var target)) { continue; }
                // Placement ownership is unrelated to direction: several earlier events can
                // enter the same later branch. Only a connection going back needs a return lane.
                bool isReturn = target[0].Bounds.Left <= node.Bounds.Right;
                edges.Add(new PatientTimelineGraphEdge(node, target[0], route.Kind, isReturn));
            }
        }
        return new PatientTimelineGraphLayout(nodes, edges,
            new Size(Math.Max(400, width + Padding), Math.Max(240, nextRow * (NodeHeight + RowGap) + Padding)));
    }
}
