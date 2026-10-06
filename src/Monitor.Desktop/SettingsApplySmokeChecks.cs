// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Monitor.Desktop;

internal static class SettingsApplySmokeChecks
{
    // Exact-time lifecycle tests use the shared synchronous core; Verify below
    // exercises the asynchronous UI path and continuing dispatcher separately.
    internal static void ApplySynchronously(DesignPreviewWindow window, bool restart = false)
    {
        if (restart) { window.RestartSettings(); }
        else { window.ApplySettings(); }
    }

    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            // Reported Windows configuration: sinus arrest, 5% rate variation,
            // both pressure targets, realtime oxygenation and a three-second delay.
            window.Settings.EcgSelection = 1;
            window.Settings.CardiacRateEnabled.IsChecked = true;
            window.Settings.RateVariation.Value = 5;
            window.Settings.OpticalEnabled.IsChecked = true;
            window.Settings.Oxygenation.Realtime.IsChecked = true;
            window.Settings.ApplyDelaySeconds.Value = 3;
            var session = window.Session;
            long before = session.SimulationTimeNs;
            var elapsed = Stopwatch.StartNew();
            var pending = window.ApplySettingsAsync();
            long dispatchMilliseconds = elapsed.ElapsedMilliseconds;
            Require(!pending.IsCompleted && window.IsApplying && !window.Settings.IsEnabled, "apply yields before preparation and guards duplicate commands");
            Require(window.ApplySettingsAsync().IsCompleted, "a duplicate apply cannot start another worker");
            bool responsive = false;
            Dispatcher.UIThread.Post(() =>
            {
                responsive = true;
                window.Pulse(window.ActiveTimer, 50_000_000);
            }, DispatcherPriority.Send);
            Wait(() => pending.IsCompleted);
            pending.GetAwaiter().GetResult();
            Require(responsive && session.SimulationTimeNs > before && ReferenceEquals(session, window.Session), "dispatcher and original monitoring continue while preparing");
            Require(window.Settings.IsEnabled && !window.IsApplying && session.PendingSourceTimeNs > session.SimulationTimeNs, "prepared sources publish with delay after completion");
            Console.WriteLine($"ok: asynchronous apply dispatch {dispatchMilliseconds}ms, preparation and publication {elapsed.ElapsedMilliseconds}ms");
            window.Settings.EcgSelection = 23;
            window.Settings.CardiacRateEnabled.IsChecked = true;
            window.Settings.HeartRate.Value = 600;
            long? accepted = session.PendingSourceTimeNs;
            var invalid = window.ApplySettingsAsync();
            Wait(() => invalid.IsCompleted);
            invalid.GetAwaiter().GetResult();
            Require(window.Settings.IsEnabled && ReferenceEquals(session, window.Session) && session.PendingSourceTimeNs == accepted,
                "background validation failure preserves accepted state and reenables editing");
            window.Settings.CardiacRateEnabled.IsChecked = false;
            var closing = window.ApplySettingsAsync(restart: true);
            window.Close();
            Wait(() => closing.IsCompleted);
            closing.GetAwaiter().GetResult();
            Require(ReferenceEquals(session, window.Session) && window.ActiveTimer is null, "closing during preparation discards the result without restarting monitoring");
        }
        finally { window.Close(); }
    }

    private static void Wait(Func<bool> done)
    {
        if (done()) { return; }
        var frame = new DispatcherFrame();
        var elapsed = Stopwatch.StartNew();
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
        poll.Tick += (_, _) => { if (done() || elapsed.Elapsed > TimeSpan.FromSeconds(30)) { frame.Continue = false; } };
        poll.Start();
        try { Dispatcher.UIThread.PushFrame(frame); }
        finally { poll.Stop(); }
        Require(done(), "asynchronous settings operation completes");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
