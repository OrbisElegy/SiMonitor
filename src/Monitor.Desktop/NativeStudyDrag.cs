// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Threading;
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Desktop;

// Retain this gesture across preview redraws. Native pointer capture remains
// the caller's responsibility; this object does not infer cancellation policy.
public sealed class NativeStudyDrag
{
    private readonly RecordStudyPresenter _presenter;
    private readonly CapturedRecordSvgDrag _drag;

    internal NativeStudyDrag(RecordStudyPresenter presenter, CapturedRecordSvgDrag drag)
    { _presenter = presenter; _drag = drag; }

    public bool IsFinished { get; private set; }

    public void Preview(RecordStudyCommandContext context, Point windowPoint, Point currentPageOrigin) =>
        Move(context, windowPoint, currentPageOrigin, false);

    public void Commit(RecordStudyCommandContext context, Point windowPoint, Point currentPageOrigin) =>
        Move(context, windowPoint, currentPageOrigin, true);

    private void Move(RecordStudyCommandContext context, Point point, Point origin, bool commit)
    {
        RequireUnfinished(context);
        if (!_presenter.IsPresented) { throw new InvalidOperationException("Native study is not currently presented."); }
        ExactPlotCoordinate x = NativeLogicalCoordinate.FromDouble(point.X);
        ExactPlotCoordinate y = NativeLogicalCoordinate.FromDouble(point.Y);
        ExactPlotCoordinate ox = NativeLogicalCoordinate.FromDouble(origin.X);
        ExactPlotCoordinate oy = NativeLogicalCoordinate.FromDouble(origin.Y);
        try
        {
            if (commit)
            {
                _drag.CommitPointer(context.CanPreserveGlobalSafetyOverlay, context.Layout, context.Screen, x, y, ox, oy);
                IsFinished = true;
            }
            else { _drag.PreviewPointer(context.CanPreserveGlobalSafetyOverlay, context.Layout, context.Screen, x, y, ox, oy); }
        }
        catch (CapturedRecordMeasurementException exception) when
            (exception.ReasonCode is "RecordMeasurement.InvalidPoint" or "RecordMeasurement.UnrepresentableTime" or "RecordMeasurement.UnrepresentableAmplitude")
        { throw; }
        catch (EcgManualMeasurementException exception) when (exception.ReasonCode == "ManualMeasurement.TimeReversed")
        { throw; }
        catch (Exception exception)
        {
            _presenter.WithdrawCommandFailure(exception);
            throw;
        }
        _presenter.Refresh(context);
    }

    public void Cancel(RecordStudyCommandContext context)
    {
        RequireUnfinished(context);
        try { _drag.Cancel(); }
        catch (Exception exception)
        {
            _presenter.WithdrawCommandFailure(exception);
            throw;
        }
        IsFinished = true;
        // Cancellation after withdrawal must not reopen an unavailable view.
        if (_presenter.IsPresented) { _presenter.Refresh(context); }
    }

    private void RequireUnfinished(RecordStudyCommandContext context)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(context);
        if (IsFinished) { throw new CapturedRecordMeasurementException("RecordMeasurement.DragFinished", nameof(context)); }
    }
}
