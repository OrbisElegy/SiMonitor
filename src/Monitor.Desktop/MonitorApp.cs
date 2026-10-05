// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Monitor.Desktop;

public sealed class MonitorApp : Avalonia.Application
{
    public override void Initialize() => DesktopFluentStyle.Install(this);

    // Default product entry and the retained preview alias share one runtime.
    internal static Window CreateLaunchWindow(string[]? arguments, bool persistDisplay = true) => arguments switch
    {
        null or [] => new DesignPreviewWindow(persistDisplay
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Monitor", "display-preferences.json") : null),
#if !SIMONITOR_RELEASE
        ["--ui-preview"] => CreateLaunchWindow([], persistDisplay),
        ["--waveform-demo"] => new WaveformDemoWindow(),
        ["--physiology-demo"] => new WaveformDemoWindow(physiology: true),
        ["--electrode-demo"] => new WaveformDemoWindow(projected: true),
        ["--product-check"] or ["--study-demo"] or ["--smoke-test"] => new MainWindow(),
#endif
        _ => throw new ArgumentException("Desktop.UnsupportedLaunch", nameof(arguments))
    };

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateLaunchWindow(desktop.Args);
#if !SIMONITOR_RELEASE
            if (desktop.MainWindow is not MainWindow window)
            {
                base.OnFrameworkInitializationCompleted();
                return;
            }
            if (desktop.Args is ["--product-check"])
            {
                window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
                { ProductReleaseChecks.Verify(); desktop.Shutdown(0); }, DispatcherPriority.Background);
            }
            if (desktop.Args is ["--study-demo"]) { DesktopStudyDemo.Start(window); }
            if (desktop.Args is ["--smoke-test"])
            {
                window.Opened += (_, _) => Dispatcher.UIThread.Post(
                    () => desktop.Shutdown(DesktopSmokeChecks.RunSuite(window) ? 0 : 1), DispatcherPriority.Background);
            }
#endif
        }
        base.OnFrameworkInitializationCompleted();
    }
}
