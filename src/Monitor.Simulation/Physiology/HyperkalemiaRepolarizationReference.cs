// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// One authored repolarization example, not a potassium concentration model.
// P/PR/QRS remain ordinary; later conduction changes are separate work.
public static class HyperkalemiaRepolarizationReference
{
    public const string EvidenceId = "HyperkalemiaRepolarizationIllustration@1";
    public static EcgCycleTiming Timing { get; } = new(800_000_000, 100_000_000,
        160_000_000, 80_000_000, 300_000_000, 120_000_000);

    public static RegularPhysiologyPlan CreatePlan() => new(0, Timing.RrIntervalNs,
        Timing.PrIntervalNs, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes()
    {
        // Zero-sum limb drive preserves Wilson's reference. These coefficients
        // yield I500/II700/III200/aVR-600/aVL150/aVF450 microvolt T peaks.
        int[] amplitudes = [-400, 100, 0, 300, 300, 600, 1000, 1200, 1000, 700];
        return Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: Timing)
            .Select((electrode, i) => electrode with
            {
                Bands = Array.AsReadOnly(electrode.Bands.Select((band, index) => index == 2
                    ? band with
                    {
                        TableQ32 = Array.AsReadOnly(TContourTables.PeakedUpright.Select(value =>
                            (long)FixedPointMath.RoundDivideTiesToEven((Int128)value * amplitudes[i], 1000)).ToArray()),
                        PhasePoints = null,
                    } : band).ToArray()),
            }).ToArray());
    }

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands()
    {
        var electrodes = CreateElectrodes();
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Zip(electrodes[(int)EcgElectrode.RA].Bands)
            .Select(pair => pair.First with
            {
                TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32)
                    .Select(values => checked(values.First - values.Second)).ToArray()),
            }).ToArray());
    }
}
