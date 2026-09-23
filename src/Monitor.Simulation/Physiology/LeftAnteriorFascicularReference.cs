// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Fixed authored axis/contour illustration, not an anatomical conduction model.
public static class LeftAnteriorFascicularReference
{
    public const string EvidenceId = "LeftAnteriorFascicularIllustration@1";
    public static EcgCycleTiming Timing { get; } = new(800_000_000, 100_000_000,
        160_000_000, 100_000_000, 400_000_000, 180_000_000);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes()
    {
        IReadOnlyList<long>[] qrs = [LeftAnteriorFascicularTables.RA, LeftAnteriorFascicularTables.LA,
            LeftAnteriorFascicularTables.RL, LeftAnteriorFascicularTables.LL, LeftAnteriorFascicularTables.C1,
            LeftAnteriorFascicularTables.C2, LeftAnteriorFascicularTables.C3, LeftAnteriorFascicularTables.C4,
            LeftAnteriorFascicularTables.C5, LeftAnteriorFascicularTables.C6];
        return Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: Timing).Select((e, i) => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Select((band, index) => index == 1 ? band with
            { TableQ32 = qrs[i] } : band).ToArray())
        }).ToArray());
    }
}
