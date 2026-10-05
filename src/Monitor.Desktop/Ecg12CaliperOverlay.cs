// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

// Manual calipers drawn over the frozen twelve-lead paper in paper coordinates.
// Pointer drags place both ends on one lead; arrow keys move the moving end by
// one sample (Shift: ten); Escape clears. Nothing is detected automatically.
internal sealed class Ecg12CaliperOverlay : Control
{
    private static readonly IPen CursorPen = new Pen(new SolidColorBrush(Color.Parse("#0067C0")), 1.5);
    private static readonly IPen LevelPen = new Pen(new SolidColorBrush(Color.Parse("#0067C0")), 1, new DashStyle([3, 3], 0));
    private static readonly IBrush Band = new SolidColorBrush(Color.Parse("#0067C0"), .08);
    private static readonly IBrush Marker = new SolidColorBrush(Color.Parse("#0067C0"));
    private bool _dragging;

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

    internal bool Press(Point point)
    {
        _dragging = Measurement.Begin(point.X, point.Y);
        Refresh();
        return _dragging;
    }

    internal void Drag(Point point)
    {
        if (_dragging && Measurement.Extend(point.X)) { Refresh(); }
    }

    internal void Release() => _dragging = false;

    internal void Clear()
    {
        _dragging = false;
        Measurement.Clear();
        Refresh();
    }

    internal void Nudge(int samples)
    {
        if (Measurement.Nudge(samples)) { Refresh(); }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { return; }
        Focus();
        if (Press(e.GetPosition(this)))
        {
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Drag(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        Release();
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        Release();
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
        // A transparent fill keeps the whole paper hit-testable for new drags.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var display = Measurement.Display;
        if (display is not { Region: { } region, Start: { } start, End: { } end }) { return; }
        context.FillRectangle(Band, new Rect(start.X, region.Top, Math.Max(end.X - start.X, 1), region.Bottom - region.Top));
        foreach (var cursor in new[] { start, end })
        {
            context.DrawLine(CursorPen, new Point(cursor.X, region.Top), new Point(cursor.X, region.Bottom));
            context.DrawLine(LevelPen, new Point(region.Left, cursor.Y), new Point(region.Right, cursor.Y));
            context.DrawEllipse(Marker, null, new Point(cursor.X, cursor.Y), 3, 3);
        }
    }

    private void Refresh()
    {
        InvalidateVisual();
        Changed?.Invoke();
    }
}
