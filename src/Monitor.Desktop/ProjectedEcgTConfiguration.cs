// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed record ProjectedEcgTConfiguration(int V1 = 1000, int V2 = 1000, int V3 = 1000,
    int V4 = 1000, int V5 = 1000, int V6 = 1000, int? PeakPositionPermille = null)
{
    internal static ProjectedEcgTConfiguration Default { get; } = new();
    internal int[] ChestScales => [V1, V2, V3, V4, V5, V6];
    internal EcgTWaveShapePlan? ResolveShape() => PeakPositionPermille is { } peak ? new(peak) : null;
    internal EcgTWaveScalePlan Resolve() => new([1000, 1000, 1000, 1000, V1, V2, V3, V4, V5, V6]);
}
