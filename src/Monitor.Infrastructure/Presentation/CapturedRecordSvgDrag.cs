// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Infrastructure.Presentation;

// One serialized gesture uses actual SVG scale; preview redraws must retain this gesture.
public sealed class CapturedRecordSvgDrag
{
    private readonly CapturedRecordSvgInputSession _input;
    private readonly CapturedRecordStudyDrag _drag;

    internal CapturedRecordSvgDrag(CapturedRecordSvgInputSession input, CapturedRecordStudyDrag drag)
    { _input = input; _drag = drag; }

    public CapturedRecordCursorPair PreviewPointer(bool canPreserveGlobalSafetyOverlay,
        CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, ExactPlotCoordinate windowX,
        ExactPlotCoordinate windowY, ExactPlotCoordinate originX, ExactPlotCoordinate originY)
    {
        _input.ValidateGeometry(layout, screen);
        Ecg12ScreenTransform transform = _input.Display.RenderedTransform!;
        return _drag.PreviewPointer(canPreserveGlobalSafetyOverlay, layout.PlotLeftPixels, layout.PlotWidthPixels,
            layout.VerticalScale, transform.InverseAt(windowX, originX), transform.InverseAt(windowY, originY));
    }

    public CapturedRecordCursorPair CommitPointer(bool canPreserveGlobalSafetyOverlay,
        CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, ExactPlotCoordinate windowX,
        ExactPlotCoordinate windowY, ExactPlotCoordinate originX, ExactPlotCoordinate originY)
    {
        _input.ValidateGeometry(layout, screen);
        Ecg12ScreenTransform transform = _input.Display.RenderedTransform!;
        return _drag.CommitPointer(canPreserveGlobalSafetyOverlay, layout.PlotLeftPixels, layout.PlotWidthPixels,
            layout.VerticalScale, transform.InverseAt(windowX, originX), transform.InverseAt(windowY, originY));
    }

    public CapturedRecordCursorPair Cancel() => _drag.Cancel();
}
