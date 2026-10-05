// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Monitor.Desktop;
using Monitor.Infrastructure.Audio;

namespace Monitor.Desktop.Smoke;

// Runs the desktop smoke suite on Avalonia's headless platform with Skia
// rendering, so no display server is needed. Output and sharding match
// Monitor.Desktop --smoke-shard for tools/run_parallel_checks.py.
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && !NativeSmokePartition.Configure(args)) { return 2; }
        // Keep checks silent: no shipped native library on Linux means no device opens.
        Environment.SetEnvironmentVariable(AudioOutputSelection.EnvironmentVariable, "native");
        AppBuilder.Configure<MonitorApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        bool valid = DesktopSmokeChecks.RunSuite(window);
        window.Close();
        return valid ? 0 : 1;
    }
}
