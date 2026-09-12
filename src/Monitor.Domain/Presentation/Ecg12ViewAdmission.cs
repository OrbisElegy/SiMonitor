// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Presentation;

public enum Ecg12RecordContext
{
    ActiveInstance,
    IndependentCapturedRecord,
}

public sealed class Ecg12ViewAdmissionException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record Ecg12ViewAdmissionDecision(bool MayEnter, string ReasonCode,
    bool RequiresGlobalSafetyOverlay, bool InheritPatientAlarmAggregate);

// Evaluate using current shell capability, never a persisted permission.
public static class Ecg12ViewAdmission
{
    public static Ecg12ViewAdmissionDecision Evaluate(Ecg12RecordContext context,
        TemporalViewMode viewMode, bool canPreserveGlobalSafetyOverlay)
    {
        if (!Enum.IsDefined(context))
        { throw new Ecg12ViewAdmissionException("Ecg12Admission.InvalidContext", nameof(context)); }
        if (!Enum.IsDefined(viewMode))
        { throw new Ecg12ViewAdmissionException("Ecg12Admission.InvalidViewMode", nameof(viewMode)); }
        if (context == Ecg12RecordContext.IndependentCapturedRecord)
        {
            if (viewMode != TemporalViewMode.CapturedRecord)
            { throw new Ecg12ViewAdmissionException("Ecg12Admission.ContextViewMismatch", nameof(viewMode)); }
            return new(true, "Ecg12Admission.Allowed", false, false);
        }

        // Pinning an active instance's trace does not detach its current safety state.
        return new(canPreserveGlobalSafetyOverlay,
            canPreserveGlobalSafetyOverlay ? "Ecg12Admission.Allowed" : "Ecg12Admission.SafetyOverlayUnavailable",
            true, true);
    }
}
