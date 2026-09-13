// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

// Serialized view-bound gesture. Pixel layout and shell capability remain current inputs.
public sealed class CapturedRecordStudyDrag
{
    private readonly CapturedRecordStudyView _view;
    private readonly CapturedRecordNavigation _navigation;
    private readonly CapturedRecordMeasurement _measurement;
    private readonly CapturedRecordDrag _drag;
    private readonly CapturedRecordPage _initialPage;
    private readonly ExactPlotCoordinate? _pointerOffsetX;
    private readonly ExactPlotCoordinate? _pointerOffsetY;

    internal CapturedRecordStudyDrag(CapturedRecordStudyView view, CapturedRecordNavigation navigation,
        bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels,
        EcgVerticalScale scale, RecordCursorEnd end, ExactPlotCoordinate? pointerX = null, ExactPlotCoordinate? pointerY = null)
    {
        RecordCursorViewport viewport = view.RequireCurrentViewport(navigation, canPreserveGlobalSafetyOverlay,
            plotLeftPixels, plotWidthPixels, scale);
        _view = view;
        _navigation = navigation;
        _initialPage = navigation.CurrentPage;
        _measurement = view.Measurement;
        _drag = new(_measurement, end, viewport, scale);
        if (pointerX is not null && pointerY is not null)
        {
            CapturedRecordCursorPair pair = _measurement.CurrentPair!;
            ProjectedRecordCursor projected = _measurement.ProjectCursor(end == RecordCursorEnd.First ? pair.First : pair.Second, viewport, scale)!;
            ExactPlotCoordinate cursorX = new((BigInteger)projected.X.WholePixels * projected.X.FractionDenominator + projected.X.FractionNumerator, projected.X.FractionDenominator);
            ExactPlotCoordinate cursorY = new((BigInteger)projected.Y.PixelNumerator, (BigInteger)projected.Y.PixelDenominator);
            _pointerOffsetX = Subtract(pointerX, cursorX);
            _pointerOffsetY = Subtract(pointerY, cursorY);
        }
    }

    // Pointer-based movement retains the exact grab offset; Preview still takes target coordinates.
    public CapturedRecordCursorPair PreviewPointer(bool canPreserveGlobalSafetyOverlay, int plotLeftPixels,
        int plotWidthPixels, EcgVerticalScale scale, ExactPlotCoordinate x, ExactPlotCoordinate y)
    {
        if (_pointerOffsetX is null || _pointerOffsetY is null)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.NoPointerAnchor", nameof(x)); }
        if (x is null || y is null || x.Denominator <= 0 || y.Denominator <= 0)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidPoint", nameof(x)); }
        return Preview(canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale,
            Subtract(x, _pointerOffsetX), Subtract(y, _pointerOffsetY));
    }

    // Serialized release applies its final position even when no last move event arrived.
    public CapturedRecordCursorPair CommitPointer(bool canPreserveGlobalSafetyOverlay, int plotLeftPixels,
        int plotWidthPixels, EcgVerticalScale scale, ExactPlotCoordinate x, ExactPlotCoordinate y)
    {
        PreviewPointer(canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale, x, y);
        return Commit(canPreserveGlobalSafetyOverlay, plotLeftPixels, plotWidthPixels, scale);
    }

    private static ExactPlotCoordinate Subtract(ExactPlotCoordinate left, ExactPlotCoordinate right)
    {
        BigInteger numerator = left.Numerator * right.Denominator - right.Numerator * left.Denominator;
        BigInteger denominator = left.Denominator * right.Denominator;
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(numerator / divisor, denominator / divisor);
    }

    public CapturedRecordCursorPair Preview(bool canPreserveGlobalSafetyOverlay, int plotLeftPixels,
        int plotWidthPixels, EcgVerticalScale scale, ExactPlotCoordinate x, ExactPlotCoordinate y)
    {
        ValidateSelection();
        ValidatePage();
        RecordCursorViewport viewport = _view.RequireCurrentViewport(_navigation, canPreserveGlobalSafetyOverlay,
            plotLeftPixels, plotWidthPixels, scale);
        return _drag.Preview(x, y, viewport, scale);
    }

    public CapturedRecordCursorPair Commit(bool canPreserveGlobalSafetyOverlay, int plotLeftPixels,
        int plotWidthPixels, EcgVerticalScale scale)
    {
        ValidateSelection();
        ValidatePage();
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

    private void ValidatePage()
    {
        if (!ReferenceEquals(_navigation.CurrentPage, _initialPage))
        { throw new CapturedRecordMeasurementException("RecordMeasurement.DragLayoutChanged", nameof(_navigation)); }
    }
}
