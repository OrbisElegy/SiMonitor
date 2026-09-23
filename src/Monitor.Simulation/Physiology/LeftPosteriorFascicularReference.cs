// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Fixed authored axis/contour illustration, not an anatomical conduction model.
public static class LeftPosteriorFascicularReference
{
    public const string EvidenceId = "LeftPosteriorFascicularIllustration@1";
    public static EcgCycleTiming Timing { get; } = new(800_000_000, 100_000_000,
        160_000_000, 100_000_000, 400_000_000, 180_000_000);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes()
    {
        IReadOnlyList<long>[] qrs = [LeftPosteriorFascicularTables.RA, LeftPosteriorFascicularTables.LA,
            LeftPosteriorFascicularTables.RL, LeftPosteriorFascicularTables.LL, LeftPosteriorFascicularTables.C1,
            LeftPosteriorFascicularTables.C2, LeftPosteriorFascicularTables.C3, LeftPosteriorFascicularTables.C4,
            LeftPosteriorFascicularTables.C5, LeftPosteriorFascicularTables.C6];
        return Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: Timing).Select((e, i) => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Select((band, index) => index == 1 ? band with
            { TableQ32 = qrs[i] } : band).ToArray())
        }).ToArray());
    }
}
