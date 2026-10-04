// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Interactivity;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Desktop;

internal static class MonitorContinuationSmokeChecks
{
    internal static void Verify()
    {
        string path = Path.Combine(Path.GetTempPath(), "monitor-continuation-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new DesignPreviewWindow(path);
        window.Show();
        try
        {
            var timer = window.ActiveTimer;
            for (int i = 0; i < 180; i++) { window.Pulse(timer, 50_000_000); }
            var session = window.Session;
            var blocks = session.Blocks.ToArray();
            var readings = session.Measurements;
            long time = session.SimulationTimeNs;
            long frontier = session.FrontierNs;
            window.Settings.ApplyDelaySeconds.Value = 1.3m;
            window.Settings.EcgSelection = 72;
            window.Settings.Apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(ReferenceEquals(session, window.Session) && ReferenceEquals(timer, window.ActiveTimer) &&
                session.SimulationTimeNs == time && session.FrontierNs == frontier && session.Blocks.SequenceEqual(blocks) &&
                session.Measurements == readings && session.PendingSourceTimeNs == time + 1_400_000_000,
                "default Apply queues a source change and retains live time, history, readings and timer");
            Require(new DisplayPreferenceStore(path).Load(out _).Generator?.ApplyDelaySeconds == 1.3m,
                "configured continuation delay persists");
            window.Pause();
            long? pending = session.PendingSourceTimeNs;
            window.Pulse(timer, 250_000_000);
            Require(session.SimulationTimeNs == time && session.PendingSourceTimeNs == pending, "pause also pauses the delay");
            window.Settings.ApplyDelaySeconds.Value = 0;
            window.Settings.Apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(window.ActiveTimer is null && session.PendingSourceTimeNs == time, "Apply while paused preserves pause and replaces pending settings");
            window.Settings.ApplyDelaySeconds.Value = null;
            window.Settings.Apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(session.PendingSourceTimeNs == time && window.Settings.Status.Text!.Contains("未应用", StringComparison.Ordinal),
                "invalid delay preserves the prior pending change");
            window.Settings.ApplyDelaySeconds.Value = 0;
            window.Settings.EcgSelection = 0; // A newer draft must not relabel the queued source.
            window.Start();
            for (int i = 0; i < 100; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            Require(ReferenceEquals(session, window.Session) && session.PendingSourceTimeNs is null &&
                session.Samples(0, time + 2_000_000_000, time + 2_500_000_000).All(s => s.Value == 0),
                "new waveform reaches the continuing scan without replacing the session");
            Require(window.Settings.AppliedEcgParameters.Text!.Contains("全心静止", StringComparison.Ordinal),
                "applied overview describes the queued snapshot, not a subsequently edited draft");
            window.Settings.EcgSelection = 0;
            window.Settings.ApplyDelaySeconds.Value = 60;
            window.ApplySettings();
            timer = window.ActiveTimer;
            window.Settings.Restart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!ReferenceEquals(session, window.Session) && window.Session.PendingSourceTimeNs is null &&
                window.Session.SimulationTimeNs == 0 && window.Session.Blocks.Count == 0,
                "From the beginning explicitly clears history and pending changes");
            window.Pulse(timer, 50_000_000);
            Require(window.Session.SimulationTimeNs == 0, "old timer cannot advance the restarted session");
        }
        finally { window.Close(); File.Delete(path); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException(message); } }
}
