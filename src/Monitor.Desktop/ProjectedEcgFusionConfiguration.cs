// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed record ProjectedEcgFusionConfiguration(int ChestMask = 0, int JMicrovolts = 200,
    int PeakMicrovolts = 500, int PeakPositionPermille = 500)
{
    internal static ProjectedEcgFusionConfiguration Default { get; } = new();

    internal EcgStTFusionPlan? Resolve()
    {
        if (ChestMask is < 0 or > 63 || JMicrovolts is < -1000 or > 1000 ||
            PeakMicrovolts is < -2000 or > 2000 || PeakPositionPermille is < 1 or > 999)
        { throw new ArgumentException("Invalid chest ST-T fusion configuration."); }
        if (ChestMask == 0) { return null; }
        EcgStTFusionContour contour = new(JMicrovolts, PeakMicrovolts, PeakPositionPermille);
        return new(Enumerable.Range(0, 10).Select(i => i >= 4 && (ChestMask & (1 << (i - 4))) != 0 ? contour : null).ToArray());
    }
}
