// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Explicit electrode repolarization gains, before lead projection. This is a
// source shape parameter, not a disease label or a post-render lead override.
public sealed record EcgTWaveScalePlan(IReadOnlyList<int> ElectrodeScalePermille)
{
    internal int[] CaptureScales()
    {
        if (ElectrodeScalePermille is null || ElectrodeScalePermille.Count != 10)
        { throw Invalid(); }
        int[] scales = ElectrodeScalePermille.ToArray();
        if (scales.Any(value => value is < -4000 or > 4000)) { throw Invalid(); }
        return scales;
    }

    private static EventWaveformException Invalid() => new("EcgTWave.InvalidScale", "tWave");
}
