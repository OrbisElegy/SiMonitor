// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Desktop;

// Manual point measurement drawn over the frozen twelve-lead paper in paper
// coordinates. A dot follows the pointer along the trace; clicks place the
// first and second points, and the horizontal and vertical legs between them
// show Δt and ΔV. Arrow keys move the latest point by one sample (Shift: ten);
// Escape clears. Nothing is detected automatically.
internal sealed class Ecg12CaliperOverlay : Control
{
    private static readonly Color Accent = Color.Parse("#0067C0");
    private static readonly IPen LegPen = new Pen(new SolidColorBrush(Accent), 1.2, new DashStyle([3, 3], 0));
    private static readonly IPen HoverPen = new Pen(new SolidColorBrush(Accent), 1.5);
    private static readonly IBrush Marker = new SolidColorBrush(Accent);
    private static readonly IBrush LabelBackground = new SolidColorBrush(Colors.White, .85);

    internal Ecg12CaliperOverlay(Ecg12PaperMeasurement measurement)
    {
        Measurement = measurement;
        Width = measurement.Layout.Width;
        Height = measurement.Layout.Height;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    internal Ecg12PaperMeasurement Measurement { get; }
    internal event Action? Changed;

    internal void Move(Point point)
    {
        if (Measurement.Hover(point.X, point.Y)) { Refresh(); }
    }

    internal bool Place(Point point)
    {
        bool placed = Measurement.Place(point.X, point.Y);
        if (placed) { Refresh(); }
        return placed;
    }

    internal void Leave()
    {
        Measurement.EndHover();
        Refresh();
    }

    internal void Clear()
    {
        Measurement.Clear();
        Refresh();
    }

    internal void Nudge(int samples)
    {
        if (Measurement.Nudge(samples)) { Refresh(); }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Move(e.GetPosition(this));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Leave();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { return; }
        Focus();
        if (Place(e.GetPosition(this))) { e.Handled = true; }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Left:
                Nudge(-step);
                e.Handled = true;
                break;
            case Key.Right:
                Nudge(step);
                e.Handled = true;
                break;
            case Key.Escape:
                Clear();
                e.Handled = true;
                break;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // A transparent fill keeps the whole paper hit-testable.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var display = Measurement.Display;
        if (display is { Start: { } start, End: { } end, Result: { } result }) { DrawLegs(context, start, end, result); }
        else if (display is { Start: { } anchor, Hover: { } preview, HoverResult: { } previewResult })
        {
            var (first, second) = anchor.TimeNs <= preview.TimeNs ? (anchor, preview) : (preview, anchor);
            DrawLegs(context, first, second, previewResult);
        }
        foreach (var point in new[] { display.Start, display.End })
        {
            if (point is not null) { context.DrawEllipse(Marker, null, new Point(point.X, point.Y), 3.5, 3.5); }
        }
        if (display.Hover is { } hover) { context.DrawEllipse(null, HoverPen, new Point(hover.X, hover.Y), 5, 5); }
    }

    // Horizontal leg at the earlier point's level and vertical leg at the later point's time.
    private static void DrawLegs(DrawingContext context, Ecg12PaperCursor first, Ecg12PaperCursor second, EcgManualMeasurementResult result)
    {
        var corner = new Point(second.X, first.Y);
        context.DrawLine(LegPen, new Point(first.X, first.Y), corner);
        context.DrawLine(LegPen, corner, new Point(second.X, second.Y));
        Label(context, "Δt " + MeasurementReadout.Exact(result.ElapsedMilliseconds) + " ms",
            new Point((first.X + second.X) / 2, first.Y + 4), centered: true);
        Label(context, "ΔV " + MeasurementReadout.Exact(result.AmplitudeChangeMillivolts) + " mV",
            new Point(second.X + 6, (first.Y + second.Y) / 2 - 7), centered: false);
    }

    private static void Label(DrawingContext context, string text, Point origin, bool centered)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(DesignPreviewWindow.PreviewFont), 11, Marker);
        var position = centered ? new Point(origin.X - formatted.Width / 2, origin.Y) : origin;
        context.FillRectangle(LabelBackground, new Rect(position.X - 2, position.Y - 1, formatted.Width + 4, formatted.Height + 2), 2);
        context.DrawText(formatted, position);
    }

    private void Refresh()
    {
        InvalidateVisual();
        Changed?.Invoke();
    }
}
