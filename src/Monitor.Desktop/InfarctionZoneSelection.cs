// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class InfarctionZoneSelection
{
    internal static string[] Names => ["关闭", "V1", "V2", "V3", "V4", "V5", "V6", "V1–V3", "V3–V5", "V1–V5", "下壁 II/III/aVF", "侧壁 I/aVL/V5/V6"];
    internal static EcgInfarctionRegion Resolve(int index) => index switch
    {
        0 => new(),
        >= 1 and <= 6 => new(1 << (index - 1)),
        7 => new(0, InfarctionTerritory.Anteroseptal),
        8 => new(0, InfarctionTerritory.Anterior),
        9 => new(0, InfarctionTerritory.ExtensiveAnterior),
        10 => new(0, InfarctionTerritory.Inferior),
        11 => new(0, InfarctionTerritory.Lateral),
        _ => throw new ArgumentException("Invalid zone selection."),
    };
    internal static int Index(EcgInfarctionRegion region) => Enumerable.Range(0, 12).Single(i => Resolve(i) == region);
}
