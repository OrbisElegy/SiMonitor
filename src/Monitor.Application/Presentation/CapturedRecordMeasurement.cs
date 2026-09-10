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

// Local measurement ownership, not authority identity or authentication of amplitude.
public sealed class CapturedRecordMeasurement
{
    private readonly object _owner = new();
    private readonly PinnedRecordRange _range;
    private SystemViewCommandAssessmentPolicy _policy;

    public CapturedRecordMeasurement(CapturedRecordBinding record, string slotId, SystemViewCommandAssessmentPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(record);
        _range = record.CapturePinnedRecordRange();
        Slot = record.Slots.FirstOrDefault(slot => string.Equals(slot.SlotId, slotId, StringComparison.Ordinal))
            ?? throw new CapturedRecordMeasurementException("RecordMeasurement.UnknownSlot", nameof(slotId));
        UpdatePolicy(policy);
    }

    public RecordSlotBinding Slot { get; }

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
