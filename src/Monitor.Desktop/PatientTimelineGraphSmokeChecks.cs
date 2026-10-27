// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Localization;

namespace Monitor.Desktop;

internal static class PatientTimelineGraphSmokeChecks
{
    internal static void Verify()
    {
        VerifyLayout();
        VerifySharedDestination();
        var localization = new DesktopLocalization("zh-CN");
        var settings = new PatientTimelineSettings(localization) { Background = DesktopFluentStyle.Surface };
        var window = new Window { Width = 1300, Height = 850, Content = settings };
        window.Show();
        try
        {
            var program = ExampleProgram();
            settings.RestoreProgram(program);
            DesktopViewportSmokeChecks.Layout(window, 1300, 850);
            var graph = settings.Graph;
            Require(graph.NodeButtons.Count == 15 && graph.RouteCount == 5,
                "the graph includes twelve event nodes, three line entries and event-specific treatment connections");
            var original = settings.ReadProgram();
            Require(settings.InspectorVisible && graph.IsVisible && !settings.BackToGraph.IsVisible &&
                settings.InspectorTitle == "Patient course" && !settings.PreviousNode.IsEnabled,
                "a wide window keeps the selected line's details beside the graph without a separate toggle");
            var shock = settings.Editors[3];
            Click(Node(graph, "Shock window"));
            DesktopViewportSmokeChecks.Layout(window, 1300, 850);
            Require(ReferenceEquals(settings.SelectedEditor, shock) && shock.IsEffectivelyVisible && Equivalent(original, settings.ReadProgram()),
                "clicking a graph node opens its exact existing editor without modifying the authored program");
            Click(settings.NextNode);
            Require(settings.SelectedEditor!.EventName.Text == "Instructor review" && settings.InspectorTitle == "Instructor review",
                "next node follows the authored sequence of the selected line");
            Click(settings.PreviousNode);
            Require(ReferenceEquals(settings.SelectedEditor, shock) && Equivalent(original, settings.ReadProgram()),
                "previous node returns to the same editor without changing the draft");

            Click(Node(graph, "Stable circulation"));
            Require(settings.SelectedTimelineId == "recovery" && settings.SelectedEditor!.EventName.Text == "Stable circulation",
                "a branch node selects its own timeline and editor");
            var route = graph.RouteButtons.Single(button => AutomationProperties.GetName(button) ==
                localization.Get("timeline.graph.routeShockSuccess") + ": Shock window → Recovery");
            Click(route);
            Require(settings.SelectedTimelineId == "main" && ReferenceEquals(settings.SelectedEditor, shock),
                "clicking a treatment connection selects the event that owns the rule, not the destination line");
            Require(shock.Treatment.GetVisualAncestors().OfType<Expander>().All(expander => expander.IsExpanded),
                "a treatment connection opens the owning event's treatment controls");
            Require(Equivalent(original, settings.ReadProgram()), "selecting treatment rules leaves the draft unchanged");

            var manual = Node(graph, "Instructor review");
            Require(AutomationProperties.GetName(manual)!.Contains(localization.Get("timeline.graph.manual"), StringComparison.Ordinal),
                "manual nodes carry an explicit manual-entry label instead of an invented trigger time");
            Click(manual);
            manual.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            Require(settings.SelectedEditor!.EventName.Text == "Observation", "arrow keys select the next authored event");

            graph.ResetView();
            DesktopViewportSmokeChecks.Layout(window, 1300, 850);
            graph.Scroller.Offset = new Vector(280, 130);
            var offset = graph.Scroller.Offset;
            double zoom = graph.Zoom;
            settings.SelectedEditor.EventName.Text = "Observation updated";
            DesktopViewportSmokeChecks.Layout(window, 1300, 850);
            Require(graph.Zoom == zoom && (graph.Scroller.Offset - offset).Length < 1,
                "editing a node retains the current zoom and canvas offset");
            settings.SelectedEditor.StartSeconds.Value = .1m;
            DesktopViewportSmokeChecks.Layout(window, 1300, 850);
            Require(!settings.SelectedEditor.IsValid && settings.SelectedEditor.Error.IsVisible &&
                Node(graph, "Observation updated").GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.Text == localization.Get("timeline.graph.invalid")),
                "invalid timing remains visible as an editable graph node with an error state");
            settings.SelectedEditor.StartSeconds.Value = 20;

            Click(Node(graph, "Instructor review"));
            Click(settings.MoveEarlier);
            Require(settings.Read()[3].Name == "Instructor review" && settings.Read()[4].Name == "Shock window",
                "node reordering preserves the authored relative sequence instead of sorting delay values");
            Click(settings.MoveLater);
            Require(settings.Read()[4].Name == "Instructor review", "moving a node back restores its sequence position");

            settings.RestoreProgram(program);
            Click(Node(graph, "Shock window"));
            foreach (string locale in new[] { "zh-CN", "en" })
            {
                localization.Select(locale);
                DesktopViewportSmokeChecks.Layout(window, 1400, 900);
                graph.Fit();
                DesktopViewportSmokeChecks.Layout(window, 1400, 900);
                Capture(window, "patient-timeline-graph-" + locale + ".png");
            }
            graph.ResetView();
            Click(Node(graph, "Shock window"));
            graph.FocusSelection();
            DesktopViewportSmokeChecks.Layout(window, 1400, 900);
            Require(graph.RouteButtons.Where(button => AutomationProperties.GetName(button)!.Contains(": Shock window →", StringComparison.Ordinal))
                .All(button => button.TranslatePoint(default, graph.Scroller) is { } point &&
                    point.X >= 0 && point.X + button.Bounds.Width * graph.Zoom <= graph.Scroller.Viewport.Width),
                "focusing a treatment node also reveals its outgoing result labels");
            Capture(window, "patient-timeline-graph-inspector-en.png");
        }
        finally { window.Close(); }
        VerifyNarrowWindow();
    }

    private static void VerifyLayout()
    {
        // Exercise the renderer's advertised limits independently of the program's smaller total-event cap.
        var lines = Enumerable.Range(0, 16).Select(line => new PatientTimelineGraphLine(
            "line" + line, "Line " + line, line == 0,
            Enumerable.Range(0, 64).Select(index => new PatientTimelineGraphEvent(new object(), "Node " + index,
                64 - index, 0, "", true,
                index == 63 ? [new("line" + (line + 1) % 16, PatientTimelineGraphRouteKind.ShockSuccess)] : [],
                IsRelativeToPrevious: true)).ToArray(), [])).ToArray();
        var layout = PatientTimelineGraphLayout.Build(lines);
        Require(layout.Nodes.Count == 1040 && layout.Edges.Count(edge => edge.Kind is not null) == 16 &&
            layout.Nodes.Select(node => node.Row).Distinct().Count() == 16,
            "sixteen long timelines and a cycle produce a finite complete graph");
        Require(layout.Size.Width > 0 && double.IsFinite(layout.Size.Width) && double.IsFinite(layout.Size.Height) &&
            layout.Nodes.All(node => node.Bounds.X >= 0 && node.Bounds.Y >= 0 &&
                node.Bounds.Right < layout.Size.Width && node.Bounds.Bottom < layout.Size.Height),
            "every event remains inside reachable scroll extents");
        foreach (var line in lines)
        {
            var nodes = layout.Nodes.Where(node => node.Line.Id == line.Id && node.Event is not null).ToArray();
            Require(nodes.Select(node => node.Key).SequenceEqual(line.Events.Select(item => item.Key)) &&
                nodes.Zip(nodes.Skip(1)).All(pair => pair.First.Bounds.Right < pair.Second.Bounds.Left),
                "relative delays do not reorder event cards or overlap adjacent cards");
        }
        Require(layout.Edges.Count(edge => edge.Kind is not null && edge.IsReturn) == 1 &&
            layout.Edges.Where(edge => edge.Kind is not null).All(edge => ReferenceEquals(edge.Source.Key, edge.Source.Line.Events[63].Key)),
            "the cycle uses a separate return connection and every treatment arrow starts at its owning event");
        var surface = new PatientTimelineGraphSurface();
        surface.SetLayout(layout);
        var forward = layout.Edges.First(edge => edge.Kind is not null && !edge.IsReturn);
        Require(surface.HitRoute(surface.GeometryFor(forward).Points[0], 1)?.Source == forward.Source,
            "visible treatment curves can be selected at their source attachment");
    }

    private static void VerifySharedDestination()
    {
        var events = Enumerable.Range(1, 4).Select(index => new PatientTimelineGraphEvent(new object(),
            index <= 3 ? "Shock opportunity " + index : "Asystole", 10, 0, "", true,
            index <= 3 ? [new("recovery", PatientTimelineGraphRouteKind.ShockSuccess)] : [], IsRelativeToPrevious: true)).ToArray();
        PatientTimelineGraphLine[] lines =
        [
            new("main", "Patient course", true, events, []),
            new("recovery", "Recovery", false, [], [])
        ];
        var layout = PatientTimelineGraphLayout.Build(lines);
        var routes = layout.Edges.Where(edge => edge.Kind is not null).ToArray();
        var surface = new PatientTimelineGraphSurface();
        surface.SetLayout(layout);
        Require(routes.Length == 3 && routes.All(edge => !edge.IsReturn),
            "three earlier events entering the same branch are all forward connections regardless of which placed the branch");
        var paths = routes.Select(surface.GeometryFor).ToArray();
        Require(paths.Select(path => path.Points[^1]).Distinct().Count() == 3,
            "shared branch connections have separate destination ports and arrowheads");
        foreach (var path in paths)
        {
            Require(path.Points.All(point => point.X >= path.Model.Source.Bounds.Right && double.IsFinite(point.X) && double.IsFinite(point.Y)) &&
                path.Points.Zip(path.Points.Skip(1)).All(pair => pair.Second.X >= pair.First.X - .001),
                "a forward shared connection never detours toward the left-hand return corridor");
            Require(path.Points.All(point => layout.Nodes.All(node => !node.Bounds.Deflate(1).Contains(point))),
                "shared connections travel between event cards instead of passing through later events");
            Require(surface.HitRoute(path.Points[0], 1) == path.Model && surface.HitRoute(path.Points[^1], 1) == path.Model &&
                path.CaptionOrigin.Y + 11 == path.Points[0].Y,
                "source labels, source hit tests and destination hit tests identify the same rendered connection");
        }
        for (int first = 0; first < paths.Length; first++)
        {
            for (int second = first + 1; second < paths.Length; second++)
            {
                Require(!PathsIntersect(paths[first].Points, paths[second].Points),
                    "parallel opportunities for the same treatment remain separately traceable into their common branch");
            }
        }

        var localization = new DesktopLocalization("zh-CN");
        var graph = new PatientTimelineGraph(localization);
        var panel = new Grid { RowDefinitions = new("Auto,*") };
        graph.ZoomControls.Margin = new Thickness(12);
        panel.Children.Add(graph.ZoomControls);
        Grid.SetRow(graph, 1);
        panel.Children.Add(graph);
        var window = new Window { Width = 1500, Height = 650, Content = panel };
        object? selected = null;
        graph.RouteSelected += (_, key) => selected = key;
        graph.SetLines(lines);
        window.Show();
        try
        {
            DesktopViewportSmokeChecks.Layout(window, 1500, 650);
            for (int index = 0; index < routes.Length; index++)
            {
                var button = graph.RouteButtons[index];
                Click(button);
                Require(ReferenceEquals(selected, events[index].Key) &&
                    Canvas.GetLeft(button) == paths[index].CaptionOrigin.X && Canvas.GetTop(button) == paths[index].CaptionOrigin.Y,
                    "each visible shared-destination label selects its own source event");
            }
            foreach (string locale in new[] { "zh-CN", "en" })
            {
                localization.Select(locale);
                graph.Fit();
                DesktopViewportSmokeChecks.Layout(window, 1500, 650);
                Capture(window, "patient-timeline-shared-branch-" + locale + ".png");
            }
        }
        finally { window.Close(); }
    }

    private static bool PathsIntersect(List<Point> first, List<Point> second)
    {
        for (int a = 1; a < first.Count; a++)
        {
            for (int b = 1; b < second.Count; b++)
            {
                double firstSide = Cross(first[a - 1], first[a], second[b - 1]);
                double secondSide = Cross(first[a - 1], first[a], second[b]);
                double thirdSide = Cross(second[b - 1], second[b], first[a - 1]);
                double fourthSide = Cross(second[b - 1], second[b], first[a]);
                if (firstSide * secondSide < 0 && thirdSide * fourthSide < 0) { return true; }
            }
        }
        return false;
    }

    private static double Cross(Point start, Point end, Point point) =>
        (end.X - start.X) * (point.Y - start.Y) - (end.Y - start.Y) * (point.X - start.X);

    private static void VerifyNarrowWindow()
    {
        var window = new DesignPreviewWindow(startAudioOutput: false);
        window.Show();
        window.Pause();
        try
        {
            window.SelectPage(2);
            window.Settings.Tabs.SelectedIndex = 7;
            var settings = window.Settings.Timeline;
            settings.RestoreProgram(ExampleProgram());
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            settings.Graph.ResetView();
            settings.Graph.Scroller.Offset = new Vector(250, 120);
            var offset = settings.Graph.Scroller.Offset;
            double zoom = settings.Graph.Zoom;
            Click(Node(settings.Graph, "Shock window"));
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            var editor = settings.SelectedEditor!;
            var editorScroll = editor.GetVisualAncestors().OfType<ScrollViewer>().First();
            var namePoint = editor.EventName.TranslatePoint(default, editorScroll);
            var triggerPoint = editor.Trigger.TranslatePoint(default, editorScroll);
            Require(!settings.Graph.IsVisible && editor.IsEffectivelyVisible && editorScroll.Viewport.Height >= 180 &&
                namePoint is { } name && name.Y >= 0 && name.Y + editor.EventName.Bounds.Height <= editorScroll.Viewport.Height &&
                triggerPoint is { } trigger && trigger.Y >= 0 && trigger.Y + editor.Trigger.Bounds.Height <= editorScroll.Viewport.Height,
                "a narrow window dedicates enough space to edit the selected node's name and trigger");
            Require(settings.BackToGraph.IsEffectivelyVisible && !settings.Graph.ZoomControls.IsVisible,
                "narrow details offer a way back and hide view commands for the hidden graph");
            Click(settings.NextNode);
            Require(!settings.Graph.IsVisible && settings.SelectedEditor!.EventName.Text == "Instructor review",
                "narrow details step to the next node without returning to the graph");
            Click(settings.PreviousNode);
            Click(settings.BackToGraph);
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            Require(settings.Graph.IsVisible && settings.Graph.Zoom == zoom && (settings.Graph.Scroller.Offset - offset).Length < 1,
                "returning from narrow node details restores the existing graph view");
            var focusedNode = Node(settings.Graph, "Shock window");
            Require(focusedNode.Focus(NavigationMethod.Tab), "the narrow graph node accepts keyboard focus");
            Click(focusedNode);
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            Dispatcher.UIThread.RunJobs();
            Require(settings.SelectedEditor!.EventName.IsKeyboardFocusWithin,
                "opening narrow node details moves keyboard focus from the hidden graph into the editor");
            Require(settings.BackToGraph.Focus(NavigationMethod.Tab), "the graph return action accepts keyboard focus");
            Click(settings.BackToGraph);
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            Dispatcher.UIThread.RunJobs();
            Require(Node(settings.Graph, "Shock window").IsKeyboardFocusWithin,
                "returning to the narrow graph restores keyboard focus to the selected node");
            Click(Node(settings.Graph, "Shock window"));
            foreach (string locale in new[] { "zh-CN", "en" })
            {
                window.Settings.Language.SelectedIndex = BuiltInLocalizations.Supported.Select(item => item.Code).ToList().IndexOf(locale);
                DesktopViewportSmokeChecks.Layout(window, 1000, 720);
                Capture(window, "patient-timeline-graph-narrow-" + locale + ".png");
            }
        }
        finally { window.Close(); }
    }

    private static PatientTimelineProgram ExampleProgram() => new("main",
    [
        new("main", "Patient course",
        [
            Step("Initial assessment", 0, new() { TemperatureDeciCelsius = 370 }),
            Step("Breathing changes", 20, new() { RespiratoryRatePerMinute = 28 }),
            Step("Rhythm deterioration", 15, new()) with { EcgTemplateId = "ecgTemplate.t023" },
            Step("Shock window", 0, new()) with { Treatment = new(true, true, "recovery", "persistent", "recovery") },
            Step("Instructor review", 0, new()) with { Trigger = PatientTimelineTrigger.Manual, Treatment = new(false, false) },
            Step("Observation", 20, new() { TemperatureDeciCelsius = 375 })
        ], new()),
        new("recovery", "Recovery",
        [
            Step("Organized rhythm", 0, new()) with { EcgTemplateId = "ecgTemplate.t000" },
            Step("Stable circulation", 10, new() { HeartRateBpm = 85 }),
            Step("Oxygenation improves", 20, new() { SaturationMilliPercent = 98000 })
        ], new()),
        new("persistent", "Persistent rhythm",
        [
            Step("Reassess rhythm", 0, new()) with { Treatment = new(true, false, "recovery", "persistent") },
            Step("Prepare another shock", 15, new()),
            Step("Instructor decision", 0, new()) with { Trigger = PatientTimelineTrigger.Manual }
        ], new())
    ]);

    private static PatientTimelineEvent Step(string name, long delaySeconds, PatientVitalTargets targets) =>
        new(name, delaySeconds * 1_000_000_000, 0, targets) { Trigger = PatientTimelineTrigger.AfterPrevious };

    private static bool Equivalent(PatientTimelineProgram first, PatientTimelineProgram second) =>
        first.InitialTimelineId == second.InitialTimelineId && first.Timelines.Count == second.Timelines.Count &&
        first.Timelines.Zip(second.Timelines).All(pair => pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name &&
            pair.First.Treatment == pair.Second.Treatment && pair.First.Events.SequenceEqual(pair.Second.Events));

    private static Button Node(PatientTimelineGraph graph, string name) => graph.NodeButtons.Single(button =>
        ((Grid)button.Content!).Children.OfType<TextBlock>().First().Text == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var root = (Control)window.Content!;
        using var image = new RenderTargetBitmap(new PixelSize((int)root.Bounds.Width, (int)root.Bounds.Height));
        image.Render(root);
        Directory.CreateDirectory("artifacts");
        image.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
