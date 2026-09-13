// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class Ecg12ZoomSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(EquivalentZoomSetPreservesIdentityAfterPolicyCheck), EquivalentZoomSetPreservesIdentityAfterPolicyCheck),
        new(nameof(ZoomSelectsModesAndCanonicalExactScale), ZoomSelectsModesAndCanonicalExactScale),
        new(nameof(ZoomRejectsInvalidSelectionAtomically), ZoomRejectsInvalidSelectionAtomically),
        new(nameof(ZoomRechecksPolicyAfterDisplay), ZoomRechecksPolicyAfterDisplay),
        new(nameof(ZoomRestoreUsesCurrentPolicy), ZoomRestoreUsesCurrentPolicy),
    ];

    private static void EquivalentZoomSetPreservesIdentityAfterPolicyCheck()
    {
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ExplicitScale, 3, 2), SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomState initial = zoom.Selection;
        zoom.Select(new(Ecg12ZoomMode.ExplicitScale, 6, 4));
        Check.That(ReferenceEquals(zoom.Selection, initial), "equivalent normalized set preserves selection identity");
        zoom.UpdatePolicy(SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(Reason(() => zoom.Select(new(Ecg12ZoomMode.ExplicitScale, 3, 2))) == "Ecg12Zoom.CourseLocked" &&
            ReferenceEquals(zoom.Selection, initial), "identical set cannot bypass current policy");
        zoom.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        zoom.Select(new(Ecg12ZoomMode.ActualSize, 1, 1));
        zoom.Select(new(Ecg12ZoomMode.ExplicitScale, 3, 2));
        Check.That(!ReferenceEquals(zoom.Selection, initial), "real mode round trip creates fresh identity");
        var restored = Ecg12ZoomSelection.Restore(zoom.CaptureState(), SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomState recovered = restored.Selection;
        restored.Select(new(Ecg12ZoomMode.ExplicitScale, 9, 6));
        Check.That(ReferenceEquals(restored.Selection, recovered) && !ReferenceEquals(recovered, zoom.Selection), "restored selection supports idempotent sets with its own identity");
    }

    private static void ZoomSelectsModesAndCanonicalExactScale()
    {
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.FitPage, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        zoom.Select(new(Ecg12ZoomMode.ExplicitScale, uint.MaxValue, uint.MaxValue));
        Check.That(zoom.Selection == new Ecg12ZoomState(Ecg12ZoomMode.ExplicitScale, 1, 1), "large exact ratios reduce without overflow");
        zoom.Select(new(Ecg12ZoomMode.ExplicitScale, 6, 4));
        Check.That(zoom.Selection == new Ecg12ZoomState(Ecg12ZoomMode.ExplicitScale, 3, 2), "fractional zoom stays exact");
        zoom.Select(new(Ecg12ZoomMode.ActualSize, 1, 1));
        Check.That(zoom.CaptureDisplay().CanSelect && zoom.Selection.Mode == Ecg12ZoomMode.ActualSize, "100 percent is explicit screen intent");
        zoom.Select(new(Ecg12ZoomMode.FitPage, 1, 1));
        Check.That(zoom.Selection.Mode == Ecg12ZoomMode.FitPage, "fit-page intent remains separate from a fixed ratio");
    }

    private static void ZoomRejectsInvalidSelectionAtomically()
    {
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.ActualSize, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        Ecg12ZoomState accepted = zoom.Selection;
        Ecg12ZoomState[] invalid = [new((Ecg12ZoomMode)99, 1, 1), new(Ecg12ZoomMode.ExplicitScale, 0, 1),
            new(Ecg12ZoomMode.ExplicitScale, 1, 0), new(Ecg12ZoomMode.FitPage, 2, 1), new(Ecg12ZoomMode.ActualSize, 2, 2)];
        foreach (Ecg12ZoomState state in invalid)
        {
            Check.That(Reason(() => zoom.Select(state)) == "Ecg12Zoom.InvalidSelection" && ReferenceEquals(zoom.Selection, accepted),
                "malformed zoom never replaces accepted selection");
        }
        Check.That(Reason(() => zoom.Select(null!)) == "Ecg12Zoom.InvalidSelection", "null input rejects with stable reason");
        Check.That(Reason(() => zoom.UpdatePolicy((SystemViewCommandAssessmentPolicy)99)) == "Ecg12Zoom.InvalidPolicy" && zoom.CaptureDisplay().CanSelect,
            "invalid policy update preserves prior permission");
    }

    private static void ZoomRechecksPolicyAfterDisplay()
    {
        Ecg12ZoomSelection zoom = new(new(Ecg12ZoomMode.FitPage, 1, 1), SystemViewCommandAssessmentPolicy.Enabled);
        Check.That(zoom.CaptureDisplay().CanSelect, "initial display enables command");
        foreach (SystemViewCommandAssessmentPolicy policy in new[] { SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.CourseLocked })
        {
            zoom.UpdatePolicy(policy);
            Check.That(!zoom.CaptureDisplay().CanSelect && Reason(() => zoom.Select(new(Ecg12ZoomMode.ActualSize, 1, 1))) == "Ecg12Zoom." + policy &&
                zoom.Selection.Mode == Ecg12ZoomMode.FitPage, "stale enabled display cannot bypass current policy");
        }
        zoom.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled);
        zoom.Select(new(Ecg12ZoomMode.ActualSize, 1, 1));
        Check.That(zoom.Selection.Mode == Ecg12ZoomMode.ActualSize, "trusted policy update permits subsequent selection");
    }

    private static void ZoomRestoreUsesCurrentPolicy()
    {
        Ecg12ZoomSelection original = new(new(Ecg12ZoomMode.ExplicitScale, 10, 4), SystemViewCommandAssessmentPolicy.Enabled);
        var restored = Ecg12ZoomSelection.Restore(original.CaptureState(), SystemViewCommandAssessmentPolicy.CourseLocked);
        Check.That(restored.Selection == new Ecg12ZoomState(Ecg12ZoomMode.ExplicitScale, 5, 2) && !restored.CaptureDisplay().CanSelect &&
            Reason(() => restored.Select(new(Ecg12ZoomMode.FitPage, 1, 1))) == "Ecg12Zoom.CourseLocked", "restore retains exact intent but not old permission");
        Check.That(Reason(() => Ecg12ZoomSelection.Restore(new(Ecg12ZoomMode.ExplicitScale, 1, 0), SystemViewCommandAssessmentPolicy.Enabled)) == "Ecg12Zoom.InvalidSelection",
            "malformed restored ratios reject");
    }

    private static string Reason(Action action)
    {
        try { action(); }
        catch (Ecg12ZoomSelectionException exception) { return exception.ReasonCode; }
        throw new InvalidOperationException("Expected zoom rejection.");
    }
}
