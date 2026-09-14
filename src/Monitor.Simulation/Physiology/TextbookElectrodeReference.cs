// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Textbook-constrained illustration. QRS uses shared early/main/terminal
// components with independent electrode coefficients, not one QRS scale.
public static class TextbookElectrodeReference
{
    public const string EvidenceId = "TextbookChestProgressionDraft@3";
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(EcgUWavePlan? uWave = null)
    {
        uWave?.Validate(TextbookEcgReference.Timing);
        IReadOnlyList<long>[] qrs = [TextbookElectrodeQrsTables.RA, TextbookElectrodeQrsTables.LA,
            TextbookElectrodeQrsTables.RL, TextbookElectrodeQrsTables.LL, TextbookElectrodeQrsTables.C1,
            TextbookElectrodeQrsTables.C2, TextbookElectrodeQrsTables.C3, TextbookElectrodeQrsTables.C4,
            TextbookElectrodeQrsTables.C5, TextbookElectrodeQrsTables.C6];
        var bands = TextbookEcgReference.CreateBands();
        var electrodes = Array.AsReadOnly(Enum.GetValues<EcgElectrode>().Select(electrode => new ElectrodeWaveformPlan(electrode,
            Array.AsReadOnly(bands.Select((band, index) => band with
            {
                TableQ32 = index == 1 ? qrs[(int)electrode] : Array.AsReadOnly(band.TableQ32.Select(value =>
                    checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)value * (index == 0 ? TextbookElectrodeQrsTables.PWeightsQ32 : TextbookElectrodeQrsTables.TWeightsQ32)[(int)electrode], 1000 * (Int128)FixedPointMath.Q32One))).ToArray()),
            }).ToArray()))).ToArray());
        if (uWave is null) { return electrodes; }
        var extended = electrodes.Select(item => item with
        {
            Bands = Array.AsReadOnly(item.Bands.Append(
            new EventWaveformBand(PhysiologyCycleEventKind.VentricularElectrical,
                TextbookEcgReference.Timing.QtIntervalNs + uWave.DelayAfterTNs, uWave.DurationNs,
                Array.AsReadOnly(TextbookEcgTables.U.Select(value => checked(value *
                    uWave.ElectrodeAmplitudesMicrovolts[(int)item.Electrode])).ToArray()))).ToArray())
        }).ToArray();
        return ElectrodeWaveformComposition.Restore(new(extended, [])).CaptureState().Electrodes;
    }
}
