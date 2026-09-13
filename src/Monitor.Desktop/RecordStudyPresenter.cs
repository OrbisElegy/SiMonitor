// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Threading;
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Desktop;

public sealed record RecordStudyCommandContext(bool CanPreserveGlobalSafetyOverlay,
    CapturedRecordSvgLayout Layout, RecordScreenZoomLayout Screen,
    EcgPaperGridSvgStyle GridStyle, EcgManualCursorSvgStyle CursorStyle, bool AllowAuxiliaryRate);

// One serialized presenter owns a window's study updates. It does not own data
// or authorize navigation, and retained gestures still require explicit resolution.
public sealed class RecordStudyPresenter
{
    private readonly MainWindow _window;
    internal MainWindow Window => _window;
    private readonly CapturedRecordSvgPresentation _presentation;
    public NativeStudyDrag? ActiveDrag { get; private set; }

    public RecordStudyPresenter(MainWindow window, CapturedRecordSvgPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(presentation);
        _window = window;
        _presentation = presentation;
    }

    public void Refresh(bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout,
        RecordScreenZoomLayout screen, EcgPaperGridSvgStyle gridStyle,
        EcgManualCursorSvgStyle cursorStyle, bool allowAuxiliaryRate,
        CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        _window.ApplyPublication(new(CapturedRecordSvgStatus.Refreshing, "SvgPresentation.Refreshing", null));
        try
        {
            _presentation.Refresh(canPreserveGlobalSafetyOverlay, layout, screen,
                gridStyle, cursorStyle, allowAuxiliaryRate, cancellationToken);
            CapturedRecordSvgPublication ready = _presentation.Publication;
            RecordStudyControl control = new(ready, gridStyle, cursorStyle);
            cancellationToken.ThrowIfCancellationRequested();
            _window.ApplyPublication(ready, control);
        }
        catch (Exception exception)
        {
            CapturedRecordSvgPublication failed = _presentation.Publication;
            if (failed.Status == CapturedRecordSvgStatus.Ready)
            {
                // Native construction can fail after the adapter has published.
                // Withdraw its input as well as the native content.
                _presentation.Withdraw();
                bool cancelled = exception is OperationCanceledException cancellation &&
                    cancellationToken.IsCancellationRequested && cancellation.CancellationToken == cancellationToken;
                failed = cancelled ? new(CapturedRecordSvgStatus.Cancelled, "SvgPresentation.Cancelled", null)
                    : new(CapturedRecordSvgStatus.Failed, "DesktopStudy.RenderFailed", null);
            }
            _window.ApplyPublication(failed);
            throw;
        }
    }

    public void Withdraw()
    {
        Dispatcher.UIThread.VerifyAccess();
        _presentation.Withdraw();
        _window.ApplyPublication(_presentation.Publication);
    }

