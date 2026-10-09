// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Application.Therapy;

namespace Monitor.Desktop;

internal static class PacingPermissionSmokeChecks
{
    internal static void Verify()
    {
        string directory = Path.Combine(Path.GetTempPath(), "pacing-permission-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        var window = new DesignPreviewWindow(path);
        window.Show();
        try
        {
            GenericMonitorSkin Skin() => (GenericMonitorSkin)window.MonitorTrace.Skin!;
            void Advance()
            {
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            }
            void Reject()
            {
                long time = window.Session.SimulationTimeNs;
                var session = window.Session;
                Require(!Skin().ApplyPacing.IsEnabled, "disallowed template disables the start control");
                Skin().ApplyPacing.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(ReferenceEquals(session, window.Session) && session.SimulationTimeNs == time &&
                    session.PendingSourceTimeNs is null && session.ActivePacing is null &&
                    Skin().Feedback.Text == window.Localization.Get("skin.pacingDisabled"),
                    "command denial preserves the active template and explains how to enable pacing");
            }
            Require(EcgPacingPermissions.TemplateIds.SequenceEqual(Enumerable.Range(0, DesignPreviewSettings.EcgChoiceCount)
                .Select(DesignPreviewSettings.EcgTemplateKey)), "permission identities cover the complete current catalog");
            Reject();
            var settings = window.Settings;
            settings.PacingPermissions.Allowed.IsChecked = true;
            Require(!window.Session.PacingAllowed, "unapplied permission draft cannot enable pacing");
            settings.EcgSelection = 21;
            Require(settings.PacingPermissions.Allowed.IsChecked == false, "each template starts with its own disabled permission");
            settings.EcgSelection = 0;
            Require(settings.PacingPermissions.Allowed.IsChecked == true, "switching selections preserves each draft");
            settings.OpenAdvanced(0);
            window.SelectPage(2);
            DesktopViewportSmokeChecks.Layout(window);
            Require(settings.PacingPermissions.Bounds.Height > 0, "every template exposes the permission in ECG advanced settings");
            settings.ApplyDelaySeconds.Value = 0;
            window.ApplySettings();
            Advance();
            Require(window.Session.PacingAllowed && Skin().ApplyPacing.IsEnabled, "accepted permission enables the live control");
            settings.PacingPermissions.Allowed.IsChecked = false;
            Require(window.Session.PacingAllowed, "an unapplied revocation does not alter the live session");
            Skin().Current.Value = 60;
            Skin().ApplyPacing.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Advance();
            Require(Skin().StopPacing.IsEnabled, "enabled template can start and retain the stop control");
            window.ApplySettings();
            Advance();
            Reject();
            settings.EcgSelection = 21;
            window.ApplySettings();
            Advance();
            Reject();
            settings.EcgSelection = 1;
            settings.PacingPermissions.Allowed.IsChecked = true;
            window.ApplySettings();
            Advance();
            var reloaded = new DesignPreviewWindow(path);
            try
            {
                Require(!reloaded.PreferenceNotice.IsVisible && reloaded.Session.PacingAllowed && reloaded.Session.ActivePacing is null,
                    "relaunch restores the selected template permission without starting pacing");
                reloaded.Settings.EcgSelection = 0;
                Require(reloaded.Settings.PacingPermissions.Allowed.IsChecked == false, "revoked template stays disabled after relaunch");
                reloaded.Settings.EcgSelection = 1;
                Require(reloaded.Settings.PacingPermissions.Allowed.IsChecked == true, "another template keeps its independent permission");
                reloaded.ResetAllSettings();
                Require(!reloaded.Session.PacingAllowed && reloaded.Settings.PacingPermissions.Capture().Count == 0,
                    "reset defaults clears the permission bank");
            }
            finally { reloaded.Close(); }
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
