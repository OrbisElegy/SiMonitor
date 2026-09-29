// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Fixed authoring examples; the shared contour plan retains its full parameter API.
internal static class TContourProductPreset
{
    internal static EcgTContourPlan Create(int index)
    {
        if (index is < 0 or > 7) { throw new ArgumentOutOfRangeException(nameof(index)); }
        var shape = (EcgTContourShape)(index + 1);
        return new(1, shape, index switch { 4 or 5 => 600, 6 => 80, _ => 300 }, EcgTContourTarget.II,
            index == 0 ? 350 : index == 1 ? 650 : null, index <= 1 ? 200 : null);
    }
}
