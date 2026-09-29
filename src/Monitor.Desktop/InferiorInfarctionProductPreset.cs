// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Independent teaching snapshots, never elapsed-time progression.
internal static class InferiorInfarctionProductPreset
{
    internal static EcgChestInfarctionPlan Create(int index)
    {
        if (index is < 0 or > 9) { throw new ArgumentOutOfRangeException(nameof(index)); }
        return new(0, (InfarctionIllustrationStage)(index + 1), InfarctionTerritory.Inferior);
    }
}
