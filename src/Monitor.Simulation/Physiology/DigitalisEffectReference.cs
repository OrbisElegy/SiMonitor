// SPDX-License-Identifier: AGPL-3.0-or-later
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
        IReadOnlyList<long>[] tables = shape switch
        {
            DigitalisTShape.FishHook => [DigitalisJoinedTables.Compound_RA, DigitalisJoinedTables.Compound_LA,
                DigitalisJoinedTables.Compound_RL, DigitalisJoinedTables.Compound_LL, DigitalisJoinedTables.Compound_C1,
                DigitalisJoinedTables.Compound_C2, DigitalisJoinedTables.Compound_C3, DigitalisJoinedTables.Compound_C4,
                DigitalisJoinedTables.Compound_C5, DigitalisJoinedTables.Compound_C6],
            DigitalisTShape.LowT => [DigitalisJoinedTables.LowT_RA, DigitalisJoinedTables.LowT_LA,
                DigitalisJoinedTables.LowT_RL, DigitalisJoinedTables.LowT_LL, DigitalisJoinedTables.LowT_C1,
                DigitalisJoinedTables.LowT_C2, DigitalisJoinedTables.LowT_C3, DigitalisJoinedTables.LowT_C4,
                DigitalisJoinedTables.LowT_C5, DigitalisJoinedTables.LowT_C6],
            DigitalisTShape.InvertedT => [DigitalisJoinedTables.InvertedT_RA, DigitalisJoinedTables.InvertedT_LA,
                DigitalisJoinedTables.InvertedT_RL, DigitalisJoinedTables.InvertedT_LL, DigitalisJoinedTables.InvertedT_C1,
                DigitalisJoinedTables.InvertedT_C2, DigitalisJoinedTables.InvertedT_C3, DigitalisJoinedTables.InvertedT_C4,
                DigitalisJoinedTables.InvertedT_C5, DigitalisJoinedTables.InvertedT_C6],
            _ => throw new EventWaveformException("Digitalis.InvalidShape", nameof(shape)),
        };
        var source = TextbookElectrodeReference.CreateElectrodes(timing: Timing);
        // QRS and repolarization share a single authored contour: adding ST
        // to an intact S recovery creates an unintended second downturn.
        var result = source.Select((e, i) => e with
        {
            Bands = Array.AsReadOnly(new[] { e.Bands[0],
                new EventWaveformBand(PhysiologyCycleEventKind.VentricularElectrical, 0,
                    Timing.QtIntervalNs, tables[i]) })
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
