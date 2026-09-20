// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Authored ST depression, low T and separate prominent U, not potassium dosing.
public static class HypokalemiaRepolarizationReference
{
    public const string EvidenceId = "HypokalemiaRepolarizationIllustration@1";
    public static EcgCycleTiming Timing { get; } = new(1_000_000_000, 100_000_000,
        160_000_000, 80_000_000, 400_000_000, 180_000_000);
    public const long QuIntervalNs = 650_000_000;
    public static RegularPhysiologyPlan CreatePlan() => new(0, Timing.RrIntervalNs,
        Timing.PrIntervalNs, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes()
    {
        // Each limb component sums to zero, preserving Wilson's reference.
        int[] t = [-30, 0, 0, 30, 50, 60, 60, 70, 70, 60];
        int[] st = [40, 0, 0, -40, -50, -80, -100, -100, -80, -60];
        var u = new EcgUWavePlan(30_000_000, 220_000_000,
            [-80, 0, 0, 80, 150, 450, 450, 350, 250, 180]);
        long peak = TextbookEcgTables.T.Max();
        var source = TextbookElectrodeReference.CreateElectrodes(u, Timing, stSegment: new(st, st));
        return Array.AsReadOnly(source.Select((electrode, i) => electrode with
        {
            Bands = Array.AsReadOnly(electrode.Bands.Select((band, index) => index == 2
                ? band with
                {
                    TableQ32 = Array.AsReadOnly(TextbookEcgTables.T.Select(value =>
                    (long)FixedPointMath.RoundDivideTiesToEven((Int128)value * t[i] * FixedPointMath.Q32One, peak)).ToArray())
                }
                : band).ToArray()),
        }).ToArray());
    }

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands()
    {
        var electrodes = CreateElectrodes();
        // Sum separate electrode bands; do not zip unlike band counts (LA/RL
        // may have no ST band). This also retains U as a distinct component.
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Concat(
            electrodes[(int)EcgElectrode.RA].Bands.Select(band => band with
            { TableQ32 = Array.AsReadOnly(band.TableQ32.Select(value => checked(-value)).ToArray()) })).ToArray());
    }
}
