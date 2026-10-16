// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace Monitor.Desktop;

// One circular input surface serves both a bounded selector and a pressable
// navigation encoder. Requested positions become visible only after acceptance
// by the owner; a rejected mode change never moves the pointer speculatively.
internal sealed class DeviceRotaryDial : Control, ICustomHitTest
{
    private const double DragThreshold = 5;
    private const double EncoderStepDegrees = 24;
    private readonly MonitorSkinMaterial _material;
    private readonly bool _pressable;
    private double[] _positions = [];
    private IPointer? _pointer;
    private Point _pressPoint;
    private double? _lastPointerAngle;
    private double _dragAngleDegrees;
    private double _encoderRemainderDegrees;
    private double _wheelRemainder;
    private int? _lastDragRequest;
    private Key? _pressKey;
    private bool _dragged;
    private bool _startedInCenter;
    private bool _hovered;

    internal int SelectedPosition { get; private set; }
    internal double AngleDegrees { get; private set; }
    internal event Action<int>? Rotated;
    internal event Action? Pressed;
    internal event Action<int>? SelectedPositionRequested;

    internal DeviceRotaryDial(DesktopLocalization localization, MonitorSkinMaterial material, string nameKey, bool pressable)
    {
        Width = Height = 106;
        Focusable = true;
        FocusAdorner = null;
        Cursor = new Cursor(StandardCursorType.Hand);
        _material = material;
        _pressable = pressable;
        localization.Bind(this, AutomationProperties.NameProperty, nameKey);
    }

    // Angles increase clockwise from twelve o'clock. The unused arc separates
    // the mechanical end stops; dragging across it clamps instead of wrapping.
    internal void ConfigurePositions(IReadOnlyList<double> angles, int selectedPosition)
    {
        ArgumentNullException.ThrowIfNull(angles);
        if (angles.Count == 0) { throw new ArgumentException("At least one position is required.", nameof(angles)); }
        if ((uint)selectedPosition >= (uint)angles.Count) { throw new ArgumentOutOfRangeException(nameof(selectedPosition)); }
        double[] positions = angles.ToArray();
        for (int index = 0; index < positions.Length; index++)
        {
            if (!double.IsFinite(positions[index]) || (index > 0 && positions[index] <= positions[index - 1]))
            { throw new ArgumentException("Position angles must be finite and strictly increasing.", nameof(angles)); }
        }
        if (positions[^1] - positions[0] >= 360)
        { throw new ArgumentException("Position angles must span less than a complete turn.", nameof(angles)); }
        CancelInteraction();
        _positions = positions;
        SetSelectedPosition(selectedPosition);
    }

    internal void SetSelectedPosition(int selectedPosition)
    {
        if ((uint)selectedPosition >= (uint)_positions.Length) { throw new ArgumentOutOfRangeException(nameof(selectedPosition)); }
        SelectedPosition = selectedPosition;
        SetAngle(_positions[selectedPosition]);
    }

    internal void SetAngle(double degrees)
    {
        if (!double.IsFinite(degrees)) { throw new ArgumentOutOfRangeException(nameof(degrees)); }
        AngleDegrees = degrees;
        InvalidateVisual();
    }

    // The composition hit tester has already inverted all ancestor transforms,
    // including the enclosure Viewbox, before calling this method.
    bool ICustomHitTest.HitTest(Point point) => Contains(point);

