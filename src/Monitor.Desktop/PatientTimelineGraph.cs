// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Monitor.Application.Localization;

namespace Monitor.Desktop;

internal sealed class PatientTimelineGraph : UserControl
{
    private static readonly IBrush InvalidBrush = Brush.Parse("#B42318");
    private static readonly IBrush SelectedBrush = Brush.Parse("#EAF4FF");
    // The first view of a program stays legible; Fit graph still shows every branch.
    private const double ReadableZoom = .75;
    private readonly DesktopLocalization _localization;
    private readonly Canvas _canvas = new();
    private readonly PatientTimelineGraphSurface _surface = new();
    private readonly LayoutTransformControl _scaled;
    private readonly TextBlock _zoomLabel = new() { MinWidth = 44, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly List<(PatientTimelineGraphNode Node, Button Button)> _buttons = [];
    private readonly List<Button> _routeButtons = [];
    private PatientTimelineGraphLayout _layout = PatientTimelineGraphLayout.Build([]);
    private string? _selectedLine;
    private object? _selectedKey;
    private bool _receivedLines;
    private bool _fitPending;
    private bool _fitReadable;
    private IPointer? _panPointer;
    private Point _panOrigin;
    private Vector _panOffset;
    private bool _panMoved;
    private PatientTimelineGraphEdge? _pressedEdge;

    internal PatientTimelineGraph(DesktopLocalization localization)
    {
        _localization = localization;
        MinHeight = 160;
        _scaled = new LayoutTransformControl
        {
            Child = _canvas,
            LayoutTransform = new ScaleTransform(1, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        Scroller = new ScrollViewer
        {
            Content = _scaled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brush.Parse("#F6F8FB"),
            MinHeight = 80
        };
        _canvas.Children.Add(_surface);
        _surface.Cursor = new Cursor(StandardCursorType.Hand);
        _surface.PointerPressed += BeginPan;
        _surface.PointerMoved += MovePan;
        _surface.PointerReleased += FinishPan;
        _surface.PointerCaptureLost += (_, _) => EndPan();
        Scroller.AddHandler(PointerWheelChangedEvent, Wheel, RoutingStrategies.Tunnel);
        Scroller.SizeChanged += (_, _) =>
        {
            UpdateExtent();
            if (_fitPending) { FitView(_fitReadable); }
        };
        Scroller.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            UpdateExtent();
            if (_fitPending) { FitView(_fitReadable); }
        }, DispatcherPriority.Loaded);
        DetachedFromVisualTree += (_, _) => EndPan();

        // The host places these view commands in its command bar beside the authoring actions.
        var zoomOut = ToolbarButton("timeline.graph.zoomOut", () => ZoomAt(Zoom / 1.2, ViewportCenter()), "−");
        var zoomIn = ToolbarButton("timeline.graph.zoomIn", () => ZoomAt(Zoom * 1.2, ViewportCenter()), "+");
        var actualSize = ToolbarButton("timeline.graph.reset", ResetView, "");
        actualSize.Content = _zoomLabel;
        actualSize.Padding = new Thickness(4, 0);
        actualSize.Background = Brushes.Transparent;
        actualSize.BorderBrush = Brushes.Transparent;
        ZoomControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { zoomOut, actualSize, zoomIn, ToolbarButton("timeline.graph.fit", Fit) }
        };
        Content = new Border
        {
            Background = Brushes.White,
            BorderBrush = DesktopFluentStyle.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = Scroller
        };
        RefreshZoomLabel();
    }

    internal event Action<string, object?>? Selected;
    internal event Action<string, object?>? RouteSelected;
    internal Control ZoomControls { get; }
    internal ScrollViewer Scroller { get; }
    internal double Zoom { get; private set; } = 1;
    internal IReadOnlyList<Button> NodeButtons => _buttons.Select(item => item.Button).ToArray();
    internal IReadOnlyList<Button> RouteButtons => _routeButtons.AsReadOnly();
    internal int RouteCount => _layout.Edges.Count(edge => edge.Kind is not null);

