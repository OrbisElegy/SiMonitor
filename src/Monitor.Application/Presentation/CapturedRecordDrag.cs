// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

// One serialized gesture. Pair identity fences replacement/clear by another action.
public sealed class CapturedRecordDrag
{
    private readonly CapturedRecordMeasurement _measurement;
    private readonly RecordCursorEnd _end;
    private readonly CapturedRecordCursorPair _initial;
    private readonly RecordCursorViewport _viewport;
    private readonly EcgVerticalScale _scale;
    private CapturedRecordCursorPair _expected;
    private bool _finished;
    private bool _layoutChanged;

    public CapturedRecordDrag(CapturedRecordMeasurement measurement, RecordCursorEnd end,
        RecordCursorViewport viewport, EcgVerticalScale scale)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        if (!Enum.IsDefined(end))
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidCursorEnd", nameof(end)); }
        _initial = measurement.CurrentPair ?? throw new CapturedRecordMeasurementException("RecordMeasurement.NoCursorPair", nameof(measurement));
        _ = measurement.Calculate(_initial.First, _initial.Second, false);
        if (measurement.ProjectCursor(end == RecordCursorEnd.First ? _initial.First : _initial.Second, viewport, scale) is null)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.DragCursorNotVisible", nameof(viewport)); }
        _viewport = viewport;
        _scale = scale;
        _measurement = measurement;
        _end = end;
        _expected = _initial;
    }

    public CapturedRecordCursorPair Preview(ExactPlotCoordinate x, ExactPlotCoordinate y,
        RecordCursorViewport viewport, EcgVerticalScale scale)
    {
        ValidateCurrent();
        ValidateLayout(viewport, scale);
        CapturedRecordCursorPair next = _measurement.MoveCursor(_end, x, y, viewport, scale);
        return _expected = next;
    }

    public CapturedRecordCursorPair Commit(RecordCursorViewport viewport, EcgVerticalScale scale)
    {
        ValidateCurrent();
        ValidateLayout(viewport, scale);
        _ = _measurement.Calculate(_expected.First, _expected.Second, false);
        _finished = true;
        return _expected;
    }

    public CapturedRecordCursorPair Cancel()
    {
        ValidateCurrent();
        CapturedRecordCursorPair restored = _measurement.ReplacePair(_initial.First.Value, _initial.Second.Value);
        _finished = true;
        return restored;
    }

    private void ValidateLayout(RecordCursorViewport viewport, EcgVerticalScale scale)
    {
        if (_layoutChanged || viewport != _viewport || scale is null ||
            scale.PlotTopPixels != _scale.PlotTopPixels || scale.PlotHeightPixels != _scale.PlotHeightPixels ||
            scale.ZeroBaselinePixels != _scale.ZeroBaselinePixels ||
            (ulong)scale.PixelsPerMillivoltNumerator * _scale.PixelsPerMillivoltDenominator !=
                (ulong)_scale.PixelsPerMillivoltNumerator * scale.PixelsPerMillivoltDenominator ||
            scale.PixelsPerMillivoltNumerator == 0 || scale.PixelsPerMillivoltDenominator == 0)
        {
            _layoutChanged = true;
            throw new CapturedRecordMeasurementException("RecordMeasurement.DragLayoutChanged", nameof(viewport));
        }
    }

    private void ValidateCurrent()
    {
        if (_finished)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.DragFinished", nameof(_finished)); }
        if (!ReferenceEquals(_measurement.CurrentPair, _expected))
        { throw new CapturedRecordMeasurementException("RecordMeasurement.DragSuperseded", nameof(_measurement)); }
    }
}
