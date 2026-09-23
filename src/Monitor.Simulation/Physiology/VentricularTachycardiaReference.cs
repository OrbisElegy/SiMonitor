// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Established monomorphic VT with independent slower atria. Authored contour,
// not an anatomical focus, onset mechanism, capture or fusion-beat model.
public static class VentricularTachycardiaReference
{
    public const string EvidenceId = "MonomorphicVtIllustration@1";
    // PR100 is only a component-builder placeholder: there is no fixed PR.
    public static EcgCycleTiming Timing { get; } = new(375_000_000, 100_000_000,
        100_000_000, 160_000_000, 275_000_000, 100_000_000);
    public static RegularPhysiologyPlan CreatePlan() => new(0, 800_000_000,
        120_000_000, 80_000_000, 200_000_000, 3_750_000_000, 1_875_000_000,
        IndependentVentricularPeriodNs: 375_000_000,
        ConductionPattern: AvConductionPattern.MonomorphicVtIllustration);

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes() =>
        CompleteAvBlockVentricularReference.CreateElectrodes(Timing);

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands()
    {
        var electrodes = CreateElectrodes();
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Concat(
            electrodes[(int)EcgElectrode.RA].Bands.Select(b => b with
            { TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v => checked(-v)).ToArray()) })).ToArray());
    }
}
