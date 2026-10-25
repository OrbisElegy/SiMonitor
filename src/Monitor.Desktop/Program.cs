// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;

namespace Monitor.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
#if SIMONITOR_RELEASE
        if (args.Length != 0)
        {
            Console.Error.WriteLine("Usage: Monitor.Desktop");
            return 2;
        }
#else
        if (args.Length > 0 && args[0] == "--product-check")
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: Monitor.Desktop --product-check ASSEMBLY_PATH");
                return 2;
            }
            try
            {
                string assemblyPath = Path.GetFullPath(args[1]);
                using var assembly = File.OpenRead(assemblyPath);
                ProductReleaseChecks.AssemblyPath = assemblyPath;
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Console.Error.WriteLine($"Cannot read product assembly '{args[1]}': {error.Message}");
                return 2;
            }
            args = ["--product-check"];
        }
        if (args.Length == 2 && args[0] == "--generate-style-previews")
        { return StylePreviewCatalog.Generate(Path.GetFullPath(args[1])); }
        if (args.Length > 0 && args[0] == "--smoke-shard")
        {
            if (!NativeSmokePartition.Configure(args)) { return 2; }
            args = ["--smoke-test"];
        }
        if (args.Length != 0 && !(args.Length == 1 && args[0] is "--product-check" or "--ui-preview" or "--smoke-test" or "--study-demo" or "--waveform-demo" or "--physiology-demo" or "--electrode-demo"))
        {
            Console.Error.WriteLine("Usage: Monitor.Desktop [--ui-preview | --smoke-test | --study-demo | --waveform-demo | --physiology-demo | --electrode-demo | --product-check ASSEMBLY_PATH]");
            return 2;
        }
#endif
        return AppBuilder.Configure<MonitorApp>().UsePlatformDetect()
            .With(new X11PlatformOptions { UseRetainedFramebuffer = true })
            .StartWithClassicDesktopLifetime(args);
    }
}
