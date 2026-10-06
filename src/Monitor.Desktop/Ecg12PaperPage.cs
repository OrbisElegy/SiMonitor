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
using Monitor.Application.Localization;
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Desktop;

// The fixed toolbar belongs to the waveform area; only the paper zooms and scrolls.
internal sealed class Ecg12PaperPage : Grid
{
    private readonly DesktopLocalization _localization;
    private readonly Viewbox _viewbox;
    private readonly Canvas _surface = new();
    private readonly Border _panSurface = new() { Background = Brushes.Transparent, IsHitTestVisible = false };
    private IPointer? _panPointer;
    private Point _panOrigin;
    private Vector _panOffset;
    private readonly TextBlock _zoom = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 42 };
    private bool _millimeters;
    private bool _fit = true;
    private bool _scaling;

    internal Ecg12PaperPage(DesignPreviewTrace paper, Ecg12PaperMeasurement measurement, DesktopLocalization localization, bool measuring, bool millimeters = false)
    {
        _localization = localization;
        _millimeters = millimeters;
        Paper = paper;
        Overlay = new Ecg12CaliperOverlay(measurement);
        ClipToBounds = true;
        var calibration = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
        localization.Bind(calibration, TextBlock.TextProperty, paper.SixRows ? "paper.headerSixRows" : "paper.headerThreeRows");
        localization.Bind(Scroller, AutomationProperties.NameProperty, "ecg12.viewport");
        localization.Bind(Overlay, AutomationProperties.NameProperty, "ecg12.overlay");
        Icon(Calipers, "ecg12.measure", "M2,2 L14,2 L14,14 L2,14 Z M5,2 L5,6 M8,2 L8,5 M11,2 L11,6 M2,8 L6,8 M2,11 L5,11");
        Icon(ClearCalipers, "ecg12.clear", "M2,4 L14,4 M6,4 L6,2 L10,2 L10,4 M4,4 L5,14 L11,14 L12,4 M7,6 L7,12 M9,6 L9,12");
        Icon(Hand, "ecg12.pan", "M5,8 L5,3 Q5,1 7,2 L7,7 L7,2 Q8,0 9,2 L9,7 L9,3 Q11,1 11,3 L11,8 L11,5 Q13,3 13,6 L13,10 Q13,15 9,15 L7,15 Q5,15 4,12 L2,9 Q1,7 3,7 Z");
        Icon(Fit, "ecg12.fit", "M1,6 L1,1 L6,1 M10,1 L15,1 L15,6 M15,10 L15,15 L10,15 M6,15 L1,15 L1,10 M5,5 L11,5 L11,11 L5,11 Z");
        Icon(ActualSize, "ecg12.actualSize", "M1,6 L3,4 L3,12 M7,6 L7,7 M7,10 L7,11 M10,6 L12,4 L12,12");
        Calipers.IsChecked = measuring && measurement.CanMeasure;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var control in new Control[] { _zoom, ActualSize, Fit, Hand, Calipers, ClearCalipers }) { actions.Children.Add(control); }
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("1*,1.2*,Auto") };
        header.Children.Add(calibration);
        SetColumn(Readout, 1);
        header.Children.Add(Readout);
        SetColumn(actions, 2);
        header.Children.Add(actions);
        var toolbar = new Border
        {
            Child = header,
            Background = Brush.Parse("#EDFFFFFF"),
            BorderBrush = Brush.Parse("#DDE1E5"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Top
        };
        var canvas = new Panel { Width = paper.Width, Height = paper.Height };
        canvas.Children.Add(paper);
        canvas.Children.Add(Overlay);
        canvas.Children.Add(_panSurface);
        _viewbox = new Viewbox { Stretch = Stretch.Fill, Child = canvas };
        _surface.Children.Add(_viewbox);
        Scroller.Content = _surface;
        Children.Add(Scroller);
        Children.Add(toolbar);
        _panSurface.PointerPressed += PanPressed;
        _panSurface.PointerMoved += PanMoved;
        _panSurface.PointerReleased += PanReleased;
        _panSurface.PointerCaptureLost += (_, _) => EndPan();
        DetachedFromVisualTree += (_, _) => EndPan();
        Scroller.ScrollChanged += (_, e) =>
        {
            if (e.ViewportDelta != default && !_scaling) { ResizePaper(); }
        };
        Scroller.AddHandler(PointerWheelChangedEvent, Wheel, RoutingStrategies.Tunnel);
        Fit.Click += (_, _) => { EndPan(); _fit = true; ResizePaper(); };
        ActualSize.Click += (_, _) => ZoomAt(1, new Point(Scroller.Viewport.Width / 2, Scroller.Viewport.Height / 2));
        Hand.IsCheckedChanged += (_, _) =>
        {
            EndPan();
            if (Hand.IsChecked == true) { Calipers.IsChecked = false; Overlay.CancelDrag(); Overlay.Leave(); }
            Refresh();
        };
        Calipers.IsCheckedChanged += (_, _) =>
        {
            if (Calipers.IsChecked == true) { Hand.IsChecked = false; }
            else { Overlay.CancelDrag(); Overlay.Leave(); }
            Refresh();
            if (Calipers.IsChecked == true) { Overlay.Focus(); }
            MeasuringChanged?.Invoke(Calipers.IsChecked == true);
        };
        ClearCalipers.Click += (_, _) => { Overlay.Clear(); Overlay.Focus(); };
        Overlay.Changed += Refresh;
        Refresh();
    }

    internal Control PanSurface => _panSurface;
    internal DesignPreviewTrace Paper { get; }
    internal Ecg12CaliperOverlay Overlay { get; }
    internal ToggleButton Calipers { get; } = new();
    internal ToggleButton Hand { get; } = new();
    internal Button ClearCalipers { get; } = new();
    internal Button Fit { get; } = new();
    internal Button ActualSize { get; } = new();
    internal TextBlock Readout { get; } = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) };
    internal ScrollViewer Scroller { get; } = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = Brushes.White
    };
    internal double Zoom { get; private set; } = 1;
    internal event Action<bool>? MeasuringChanged;

    private void ResizePaper()
    {
        var viewport = Scroller.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0) { return; }
        double scale = _fit ? Math.Min(viewport.Width / Paper.Width, viewport.Height / Paper.Height) : Zoom;
        ApplyScale(scale);
        if (_fit) { Scroller.Offset = default; }
    }

    private void ApplyScale(double scale)
    {
        _scaling = true;
        try
        {
            Zoom = Math.Clamp(scale, .1, 4);
            _viewbox.Width = Paper.Width * Zoom;
            _viewbox.Height = Paper.Height * Zoom;
            _surface.Width = Math.Max(Scroller.Viewport.Width, _viewbox.Width);
            _surface.Height = Math.Max(Scroller.Viewport.Height, _viewbox.Height);
            Canvas.SetLeft(_viewbox, (_surface.Width - _viewbox.Width) / 2);
            Canvas.SetTop(_viewbox, (_surface.Height - _viewbox.Height) / 2);
            Overlay.VisualScale = Zoom;
            Overlay.InvalidateVisual();
            _zoom.Text = Zoom.ToString("0%", CultureInfo.InvariantCulture);
            UpdateLayout();
        }
        finally { _scaling = false; }
    }

    private void Icon(Button button, string key, string geometry)
    {
        button.Width = 32;
        button.Height = 32;
        button.Padding = new Thickness(6);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        var icon = new Avalonia.Controls.Shapes.Path { Width = 16, Height = 16, StrokeThickness = 1.4, Data = Geometry.Parse(geometry) };
        icon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, new Avalonia.Data.Binding("Foreground") { Source = button });
        button.Content = icon;
        _localization.Bind(button, AutomationProperties.NameProperty, key);
        _localization.Bind(button, ToolTip.TipProperty, key);
    }

    private void ZoomAt(double scale, Point pointer)
    {
        EndPan();
        var anchor = Scroller.TranslatePoint(pointer, Paper);
        if (anchor is not { } point) { return; }
        _fit = false;
        ApplyScale(scale);
        Scroller.Offset = new Vector(
            Canvas.GetLeft(_viewbox) + point.X * Zoom - pointer.X,
            Canvas.GetTop(_viewbox) + point.Y * Zoom - pointer.Y);
        UpdateLayout();
    }

    private void Wheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0) { return; }
        ZoomAt(Zoom * Math.Pow(1.15, e.Delta.Y), e.GetPosition(Scroller));
        e.Handled = true;
    }

    private void PanPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Hand.IsChecked != true || !e.GetCurrentPoint(Scroller).Properties.IsLeftButtonPressed) { return; }
        _panOrigin = e.GetPosition(Scroller);
        _panOffset = Scroller.Offset;
        _panPointer = e.Pointer;
        e.Pointer.Capture(_panSurface);
        _panSurface.Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Handled = true;
    }

    private void PanMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(_panPointer, e.Pointer)) { return; }
        if (!e.GetCurrentPoint(Scroller).Properties.IsLeftButtonPressed) { EndPan(); return; }
        PanTo(e.GetPosition(Scroller));
        e.Handled = true;
    }

    private void PanTo(Point point)
    {
        Scroller.Offset = new Vector(_panOffset.X + _panOrigin.X - point.X, _panOffset.Y + _panOrigin.Y - point.Y);
    }

    private void PanReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(_panPointer, e.Pointer) || e.InitialPressMouseButton != MouseButton.Left) { return; }
        PanTo(e.GetPosition(Scroller));
        EndPan();
        e.Handled = true;
    }

    private void EndPan()
    {
        var pointer = _panPointer;
        _panPointer = null;
        if (ReferenceEquals(pointer?.Captured, _panSurface)) { pointer.Capture(null); }
        _panSurface.Cursor = new Cursor(StandardCursorType.Hand);
    }

    private void Refresh()
    {
        var measurement = Overlay.Measurement;
        bool measuring = Calipers.IsChecked == true && measurement.CanMeasure;
        Calipers.IsEnabled = measurement.CanMeasure;
        Overlay.IsHitTestVisible = measuring;
        _panSurface.IsHitTestVisible = Hand.IsChecked == true;
        _panSurface.Cursor = new Cursor(StandardCursorType.Hand);
        ClearCalipers.IsEnabled = measurement.Display.Region is not null;
        _localization.Bind(Readout, TextBlock.TextProperty, text => Describe(text, measurement.Display, measuring, _millimeters));
    }

    internal void SetMeasurementUnits(bool millimeters)
    {
        _millimeters = millimeters;
        Refresh();
    }

    internal static string Describe(ITextLocalizer text, Ecg12PaperMeasurementDisplay display, bool measuring, bool millimeters = false)
    {
        if (display.ReasonCode == "Ecg12Measurement.Disabled") { return text.GetString("ecg12.disabled"); }
        if (display.ReasonCode == "Ecg12Measurement.CourseLocked") { return text.GetString("ecg12.courseLocked"); }
        if (display.Region is not { } region) { return measuring ? text.GetString("ecg12.idle") : ""; }
        string lead = region.Lead == Ecg12PaperLayout.LongLeadIndex
            ? text.GetString("ecg12.rhythmLead")
            : ProjectedEcgDemoSource.LeadNames[region.Lead];
        var result = display.Result ?? display.HoverResult;
        if (result is null || (display.End is null && result.ElapsedMilliseconds.Numerator == 0)) { return text.Format("ecg12.placing", lead); }
        string readout;
        if (millimeters)
        {
            // Fixed paper calibration: 25 mm/s and 10 mm/mV, independent of display zoom.
            string horizontal = MeasurementReadout.Exact(new(result.ElapsedMilliseconds.Numerator,
                result.ElapsedMilliseconds.Denominator * 40));
            string vertical = MeasurementReadout.Exact(new(result.AmplitudeChangeMillivolts.Numerator * 10,
                result.AmplitudeChangeMillivolts.Denominator));
            readout = text.Format("ecg12.resultMillimeters", lead, horizontal, vertical);
        }
        else
        {
            string elapsed = MeasurementReadout.Exact(result.ElapsedMilliseconds);
            string amplitude = MeasurementReadout.Exact(result.AmplitudeChangeMillivolts);
            readout = result.AuxiliaryRatePerMinute is { } rate
                ? text.Format("ecg12.resultRate", lead, elapsed, amplitude, Rate(rate))
                : text.Format("ecg12.result", lead, elapsed, amplitude);
        }
        return display.Result is null ? text.Format("ecg12.preview", readout) : readout;
    }

    // Rates rarely divide evenly; show one decimal and mark rounding explicitly.
    internal static string Rate(EcgMeasurementRatio rate)
    {
        string exact = MeasurementReadout.Exact(rate);
        int point = exact.IndexOf('.', StringComparison.Ordinal);
        if (!exact.Contains('/', StringComparison.Ordinal) && (point < 0 || exact.Length - point <= 2)) { return exact; }
        double value = (double)rate.Numerator / (double)rate.Denominator;
        return "≈" + value.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
