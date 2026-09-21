// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public enum QuinidineIllustration { Reference, LowT, InvertedT, WideQrsLowT, WideQrsInvertedT }

// Fixed drug-effect illustration, not dosing, toxicity or rhythm prediction.
public static class QuinidineEffectReference
{
    public const string EvidenceId = "QuinidineEffectIllustration@1";
    public static EcgCycleTiming Timing { get; } = new(1_000_000_000, 120_000_000,
        200_000_000, 80_000_000, 480_000_000, 180_000_000);
    public const long QuIntervalNs = 710_000_000;
    public static EcgCycleTiming ResolveTiming(QuinidineIllustration mode) => mode switch
    {
        QuinidineIllustration.LowT or QuinidineIllustration.InvertedT => Timing,
        QuinidineIllustration.WideQrsLowT or QuinidineIllustration.WideQrsInvertedT =>
            Timing with { QrsDurationNs = 140_000_000, QtIntervalNs = 560_000_000 },
        _ => throw new EventWaveformException("Quinidine.InvalidMode", nameof(mode)),
    };
    public static long ResolveQuIntervalNs(QuinidineIllustration mode) =>
        ResolveTiming(mode).QtIntervalNs + 230_000_000;
    public static RegularPhysiologyPlan CreatePlan(QuinidineIllustration mode = QuinidineIllustration.LowT)
    {
        var timing = ResolveTiming(mode);
        return new(0, timing.RrIntervalNs, timing.PrIntervalNs, 80_000_000,
            timing.PrIntervalNs + 80_000_000, 3_750_000_000, 1_875_000_000);
    }
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(QuinidineIllustration mode, bool notchedP = false)
    {
        var timing = ResolveTiming(mode);
        var u = new EcgUWavePlan(30_000_000, 200_000_000, [-60, 0, 0, 60, 100, 300, 300, 200, 150, 100]);
        var source = TextbookElectrodeReference.CreateElectrodes(u, timing);
        int sign = mode is QuinidineIllustration.InvertedT or QuinidineIllustration.WideQrsInvertedT ? -1 : 1;
        var result = source.Select(e => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Select((b, i) => i == 0 && notchedP ? b with
            {
                TableQ32 = Array.AsReadOnly(QuinidineEffectTables.NotchedP.Select(v =>
                    checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)v *
                        TextbookElectrodeQrsTables.PWeightsQ32[(int)e.Electrode],
                        1000 * (Int128)FixedPointMath.Q32One))).ToArray())
            } : i == 2 ? b with
            {
                TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v =>
                    (long)FixedPointMath.RoundDivideTiesToEven((Int128)v * sign, 4)).ToArray())
            } : b).ToArray())
        }).ToArray();
        return ElectrodeWaveformComposition.Restore(new(result, [])).CaptureState().Electrodes;
    }
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(QuinidineIllustration mode, bool notchedP = false)
    {
        var electrodes = CreateElectrodes(mode, notchedP);
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Concat(
            electrodes[(int)EcgElectrode.RA].Bands.Select(b => b with
            { TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v => checked(-v)).ToArray()) })).ToArray());
    }
}
