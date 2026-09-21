// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public enum CalciumIllustration { Reference, High, Low, HighAbsentSt, LowFlatT, LowInvertedT }

// Fixed repolarization illustrations, not a concentration/medication model.
public static class CalciumRepolarizationReference
{
    public const string VariantsEvidenceId = "CalciumRepolarizationVariantsIllustration@1";
    public const string EvidenceId = "CalciumRepolarizationIllustration@1";
    public static EcgCycleTiming Timing(CalciumIllustration mode) => mode switch
    {
        CalciumIllustration.Reference => new(1_000_000_000, 100_000_000, 160_000_000, 80_000_000, 400_000_000, 180_000_000),
        CalciumIllustration.High => new(1_000_000_000, 100_000_000, 160_000_000, 80_000_000, 300_000_000, 180_000_000),
        CalciumIllustration.HighAbsentSt => new(1_000_000_000, 100_000_000, 160_000_000, 80_000_000, 260_000_000, 180_000_000),
        CalciumIllustration.Low or CalciumIllustration.LowFlatT or CalciumIllustration.LowInvertedT => new(1_000_000_000, 100_000_000, 160_000_000, 80_000_000, 460_000_000, 120_000_000),
        _ => throw new EventWaveformException("Calcium.InvalidMode", nameof(mode)),
    };
    public static RegularPhysiologyPlan CreatePlan() => new(0, 1_000_000_000,
        160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(CalciumIllustration mode)
    {
        var source = TextbookElectrodeReference.CreateElectrodes(timing: Timing(mode));
        if (mode is not (CalciumIllustration.LowFlatT or CalciumIllustration.LowInvertedT)) { return source; }
        int numerator = mode == CalciumIllustration.LowInvertedT ? -1 : 1;
        int denominator = mode == CalciumIllustration.LowFlatT ? 4 : 1;
        return Array.AsReadOnly(source.Select(e => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Select((b, i) => i == 2 ? b with
            {
                TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v =>
                    (long)FixedPointMath.RoundDivideTiesToEven((Int128)v * numerator, denominator)).ToArray())
            } : b).ToArray())
        }).ToArray());
    }
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(CalciumIllustration mode)
    {
        var electrodes = CreateElectrodes(mode);
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Zip(electrodes[(int)EcgElectrode.RA].Bands)
            .Select(p => p.First with
            {
                TableQ32 = Array.AsReadOnly(p.First.TableQ32.Zip(p.Second.TableQ32)
                .Select(v => checked(v.First - v.Second)).ToArray())
            }).ToArray());
    }
}
