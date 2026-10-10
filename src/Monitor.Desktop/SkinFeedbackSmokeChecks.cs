// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Preferences;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class SkinFeedbackSmokeChecks
{
    internal static void VerifyPacingControls()
    {
        string path = Path.Combine(Path.GetTempPath(), "skin-therapy-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new DesignPreviewWindow(path);
        window.Settings.PacingPermissions.Allowed.IsChecked = true;
        window.RestartSettings();
        window.Show();
        try
        {
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            skin.Energy.SelectedItem = 200;
            skin.Rate.Value = 90;
            skin.Current.Value = 60;
            skin.PacingType.SelectedIndex = (int)PacingIllustration.DualChamberDdd;
            var session = window.Session;
            skin.ApplyPacing.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            Require(window.Settings.Sound.TherapyRelayCount > 0, "actual pacing stimuli publish relay sounds");
            Require(ReferenceEquals(session, window.Session) && session.ActivePacing == PacingIllustration.DualChamberDdd &&
                session.Measurements!.HeartRate.MilliBeatsPerMinute is > 89000 and < 91000,
                $"start applies the selected pacing type and actual rate without restarting: {skin.Feedback.Text}; mode={session.ActivePacing}; HR={session.Measurements!.HeartRate}; time={session.SimulationTimeNs}");
            skin.Rate.Value = 80;
            skin.PacingType.SelectedIndex = (int)PacingIllustration.RightVentricularVvi;
            skin.ApplyPacing.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            Require(session.ActivePacing == PacingIllustration.RightVentricularVvi &&
                session.Measurements!.HeartRate.MilliBeatsPerMinute is > 79000 and < 81000, "reapply changes pacing type and rate");
            skin.Rate.Value = null;
            skin.StopPacing.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ulong stoppedRelays = window.Settings.Sound.TherapyRelayCount;
            for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            Require(window.Settings.Sound.TherapyRelayCount == stoppedRelays, "stopped pacing does not publish more relay sounds");
            Require(session.ActivePacing is null && session.Measurements!.HeartRate.MilliBeatsPerMinute is > 74000 and < 76000,
                "stop remains available with invalid drafts and restores the original rhythm");
            var reopened = new DesignPreviewWindow(path);
            try
            {
                var restored = ((GenericMonitorSkin)reopened.MonitorTrace.Skin!).Read();
                Require(restored == new MonitorTherapyPreferences(200, 80, 60, PacingIllustration.RightVentricularVvi) &&
                    reopened.Session.ActivePacing is null, "therapy parameters survive relaunch but running state does not");
            }
            finally { reopened.Close(); }
        }
        finally
        {
            window.Close();
            File.Delete(path);
            File.Delete(Path.ChangeExtension(path, ".therapy.json"));
            File.Delete(Path.ChangeExtension(path, ".language.json"));
        }
        VerifyTemplateSwitchClearsPacing();
    }

    private static void VerifyTemplateSwitchClearsPacing()
    {
        // Active output, a pending start, and a full restart must all project
        // the new template's state rather than retain the pacing command UI.
        for (int scenario = 0; scenario < 3; scenario++)
        {
            var window = new DesignPreviewWindow();
            window.Settings.PacingPermissions.Allowed.IsChecked = true;
            window.RestartSettings();
            window.Show();
            try
            {
                GenericMonitorSkin Skin() => (GenericMonitorSkin)window.MonitorTrace.Skin!;
                void Advance()
                {
                    for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                }
                var skin = Skin();
                Require(!skin.StopPacing.IsEnabled, "idle monitor cannot stop nonexistent pacing");
                skin.Current.Value = 60;
                skin.Rate.Value = 90;
                skin.PacingType.SelectedIndex = (int)PacingIllustration.DualChamberDdd;
                skin.ApplyPacing.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(!skin.ApplyPacing.IsEnabled && !skin.StopPacing.IsEnabled, "pending pacing waits for the acquisition boundary");
                if (scenario != 1)
                {
                    Advance();
                    Require(skin.StopPacing.IsEnabled && skin.PacingStatus.Text == window.Localization.Get("skin.pacingRunning") &&
                        !skin.Feedback.IsVisible, "running state replaces the old queued-command feedback");
                    window.Settings.ApplyDelaySeconds.Value = null;
                    window.ApplySettings();
                    Require(ReferenceEquals(skin, Skin()) && skin.StopPacing.IsEnabled && window.Session.ActivePacing is not null,
                        "rejected settings preserve the running pacing state and stop control");
                }
                window.SelectPage(2);
                window.Settings.EcgSelection = 72;
                window.Settings.PacingPermissions.Allowed.IsChecked = true;
                window.Settings.ApplyDelaySeconds.Value = 2;
                if (scenario == 2) { window.RestartSettings(); }
                else
                {
                    window.Pause();
                    window.ApplySettings();
                    Require(!Skin().StopPacing.IsEnabled && !Skin().ApplyPacing.IsEnabled &&
                        Skin().PacingStatus.Text == window.Localization.Get("skin.pacingWaiting"),
                        "a paused template replacement shows pending state instead of active pacing controls");
                    window.Start();
                }
                Advance();
                window.SelectPage(0);
                skin = Skin();
                Require(window.Session.ActivePacing is null && window.Session.Measurements!.HeartRate.MilliBeatsPerMinute is null &&
                    !skin.StopPacing.IsEnabled && skin.ApplyPacing.IsEnabled &&
                    skin.PacingStatus.Text == window.Localization.Get("skin.pacingStopped"),
                    "template replacement clears pacing UI and plays the newly selected flatline");
                skin.ApplyPacing.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Advance();
                Require(skin.StopPacing.IsEnabled && window.Session.ActivePacing == PacingIllustration.DualChamberDdd,
                    "pacing can start again after replacing a template");
                skin.StopPacing.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Advance();
                Require(!skin.StopPacing.IsEnabled && window.Session.ActivePacing is null &&
                    window.Session.Measurements!.HeartRate.MilliBeatsPerMinute is null,
                    "stop restores the replacement template rather than the original pre-switch template");
            }
            finally { window.Close(); }
        }
    }

    internal static void VerifyMonitoringCapabilities()
    {
        var alerts = new MonitorAlertSettings();
        alerts.HeartRateEnabled.IsChecked = true;
        alerts.AdditionalLimits.Editors[MonitorNumeric.AbpMean].Enabled.IsChecked = true;
        var empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
        var snapshot = empty with
        {
            HeartRate = new(WaveformMeasurementStatus.Valid, 190000, 0),
            AbpMean = new(WaveformMeasurementStatus.Valid, 3000, 0, 0),
            EcgMonitoring = empty.EcgMonitoring with
            {
                Status = WaveformMeasurementStatus.Valid,
                ActiveConditions = EcgMonitoringConditions.SuspectedVentricularFibrillation
            }
        };
        for (int i = 0; i < 30; i++) { _ = alerts.Notices(snapshot with { SampleTimeNs = i * 200_000_000L }).ToArray(); }
        Require(alerts.AlarmLifecycles.Any(j => j.Attention.Conditions.Any(c => c.State != AlarmAttentionState.None)), "fixture starts actual alarms");
        alerts.SetMonitoredChannels(MonitorChannels.Resp);
        Require(alerts.Notices(snapshot with { SampleTimeNs = 6_000_000_000 }).All(n => n.Id != "ecg-vf" && n.Numeric != MonitorNumeric.HeartRate && n.Numeric != MonitorNumeric.AbpMean) &&
            !alerts.ProjectAttention([]).Any() && alerts.AlarmLifecycles.All(j => j.Attention.Conditions.All(c => c.State == AlarmAttentionState.None)),
            "unmonitored channels end alarm lifecycles, pending confirmations and retained indications");
        Require(alerts.HeartRateEnabled.IsChecked == true && !alerts.HeartRateEnabled.IsEnabled &&
            alerts.CapturePreferences().HeartRate.Enabled, "effective disable preserves the user's saved alarm preference");
        alerts.SetMonitoredChannels(MonitorChannels.All);
        Require(alerts.HeartRateEnabled.IsEnabled && alerts.Notices(snapshot with { SampleTimeNs = 7_000_000_000 }).Any(n => n.Id == "ecg-vf"),
            "reconnected channels restart monitoring from fresh observations");
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            Require(!window.Settings.Alerts.AdditionalLimits.Editors[MonitorNumeric.AbpMean].Enabled.IsEnabled,
                "generic four-row monitor does not monitor absent ABP");
            foreach (var slot in window.Settings.Slots) { slot.Channel.SelectedIndex = 1; }
            window.RestartSettings();
            Require(window.Settings.Alerts.MonitoredChannels == MonitorChannels.Resp && !window.Settings.Alerts.EcgMonitoringEnabled.IsEnabled,
                "applied row bindings determine the host's effective monitored channel set");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