    internal void SetLines(IReadOnlyList<PatientTimelineGraphLine> lines)
    {
        EndPan();
        var offset = Scroller.Offset;
        _layout = PatientTimelineGraphLayout.Build(lines);
        _surface.SetLayout(_layout);
        _canvas.Children.Clear();
        _canvas.Children.Add(_surface);
        _buttons.Clear();
        _routeButtons.Clear();
        foreach (var edge in _layout.Edges) { AddConnectionCaption(edge); }
        foreach (var node in _layout.Nodes)
        {
            var button = CreateNode(node);
            _buttons.Add((node, button));
            _canvas.Children.Add(button);
            Canvas.SetLeft(button, node.Bounds.X);
            Canvas.SetTop(button, node.Bounds.Y);
        }
        if (_layout.Nodes.Count == 0)
        {
            var empty = new TextBlock { Foreground = DesktopFluentStyle.SecondaryText, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
            _localization.Bind(empty, TextBlock.TextProperty, "timeline.graph.empty");
            _canvas.Children.Add(empty);
            Canvas.SetLeft(empty, PatientTimelineGraphLayout.Padding);
            Canvas.SetTop(empty, PatientTimelineGraphLayout.Padding);
        }
        UpdateExtent();
        if (_selectedLine is { } lineId) { Select(lineId, _selectedKey); }
        Scroller.Offset = offset;
        if (!_receivedLines && _layout.Nodes.Count > 0)
        {
            _receivedLines = true;
            _fitPending = true;
            _fitReadable = true;
            Dispatcher.UIThread.Post(() => { if (_fitPending) { FitView(_fitReadable); } }, DispatcherPriority.Loaded);
        }
    }

    // A replaced program opens at its start instead of inheriting the previous program's view.
    internal void ScheduleInitialView() => _receivedLines = false;

    internal void Select(string lineId, object? eventKey)
    {
        _selectedLine = lineId;
        _selectedKey = eventKey;
        foreach (var item in _buttons)
        {
            bool selected = IsSelected(item.Node);
            item.Button.Background = selected ? SelectedBrush : Brushes.White;
            item.Button.BorderBrush = !item.Node.IsValid ? InvalidBrush : selected ? DesktopFluentStyle.Accent : DesktopFluentStyle.Stroke;
            item.Button.BorderThickness = new Thickness(selected || !item.Node.IsValid ? 2 : 1);
        }
        _surface.Select(lineId, eventKey);
    }

    internal void FocusSelection()
    {
        var item = _buttons.FirstOrDefault(item => IsSelected(item.Node));
        if (item.Button is null) { return; }
        item.Button.Focus();
        UpdateLayout();
        Reveal(item.Node);
    }

    // Scrolls to the selection while keyboard focus stays with the command that changed it.
    internal void RevealSelection()
    {
        var item = _buttons.FirstOrDefault(item => IsSelected(item.Node));
        if (item.Button is null || !IsEffectivelyVisible) { return; }
        UpdateLayout();
        Reveal(item.Node);
    }

    internal void Fit() => FitView(readable: false);

    private void FitView(bool readable)
    {
        double width = Scroller.Viewport.Width;
        double height = Scroller.Viewport.Height;
        if (width <= 1 || height <= 1)
        {
            _fitPending = true;
            _fitReadable = readable;
            return;
        }
        _fitPending = false;
        double scale = Math.Min(1, Math.Min((width - 16) / _layout.Size.Width, (height - 16) / _layout.Size.Height));
        ApplyZoom(Math.Max(readable ? ReadableZoom : 0.001, scale));
        Scroller.Offset = default;
    }

    internal void ResetView()
    {
        _fitPending = false;
        ApplyZoom(1);
        Scroller.Offset = default;
    }

    private Button ToolbarButton(string key, Action action, string? symbol = null)
    {
        var button = new Button { Padding = new Thickness(12, 0), MinHeight = 40, MinWidth = 40 };
        if (symbol is null) { _localization.Bind(button, ContentControl.ContentProperty, key); }
        else { button.Content = symbol; }
        _localization.Bind(button, AutomationProperties.NameProperty, key);
        _localization.Bind(button, ToolTip.TipProperty, key);
        button.Click += (_, _) => action();
        return button;
    }

    private Button CreateNode(PatientTimelineGraphNode node)
    {
        var button = new Button
        {
            Width = node.Bounds.Width,
            Height = node.Bounds.Height,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(14, 11),
            CornerRadius = new CornerRadius(9),
            Background = Brushes.White,
            BorderBrush = node.IsValid ? DesktopFluentStyle.Stroke : InvalidBrush,
            BorderThickness = new Thickness(node.IsValid ? 1 : 2)
        };
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        var title = new TextBlock
        {
            Text = node.Event?.Name ?? node.Line.Name,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = DesktopFluentStyle.Text
        };
        panel.Children.Add(title);
        var time = new TextBlock { FontSize = 12, Foreground = DesktopFluentStyle.Accent, Margin = new Thickness(0, 5, 0, 5) };
        _localization.Bind(time, TextBlock.TextProperty, text => TimeCaption(text, node));
        Grid.SetRow(time, 1);
        panel.Children.Add(time);
        var summary = new TextBlock
        {
            Text = node.Event?.Summary ?? node.Line.EntrySummary,
            FontSize = 12,
            Foreground = DesktopFluentStyle.SecondaryText,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 3
        };
        if (node.Event is null && string.IsNullOrEmpty(node.Line.EntrySummary))
        { _localization.Bind(summary, TextBlock.TextProperty, node.Line.IsInitial ? "timeline.graph.initial" : "timeline.graph.branch"); }
        Grid.SetRow(summary, 2);
        panel.Children.Add(summary);
        var status = new TextBlock { FontSize = 11, Foreground = node.IsValid ? DesktopFluentStyle.SecondaryText : InvalidBrush, Margin = new Thickness(0, 5, 0, 0) };
        _localization.Bind(status, TextBlock.TextProperty, text =>
        {
            if (!node.IsValid) { return text.GetString("timeline.graph.invalid"); }
            if (node.Event is null) { return text.GetString("timeline.graph.entry"); }
            return node.Event.DurationSeconds is > 0
                ? text.Format("timeline.graph.duration", Seconds(node.Event.DurationSeconds.Value))
                : text.GetString("timeline.graph.instant");
        });
        Grid.SetRow(status, 3);
        panel.Children.Add(status);
        button.Content = panel;
        _localization.Bind(button, AutomationProperties.NameProperty, text =>
            $"{node.Line.Name}: {node.Event?.Name ?? text.GetString("timeline.graph.entry")}, {TimeCaption(text, node)}, {node.Event?.Summary ?? node.Line.EntrySummary}");
        _localization.Bind(button, ToolTip.TipProperty, text =>
            $"{node.Event?.Name ?? node.Line.Name}\n{TimeCaption(text, node)}\n{node.Event?.Summary ?? node.Line.EntrySummary}");
        button.Click += (_, _) => Choose(node);
        button.KeyDown += (_, e) => Navigate(e, node);
        return button;
    }

    private void AddConnectionCaption(PatientTimelineGraphEdge edge)
    {
        if (edge.Kind is { } kind)
        {
            string key = kind switch
            {
                PatientTimelineGraphRouteKind.ShockSuccess => "timeline.graph.routeShockSuccess",
                PatientTimelineGraphRouteKind.ShockFailure => "timeline.graph.routeShockFailure",
                _ => "timeline.graph.routePacing"
            };
            var label = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = PatientTimelineGraphSurface.RouteBrush(kind) };
            _localization.Bind(label, TextBlock.TextProperty, key);
            var button = new Button
            {
                Content = label,
                MaxWidth = PatientTimelineGraphLayout.ColumnGap - 16,
                Padding = new Thickness(6, 2),
                MinHeight = 22,
                CornerRadius = new CornerRadius(10),
                Background = Brushes.White,
                BorderBrush = PatientTimelineGraphSurface.RouteBrush(kind),
                BorderThickness = new Thickness(1)
            };
            _localization.Bind(button, ToolTip.TipProperty, text => $"{text.GetString(key)}: {edge.Source.Line.Name} / {edge.Source.Event?.Name ?? text.GetString("timeline.graph.entry")} → {edge.Target.Line.Name}");
            _localization.Bind(button, AutomationProperties.NameProperty, text => $"{text.GetString(key)}: {edge.Source.Event?.Name ?? edge.Source.Line.Name} → {edge.Target.Line.Name}");
            button.Click += (_, _) => ChooseRoute(edge);
            _routeButtons.Add(button);
            _canvas.Children.Add(button);
            var caption = _surface.GeometryFor(edge).CaptionOrigin;
            Canvas.SetLeft(button, caption.X);
            Canvas.SetTop(button, caption.Y);
        }
        else if (edge.Target.Event is { } target && (target.IsRelativeToPrevious || target.IsManual))
        {
            var caption = new TextBlock
            {
                FontSize = 11,
                Foreground = DesktopFluentStyle.SecondaryText,
                MaxWidth = PatientTimelineGraphLayout.ColumnGap - 18,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Background = Brush.Parse("#F6F8FB")
            };
            _localization.Bind(caption, TextBlock.TextProperty, text => TimeCaption(text, edge.Target));
            _canvas.Children.Add(caption);
            Canvas.SetLeft(caption, edge.Source.Bounds.Right + 10);
            Canvas.SetTop(caption, edge.Source.Bounds.Center.Y + 26);
        }
    }

