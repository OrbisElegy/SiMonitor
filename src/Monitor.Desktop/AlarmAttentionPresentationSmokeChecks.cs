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
        var window = new DesignPreviewWindow();
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
            Read(0, 130000);
            Require(!window.MonitorView.GetVisualDescendants().OfType<Button>().Any() && window.Settings.Sound.PublishedAlarm is { NotificationSequence: 0 },
                "default monitor skin retains its display-only surface without alarm action buttons");
            var old = State();
            _ = alerts.Notices(Snapshot(100, 180001)).ToArray(); // A future skin command may hold an old snapshot.
            var escalated = State();
            Require(!Confirm(old) && State() == escalated,
                "stale command cannot acknowledge an escalation or re-observe an obsolete sample");
            Read(100, 180001);
            Require(Confirm(State()), "current application acknowledgement succeeds");
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
            Require(!window.MonitorView.GetVisualDescendants().OfType<Button>().Any(),
                "compact retained indication never adds a confirmation button to the default skin");
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
            Require(Confirm(State()), "current application acknowledgement succeeds");
            Require(State().State == AlarmAttentionState.None,
                "acknowledging a retained indication clears it");
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
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Alarm attention presentation: " + message); } }
}
