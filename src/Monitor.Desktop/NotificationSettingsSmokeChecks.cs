// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class NotificationSettingsSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow(); window.Show(); window.Pause();
        try
        {
            var alerts = window.Settings.Alerts;
            var panel = alerts.NotificationSettings;
            var sound = window.Settings.Sound;
            Require(panel.Editors.Keys.Order(StringComparer.Ordinal).SequenceEqual(MonitorAlarmPreferences.NotificationConditionIds),
                "every registered condition has an independent editor");
            alerts.HeartRateEnabled.IsChecked = true;
            panel.Mode.SelectedIndex = 1;
            var high = panel.Editors["hr-high"];
            var otherEditor = new AlarmNotificationSettingsPanel.ConditionEditor(high.Label);
            otherEditor.SoundChoices[1].IsChecked = true;
            Require(high.SelectedSoundMode == 0 && otherEditor.SelectedSoundMode == 1,
                "separate editors for the same condition never share a radio group");
            high.RepeatSeconds.Value = 1.125m;
            high.ReminderSeconds.Value = .5m;
            high.ReminderEnabled.IsChecked = true;
            var journal = alerts.AlarmLifecycles.Single(j => j.Conditions.Any(c => c.ConditionId == "hr-high"));
            var empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
            void Read(long milliseconds, int rate)
            {
                var snapshot = empty with { SampleTimeNs = milliseconds * 1_000_000, HeartRate = new(WaveformMeasurementStatus.Valid, rate, milliseconds * 1_000_000) };
                window.MonitorView.RefreshReadings(snapshot);
                sound.UpdateAlarm(window.MonitorView.HighestNotice, alerts.Timing, measurement: snapshot, notices: window.MonitorView.ActiveNotices);
            }
            Read(0, 130000);
            Require(sound.PublishedAlarm is { NotificationSequence: > 0 }, "user-selected mode reaches single-group playback");
            Read(100, 100000); Read(200, 130000);
            Require(sound.PublishedAlarm is null && window.MonitorView.ActiveNotices.Any(n => n.Id == "hr-high"), "editor suppression silences recurrence without hiding physiology");
            Read(300, 180001);
            Require(sound.PublishedAlarm is { Level: MonitorNoticeLevel.Critical }, "user policy retains escalation bypass");
            var episode = journal.Conditions.Single(c => c.ConditionId == "hr-high").Episode;
            var policy = journal.NotificationPolicyFor("hr-high");
            high.RepeatSeconds.Value = null;
            var sections = window.Settings.SectionPages[4];
            sections.SelectedSection = 1;
            alerts.HeartRateConfirmation.Groups.SelectedIndex = 0;
            panel.Overview[MonitorNumeric.HeartRate].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Require(high.Advanced.IsExpanded && panel.SummaryFor("hr-high").Contains("待修正", StringComparison.Ordinal) &&
                sections.SelectedSection == 0 && ReferenceEquals(alerts.HeartRateConfirmation.Groups.SelectedItem, alerts.SoundPages[MonitorNumeric.HeartRate]),
                "invalid draft exposes its fields, stays marked in the overview, and the overview opens the parameter sound page");
            bool rejected = false;
            try { alerts.CapturePreferences(); } catch (ArgumentException) { rejected = true; }
            Require(rejected && high.RepeatSeconds.Value is null && journal.NotificationPolicyFor("hr-high") == policy,
                "invalid draft survives navigation, blocks saving and preserves the effective policy");
            var session = window.Session;
            window.RestartSettings();
            Require(ReferenceEquals(session, window.Session) && window.Settings.Status.Text!.Contains("通知策略", StringComparison.Ordinal) &&
                journal.Conditions.Single(c => c.ConditionId == "hr-high").Episode == episode,
                "invalid policy blocks restart before replacing session or clearing episodes even without a preference path");
            high.RepeatSeconds.Value = 2;
            Read(400, 180001);
            Require(journal.Conditions.Single(c => c.ConditionId == "hr-high").Episode == episode &&
                journal.NotificationRecords[^1].Decision.Kind == AlarmNotificationKind.PolicyChanged,
                "valid policy edits reschedule notification without reconfirming physiological evidence");
            high.ReminderEnabled.IsChecked = false;
            Require(high.Read() == new AlarmNotificationSettings(2000, false, 500) &&
                journal.NotificationPolicyFor("hr-high").ReminderMilliseconds == 0 && !high.ReminderSeconds.IsEnabled,
                "disabling reminders retains the interval while stopping runtime reminders");
            var beforeMode = journal.Conditions.Single(c => c.ConditionId == "hr-high").Episode;
            panel.Mode.SelectedIndex = 0;
            Require(sound.PublishedAlarm is { NotificationSequence: 0 }, "continuous selection immediately restores legacy playback");
            panel.Mode.SelectedIndex = 1;
            Read(500, 180001);
            Require(sound.PublishedAlarm is { NotificationSequence: > 0 } && journal.Conditions.Single(c => c.ConditionId == "hr-high").Episode == beforeMode,
                "switching playback mode leaves the active episode intact");
            var saved = alerts.CapturePreferences();
            bool invalidRestore = false;
            try { alerts.RestorePreferences(saved with { Notifications = null! }); } catch (ArgumentException) { invalidRestore = true; }
            Require(invalidRestore && journal.Conditions.Single(c => c.ConditionId == "hr-high").Episode == beforeMode,
                "invalid saved policy is rejected before resetting live state");
            Require(!sound.Muted, "selecting notification mode preserves master mute preference");

            high.SoundChoices[1].IsChecked = true;
            panel.Mode.SelectedIndex = 0;
            Read(600, 180001);
            Require(sound.PublishedAlarm is { NotificationSequence: > 0 } && high.RepeatSeconds.IsEnabled,
                "explicit short high-limit alarm overrides the long global default");
            Read(700, 30000);
            Require(sound.PublishedAlarm is { NotificationSequence: 0 }, "low-limit event independently inherits long playback");
            panel.Editors["hr-low"].SelectedSoundMode = 1;
            Read(800, 30000);
            Require(sound.PublishedAlarm is { NotificationSequence: > 0 }, "low-limit duration can be changed independently");
            high.SoundChoices[2].IsChecked = true;
            Read(900, 180001);
            panel.Mode.SelectedIndex = 1;
            Require(sound.PublishedAlarm is { NotificationSequence: 0, Level: MonitorNoticeLevel.Critical } &&
                !high.RepeatSeconds.IsEnabled && !high.ReminderEnabled.IsEnabled && high.Read().ReminderMilliseconds == 500,
                "explicit long high-limit alarm overrides short default and retains dormant short settings");

            window.SelectPage(2); window.Settings.Tabs.SelectedIndex = 4;
            sections.SelectedSection = 0;
            alerts.ShowSoundPage(MonitorNumeric.HeartRate);
            high.Advanced.IsExpanded = false;
            panel.Editors["hr-low"].Advanced.IsExpanded = false;
            window.ApplySettings();
            Capture("event-long-compact", 1000, 720);
            var scroll = high.GetVisualAncestors().OfType<ScrollViewer>().First();
            Require(scroll.Extent.Width <= scroll.Viewport.Width + 1 &&
                high.SoundChoices.Concat(panel.Editors["hr-low"].SoundChoices).All(c => c.GetVisualAncestors().Contains(window.Settings)) &&
                !panel.Editors["spo2-low"].SoundChoices[0].GetVisualAncestors().Contains(window.Settings) &&
                !panel.Mode.GetVisualAncestors().Contains(window.Settings),
                $"compact parameter sound page exposes only this parameter's events without horizontal scrolling: extent={scroll.Extent}, viewport={scroll.Viewport}");
            Require(panel.SummaryFor("hr-high").Contains("长警报（独立）", StringComparison.Ordinal) &&
                high.SoundChoices.Count(c => c.IsChecked == true) == 1,
                "event summary resolves the applied independent duration and radio choice stays exclusive");
            high.SoundChoices[0].IsChecked = true;
            Require(high.Read().SoundMode == AlarmSoundMode.Inherit && high.Read().ReminderMilliseconds == 500 &&
                panel.SummaryFor("hr-high").Contains("短警报（默认）", StringComparison.Ordinal),
                "following default updates the effective summary without clearing advanced values");
            Capture("event-inherit-compact", 1000, 720);
            sections.SelectedSection = alerts.Parameters.Count + 2;
            Capture("default-compact", 1000, 720);
            Require(panel.Mode.GetVisualAncestors().Contains(window.Settings) &&
                panel.Overview[MonitorNumeric.HeartRate].GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => text.Text?.Contains("高限 短警报（默认）", StringComparison.Ordinal) == true),
                "visible overview summary refreshes when a radio choice changes");
            var overviewScroll = panel.GetVisualAncestors().OfType<ScrollViewer>().First();
            Require(overviewScroll.Extent.Width <= overviewScroll.Viewport.Width + 1, "compact overview avoids horizontal scrolling");
            sections.SelectedSection = 0;
            high.SoundChoices[1].IsChecked = true;
            high.Advanced.IsExpanded = true;
            Capture("event-short-expanded", 1440, 940);
            Require(panel.SummaryFor("hr-high").Contains("短警报（独立）", StringComparison.Ordinal) &&
                high.RepeatSeconds.GetVisualAncestors().Contains(window.Settings),
                "summary distinguishes independent short duration and expanded fields stay on the parameter page");
            high.Advanced.IsExpanded = false;
            Capture("event-short-minimum", 960, 640);
            Require(scroll.Extent.Width <= scroll.Viewport.Width + 1, "minimum window wraps choices without horizontal scrolling");

            void Capture(string name, int width, int height)
            {
                window.Width = width;
                window.Height = height;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var root = (Control)window.Content!;
                root.InvalidateMeasure();
                root.Measure(new Size(width, height));
                root.Arrange(new Rect(0, 0, width, height));
                using var image = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
                image.Render(root);
                Directory.CreateDirectory("artifacts/alarm-ui-review-2026-10-04");
                image.Save($"artifacts/alarm-ui-review-2026-10-04/{name}.png", PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Notification settings: " + message); } }
}