    // Resolve trusted current inputs at activation, not when the button is bound.
    public void BindClearButton(Func<RecordStudyCommandContext> currentContext)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(currentContext);
        _window.SetClearCommand(input =>
        {
            try
            {
                RecordStudyCommandContext context = currentContext();
                ArgumentNullException.ThrowIfNull(context);
                ClearPair(input, context.CanPreserveGlobalSafetyOverlay, context.Layout,
                    context.Screen, context.GridStyle, context.CursorStyle, context.AllowAuxiliaryRate);
            }
            catch (CapturedRecordMeasurementException exception) when (exception.ReasonCode == "RecordMeasurement.StaleRenderedView")
            {
                // A superseded activation must not remove a newer picture.
            }
            catch (Exception)
            {
                // Clear/Refresh already publish their failures. A context-provider
                // failure occurs earlier and must also withdraw both boundaries.
                if (_window.CurrentPublication?.Input is not null)
                {
                    _presentation.Withdraw();
                    _window.ApplyPublication(new(CapturedRecordSvgStatus.Failed, "DesktopStudy.CommandContextFailed", null));
                }
            }
        });
    }

    public void UnbindClearButton()
    {
        Dispatcher.UIThread.VerifyAccess();
        _window.SetClearCommand(null);
    }

    public void BindPointerQueries(Func<RecordStudyCommandContext> currentContext, double radius)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(currentContext);
        _ = NativeLogicalCoordinate.FromDouble(radius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        _window.SetPointerQuery((input, localPoint) =>
        {
            try
            {
                RecordStudyCommandContext context = currentContext();
                ArgumentNullException.ThrowIfNull(context);
                return HitTest(input, context.CanPreserveGlobalSafetyOverlay, context.Layout,
                    context.Screen, localPoint, default, radius);
            }
            catch (CapturedRecordMeasurementException exception) when
                (exception.ReasonCode is "RecordMeasurement.InvalidPoint" or "RecordMeasurement.StaleRenderedView")
            { return RecordCursorHits.None; }
            catch (Exception exception)
            {
                if (ReferenceEquals(input, _window.CurrentPublication?.Input)) { WithdrawCommandFailure(exception); }
                return RecordCursorHits.None;
            }
        });
    }

    public void UnbindPointerQueries()
    {
        Dispatcher.UIThread.VerifyAccess();
        _window.SetPointerQuery(null);
    }

    // The caller captures the input shown when the command is issued. A queued
    // command from an older picture must not be redirected to the latest one.
    public void ClearPair(CapturedRecordSvgInputSession expectedInput,
        bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout,
        RecordScreenZoomLayout screen, EcgPaperGridSvgStyle gridStyle,
        EcgManualCursorSvgStyle cursorStyle, bool allowAuxiliaryRate)
    {
        RequireCurrentInput(expectedInput);
        try
        {
            expectedInput.ClearPair(canPreserveGlobalSafetyOverlay, layout, screen);
        }
        catch (Exception exception)
        {
            WithdrawCommandFailure(exception);
            throw;
        }
        // The accepted edit is not rolled back if the subsequent paint fails.
        Refresh(canPreserveGlobalSafetyOverlay, layout, screen, gridStyle, cursorStyle, allowAuxiliaryRate);
    }

    public RecordCursorHits HitTest(CapturedRecordSvgInputSession expectedInput,
        bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen,
        Point windowPoint, Point currentPageOrigin, double radius)
    {
        RequireCurrentInput(expectedInput);
        ExactPlotCoordinate x = NativeLogicalCoordinate.FromDouble(windowPoint.X);
        ExactPlotCoordinate y = NativeLogicalCoordinate.FromDouble(windowPoint.Y);
        ExactPlotCoordinate originX = NativeLogicalCoordinate.FromDouble(currentPageOrigin.X);
        ExactPlotCoordinate originY = NativeLogicalCoordinate.FromDouble(currentPageOrigin.Y);
        ExactPlotCoordinate exactRadius = NativeLogicalCoordinate.FromDouble(radius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        try
        {
            return expectedInput.HitTest(canPreserveGlobalSafetyOverlay, layout, screen,
                x, y, originX, originY, exactRadius);
        }
        catch (CapturedRecordMeasurementException exception) when (exception.ReasonCode == "RecordMeasurement.InvalidPoint")
        {
            // A pointer outside the plot is invalid query input, not loss of
            // admission. Preserve the picture so a subsequent point can retry.
            throw;
        }
        catch (Exception exception)
        {
            WithdrawCommandFailure(exception);
            throw;
        }
    }

    private void RequireCurrentInput(CapturedRecordSvgInputSession expectedInput)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(expectedInput);
        if (!ReferenceEquals(expectedInput, _presentation.Current) ||
            !ReferenceEquals(expectedInput, _window.CurrentPublication?.Input))
        { throw new CapturedRecordMeasurementException("RecordMeasurement.StaleRenderedView", nameof(expectedInput)); }
    }

    public NativeStudyDrag BeginDrag(CapturedRecordSvgInputSession expectedInput,
        RecordStudyCommandContext context, Point windowPoint, Point currentPageOrigin, double radius)
    {
        RequireCurrentInput(expectedInput);
        if (ActiveDrag is not null)
        { throw new CapturedRecordMeasurementException("DesktopStudy.DragAlreadyActive", nameof(expectedInput)); }
        ArgumentNullException.ThrowIfNull(context);
        ExactPlotCoordinate x = NativeLogicalCoordinate.FromDouble(windowPoint.X);
        ExactPlotCoordinate y = NativeLogicalCoordinate.FromDouble(windowPoint.Y);
        ExactPlotCoordinate ox = NativeLogicalCoordinate.FromDouble(currentPageOrigin.X);
        ExactPlotCoordinate oy = NativeLogicalCoordinate.FromDouble(currentPageOrigin.Y);
        ExactPlotCoordinate r = NativeLogicalCoordinate.FromDouble(radius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        try
        {
            NativeStudyDrag candidate = new(this, expectedInput.BeginDrag(context.CanPreserveGlobalSafetyOverlay,
                context.Layout, context.Screen, x, y, ox, oy, r));
            ActiveDrag = candidate;
            return candidate;
        }
        catch (CapturedRecordMeasurementException exception) when
            (exception.ReasonCode is "RecordMeasurement.InvalidPoint" or "RecordMeasurement.NoCursorHit" or "RecordMeasurement.AmbiguousCursorHit")
        { throw; }
        catch (Exception exception)
        {
            WithdrawCommandFailure(exception);
            throw;
        }
    }

    internal bool IsPresented => _presentation.Current is { } input && ReferenceEquals(input, _window.CurrentPublication?.Input);

    internal void ReleaseDrag(NativeStudyDrag drag)
    {
        if (ReferenceEquals(ActiveDrag, drag)) { ActiveDrag = null; }
    }

    internal void Refresh(RecordStudyCommandContext context) => Refresh(context.CanPreserveGlobalSafetyOverlay,
        context.Layout, context.Screen, context.GridStyle, context.CursorStyle, context.AllowAuxiliaryRate);

    internal void WithdrawCommandFailure(Exception exception)
    {
        _presentation.Withdraw();
        CapturedRecordSvgPublication failed = exception switch
        {
            Ecg12ViewAdmissionException admission => new(CapturedRecordSvgStatus.Denied, admission.ReasonCode, null),
            CapturedRecordMeasurementException measurement => new(CapturedRecordSvgStatus.Failed, measurement.ReasonCode, null),
            _ => new(CapturedRecordSvgStatus.Failed, "DesktopStudy.CommandFailed", null),
        };
        _window.ApplyPublication(failed);
    }
}
