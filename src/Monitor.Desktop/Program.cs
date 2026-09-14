// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;

namespace Monitor.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 0 && !(args.Length == 1 && args[0] is "--smoke-test" or "--study-demo" or "--waveform-demo" or "--physiology-demo"))
        {
            Console.Error.WriteLine("Usage: Monitor.Desktop [--smoke-test | --study-demo | --waveform-demo | --physiology-demo]");
            return 2;
        }
        return AppBuilder.Configure<MonitorApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
