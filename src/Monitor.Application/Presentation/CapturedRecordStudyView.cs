// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed record CapturedRecordStudyDisplay(Ecg12ViewAdmissionDecision Admission,
    CapturedRecordBinding? Record, RecordMeasurementDisplay? Measurement, RecordSlotBinding? MeasurementSlot);

// Serialized composition. The shell must replace the old display with this result.
public sealed class CapturedRecordStudyView
{
    private readonly CapturedRecordBinding _record;
    private readonly Ecg12RecordContext _context;

    public CapturedRecordStudyView(CapturedRecordBinding record, Ecg12RecordContext context,
        string slotId, SystemViewCommandAssessmentPolicy measurementPolicy)
    {
        ArgumentNullException.ThrowIfNull(record);
        _ = Ecg12ViewAdmission.Evaluate(context, TemporalViewMode.CapturedRecord, true);
        _record = record;
        _context = context;
        Measurement = new(record, slotId, measurementPolicy);
    }

    public CapturedRecordMeasurement Measurement { get; private set; }

    public void SelectMeasurementSlot(string slotId)
    {
        if (string.Equals(Measurement.Slot.SlotId, slotId, StringComparison.Ordinal)) { return; }
        CapturedRecordMeasurement replacement = new(_record, slotId, Measurement.CurrentPolicy);
        Measurement = replacement;
    }

    public CapturedRecordStudyDisplay CaptureDisplay(bool canPreserveGlobalSafetyOverlay,
        RecordCursorViewport viewport, EcgVerticalScale scale, bool allowAuxiliaryRate)
    {
        Ecg12ViewAdmissionDecision admission = Ecg12ViewAdmission.Evaluate(_context,
            TemporalViewMode.CapturedRecord, canPreserveGlobalSafetyOverlay);
        if (!admission.MayEnter) { return new(admission, null, null, null); }
        RecordMeasurementDisplay measurement = Measurement.CaptureDisplay(viewport, scale, allowAuxiliaryRate);
        return new(admission, _record, measurement, Measurement.Slot);
    }
}
