// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed record CapturedRecordStudySessionState(CapturedRecordNavigationState Navigation,
    string SlotId, EcgManualCursor? First, EcgManualCursor? Second);
public sealed record RestoredRecordStudySession(CapturedRecordNavigation Navigation, CapturedRecordStudyView View);

// In-memory state only. Context, permissions, display pixels and gestures are not persisted.
public static class CapturedRecordStudySession
{
    public static RestoredRecordStudySession Restore(CapturedRecordStudySessionState state,
        Ecg12RecordContext currentContext, SystemViewCommandAssessmentPolicy paginationPolicy,
        SystemViewCommandAssessmentPolicy measurementPolicy)
    {
        _ = Ecg12ViewAdmission.Evaluate(currentContext, TemporalViewMode.CapturedRecord, true);
        if (!Enum.IsDefined(paginationPolicy) || !Enum.IsDefined(measurementPolicy))
        { throw new CapturedRecordPaginationException("RecordStudy.InvalidPolicy", nameof(paginationPolicy)); }
        try
        {
            ArgumentNullException.ThrowIfNull(state);
            if ((state.First is null) != (state.Second is null))
            { throw new ArgumentException("Incomplete cursor pair", nameof(state)); }
            CapturedRecordNavigation navigation = CapturedRecordNavigation.Restore(state.Navigation, paginationPolicy);
            CapturedRecordStudyView view = navigation.CreateStudyView(currentContext, state.SlotId, measurementPolicy);
            if (state.First is not null)
            { view.Measurement.ReplacePair(state.First, state.Second!); }
            return new(navigation, view);
        }
        catch (CapturedRecordMeasurementException exception) when
            (exception.ReasonCode is "RecordMeasurement.Disabled" or "RecordMeasurement.CourseLocked")
        { throw; }
        catch (ArgumentException)
        { throw new CapturedRecordPaginationException("RecordStudy.InvalidCheckpoint", nameof(state)); }
    }
}
