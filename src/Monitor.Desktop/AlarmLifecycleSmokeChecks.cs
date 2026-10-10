// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class AlarmLifecycleSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Settings.Skin.SelectedIndex = 2;
        window.RestartSettings();
        window.Show();
        window.Pause();
        try
        {
            var alerts = window.Settings.Alerts;
            alerts.HeartRateEnabled.IsChecked = true;
            alerts.SpO2Enabled.IsChecked = true;
            alerts.NoExpirationEnabled.IsChecked = true;
            alerts.NoExpirationSeconds.Value = 5;
            var immediate = new MeasurementConfirmationTiming(new(0, 0), new(0, 0), new(0, 0), new(0, 0));
            foreach (var editor in alerts.AdditionalLimits.Editors.Values)
            {
                editor.Enabled.IsChecked = true;
                editor.Confirmation.Restore(immediate);
            }
            var snapshot = LiveWaveformMeasurements.CreateIllustration().Read(0) with
            {
                SampleTimeNs = 5_000_000_000,
                HeartRate = new(WaveformMeasurementStatus.Valid, 39999, 5_000_000_000),
                SpO2 = new(WaveformMeasurementStatus.Valid, 84999, null, 5_000_000_000),
                ImpedanceRespiration = new(WaveformMeasurementStatus.Valid, 3999, 5_000_000_000),
                PulseRate = new(WaveformMeasurementStatus.Valid, 39999, 5_000_000_000),
                Capnography = new(new(WaveformMeasurementStatus.Valid, 1499, 5_000_000_000),
                    new(WaveformMeasurementStatus.Valid, 3999, 5_000_000_000))
                { Activity = new(WaveformMeasurementStatus.Valid, 0, null, 5_000_000_000) },
                AbpMean = new(WaveformMeasurementStatus.Valid, 3999, 0, 5_000_000_000),
                PaMean = new(WaveformMeasurementStatus.Valid, -1, 0, 5_000_000_000),
                CvpMean = new(WaveformMeasurementStatus.Valid, -501, 0, 5_000_000_000)
            };
            void Read()
            {
                window.MonitorView.RefreshReadings(snapshot);
                window.Settings.Sound.UpdateAlarm(window.MonitorView.HighestNotice, alerts.Timing, measurement: snapshot);
            }
            AlarmConditionSnapshot State(string id) => alerts.AlarmLifecycles.SelectMany(j => j.Conditions).Single(s => s.ConditionId == id);
            AlarmLifecycleTransition[] Events() => alerts.AlarmLifecycles.SelectMany(j => j.Transitions).ToArray();
            Read();
            Require(alerts.AlarmLifecycles.Count == 11 && alerts.AlarmLifecycles.Sum(j => j.Conditions.Count) == 42 &&
                alerts.AlarmLifecycles.SelectMany(j => j.Conditions).Count(s => s.State == AlarmConditionState.Active) == 10,
                "all existing numeric and absence conditions have independent runtime lifecycle owners");
            foreach (var notice in window.MonitorView.ActiveNotices.Where(n => n.Level == MonitorNoticeLevel.Critical))
            { Require(State(notice.Id).Level == notice.Level && State(notice.Id).Episode is not null, "displayed alarm maps to confirmed event state"); }
            Require(alerts.AlarmLifecycles.Sum(j => j.NotificationRecords.Count) == 10 &&
                alerts.AlarmLifecycles.SelectMany(j => j.NotificationRecords).All(r => r.Decision.Kind == AlarmNotificationKind.FirstOccurrence),
                "all existing alarm owners generate independent default notification intents");
            var original = Events();
            var sound = window.Settings.Sound.PublishedAlarm;
            Read(); Read();
            Require(Events().SequenceEqual(original) && window.Settings.Sound.PublishedAlarm == sound,
                "repeated frames do not restart episodes or sound");
            var spo2 = State("spo2-low").Episode;
            alerts.CriticalLowHeartRate.Value = 39;
            alerts.CriticalLowHeartRate.Value = 40;
            Require(Events().Any(t => t.ConditionId == "hr-low" && t.Kind == AlarmTransitionKind.Ended && t.Reason == AlarmTransitionReason.ConfigurationChanged) &&
                State("spo2-low").Episode == spo2, "edit and revert records a configuration end without touching another parameter");
            Read();
            alerts.NoExpirationEnabled.IsChecked = false;
            Require(Events().Any(t => t.ConditionId == "co2-no-expiration" && t.Kind == AlarmTransitionKind.Ended && t.Reason == AlarmTransitionReason.Disabled),
                "turning off an active condition is distinct from recovery");
            alerts.NoExpirationEnabled.IsChecked = true;
            Read();
            var saved = alerts.CapturePreferences();
            var beforeInvalid = Events();
            bool rejected = false;
            try { alerts.RestorePreferences(saved with { NoExpirationConfirmation = new(-1, 0) }); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected && Events().SequenceEqual(beforeInvalid), "invalid restore is atomic for event records as well as settings");
            snapshot = snapshot with
            {
                SampleTimeNs = 5_100_000_000,
                HeartRate = snapshot.HeartRate with { Status = WaveformMeasurementStatus.PoorSignal },
                Capnography = snapshot.Capnography with { Activity = null }
            };
            Read();
            Require(State("hr-low").State == AlarmConditionState.Indeterminate && State("co2-no-expiration").State == AlarmConditionState.Indeterminate &&
                State("spo2-low").Episode == spo2 && !window.MonitorView.ActiveNotices.Any(n => n.Id is "hr-low" or "co2-no-expiration"),
                "lost evidence is indeterminate while existing visual suppression and independent alarms remain consistent");
            var previous = window.Session;
            var beforeRestart = Events();
            window.Settings.CardiacRateEnabled.IsChecked = true;
            window.Settings.RateSeed.Text = "invalid";
            window.RestartSettings();
            Require(ReferenceEquals(previous, window.Session) && Events().SequenceEqual(beforeRestart), "failed restart does not end episodes");
            window.Settings.CardiacRateEnabled.IsChecked = false;
            window.RestartSettings(); window.Pause();
            Require(!ReferenceEquals(previous, window.Session) && Events().Any(t => t.Episode == spo2 &&
                t.Kind == AlarmTransitionKind.Ended && t.Reason == AlarmTransitionReason.SessionReset), "successful restart records an explicit session end");
            Require(!Events().Any(t => t.Kind == AlarmTransitionKind.Ended && t.Reason == AlarmTransitionReason.Recovered),
                "no disabled, edited, lost or restarted condition is falsely reported as recovered");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Alarm lifecycle: " + message); } }
}
