// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Monitor.Desktop;

// Native windows may be constrained by the desktop work area. Layout assertions
// use an explicit content viewport, independent of screen resolution and DPI.
internal static class DesktopViewportSmokeChecks
{
    internal static void Layout(Window window, double width = 1440, double height = 940)
    {
        var root = (Control)window.Content!;
        root.Width = width;
        root.Height = height;
        Dispatcher.UIThread.RunJobs();
        root.InvalidateMeasure();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
    }
}
