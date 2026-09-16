// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Textbook-constrained illustration. QRS uses shared early/main/terminal
// components with independent electrode coefficients, not one QRS scale.
public static class TextbookElectrodeReference
{
    public const string EvidenceId = "TextbookChestProgressionDraft@3";
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(EcgUWavePlan? uWave = null,
        EcgCycleTiming? timing = null, EcgTWaveScalePlan? tWave = null, EcgTWaveShapePlan? tShape = null, EcgStSegmentPlan? stSegment = null, EcgPWavePlan? pWave = null, EcgStTFusionPlan? fusion = null, EcgChestInfarctionPlan? infarction = null, EcgInfarctionZones? zones = null, EcgAtrialIllustration atrial = EcgAtrialIllustration.Reference)
    {
        if (zones is not null && (tWave is not null || tShape is not null || stSegment is not null || fusion is not null || infarction is not null))
        { throw new EventWaveformException("EcgInfarction.ConflictingModes", "zones"); }
        timing ??= TextbookEcgReference.Timing with
        { PDurationNs = EcgAtrialIllustrations.PDurationNs(atrial) ?? TextbookEcgReference.Timing.PDurationNs };
        timing.Validate();
        var atrialPlan = EcgAtrialIllustrations.Resolve(atrial, timing);
        if (atrialPlan is not null && pWave is not null)
        { throw new EventWaveformException("EcgAtrial.ConflictingModes", "pWave"); }
        pWave = atrialPlan ?? pWave;
        uWave?.Validate(timing);
        int[]? tScales = tWave?.CaptureScales();
        var tPhases = tShape?.CreatePhasePoints(timing);
        var stBands = stSegment?.CreateBands(timing);
        var fusionBands = fusion?.CreateBands(timing);
        var pBands = pWave?.CreateBands(timing);
        IReadOnlyList<long>[] qrs = [TextbookElectrodeQrsTables.RA, TextbookElectrodeQrsTables.LA,
            TextbookElectrodeQrsTables.RL, TextbookElectrodeQrsTables.LL, TextbookElectrodeQrsTables.C1,
            TextbookElectrodeQrsTables.C2, TextbookElectrodeQrsTables.C3, TextbookElectrodeQrsTables.C4,
            TextbookElectrodeQrsTables.C5, TextbookElectrodeQrsTables.C6];
        var bands = TextbookEcgReference.CreateBands(timing);
        var electrodes = Array.AsReadOnly(Enum.GetValues<EcgElectrode>().Select(electrode => new ElectrodeWaveformPlan(electrode,
            Array.AsReadOnly(bands.Select((band, index) => band with
            {
                TableQ32 = index == 1 ? qrs[(int)electrode] : Array.AsReadOnly(band.TableQ32.Select(value =>
                    checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)value * (index == 0 ? TextbookElectrodeQrsTables.PWeightsQ32 : TextbookElectrodeQrsTables.TWeightsQ32)[(int)electrode], 1000 * (Int128)FixedPointMath.Q32One))).ToArray()),
            }).ToArray()))).ToArray());
        if (tScales is not null || tPhases is not null)
        {
            electrodes = Array.AsReadOnly(electrodes.Select(item => item with
            {
                Bands = Array.AsReadOnly(item.Bands.Select((band, index) => index != 2 ? band : band with
                {
                    TableQ32 = tScales is null ? band.TableQ32 : Array.AsReadOnly(band.TableQ32.Select(value => checked((long)FixedPointMath.RoundDivideTiesToEven(
                        (Int128)value * tScales[(int)item.Electrode], 1000))).ToArray()),
                    PhasePoints = tPhases,
                }).ToArray()),
            }).ToArray());
        }
        if (fusionBands is not null)
        {
            electrodes = Array.AsReadOnly(electrodes.Select(item => item with
            {
                Bands = fusionBands[(int)item.Electrode] is { } replacement
                    ? Array.AsReadOnly(item.Bands.Select((band, index) => index == 2 ? replacement : band).ToArray()) : item.Bands,
            }).ToArray());
        }
        if (stBands is not null)
        {
            electrodes = Array.AsReadOnly(electrodes.Select(item => item with
            {
                Bands = fusionBands?[(int)item.Electrode] is null && stBands[(int)item.Electrode] is { } additions
                    ? Array.AsReadOnly(item.Bands.Concat(additions).ToArray()) : item.Bands,
            }).ToArray());
        }
        // Chest fusion is specified relative to Wilson repolarization. Carry
        // that reference into each selected chest electrode so subtraction
        // cannot leave a residual inverted T wave in an otherwise single crown.
        if (fusionBands is not null)
        {
            var reference = new[] { EcgElectrode.RA, EcgElectrode.LA, EcgElectrode.LL }
                .SelectMany(electrode => electrodes[(int)electrode].Bands.Skip(2))
                .Select(band => band with
                {
                    TableQ32 = Array.AsReadOnly(band.TableQ32.Select(value =>
                        (long)FixedPointMath.RoundDivideTiesToEven(value, 3)).ToArray()),
                }).ToArray();
            electrodes = Array.AsReadOnly(electrodes.Select(item => item with
            {
                Bands = item.Electrode >= EcgElectrode.C1 && fusionBands[(int)item.Electrode] is not null
                    ? Array.AsReadOnly(item.Bands.Concat(reference).ToArray()) : item.Bands,
            }).ToArray());
        }
        if (pBands is not null)
        {
            electrodes = Array.AsReadOnly(electrodes.Select(item => item with
            {
                Bands = pBands[(int)item.Electrode] is { } replacement
                    ? Array.AsReadOnly(item.Bands.Select((band, index) => index == 0 ? replacement[0] : band)
                        .Append(replacement[1]).ToArray()) : item.Bands,
            }).ToArray());
        }
        // U timing remains caller-owned. Until explicit T-U overlap modelling
        // exists, reject an extended global repolarization endpoint past U onset.
        if (infarction is { HasActiveRegion: true } && uWave is not null &&
            uWave.ElectrodeAmplitudesMicrovolts.Any(value => value != 0) &&
            infarction.RepolarizationDelayNs > uWave.DelayAfterTNs)
        { throw new EventWaveformException("EcgInfarction.UOverlap", "infarction"); }
        var staged = infarction?.Apply(electrodes, timing) ?? electrodes;
        staged = zones?.Apply(staged, timing, uWave) ?? staged;
        if (uWave is null) { return staged; }
        var extended = staged.Select(item => item with
        {
            Bands = Array.AsReadOnly(item.Bands.Append(
            new EventWaveformBand(PhysiologyCycleEventKind.VentricularElectrical,
                timing.QtIntervalNs + uWave.DelayAfterTNs, uWave.DurationNs,
                Array.AsReadOnly(TextbookEcgTables.U.Select(value => checked(value *
                    uWave.ElectrodeAmplitudesMicrovolts[(int)item.Electrode])).ToArray()))).ToArray())
        }).ToArray();
        return ElectrodeWaveformComposition.Restore(new(extended, [])).CaptureState().Electrodes;
    }
}
