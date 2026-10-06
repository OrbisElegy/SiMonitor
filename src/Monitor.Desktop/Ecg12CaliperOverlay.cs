// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

// Manual point measurement drawn over the frozen twelve-lead paper in paper
// coordinates. A dot follows the pointer along the trace; clicks place the
// first and second points, and the horizontal and vertical legs between them
// show Δt and ΔV. Arrow keys move the latest point by one sample (Shift: ten);
// Escape clears. Nothing is detected automatically.
internal sealed class Ecg12CaliperOverlay : Control
{
    private static readonly Color Accent = Color.Parse("#0067C0");
    private static readonly IBrush Marker = new SolidColorBrush(Accent);
    private IPointer? _pointer;
    internal double VisualScale { get; set; } = 1;

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
        CancelDrag();
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
        if (ReferenceEquals(_pointer, e.Pointer))
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { CancelDrag(); }
            else if (Measurement.DragTo(e.GetPosition(this).X)) { Refresh(); }
            e.Handled = true;
            return;
        }
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
        var point = e.GetPosition(this);
        if (Measurement.BeginDrag(point.X, point.Y, 12 / VisualScale))
        {
            _pointer = e.Pointer;
            _pointer.Capture(this);
            Refresh();
            e.Handled = true;
        }
        else if (Place(point)) { e.Handled = true; }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!ReferenceEquals(_pointer, e.Pointer) || e.InitialPressMouseButton != MouseButton.Left) { return; }
        Measurement.DragTo(e.GetPosition(this).X);
        CancelDrag();
        Refresh();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelDrag();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelDrag();
        base.OnDetachedFromVisualTree(e);
    }

    internal void CancelDrag()
    {
        var pointer = _pointer;
        _pointer = null;
        Measurement.EndDrag();
        if (ReferenceEquals(pointer?.Captured, this)) { pointer.Capture(null); }
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
        if (display is { Start: { } start, End: { } end }) { DrawLegs(context, start, end); }
        else if (display is { Start: { } anchor, Hover: { } preview, HoverResult: not null })
        {
            var (first, second) = anchor.TimeNs <= preview.TimeNs ? (anchor, preview) : (preview, anchor);
            DrawLegs(context, first, second);
        }
        foreach (var point in new[] { display.Start, display.End })
        {
            if (point is not null) { context.DrawEllipse(Marker, null, new Point(point.X, point.Y), 5 / VisualScale, 5 / VisualScale); }
        }
        if (display.Hover is { } hover) { context.DrawEllipse(null, new Pen(Marker, 1.5 / VisualScale), new Point(hover.X, hover.Y), 7 / VisualScale, 7 / VisualScale); }
    }

    // Horizontal leg at the earlier point's level and vertical leg at the later point's time.
    private void DrawLegs(DrawingContext context, Ecg12PaperCursor first, Ecg12PaperCursor second)
    {
        var pen = new Pen(Marker, 1.5 / VisualScale, new DashStyle([3, 3], 0));
        var corner = new Point(second.X, first.Y);
        context.DrawLine(pen, new Point(first.X, first.Y), corner);
        context.DrawLine(pen, corner, new Point(second.X, second.Y));
    }

    private void Refresh()
    {
        InvalidateVisual();
        Changed?.Invoke();
    }
}
