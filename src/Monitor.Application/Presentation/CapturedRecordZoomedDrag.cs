// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

// Serialized screen-local gesture. Shell origin/scroll translation is external.
public sealed class CapturedRecordZoomedDrag
{
    private readonly CapturedRecordStudyDrag _drag;
    private readonly Ecg12ZoomSelection _zoom;
    private readonly Ecg12ZoomState _initialSelection;
    private readonly RecordScreenZoomLayout _layout;
    private readonly Ecg12ScreenTransform _transform;
    private bool _layoutChanged;

    internal CapturedRecordZoomedDrag(CapturedRecordStudyView view, CapturedRecordNavigation navigation,
        Ecg12ZoomSelection zoom, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels,
        EcgVerticalScale scale, RecordScreenZoomLayout layout, ExactPlotCoordinate x, ExactPlotCoordinate y,
        ExactPlotCoordinate radius)
    {
        _ = view.RequireCurrentViewport(navigation, canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale);
        ArgumentNullException.ThrowIfNull(zoom);
        ArgumentNullException.ThrowIfNull(layout);
        _zoom = zoom;
        _initialSelection = zoom.Selection;
        _layout = layout;
        _transform = Ecg12ScreenTransform.Resolve(_initialSelection, layout.PageWidth, layout.PageHeight, layout.AvailableWidth, layout.AvailableHeight);
        if (radius is null || radius.Numerator <= 0 || radius.Denominator <= 0)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidHitRadius", nameof(radius)); }
        _drag = view.BeginCursorDragAtPoint(navigation, canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels,
            scale, _transform.Inverse(x), _transform.Inverse(y), _transform.Inverse(radius));
    }

    public CapturedRecordCursorPair PreviewPointer(bool canPreserveGlobalSafetyOverlay, int plotLeftPixels,
        int plotWidthPixels, EcgVerticalScale scale, RecordScreenZoomLayout layout, ExactPlotCoordinate x, ExactPlotCoordinate y)
    {
        ValidateLayout(layout);
        return _drag.PreviewPointer(canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale,
            _transform.Inverse(x), _transform.Inverse(y));
    }

    public CapturedRecordCursorPair CommitPointer(bool canPreserveGlobalSafetyOverlay, int plotLeftPixels,
        int plotWidthPixels, EcgVerticalScale scale, RecordScreenZoomLayout layout, ExactPlotCoordinate x, ExactPlotCoordinate y)
    {
        ValidateLayout(layout);
        return _drag.CommitPointer(canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale,
            _transform.Inverse(x), _transform.Inverse(y));
    }

    public CapturedRecordCursorPair Cancel() => _drag.Cancel();

    private void ValidateLayout(RecordScreenZoomLayout layout)
    {
        if (_layoutChanged || !ReferenceEquals(_zoom.Selection, _initialSelection) || layout != _layout)
        {
            _layoutChanged = true;
            throw new CapturedRecordMeasurementException("RecordMeasurement.DragLayoutChanged", nameof(layout));
        }
    }
}
