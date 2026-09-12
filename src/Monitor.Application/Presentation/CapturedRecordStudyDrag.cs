// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

// Serialized view-bound gesture. Pixel layout and shell capability remain current inputs.
public sealed class CapturedRecordStudyDrag
{
    private readonly CapturedRecordStudyView _view;
    private readonly CapturedRecordNavigation _navigation;
    private readonly CapturedRecordMeasurement _measurement;
    private readonly CapturedRecordDrag _drag;

    internal CapturedRecordStudyDrag(CapturedRecordStudyView view, CapturedRecordNavigation navigation,
        bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels,
        EcgVerticalScale scale, RecordCursorEnd end)
    {
        RecordCursorViewport viewport = view.RequireCurrentViewport(navigation, canPreserveGlobalSafetyOverlay,
            plotLeftPixels, plotWidthPixels, scale);
        _view = view;
        _navigation = navigation;
        _measurement = view.Measurement;
        _drag = new(_measurement, end, viewport, scale);
    }

    public CapturedRecordCursorPair Preview(bool canPreserveGlobalSafetyOverlay, int plotLeftPixels,
        int plotWidthPixels, EcgVerticalScale scale, ExactPlotCoordinate x, ExactPlotCoordinate y)
    {
        ValidateSelection();
        RecordCursorViewport viewport = _view.RequireCurrentViewport(_navigation, canPreserveGlobalSafetyOverlay,
            plotLeftPixels, plotWidthPixels, scale);
        return _drag.Preview(x, y, viewport, scale);
    }

    public CapturedRecordCursorPair Commit(bool canPreserveGlobalSafetyOverlay, int plotLeftPixels,
        int plotWidthPixels, EcgVerticalScale scale)
    {
        ValidateSelection();
        RecordCursorViewport viewport = _view.RequireCurrentViewport(_navigation, canPreserveGlobalSafetyOverlay,
            plotLeftPixels, plotWidthPixels, scale);
        return _drag.Commit(viewport, scale);
    }

    // Rollback requires current caliper policy, but not a visible/admitted viewport.
    public CapturedRecordCursorPair Cancel()
    {
        ValidateSelection();
        return _drag.Cancel();
    }

    private void ValidateSelection()
    {
        if (!ReferenceEquals(_view.Measurement, _measurement))
        { throw new CapturedRecordMeasurementException("RecordMeasurement.DragSuperseded", nameof(_view)); }
    }
}
