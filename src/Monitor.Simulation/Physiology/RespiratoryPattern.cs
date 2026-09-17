// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

public enum RespiratoryPattern { Regular, CheyneStokesIllustration }

// Authored eleven-slot example: nine crescendo/decrescendo breaths, then
// two absent slots. Depth is relative effort, not tidal volume or gas pressure.
internal static class RespiratoryPatternDepth
{
    internal static int At(RespiratoryPattern pattern, ulong cycle) => pattern switch
    {
        RespiratoryPattern.Regular => 1000,
        RespiratoryPattern.CheyneStokesIllustration => (cycle % 11) switch
        {
            0 or 8 => 200,
            1 or 7 => 400,
            2 or 6 => 600,
            3 or 5 => 800,
            4 => 1000,
            _ => 0,
        },
        _ => throw new PhysiologyTimelineException("PhysiologyTimeline.InvalidPattern", "pattern"),
    };
    internal static Int128 ActiveBefore(Int128 slot) => slot / 11 * 9 + Int128.Min(slot % 11, 9);
}
