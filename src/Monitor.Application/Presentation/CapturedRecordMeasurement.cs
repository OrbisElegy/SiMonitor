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
    internal CapturedRecordCursor(object owner, EcgManualCursor value) { Owner = owner; Value = value; }
    internal object Owner { get; }
    public EcgManualCursor Value { get; }
}

// Local measurement ownership, not authority identity or authentication of amplitude.
public sealed class CapturedRecordMeasurement
{
    private readonly object _owner = new();
    private readonly PinnedRecordRange _range;

    public CapturedRecordMeasurement(CapturedRecordBinding record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _range = record.CapturePinnedRecordRange();
    }

    public CapturedRecordCursor CreateCursor(EcgManualCursor value)
    {
        _ = EcgManualMeasurement.Calculate(value, value, false);
        if (value.DataTimeNs < _range.StartDataSimTimeNs || value.DataTimeNs >= _range.EndExclusiveDataSimTimeNs)
        { throw new CapturedRecordMeasurementException("RecordMeasurement.CursorOutsideRecord", nameof(value)); }
        return new(_owner, value);
    }

    public EcgManualMeasurementResult Calculate(CapturedRecordCursor first, CapturedRecordCursor second,
        bool allowAuxiliaryRate)
    {
        ValidateOwner(first, nameof(first));
        ValidateOwner(second, nameof(second));
        return EcgManualMeasurement.Calculate(first.Value, second.Value, allowAuxiliaryRate);
    }

    private void ValidateOwner(CapturedRecordCursor cursor, string parameterName)
    {
        if (cursor is null || !ReferenceEquals(cursor.Owner, _owner))
        { throw new CapturedRecordMeasurementException("RecordMeasurement.ForeignCursor", parameterName); }
    }
}
