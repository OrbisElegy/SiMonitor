// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

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
            window.SelectPage(2);
            var editor = window.Settings;
            editor.Tabs.SelectedIndex = 1;
            var templates = editor.TemplatePages[0];
            templates.Search.Text = "窦";
            DesktopViewportSmokeChecks.Layout(window);
            var cards = templates.CardButtons.ToArray();
            var scroll = cards[0].GetVisualAncestors().OfType<ScrollViewer>().First();
            scroll.Offset = new(0, 100);
            DesktopViewportSmokeChecks.Layout(window);
            var offset = scroll.Offset;
            int disabled = 0;
            int detached = 0;
            editor.PropertyChanged += (_, change) =>
            {
                if (change.Property == Control.IsEnabledProperty && !editor.IsEnabled) { disabled++; }
            };
            editor.DetachedFromVisualTree += (_, _) => detached++;
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
            Require(!pending.IsCompleted && window.IsApplying && editor.IsEnabled && templates.Search.IsEffectivelyEnabled,
                "apply yields before preparation without disabling the settings page");
            Require(!editor.Apply.IsEnabled && !editor.Restart.IsEnabled && !editor.ResetAll.IsEnabled,
                "preparation disables conflicting commands only");
            Require(window.ApplySettingsAsync().IsCompleted, "a duplicate apply cannot start another worker");
            Require(window.ApplySettingsAsync(restart: true).IsCompleted, "restart cannot race an active apply");
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
            DesktopViewportSmokeChecks.Layout(window);
            Require(disabled == 0 && detached == 0 && ReferenceEquals(editor, window.Settings) &&
                editor.Tabs.SelectedIndex == 1 && templates.Search.Text == "窦" &&
                templates.CardButtons.SequenceEqual(cards) && scroll.Offset == offset,
                "apply preserves the attached settings page, template cards, search and scroll position");
            Require(editor.Apply.IsEnabled && editor.Restart.IsEnabled && editor.ResetAll.IsEnabled,
                "successful apply restores all conflicting commands");
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
            Require(editor.Apply.IsEnabled && editor.Restart.IsEnabled && editor.ResetAll.IsEnabled && disabled == 0 && detached == 0,
                "validation failure restores commands without refreshing the settings page");
            window.Settings.CardiacRateEnabled.IsChecked = false;
            editor.EcgSelection = 1;
            var restarting = window.ApplySettingsAsync(restart: true);
            editor.EcgSelection = 0;
            Wait(() => restarting.IsCompleted);
            restarting.GetAwaiter().GetResult();
            Require(!ReferenceEquals(session, window.Session) && editor.EcgSelection == 0 &&
                editor.AppliedEcgParameters.Text!.Contains(window.Localization.Current.GetString(DesignPreviewSettings.EcgTemplateKey(1)), StringComparison.Ordinal),
                "restart publishes the captured template while preserving edits made during preparation");
            Require(editor.Apply.IsEnabled && editor.Restart.IsEnabled && editor.ResetAll.IsEnabled && disabled == 0 && detached == 0,
                "restart restores commands without disabling or detaching settings");
            session = window.Session;
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
