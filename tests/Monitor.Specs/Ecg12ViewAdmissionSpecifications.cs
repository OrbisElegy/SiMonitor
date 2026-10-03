// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class Ecg12ViewAdmissionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ActiveEcg12RequiresPreservedSafetyOverlay), ActiveEcg12RequiresPreservedSafetyOverlay),
        new(nameof(IndependentRecordDoesNotInheritPatientAlarms), IndependentRecordDoesNotInheritPatientAlarms),
        new(nameof(InvalidEcg12ContextCannotBypassSafety), InvalidEcg12ContextCannotBypassSafety),
    ];

    private static void ActiveEcg12RequiresPreservedSafetyOverlay()
    {
        foreach (TemporalViewMode mode in Enum.GetValues<TemporalViewMode>())
        {
            Ecg12ViewAdmissionDecision denied = Ecg12ViewAdmission.Evaluate(Ecg12RecordContext.ActiveInstance, mode, false);
            Ecg12ViewAdmissionDecision allowed = Ecg12ViewAdmission.Evaluate(Ecg12RecordContext.ActiveInstance, mode, true);
            Check.That(!denied.MayEnter && denied.ReasonCode == "Ecg12Admission.SafetyOverlayUnavailable" &&
                denied.RequiresGlobalSafetyOverlay && denied.InheritPatientAlarmAggregate,
                "active views including pinned traces cannot hide current safety state");
            Check.That(allowed.MayEnter && allowed.RequiresGlobalSafetyOverlay && allowed.InheritPatientAlarmAggregate,
                "preserved shell overlay permits entry without dropping patient aggregate");
        }
    }

    private static void IndependentRecordDoesNotInheritPatientAlarms()
    {
        foreach (bool capability in new[] { false, true })
        {
            Ecg12ViewAdmissionDecision decision = Ecg12ViewAdmission.Evaluate(
                Ecg12RecordContext.IndependentCapturedRecord, TemporalViewMode.CapturedRecord, capability);
            Check.That(decision.MayEnter && !decision.RequiresGlobalSafetyOverlay && !decision.InheritPatientAlarmAggregate,
                "independent static question does not acquire live patient alarms from shell capability");
        }
    }

    private static void InvalidEcg12ContextCannotBypassSafety()
    {
        ExpectReason(() => Ecg12ViewAdmission.Evaluate((Ecg12RecordContext)99, TemporalViewMode.CapturedRecord, true),
            "Ecg12Admission.InvalidContext");
        ExpectReason(() => Ecg12ViewAdmission.Evaluate(Ecg12RecordContext.ActiveInstance, (TemporalViewMode)99, true),
            "Ecg12Admission.InvalidViewMode");
        foreach (TemporalViewMode mode in Enum.GetValues<TemporalViewMode>())
        {
            if (mode == TemporalViewMode.CapturedRecord) { continue; }
            ExpectReason(() => Ecg12ViewAdmission.Evaluate(Ecg12RecordContext.IndependentCapturedRecord, mode, true),
                "Ecg12Admission.ContextViewMismatch");
        }
    }

    private static void ExpectReason(Action action, string expected)
    {
        try { action(); }
        catch (Ecg12ViewAdmissionException exception)
        {
            Check.That(exception.ReasonCode == expected, "invalid input has a stable rejection reason");
            return;
        }
        throw new InvalidOperationException("invalid admission input was accepted");
    }
}