    private static string TimeCaption(ITextLocalizer text, PatientTimelineGraphNode node)
    {
        if (node.Event is null) { return text.Format(node.Line.IsInitial ? "timeline.graph.atTime" : "timeline.graph.relativeTime", "0"); }
        if (node.Event.IsManual) { return text.GetString("timeline.graph.manual"); }
        if (node.Event.StartSeconds is not { } seconds) { return text.GetString("timeline.graph.invalid"); }
        string key = node.Event.IsRelativeToPrevious ? "timeline.graph.wait" : node.Line.IsInitial ? "timeline.graph.atTime" : "timeline.graph.relativeTime";
        return text.Format(key, Seconds(seconds));
    }

    private static string Seconds(decimal seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);

    private bool IsSelected(PatientTimelineGraphNode node) => node.Line.Id == _selectedLine && ReferenceEquals(node.Key, _selectedKey);

    private void Choose(PatientTimelineGraphNode node)
    {
        Select(node.Line.Id, node.Key);
        Selected?.Invoke(node.Line.Id, node.Key);
    }

    private void ChooseRoute(PatientTimelineGraphEdge edge)
    {
        Choose(edge.Source);
        RouteSelected?.Invoke(edge.Source.Line.Id, edge.Source.Key);
    }

    private void Navigate(KeyEventArgs e, PatientTimelineGraphNode node)
    {
        if (e.KeyModifiers != KeyModifiers.None) { return; }
        PatientTimelineGraphNode? target = e.Key switch
        {
            Key.Left => _layout.Nodes.FirstOrDefault(candidate => candidate.Row == node.Row && candidate.Column == node.Column - 1),
            Key.Right => _layout.Nodes.FirstOrDefault(candidate => candidate.Row == node.Row && candidate.Column == node.Column + 1),
            Key.Up => _layout.Nodes.Where(candidate => candidate.Row == node.Row - 1).MinBy(candidate => Math.Abs(candidate.Bounds.X - node.Bounds.X)),
            Key.Down => _layout.Nodes.Where(candidate => candidate.Row == node.Row + 1).MinBy(candidate => Math.Abs(candidate.Bounds.X - node.Bounds.X)),
            _ => null
        };
        if (target is null) { return; }
        e.Handled = true;
        Choose(target);
        FocusSelection();
    }

