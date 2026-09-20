// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

public enum RespiratoryPattern { Regular, CheyneStokesIllustration, IntermittentIllustration }

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
        // Authored groups of three and two equal-depth breaths separated by
        // two and three silent slots; not a complete ataxic/Biot model.
        RespiratoryPattern.IntermittentIllustration => (cycle % 10) is 0 or 1 or 2 or 5 or 6 ? 1000 : 0,
        _ => throw new PhysiologyTimelineException("PhysiologyTimeline.InvalidPattern", "pattern"),
    };
    internal static int Length(RespiratoryPattern pattern) => pattern switch
    {
        RespiratoryPattern.Regular => 1,
        RespiratoryPattern.CheyneStokesIllustration => 11,
        RespiratoryPattern.IntermittentIllustration => 10,
        _ => throw new PhysiologyTimelineException("PhysiologyTimeline.InvalidPattern", "pattern"),
    };
    internal static Int128 ActiveBefore(RespiratoryPattern pattern, Int128 slot)
    {
        int length = Length(pattern), active = 0, prefix = 0;
        for (int i = 0; i < length; i++)
        {
            if (At(pattern, (ulong)i) == 0) { continue; }
            active++;
            if (i < slot % length) { prefix++; }
        }
        return slot / length * active + prefix;
    }
}
