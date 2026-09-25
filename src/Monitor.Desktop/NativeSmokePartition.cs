// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Desktop;

// Each shard owns a separate process and Avalonia dispatcher. Never run controls
// concurrently on worker threads or skip checks inside an individual scenario.
internal static class NativeSmokePartition
{
    internal static int Index { get; private set; }
    private static int _count = 1, _ordinal;
    internal static bool Configure(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[1], out int index) || !int.TryParse(args[2], out int count) ||
            count is < 1 or > 32 || index < 0 || index >= count)
        { Console.Error.WriteLine("Usage: --smoke-shard INDEX COUNT (0 <= INDEX < COUNT <= 32)"); return false; }
        Index = index; _count = count; return true;
    }
    internal static void Complete() => Console.WriteLine($"native scenarios total: {_ordinal}");
    internal static void Run(Action action)
    {
        int ordinal = _ordinal++;
        if (ordinal % _count == Index) { action(); Console.WriteLine($"ok: native scenario {ordinal} (shard {Index + 1}/{_count})"); }
    }
}
