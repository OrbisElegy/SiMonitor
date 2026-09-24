// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Established atrial escape rhythm with1:1 conduction, not an automaticity model.
public static class AtrialEscapeReference
{
    public const string EvidenceId = "AtrialEscapeIllustration@1";
    public static EcgCycleTiming Timing { get; } = new(1_200_000_000, 80_000_000,
        160_000_000, 80_000_000, 400_000_000, 180_000_000);
    public static RegularPhysiologyPlan CreatePlan() => new(0, 1_200_000_000,
        160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes() =>
        AcceleratedAtrialReference.CreateElectrodes(Timing);

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
