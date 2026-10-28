// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Monitor.Desktop;

// Native windows may be constrained by the desktop work area. Layout assertions
// use an explicit content viewport, independent of screen resolution and DPI.
internal static class DesktopViewportSmokeChecks
{
    internal static void Layout(Window window, double width = 1440, double height = 940,
        bool processPendingEvents = true)
    {
        var root = (Control)window.Content!;
        root.Width = width;
        root.Height = height;
        // Manually driven snapshots must not consume unrelated input or timers.
        if (processPendingEvents) { Dispatcher.UIThread.RunJobs(); }
        root.InvalidateMeasure();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
    }

    internal static void WaitFor(Func<bool> completed, string message)
    {
        if (completed()) { return; }
        // Native frames drive Fluent transitions and compositor hit testing.
        var elapsed = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        poll.Tick += (_, _) =>
        {
            if (completed() || elapsed.Elapsed > TimeSpan.FromSeconds(3)) { frame.Continue = false; }
        };
        poll.Start();
        try { Dispatcher.UIThread.PushFrame(frame); }
        finally { poll.Stop(); }
        if (!completed()) { throw new InvalidOperationException(message); }
    }
}
