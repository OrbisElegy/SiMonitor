// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;

namespace Monitor.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 0 && !(args.Length == 1 && args[0] == "--smoke-test"))
        {
            Console.Error.WriteLine("Usage: Monitor.Desktop [--smoke-test]");
            return 2;
        }
        return AppBuilder.Configure<MonitorApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