    protected override AutomationPeer OnCreateAutomationPeer() => new ControlAutomationPeer(this);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double radius = Radius;
        if (radius <= 0) { return; }
        Point center = Center;
        bool pressed = _pointer is not null || _pressKey is not null;
        using var enabled = context.PushOpacity(IsEffectivelyEnabled ? 1 : .45);
        context.DrawEllipse(Brush.Parse("#40000000"), null, center + new Vector(0, 2), radius, radius);
        context.DrawEllipse(pressed ? _material.PressedFill : _material.Fill,
            new Pen(_material.Border, Math.Max(2, _material.BorderThickness)), center, radius, radius);
        if (_hovered && !pressed)
        {
            using var hover = context.PushOpacity(.09);
            context.DrawEllipse(_material.Foreground, null, center, radius - 2, radius - 2);
        }
        using (context.PushOpacity(.3))
        {
            var grip = new Pen(_material.Foreground, 1, lineCap: PenLineCap.Round);
            for (int angle = 0; angle < 360; angle += 15)
            {
                context.DrawLine(grip, AtAngle(center, radius * .85, angle), AtAngle(center, radius * .92, angle));
            }
        }
        double innerRadius = radius * .62;
        using (context.PushOpacity(.4))
        {
            context.DrawEllipse(null, new Pen(_material.Border, 1.5), center, innerRadius, innerRadius);
        }
        if (_pressable && _positions.Length == 0)
        {
            using var centerMark = context.PushOpacity(pressed && !_dragged ? .85 : .45);
            context.DrawEllipse(_material.Foreground, null, center, radius * .075, radius * .075);
        }
        var pointerPen = new Pen(_material.Foreground, Math.Max(3, radius * .085), lineCap: PenLineCap.Round);
        context.DrawLine(pointerPen, AtAngle(center, radius * .44, AngleDegrees), AtAngle(center, radius * .78, AngleDegrees));
        if (IsFocused)
        {
            context.DrawEllipse(null, new Pen(_material.Foreground, 1.5), center, radius - 4, radius - 4);
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        SetHovered(Contains(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHovered(false);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Point point = e.GetPosition(this);
        if (!IsEffectivelyEnabled || _pointer is not null || !Contains(point) ||
            (e.Pointer.Type != PointerType.Touch && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)) { return; }
        Focus();
        _pressKey = null;
        _pointer = e.Pointer;
        _pressPoint = point;
        _dragged = false;
        _startedInCenter = ContainsCenter(point);
        _lastPointerAngle = PointerAngle(point);
        _dragAngleDegrees = _lastPointerAngle is { } angle ? PositionAngle(angle) : AngleDegrees;
        _encoderRemainderDegrees = 0;
        _lastDragRequest = null;
        _pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point point = e.GetPosition(this);
        SetHovered(Contains(point));
        if (!ReferenceEquals(_pointer, e.Pointer)) { return; }
        if (!IsEffectivelyEnabled || (e.Pointer.Type != PointerType.Touch && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed))
        {
            CancelInteraction();
        }
        else { DragTo(point); }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!ReferenceEquals(_pointer, e.Pointer) ||
            (e.Pointer.Type != PointerType.Touch && e.InitialPressMouseButton != MouseButton.Left)) { return; }
        Point point = e.GetPosition(this);
        if (IsEffectivelyEnabled) { DragTo(point); }
        bool activate = IsEffectivelyEnabled && !_dragged && _startedInCenter && ContainsCenter(point) && _pressable && _positions.Length == 0;
        int? position = IsEffectivelyEnabled && !_dragged && Contains(point) && PointerAngle(point) is { } angle && _positions.Length > 0
            ? ClosestPosition(PositionAngle(angle)) : null;
        CancelInteraction();
        if (position is { } selected) { RequestPosition(selected); }
        else if (activate) { Pressed?.Invoke(); }
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelInteraction();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsEffectivelyEnabled || !Contains(e.GetPosition(this))) { return; }
        double delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (!double.IsFinite(delta) || delta == 0) { return; }
        Focus();
        CancelInteraction();
        if (Math.Sign(delta) != Math.Sign(_wheelRemainder)) { _wheelRemainder = 0; }
        _wheelRemainder += Math.Clamp(delta, -32, 32);
        while (Math.Abs(_wheelRemainder) >= 1)
        {
            int direction = Math.Sign(_wheelRemainder);
            _wheelRemainder -= direction;
            Step(direction);
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEffectivelyEnabled) { return; }
        switch (e.Key)
        {
            case Key.Left:
            case Key.Down:
            case Key.Right:
            case Key.Up:
                CancelInteraction();
                Step(e.Key is Key.Right or Key.Up ? 1 : -1);
                e.Handled = true;
                break;
            case Key.Home when _positions.Length > 0:
            case Key.End when _positions.Length > 0:
                CancelInteraction();
                RequestPosition(e.Key == Key.Home ? 0 : _positions.Length - 1);
                e.Handled = true;
                break;
            case Key.Enter when _pressable && _positions.Length == 0:
            case Key.Space when _pressable && _positions.Length == 0:
                if (_pressKey is null && _pointer is null) { _pressKey = e.Key; }
                InvalidateVisual();
                e.Handled = true;
                break;
            case Key.Escape:
                CancelInteraction();
                e.Handled = true;
                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_pressKey != e.Key) { return; }
        _pressKey = null;
        InvalidateVisual();
        if (IsEffectivelyEnabled && IsFocused) { Pressed?.Invoke(); }
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        CancelInteraction();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == IsEffectivelyEnabledProperty && !IsEffectivelyEnabled) ||
            (change.Property == IsVisibleProperty && !IsVisible)) { CancelInteraction(); }
        if (change.Property == IsEffectivelyEnabledProperty) { InvalidateVisual(); }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelInteraction();
        base.OnDetachedFromVisualTree(e);
    }

    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2);
    private double Radius => Math.Max(0, Math.Min(Bounds.Width, Bounds.Height) / 2 - 4);
    private bool Contains(Point point) => Radius > 0 && ((Vector)(point - Center)).Length <= Radius;
    private bool ContainsCenter(Point point) => Radius > 0 && ((Vector)(point - Center)).Length <= Radius * .55;

    private double? PointerAngle(Point point)
    {
        Vector delta = point - Center;
        return delta.Length < Math.Max(5, Radius * .2) ? null : Math.Atan2(delta.X, -delta.Y) * 180 / Math.PI;
    }

    private static Point AtAngle(Point center, double radius, double degrees)
    {
        double radians = degrees % 360 * Math.PI / 180;
        return center + new Vector(Math.Sin(radians) * radius, -Math.Cos(radians) * radius);
    }

    private static double AngularDelta(double degrees) => (degrees % 360 + 540) % 360 - 180;

    private double PositionAngle(double degrees)
    {
        double midpoint = _positions.Length > 0 ? (_positions[0] + _positions[^1]) / 2 : AngleDegrees;
        return midpoint + AngularDelta(degrees - midpoint);
    }

    private void SetHovered(bool hovered)
    {
        if (_hovered == hovered) { return; }
        _hovered = hovered;
        InvalidateVisual();
    }

    private void DragTo(Point point)
    {
        if (!_dragged && ((Vector)(point - _pressPoint)).Length > DragThreshold) { _dragged = true; }
        if (PointerAngle(point) is not { } angle)
        {
            _lastPointerAngle = null;
            return;
        }
        if (_lastPointerAngle is { } previous)
        {
            double delta = AngularDelta(angle - previous);
            _dragAngleDegrees += delta;
            _encoderRemainderDegrees += delta;
        }
        else { _dragAngleDegrees = PositionAngle(angle); }
        _lastPointerAngle = angle;
        if (!_dragged) { return; }
        if (_positions.Length > 0)
        {
            _dragAngleDegrees = Math.Clamp(_dragAngleDegrees, _positions[0], _positions[^1]);
            int position = ClosestPosition(_dragAngleDegrees);
            if (_lastDragRequest == position) { return; }
            _lastDragRequest = position;
            RequestPosition(position);
        }
        else
        {
            while (Math.Abs(_encoderRemainderDegrees) >= EncoderStepDegrees)
            {
                int direction = Math.Sign(_encoderRemainderDegrees);
                _encoderRemainderDegrees -= direction * EncoderStepDegrees;
                Rotated?.Invoke(direction);
            }
        }
    }

    private int ClosestPosition(double degrees)
    {
        int closest = 0;
        for (int index = 1; index < _positions.Length; index++)
        {
            if (Math.Abs(_positions[index] - degrees) < Math.Abs(_positions[closest] - degrees)) { closest = index; }
        }
        return closest;
    }

    private void Step(int direction)
    {
        if (_positions.Length == 0) { Rotated?.Invoke(direction); }
        else { RequestPosition(Math.Clamp(SelectedPosition + direction, 0, _positions.Length - 1)); }
    }

    private void RequestPosition(int position)
    {
        if (position != SelectedPosition) { SelectedPositionRequested?.Invoke(position); }
    }

    private void CancelInteraction()
    {
        IPointer? pointer = _pointer;
        _pointer = null;
        _pressKey = null;
        _lastPointerAngle = null;
        _lastDragRequest = null;
        _encoderRemainderDegrees = 0;
        _dragged = false;
        _startedInCenter = false;
        if (ReferenceEquals(pointer?.Captured, this)) { pointer.Capture(null); }
        InvalidateVisual();
    }
}
