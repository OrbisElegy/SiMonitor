// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public static class AtrialFlutterReference
{
    public const string EvidenceId = "AtrialFlutterIllustrationDraft@2";
    public static EcgCycleTiming Timing(int ratio)
    {
        if (ratio is not (2 or 3 or 4)) { throw new ArgumentOutOfRangeException(nameof(ratio)); }
        // P/PR here are construction placeholders; the P band is replaced in full.
        return new(ratio * 200_000_000L, 40_000_000, 80_000_000, 80_000_000, 300_000_000, 140_000_000);
    }

    public static RegularPhysiologyPlan CreatePlan(int ratio = 4)
    {
        _ = Timing(ratio);
        return new(0, 200_000_000, 80_000_000, 80_000_000, 160_000_000,
            3_750_000_000, 1_875_000_000, VentricularConductionRatio: ratio,
            ConductionPattern: AvConductionPattern.AtrialFlutterIllustration);
    }

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(int ratio = 4) =>
        Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: Timing(ratio)).Select((electrode, i) => electrode with
        {
            Bands = Array.AsReadOnly(electrode.Bands.Select((band, index) => index == 0
                ? new EventWaveformBand(PhysiologyCycleEventKind.AtrialElectrical, 0, 200_000_000,
                    Array.AsReadOnly(AtrialFlutterTables.Cycle.Select(value => checked((long)FixedPointMath.RoundDivideTiesToEven(
                        (Int128)value * AtrialFlutterTables.AmplitudesQ32[i], 1000 * (Int128)FixedPointMath.Q32One))).ToArray()))
                : band).ToArray()),
        }).ToArray());

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(int ratio = 4)
    {
        var electrodes = CreateElectrodes(ratio);
        var ra = electrodes.Single(e => e.Electrode == EcgElectrode.RA);
        var ll = electrodes.Single(e => e.Electrode == EcgElectrode.LL);
        return Array.AsReadOnly(ll.Bands.Zip(ra.Bands).Select(pair => pair.First with
        {
            TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32)
                .Select(values => checked(values.First - values.Second)).ToArray()),
        }).ToArray());
    }
}
