// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Established AIVR contour example, not automaticity or AV competition.
public static class AcceleratedVentricularReference
{
    public const string EvidenceId = "AcceleratedVentricularIllustration@1";
    // PR100 is only the electrode builder's placeholder, not a measured PR.
    public static EcgCycleTiming Timing { get; } = new(750_000_000, 100_000_000,
        100_000_000, 160_000_000, 400_000_000, 180_000_000);

    public static RegularPhysiologyPlan CreatePlan() => new(0, 800_000_000,
        120_000_000, 80_000_000, 200_000_000, 3_750_000_000, 1_875_000_000,
        IndependentVentricularPeriodNs: 750_000_000,
        ConductionPattern: AvConductionPattern.AcceleratedVentricularIllustration);

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes() =>
        CompleteAvBlockVentricularReference.CreateElectrodes(Timing);

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands()
    {
        var electrodes = CreateElectrodes();
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Zip(electrodes[(int)EcgElectrode.RA].Bands)
            .Select(pair => pair.First with
            {
                TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32)
                    .Select(v => checked(v.First - v.Second)).ToArray())
            }).ToArray());
    }
}
