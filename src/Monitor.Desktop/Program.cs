// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;

namespace Monitor.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--smoke-shard")
        {
            if (!NativeSmokePartition.Configure(args)) { return 2; }
            args = ["--smoke-test"];
        }
        if (args.Length != 0 && !(args.Length == 1 && args[0] is "--ui-preview" or "--smoke-test" or "--study-demo" or "--waveform-demo" or "--physiology-demo" or "--electrode-demo"))
        {
            Console.Error.WriteLine("Usage: Monitor.Desktop [--ui-preview | --smoke-test | --study-demo | --waveform-demo | --physiology-demo | --electrode-demo]");
            return 2;
        }
        return AppBuilder.Configure<MonitorApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
