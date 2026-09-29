// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Independent teaching snapshots, never elapsed-time progression.
internal static class InfarctionProductPreset
{
    internal static EcgChestInfarctionPlan Create(int index, InfarctionTerritory territory = InfarctionTerritory.Inferior)
    {
        if (index is < 0 or > 9) { throw new ArgumentOutOfRangeException(nameof(index)); }
        if (territory is not (InfarctionTerritory.Inferior or InfarctionTerritory.Lateral)) { throw new ArgumentOutOfRangeException(nameof(territory)); }
        return new(0, (InfarctionIllustrationStage)(index + 1), territory);
    }
}
