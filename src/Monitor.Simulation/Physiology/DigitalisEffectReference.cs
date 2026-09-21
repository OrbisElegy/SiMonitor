// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public enum DigitalisTShape { FishHook, LowT, InvertedT }

// Fixed fish-hook ST-T illustration, not dose response or toxicity diagnosis.
public static class DigitalisEffectReference
{
    public const string VariantsEvidenceId = "DigitalisTVariantsIllustration@1";
    public const string EvidenceId = "DigitalisEffectIllustration@1";
    // T duration is a construction field; the compound ST-T has no sharp partition.
    public static EcgCycleTiming Timing { get; } = new(1_000_000_000, 100_000_000,
        160_000_000, 80_000_000, 320_000_000, 120_000_000);
    public static RegularPhysiologyPlan CreatePlan() => new(0, Timing.RrIntervalNs,
        Timing.PrIntervalNs, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(DigitalisTShape shape = DigitalisTShape.FishHook)
    {
        var table = shape switch
        {
            DigitalisTShape.FishHook => DigitalisEffectTables.Compound,
            DigitalisTShape.LowT => DigitalisEffectTables.LowT,
            DigitalisTShape.InvertedT => DigitalisEffectTables.InvertedT,
            _ => throw new EventWaveformException("Digitalis.InvalidShape", nameof(shape)),
        };
        int[] weights = [-400, 100, 0, 300, 300, 600, 1000, 1200, 1000, 700];
        var source = TextbookElectrodeReference.CreateElectrodes(timing: Timing);
        var phases = Array.AsReadOnly(new EventWaveformPhasePoint[]
        { new(0, 0), new(20_000_000, 32), new(90_000_000, 64), new(220_000_000, 96), new(260_000_000, 128) });
        var result = source.Select((e, i) => e with
        {
            Bands = Array.AsReadOnly(new[] { e.Bands[0], e.Bands[1],
                new EventWaveformBand(PhysiologyCycleEventKind.VentricularElectrical, 60_000_000, 260_000_000,
                    Array.AsReadOnly(table.Select(v =>
                        (long)FixedPointMath.RoundDivideTiesToEven((Int128)v * weights[i], 1000)).ToArray()), phases) })
        }).ToArray();
        return ElectrodeWaveformComposition.Restore(new(result, [])).CaptureState().Electrodes;
    }
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(DigitalisTShape shape = DigitalisTShape.FishHook)
    {
        var electrodes = CreateElectrodes(shape);
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Zip(electrodes[(int)EcgElectrode.RA].Bands)
            .Select(p => p.First with
            {
                TableQ32 = Array.AsReadOnly(p.First.TableQ32.Zip(p.Second.TableQ32)
                .Select(v => checked(v.First - v.Second)).ToArray())
            }).ToArray());
    }
}
