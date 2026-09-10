// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed class CapturedRecordMeasurementException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed class CapturedRecordCursor
{
    internal CapturedRecordCursor(object owner, RecordSlotBinding slot, EcgManualCursor value)
    { Owner = owner; Slot = slot; Value = value; }
    internal object Owner { get; }
    public RecordSlotBinding Slot { get; }
    public EcgManualCursor Value { get; }
}

public sealed record CapturedRecordMeasurementCheckpoint(CapturedRecordBindingState Record,
    string SlotId, EcgManualCursor First, EcgManualCursor Second);
public sealed record RestoredRecordMeasurement(CapturedRecordMeasurement Measurement,
    CapturedRecordCursor First, CapturedRecordCursor Second);
public sealed record ProjectedRecordCursor(CapturedRecordCursor Cursor, SweepPixelPosition X, EcgVerticalPosition Y);
public sealed record RecordCursorViewport(long StartDataTimeNs, long EndExclusiveDataTimeNs, int PlotLeftPixels, int PlotWidthPixels);

// Local measurement ownership, not authority identity or authentication of amplitude.
public sealed class CapturedRecordMeasurement
{
    private readonly object _owner = new();
    private readonly CapturedRecordBinding _record;
    private readonly PinnedRecordRange _range;
    private SystemViewCommandAssessmentPolicy _policy;

    public CapturedRecordMeasurement(CapturedRecordBinding record, string slotId, SystemViewCommandAssessmentPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(record);
        _record = record;
        _range = record.CapturePinnedRecordRange();
        Slot = record.Slots.FirstOrDefault(slot => string.Equals(slot.SlotId, slotId, StringComparison.Ordinal))
            ?? throw new CapturedRecordMeasurementException("RecordMeasurement.UnknownSlot", nameof(slotId));
        UpdatePolicy(policy);
    }

    public RecordSlotBinding Slot { get; }

    public CapturedRecordMeasurementCheckpoint CaptureCheckpoint(CapturedRecordCursor first, CapturedRecordCursor second)
    {
        _ = Calculate(first, second, false);
        return new(_record.CaptureState(), Slot.SlotId, first.Value, second.Value);
    }

    public static RestoredRecordMeasurement Restore(CapturedRecordMeasurementCheckpoint checkpoint,
        SystemViewCommandAssessmentPolicy currentPolicy)
    {
        if (!Enum.IsDefined(currentPolicy))
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidPolicy", nameof(currentPolicy)); }
        try
        {
            ArgumentNullException.ThrowIfNull(checkpoint);
            CapturedRecordMeasurement measurement = new(CapturedRecordBinding.Restore(checkpoint.Record), checkpoint.SlotId, currentPolicy);
            CapturedRecordCursor first = measurement.CreateCursor(checkpoint.First);
            CapturedRecordCursor second = measurement.CreateCursor(checkpoint.Second);
            _ = measurement.Calculate(first, second, false);
            return new(measurement, first, second);
        }
        catch (CapturedRecordMeasurementException exception) when
            (exception.ReasonCode is "RecordMeasurement.Disabled" or "RecordMeasurement.CourseLocked")
        { throw; }
        catch (ArgumentException)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidCheckpoint", nameof(checkpoint)); }
    }

    // Serialized caller supplies the currently resolved course policy.
    public void UpdatePolicy(SystemViewCommandAssessmentPolicy policy)
    {
        if (!Enum.IsDefined(policy))
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidPolicy", nameof(policy)); }
        _policy = policy;
    }

    public CapturedRecordCursor CreateCursor(EcgManualCursor value)
    {
        EnsureEnabled();
        _ = EcgManualMeasurement.Calculate(value, value, false);
        if (value.DataTimeNs < _range.StartDataSimTimeNs || value.DataTimeNs >= _range.EndExclusiveDataSimTimeNs)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.CursorOutsideRecord", nameof(value)); }
        return new(_owner, Slot, value);
    }

    // Pixel layout is never written back to cursor evidence.
    public ProjectedRecordCursor ProjectCursor(CapturedRecordCursor cursor, int plotLeftPixels, int plotWidthPixels,
        EcgVerticalScale verticalScale) => ProjectCursor(cursor,
            new RecordCursorViewport(_range.StartDataSimTimeNs, _range.EndExclusiveDataSimTimeNs, plotLeftPixels, plotWidthPixels), verticalScale)!;

    // Null means the valid cursor lies outside this half-open page, not missing data.
    public ProjectedRecordCursor? ProjectCursor(CapturedRecordCursor cursor, RecordCursorViewport viewport,
        EcgVerticalScale verticalScale)
    {
        EnsureEnabled();
        ValidateOwner(cursor, nameof(cursor));
        if (viewport is null || viewport.StartDataTimeNs < _range.StartDataSimTimeNs ||
            viewport.EndExclusiveDataTimeNs > _range.EndExclusiveDataSimTimeNs || viewport.StartDataTimeNs >= viewport.EndExclusiveDataTimeNs)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.InvalidViewport", nameof(viewport)); }
        ulong duration = (ulong)(viewport.EndExclusiveDataTimeNs - viewport.StartDataTimeNs);
        _ = SweepPlotGeometry.MapSampleOffset(0, duration, viewport.PlotLeftPixels, viewport.PlotWidthPixels);
        EcgVerticalPosition y = EcgVerticalGeometry.MapMicrovolts(verticalScale,
            cursor.Value.MicrovoltsNumerator, cursor.Value.MicrovoltsDenominator);
        if (cursor.Value.DataTimeNs < viewport.StartDataTimeNs || cursor.Value.DataTimeNs >= viewport.EndExclusiveDataTimeNs) { return null; }
        ulong offset = (ulong)(cursor.Value.DataTimeNs - viewport.StartDataTimeNs);
        SweepPixelPosition x = SweepPlotGeometry.MapSampleOffset(offset, duration, viewport.PlotLeftPixels, viewport.PlotWidthPixels);
        return new(cursor, x, y);
    }

    public EcgManualMeasurementResult Calculate(CapturedRecordCursor first, CapturedRecordCursor second,
        bool allowAuxiliaryRate)
    {
        EnsureEnabled();
        ValidateOwner(first, nameof(first));
        ValidateOwner(second, nameof(second));
        return EcgManualMeasurement.Calculate(first.Value, second.Value, allowAuxiliaryRate);
    }

    private void EnsureEnabled()
    {
        if (_policy != SystemViewCommandAssessmentPolicy.Enabled)
        {
            throw new CapturedRecordMeasurementException(_policy == SystemViewCommandAssessmentPolicy.CourseLocked
                ? "RecordMeasurement.CourseLocked" : "RecordMeasurement.Disabled", "policy");
        }
    }

    private void ValidateOwner(CapturedRecordCursor cursor, string parameterName)
    {
        if (cursor is null || !ReferenceEquals(cursor.Owner, _owner))
        { throw new CapturedRecordMeasurementException("RecordMeasurement.ForeignCursor", parameterName); }
    }
}