    private void Reveal(PatientTimelineGraphNode node)
    {
        var bounds = node.Bounds;
        double x = bounds.X * Zoom;
        double y = bounds.Y * Zoom;
        double offsetX = Scroller.Offset.X;
        double offsetY = Scroller.Offset.Y;
        bool hasRoutes = (node.Event?.Routes ?? node.Line.EntryRoutes).Count > 0;
        double right = bounds.Right + (hasRoutes ? PatientTimelineGraphLayout.ColumnGap : 0);
        if ((right - bounds.Left) * Zoom + 32 > Scroller.Viewport.Width) { right = bounds.Right; }
        if (x < offsetX + 16) { offsetX = Math.Max(0, x - 16); }
        else if (right * Zoom > offsetX + Scroller.Viewport.Width - 16) { offsetX = right * Zoom - Scroller.Viewport.Width + 16; }
        if (y < offsetY + 16) { offsetY = Math.Max(0, y - 16); }
        else if (bounds.Bottom * Zoom > offsetY + Scroller.Viewport.Height - 16) { offsetY = bounds.Bottom * Zoom - Scroller.Viewport.Height + 16; }
        Scroller.Offset = new Vector(Math.Max(0, offsetX), Math.Max(0, offsetY));
    }

    private Point ViewportCenter() => new(Scroller.Viewport.Width / 2, Scroller.Viewport.Height / 2);

