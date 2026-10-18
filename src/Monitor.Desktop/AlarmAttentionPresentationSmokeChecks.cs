// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class AlarmAttentionPresentationSmokeChecks
{
    internal static void Verify()
    {
        // This fixture publishes synthetic readings and checks sound requests.
        // A real device failure can asynchronously refresh the untouched session
        // over those readings when popup layout drains the dispatcher.
        var window = new DesignPreviewWindow(startAudioOutput: false);
        window.Show();
        window.Pause();
        try
        {
            var alerts = window.Settings.Alerts;
            alerts.HeartRateEnabled.IsChecked = true;
            var editor = alerts.NotificationSettings.Editors["hr-high"];
            Require(editor.LatchUntilAcknowledged.IsChecked == false,
                "the default skin starts with recovery latching disabled");
            editor.LatchUntilAcknowledged.IsChecked = true;
            var journal = alerts.AlarmLifecycles.Single(j => j.Conditions.Any(c => c.ConditionId == "hr-high"));
            var empty = LiveWaveformMeasurements.CreateIllustration().Read(0);
            LiveMeasurementSnapshot Snapshot(long milliseconds, int heartRateMilliBeatsPerMinute) => empty with
            {
                SampleTimeNs = milliseconds * 1_000_000,
                HeartRate = new(WaveformMeasurementStatus.Valid, heartRateMilliBeatsPerMinute, milliseconds * 1_000_000)
            };
            void Read(long milliseconds, int heartRateMilliBeatsPerMinute)
            {
                window.MonitorView.RefreshReadings(Snapshot(milliseconds, heartRateMilliBeatsPerMinute));
                window.Settings.Sound.UpdateAlarm(window.MonitorView.HighestNotice, alerts.Timing, notices: window.MonitorView.ActiveNotices);
            }
            AlarmAttentionSnapshot State() => journal.Attention.Conditions.Single(c => c.ConditionId == "hr-high");
            bool Confirm(AlarmAttentionSnapshot state)
            {
                bool accepted = journal.Attention.Acknowledge(state.Episode!, state.Revision);
                window.MonitorView.RefreshAttention();
                window.Settings.Sound.RefreshAlarmNotices(window.MonitorView.ActiveNotices);
                return accepted;
            }
            void VerifyAlarmActions()
            {
                var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
                var state = State();
                skin.AlarmsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(State() == state && skin.AlarmList.AcknowledgeButtons.Count > 0,
                    "opening the alarm list exposes per-event actions without acknowledging");
                skin.SilenceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(State() == state && window.Settings.Sound.PublishedAlarm is null && window.Settings.Sound.AlarmAudioPaused &&
                    !window.Settings.Sound.Muted && window.Settings.Sound.EffectiveHeartbeatVolume > 0,
                    "toolbar silence pauses only alarm sound without acknowledging or muting heartbeat");
                skin.SilenceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(State() == state && !window.Settings.Sound.AlarmAudioPaused,
                    "a second toolbar click resumes sound without changing attention");
            }
            Read(0, 130000);
            VerifyAlarmActions();
            Require(window.Settings.Sound.PublishedAlarm is { NotificationSequence: 0 },
                "default monitor retains the continuous alarm sound request");
            var old = State();
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            var staleButton = skin.AlarmList.AcknowledgeButtons["hr-high"];
            Read(50, 131000);
            Require(ReferenceEquals(staleButton, skin.AlarmList.AcknowledgeButtons["hr-high"]),
                "numeric updates keep the same event button and keyboard focus target");
            window.Localization.Select("en");
            Require(staleButton.Content?.ToString() == "Acknowledge" &&
                Avalonia.Automation.AutomationProperties.GetName(staleButton)!.StartsWith("Acknowledge alarm:", StringComparison.Ordinal),
                "alarm rows and accessible actions follow language changes while paused");
            window.Localization.Select("zh-CN");
            _ = alerts.Notices(Snapshot(100, 180001)).ToArray(); // A future skin command may hold an old snapshot.
            var escalated = State();
            Require(!Confirm(old) && State() == escalated,
                "stale command cannot acknowledge an escalation or re-observe an obsolete sample");
            staleButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(State() == escalated && skin.AlarmList.Status.IsVisible, "stale UI click is rejected with feedback");
            Read(100, 180001);
            skin.AlarmList.AcknowledgeButtons["hr-high"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!skin.AlarmList.AcknowledgeButtons.ContainsKey("hr-high"), "confirmed rows no longer offer acknowledgement");
            Require(State().State == AlarmAttentionState.ActiveAcknowledged && window.Settings.Sound.PublishedAlarm is null &&
                window.MonitorView.Notice.Text!.Contains("已确认", StringComparison.Ordinal),
                "acknowledgement immediately stops long sound while retaining marked active text");
            window.MonitorView.RefreshNumericHighlights(0);
            Require(Equals(LiveMonitorView.NumericBackground(window.MonitorView.NumericBlocks[0]), Brushes.Transparent),
                "acknowledged numerics stop blinking without changing their measured values");
            window.Settings.Sound.StartAudioPause(60);
            window.Settings.Sound.ResumeAlarmAudio.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(window.Settings.Sound.PublishedAlarm is null && State().State == AlarmAttentionState.ActiveAcknowledged,
                "pause and resume never undo acknowledgement");
            Read(200, 100000);
            Require(State().State == AlarmAttentionState.None, "acknowledged recovery clears even with visual latching enabled");
            Read(300, 180001);
            Require(State().NeedsAcknowledgement && window.Settings.Sound.PublishedAlarm is { Level: MonitorNoticeLevel.Critical },
                "recurrence has a fresh acknowledgement and sound");
            Read(400, 100000);
            Require(State().State == AlarmAttentionState.RecoveredUnacknowledged && window.Settings.Sound.PublishedAlarm is null &&
                window.MonitorView.Notice.Text!.Contains("已恢复，待确认", StringComparison.Ordinal) &&
                !window.MonitorView.Notice.Text.Contains("00:", StringComparison.Ordinal),
                "recovery retains a visibly recovered indication without sound or active elapsed counter");
            window.Width = 1000;
            window.Height = 720;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var root = (Control)window.Content!;
            root.Measure(new Size(window.Width, window.Height));
            root.Arrange(new Rect(0, 0, window.Width, window.Height));
            using var image = new RenderTargetBitmap(new PixelSize(1000, 720), new Vector(96, 96));
            image.Render(root);
            Directory.CreateDirectory("artifacts/alarm-acknowledgement-validation-2026-10-04");
            image.Save("artifacts/alarm-acknowledgement-validation-2026-10-04/recovered-compact.png", PngBitmapEncoderOptions.Default);
            skin.AlarmsButton.Flyout!.ShowAt(skin.AlarmsButton);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Require(skin.AlarmList.Bounds.Width > 0 && skin.AlarmList.Bounds.Height > 0,
                "alarm list opens in a native popup at a compact viewport");
            var popup = TopLevel.GetTopLevel(skin.AlarmList)!;
            using (var popupImage = new RenderTargetBitmap(
                new PixelSize((int)Math.Ceiling(popup.Bounds.Width), (int)Math.Ceiling(popup.Bounds.Height)), new Vector(96, 96)))
            {
                popupImage.Render(popup);
                popupImage.Save("artifacts/alarm-list.png", PngBitmapEncoderOptions.Default);
            }
            skin.AlarmsButton.Flyout.Hide();
            Require(!window.Settings.Sound.OutputStarted && window.Settings.Sound.OutputNotice is null,
                "native popup rendering does not open an audio device or overwrite the injected alarm readings");
            VerifyAlarmActions();
            var retained = State();
            editor.LatchUntilAcknowledged.IsChecked = false;
            Require(State().State == AlarmAttentionState.None && window.Settings.Sound.PublishedAlarm is null &&
                !window.MonitorView.ActiveNotices.Any(n => n.Id == MonitorAlertSettings.RetainedNoticePrefix + "hr-high") &&
                journal.Attention.Records[^1].Kind == AlarmAttentionKind.PolicyChanged,
                "turning off recovery latching clears the visible retained notice immediately without a confirmation button");
            editor.LatchUntilAcknowledged.IsChecked = true;
            Require(State().State == AlarmAttentionState.None && !Confirm(retained),
                "reenabling latching does not restore an old indication or accept its stale acknowledgement");
            Read(500, 180001);
            Read(600, 100000);
            skin.AlarmList.AcknowledgeButtons[MonitorAlertSettings.RetainedNoticePrefix + "hr-high"]
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(State().State == AlarmAttentionState.None,
                "acknowledging a retained indication clears it");
            Require(!skin.AlarmList.AcknowledgeButtons.ContainsKey(MonitorAlertSettings.RetainedNoticePrefix + "hr-high"),
                "acknowledged retained rows immediately leave the list");
            Read(700, 180001);
            window.Settings.Sound.PauseMonitor();
            Require(Confirm(State()), "current application acknowledgement succeeds");
            window.Settings.Sound.RefreshAlarmNotices([new("pause-probe", MonitorNoticeLevel.Warning, "Paused source")]);
            Require(window.Settings.Sound.PublishedAlarm is null && State().State == AlarmAttentionState.ActiveAcknowledged,
                "acknowledgement never resumes a paused monitor even when another audible source exists");
            var saved = alerts.CapturePreferences();
            alerts.RestorePreferences(saved);
            Require(editor.LatchUntilAcknowledged.IsChecked == true && State().LatchingMode == AlarmLatchingMode.UntilAcknowledged &&
                State().State == AlarmAttentionState.None && !window.Settings.Sound.Muted,
                "restoration retains the visual policy without active attention or audio opt-in");
            skin.SilenceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.RestartSettings();
            var replacement = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            Require(window.Settings.Sound.AlarmAudioPaused &&
                ((TextBlock)replacement.SilenceButton.Content!).Text == window.Localization.Get("sound.resume"),
                "a replacement skin inherits the current audio pause");
            skin.SilenceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(window.Settings.Sound.AlarmAudioPaused, "detached toolbar cannot resume the current skin's audio");
            replacement.SilenceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!window.Settings.Sound.AlarmAudioPaused, "the replacement toolbar can resume audio");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Alarm attention presentation: " + message); } }
}
