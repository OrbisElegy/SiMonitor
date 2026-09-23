// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Authored normal-PR/delta morphology; not Mahaim pathway physiology or diagnosis.
public static class NormalPrDeltaReference
{
    public const string EvidenceId = "NormalPrDeltaIllustration@1";
    public static EcgCycleTiming Timing { get; } = new(800_000_000, 100_000_000,
        160_000_000, 140_000_000, 400_000_000, 180_000_000);
    public static RegularPhysiologyPlan CreatePlan() => new(0, Timing.RrIntervalNs,
        Timing.PrIntervalNs, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes() =>
        WpwReference.CreateElectrodes(true, Timing);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands()
    {
        var e = CreateElectrodes();
        return Array.AsReadOnly(e[(int)EcgElectrode.LL].Bands.Concat(e[(int)EcgElectrode.RA].Bands.Select(b => b with
        { TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v => checked(-v)).ToArray()) })).ToArray());
    }
}
