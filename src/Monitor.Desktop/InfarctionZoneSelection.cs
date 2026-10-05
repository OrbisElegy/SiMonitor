// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Localization;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class InfarctionZoneSelection
{
    internal static string[] Names => ["关闭", "V1", "V2", "V3", "V4", "V5", "V6", "V1–V3", "V3–V5", "V1–V5", "下壁 II/III/aVF", "侧壁 I/aVL/V5/V6"];
    // Names that need translation are catalog keys; lead names are shown as written.
    private static readonly string[] NameKeys = ["infarction.zoneOff", "V1", "V2", "V3", "V4", "V5", "V6", "V1–V3", "V3–V5", "V1–V5", "infarction.zoneInferior", "infarction.zoneLateral"];
    internal static string Name(ITextLocalizer text, int index) => DesktopLocalization.Label(text, NameKeys[index]);
    internal static IEnumerable<Func<ITextLocalizer, string>> LocalizedNames => NameKeys.Select(key => (Func<ITextLocalizer, string>)(text => DesktopLocalization.Label(text, key)));
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
