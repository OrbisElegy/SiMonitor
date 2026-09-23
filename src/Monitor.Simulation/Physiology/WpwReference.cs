// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Authored positive-V1 delta illustration; no pathway or arrhythmia inference.
public static class WpwReference
{
    public const string EvidenceId = "WpwPositiveV1Illustration@1";
    public static EcgCycleTiming Timing { get; } = new(800_000_000, 80_000_000,
        100_000_000, 140_000_000, 400_000_000, 180_000_000);
    public static RegularPhysiologyPlan CreatePlan() => new(0, Timing.RrIntervalNs,
        Timing.PrIntervalNs, 80_000_000, 180_000_000, 3_750_000_000, 1_875_000_000);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes()
    {
        IReadOnlyList<long>[] qrs = [WpwTables.RA, WpwTables.LA, WpwTables.RL, WpwTables.LL,
            WpwTables.C1, WpwTables.C2, WpwTables.C3, WpwTables.C4, WpwTables.C5, WpwTables.C6];
        long peak = TextbookEcgTables.T.Max();
        int[] levels = WpwTables.TAmplitudesQ32.Select(v => checked((int)FixedPointMath.RoundDivideTiesToEven(v, 5 * (Int128)FixedPointMath.Q32One))).ToArray();
        var st = new EcgStSegmentPlan(levels, levels).CreateBands(Timing);
        return Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: Timing).Select((e, i) => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Select((b, index) => index switch
            {
                1 => b with { TableQ32 = qrs[i] },
                2 => b with
                {
                    TableQ32 = Array.AsReadOnly(TextbookEcgTables.T.Select(v =>
                    checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)v * WpwTables.TAmplitudesQ32[i], peak))).ToArray())
                },
                _ => b,
            }).Concat(st[i] ?? []).ToArray())
        }).ToArray());
    }
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands()
    {
        var e = CreateElectrodes();
        return Array.AsReadOnly(e[(int)EcgElectrode.LL].Bands.Concat(e[(int)EcgElectrode.RA].Bands.Select(b => b with
        { TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v => checked(-v)).ToArray()) })).ToArray());
    }
}
