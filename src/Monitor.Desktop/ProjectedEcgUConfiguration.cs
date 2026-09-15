// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Explicit chest electrode source amplitudes. Limb electrode U potentials stay
// zero, so chest lead U amplitudes equal these values without changing QT.
internal sealed record ProjectedEcgUConfiguration(int DelayMilliseconds = 30, int DurationMilliseconds = 120,
    int V1 = 0, int V2 = 0, int V3 = 0, int V4 = 0, int V5 = 0, int V6 = 0)
{
    internal static ProjectedEcgUConfiguration Default { get; } = new();
    internal int[] ChestAmplitudes => [V1, V2, V3, V4, V5, V6];

    internal EcgUWavePlan? Resolve(EcgCycleTiming timing)
    {
        if (DelayMilliseconds is < 0 or > 1000 || DurationMilliseconds is < 1 or > 1000 ||
            ChestAmplitudes.Any(value => value is < -1000 or > 1000))
        { throw new ArgumentException("U input outside demo bounds."); }
        if (ChestAmplitudes.All(value => value == 0)) { return null; }
        EcgUWavePlan plan = new(DelayMilliseconds * 1_000_000L, DurationMilliseconds * 1_000_000L,
            [0, 0, 0, 0, V1, V2, V3, V4, V5, V6]);
        plan.Validate(timing);
        return plan;
    }
}
