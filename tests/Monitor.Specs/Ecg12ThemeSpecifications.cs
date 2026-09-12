// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class Ecg12ThemeSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ThemeSelectionSupportsBothFrozenThemes), ThemeSelectionSupportsBothFrozenThemes),
        new(nameof(ThemeSelectionChecksCurrentCoursePolicy), ThemeSelectionChecksCurrentCoursePolicy),
        new(nameof(ThemeSelectionRequiresExplicitLocalPermission), ThemeSelectionRequiresExplicitLocalPermission),
        new(nameof(ThemeRestoreUsesCurrentPolicyAndRejectsUnknownTheme), ThemeRestoreUsesCurrentPolicyAndRejectsUnknownTheme),
    ];

    private static void ThemeSelectionSupportsBothFrozenThemes()
    {
        Ecg12ThemeSelection selection = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ThemeDisplay initial = selection.CaptureDisplay();
        selection.Select(Ecg12Theme.PaperGridBlack);
        selection.Select(Ecg12Theme.PaperGridBlack);
        Check.That(selection.Theme == Ecg12Theme.PaperGridBlack && selection.CaptureDisplay().CanSelect && initial.Theme == Ecg12Theme.MonitorDarkGreen,
            "both explicit themes and same-theme selection preserve immutable display snapshots");
        ExpectReason(() => selection.Select((Ecg12Theme)99), "Ecg12Theme.InvalidTheme");
        Check.That(selection.Theme == Ecg12Theme.PaperGridBlack, "unknown theme does not replace accepted theme");
        selection.Select(Ecg12Theme.MonitorDarkGreen);
    }

    private static void ThemeSelectionChecksCurrentCoursePolicy()
    {
        Ecg12ThemeSelection selection = new(Ecg12Theme.MonitorDarkGreen, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ThemeDisplay stale = selection.CaptureDisplay();
        foreach (SystemViewCommandAssessmentPolicy policy in new[] { SystemViewCommandAssessmentPolicy.Disabled, SystemViewCommandAssessmentPolicy.CourseLocked })
        {
            selection.UpdatePolicy(policy, true);
            ExpectReason(() => selection.Select(Ecg12Theme.PaperGridBlack), $"Ecg12Theme.{policy}");
            ExpectReason(() => selection.Select(Ecg12Theme.MonitorDarkGreen), $"Ecg12Theme.{policy}");
            Check.That(!selection.CaptureDisplay().CanSelect && selection.Theme == stale.Theme && stale.CanSelect,
                "old enabled display cannot bypass current policy even for same-theme commands");
        }
    }

    private static void ThemeSelectionRequiresExplicitLocalPermission()
    {
        Ecg12ThemeSelection selection = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, false);
        ExpectReason(() => selection.Select(Ecg12Theme.MonitorDarkGreen), "Ecg12Theme.LocalSelectionNotAllowed");
        ExpectReason(() => selection.UpdatePolicy((SystemViewCommandAssessmentPolicy)99, true), "Ecg12Theme.InvalidPolicy");
        Check.That(selection.CaptureDisplay().ReasonCode == "Ecg12Theme.LocalSelectionNotAllowed", "invalid policy cannot partially grant local selection");
        selection.UpdatePolicy(SystemViewCommandAssessmentPolicy.Enabled, true);
        selection.Select(Ecg12Theme.MonitorDarkGreen);
        Check.That(selection.Theme == Ecg12Theme.MonitorDarkGreen, "explicit permission allows a subsequent command");
    }

    private static void ThemeRestoreUsesCurrentPolicyAndRejectsUnknownTheme()
    {
        Ecg12ThemeSelection original = new(Ecg12Theme.PaperGridBlack, SystemViewCommandAssessmentPolicy.Enabled, true);
        Ecg12ThemeState state = original.CaptureState();
        var restored = Ecg12ThemeSelection.Restore(state, SystemViewCommandAssessmentPolicy.CourseLocked, true);
        Check.That(restored.Theme == original.Theme && !restored.CaptureDisplay().CanSelect, "restore retains selection but never persisted permission");
        ExpectReason(() => restored.Select(Ecg12Theme.MonitorDarkGreen), "Ecg12Theme.CourseLocked");
        ExpectReason(() => Ecg12ThemeSelection.Restore(state with { Theme = (Ecg12Theme)99 }, SystemViewCommandAssessmentPolicy.Enabled, true), "Ecg12Theme.InvalidTheme");
        var noLocalPermission = Ecg12ThemeSelection.Restore(state, SystemViewCommandAssessmentPolicy.Enabled, false);
        ExpectReason(() => noLocalPermission.Select(Ecg12Theme.MonitorDarkGreen), "Ecg12Theme.LocalSelectionNotAllowed");
        Check.That(restored.Theme == Ecg12Theme.PaperGridBlack, "invalid restoration leaves existing state untouched");
    }

    private static void ExpectReason(Action action, string expected)
    {
        try { action(); }
        catch (Ecg12ThemeSelectionException exception)
        {
            Check.That(exception.ReasonCode == expected, "theme rejection has a stable reason");
            return;
        }
        throw new InvalidOperationException("invalid theme action accepted");
    }
}
