// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

// One window-level mouse route survives replacement of the rendered control.
public sealed class NativeDragInput : IDisposable
{
    private readonly RecordStudyPresenter _presenter;
    private readonly MainWindow _window;
    private readonly Func<RecordStudyCommandContext> _context;
    private readonly Action<NativeStudyDrag> _interrupted;
    private readonly double _radius;
    private IPointer? _pointer;
    private NativeStudyDrag? _gesture;
    private bool _disposed;
    private bool _processing;

    public NativeDragInput(RecordStudyPresenter presenter, Func<RecordStudyCommandContext> currentContext,
        double radius, Action<NativeStudyDrag> interrupted)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(currentContext);
        ArgumentNullException.ThrowIfNull(interrupted);
        _ = NativeLogicalCoordinate.FromDouble(radius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        _presenter = presenter;
        _window = presenter.Window;
        if (_window.DragInput is not null) { throw new InvalidOperationException("Window already has native drag input."); }
        _context = currentContext;
        _interrupted = interrupted;
        _radius = radius;
        _window.SetDragInput(this);
        _window.PointerPressed += Pressed;
        _window.PointerMoved += Moved;
        _window.PointerReleased += Released;
        _window.PointerCaptureLost += CaptureLost;
        _window.Closed += Closed;
        _window.PublicationChanging += PublicationChanging;
    }

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        RecordStudyControl? control = _window.RecordControl;
        if (_processing || _pointer is not null || _presenter.ActiveDrag is not null || control is null ||
            !ReferenceEquals(e.Source, control) || !control.IsHitTestVisible || e.Pointer.Type != PointerType.Mouse ||
            e.GetCurrentPoint(_window).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) { return; }
        _processing = true;
        try
        {
            RecordStudyCommandContext context = _context();
            if (_disposed) { return; }
            _gesture = _presenter.BeginDrag(control.InputSession, context, e.GetPosition(_window), Origin(control), _radius);
            _pointer = e.Pointer;
            _pointer.Capture(_window);
            if (!ReferenceEquals(_pointer?.Captured, _window)) { Interrupt(); }
            e.Handled = true;
        }
        catch (CapturedRecordMeasurementException exception) when
            (exception.ReasonCode is "RecordMeasurement.NoCursorHit" or "RecordMeasurement.AmbiguousCursorHit" or "RecordMeasurement.InvalidPoint" or "RecordMeasurement.StaleRenderedView")
        { }
        catch (Exception exception) { Fail(exception); }
        finally { _processing = false; }
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        if (_processing || !ReferenceEquals(_pointer, e.Pointer) || _gesture is null) { return; }
        if (_gesture.IsFinished || !ReferenceEquals(_gesture, _presenter.ActiveDrag)) { ReleasePointer(); return; }
        e.Handled = true;
        // A platform may omit the release event while focus/capture changes.
        // A later move with no left button must not publish another preview.
        if (!e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed)
        {
            Interrupt();
            return;
        }
        _processing = true;
        try
        {
            RecordStudyCommandContext context = _context();
            if (_disposed || _gesture is null) { return; }
            RecordStudyControl control = _window.RecordControl ?? throw new InvalidOperationException("Study was withdrawn.");
            _gesture.Preview(context, e.GetPosition(_window), Origin(control));
        }
        catch (CapturedRecordMeasurementException exception) when
            (exception.ReasonCode is "RecordMeasurement.InvalidPoint" or "RecordMeasurement.UnrepresentableTime" or "RecordMeasurement.UnrepresentableAmplitude")
        { }
        catch (Monitor.Domain.Presentation.EcgManualMeasurementException exception) when (exception.ReasonCode == "ManualMeasurement.TimeReversed") { }
        catch (Exception exception) { Fail(exception); }
        finally { _processing = false; }
    }

    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_processing || !ReferenceEquals(_pointer, e.Pointer) || _gesture is null || e.InitialPressMouseButton != MouseButton.Left) { return; }
        if (_gesture.IsFinished || !ReferenceEquals(_gesture, _presenter.ActiveDrag)) { ReleasePointer(); return; }
        e.Handled = true;
        _processing = true;
        try
        {
            RecordStudyCommandContext context = _context();
            if (_disposed || _gesture is null) { return; }
            RecordStudyControl control = _window.RecordControl ?? throw new InvalidOperationException("Study was withdrawn.");
            _gesture.Commit(context, e.GetPosition(_window), Origin(control));
            ReleasePointer();
        }
        catch (Exception exception) { Fail(exception); }
        finally { _processing = false; }
    }

    private Point Origin(RecordStudyControl control)
    {
        _window.UpdateLayout();
        return control.TranslatePoint(default, _window) ?? throw new InvalidOperationException("Record control is detached.");
    }

    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (ReferenceEquals(_pointer, e.Pointer)) { Interrupt(); }
    }

    private void Fail(Exception exception)
    {
        if (_presenter.IsPresented) { _presenter.WithdrawCommandFailure(exception); }
        Interrupt();
    }

    private void Interrupt()
    {
        NativeStudyDrag? gesture = _gesture;
        ReleasePointer();
        if (gesture is null || gesture.IsFinished) { return; }
        try { _interrupted(gesture); }
        catch (Exception exception) { _presenter.WithdrawCommandFailure(exception); }
    }

    private void ReleasePointer()
    {
        IPointer? pointer = _pointer;
        _pointer = null;
        _gesture = null;
        if (ReferenceEquals(pointer?.Captured, _window)) { pointer.Capture(null); }
    }

    private void PublicationChanging(object? sender, EventArgs e)
    {
        if (!_processing) { Interrupt(); }
    }

    private void Closed(object? sender, EventArgs e)
    {
        _presenter.Withdraw();
        Dispose();
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) { return; }
        _disposed = true;
        _window.PointerPressed -= Pressed;
        _window.PointerMoved -= Moved;
        _window.PointerReleased -= Released;
        _window.PointerCaptureLost -= CaptureLost;
        _window.Closed -= Closed;
        _window.PublicationChanging -= PublicationChanging;
        _window.SetDragInput(null);
        Interrupt();
    }
}