    private void Wheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ZoomAt(Zoom * Math.Pow(1.15, e.Delta.Y), e.GetPosition(Scroller));
        }
        else
        {
            double x = e.Delta.X;
            double y = e.Delta.Y;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || Scroller.Extent.Height <= Scroller.Viewport.Height + 1)
            {
                x += y;
                y = 0;
            }
            Scroller.Offset = new Vector(Scroller.Offset.X - x * 64, Scroller.Offset.Y - y * 64);
        }
        e.Handled = true;
    }

    private void ZoomAt(double scale, Point pointer)
    {
        _fitPending = false;
        var oldOffset = Scroller.Offset;
        var world = new Point((oldOffset.X + pointer.X) / Zoom, (oldOffset.Y + pointer.Y) / Zoom);
        ApplyZoom(Math.Clamp(scale, 0.001, 2.5));
        Scroller.Offset = new Vector(world.X * Zoom - pointer.X, world.Y * Zoom - pointer.Y);
    }

    private void ApplyZoom(double scale)
    {
        EndPan();
        Zoom = scale;
        _scaled.LayoutTransform = new ScaleTransform(Zoom, Zoom);
        UpdateExtent();
        UpdateLayout();
        RefreshZoomLabel();
    }

    private void UpdateExtent()
    {
        double width = Math.Max(_layout.Size.Width, Scroller.Viewport.Width / Zoom);
        double height = Math.Max(_layout.Size.Height, Scroller.Viewport.Height / Zoom);
        _canvas.Width = width;
        _canvas.Height = height;
        _surface.Width = width;
        _surface.Height = height;
    }

    private void RefreshZoomLabel() => _localization.Bind(_zoomLabel, TextBlock.TextProperty, "timeline.graph.zoomPercent", Math.Round(Zoom * 100, 1));

    private void BeginPan(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed) { return; }
        _pressedEdge = _surface.HitRoute(e.GetPosition(_surface), 8 / Zoom);
        _panOrigin = e.GetPosition(Scroller);
        _panOffset = Scroller.Offset;
        _panMoved = false;
        _panPointer = e.Pointer;
        e.Pointer.Capture(_surface);
        _surface.Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    private void MovePan(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(_panPointer, e.Pointer)) { return; }
        var point = e.GetPosition(Scroller);
        var delta = new Vector(_panOrigin.X - point.X, _panOrigin.Y - point.Y);
        _panMoved |= delta.Length > 4;
        if (_panMoved) { Scroller.Offset = _panOffset + delta; }
        e.Handled = true;
    }

    private void FinishPan(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(_panPointer, e.Pointer)) { return; }
        var route = _panMoved ? null : _pressedEdge;
        EndPan();
        if (route is not null) { ChooseRoute(route); }
        e.Handled = true;
    }

    private void EndPan()
    {
        var pointer = _panPointer;
        _panPointer = null;
        _pressedEdge = null;
        pointer?.Capture(null);
        _surface.Cursor = new Cursor(StandardCursorType.Hand);
    }
}
