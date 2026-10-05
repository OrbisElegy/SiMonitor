// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Localization;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Independent teaching snapshots, never elapsed-time progression.
internal static class InfarctionProductPreset
{
    internal static string TerritoryName(InfarctionTerritory territory) => territory switch
    {
        InfarctionTerritory.Inferior => "下壁",
        InfarctionTerritory.Lateral => "侧壁",
        InfarctionTerritory.Anteroseptal => "前间壁",
        InfarctionTerritory.Anterior => "前壁",
        InfarctionTerritory.ExtensiveAnterior => "广泛前壁",
        _ => throw new ArgumentOutOfRangeException(nameof(territory))
    };
    internal static string TerritoryName(ITextLocalizer text, InfarctionTerritory territory) => text.GetString(territory switch
    {
        InfarctionTerritory.Inferior => "infarction.territoryInferior",
        InfarctionTerritory.Lateral => "infarction.territoryLateral",
        InfarctionTerritory.Anteroseptal => "infarction.territoryAnteroseptal",
        InfarctionTerritory.Anterior => "infarction.territoryAnterior",
        InfarctionTerritory.ExtensiveAnterior => "infarction.territoryExtensiveAnterior",
        _ => throw new ArgumentOutOfRangeException(nameof(territory))
    });
    internal static EcgChestInfarctionPlan Create(int index, InfarctionTerritory territory = InfarctionTerritory.Inferior)
    {
        if (index is < 0 or > 9) { throw new ArgumentOutOfRangeException(nameof(index)); }
        if (territory is not (InfarctionTerritory.Inferior or InfarctionTerritory.Lateral or InfarctionTerritory.Anteroseptal or InfarctionTerritory.Anterior or InfarctionTerritory.ExtensiveAnterior)) { throw new ArgumentOutOfRangeException(nameof(territory)); }
        return new(0, (InfarctionIllustrationStage)(index + 1), territory);
    }
}
