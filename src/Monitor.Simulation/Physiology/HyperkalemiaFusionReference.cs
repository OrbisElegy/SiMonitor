// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// One inseparable QRS-T contour, not a concentration or pump-failure model.
public static class HyperkalemiaFusionReference
{
    public const string EvidenceId = "HyperkalemiaFusionIllustration@1";
    public const long CompoundDurationNs = 720_000_000;
    // Construction placeholders only; QRS/T partition is not measurable here.
    public static EcgCycleTiming Timing { get; } = new(1_000_000_000, 140_000_000,
        240_000_000, 320_000_000, CompoundDurationNs, 400_000_000);
    public static RegularPhysiologyPlan CreatePlan() => HyperkalemiaConductionReference.CreatePlan(true);

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes()
    {
        var electrodes = Enum.GetValues<EcgElectrode>().Select(electrode => new ElectrodeWaveformPlan(electrode,
            Array.AsReadOnly(new[] { new EventWaveformBand(PhysiologyCycleEventKind.VentricularElectrical, 0, CompoundDurationNs,
                Array.AsReadOnly(HyperkalemiaFusionTables.Compound.Select(value =>
                    (long)FixedPointMath.RoundDivideTiesToEven((Int128)value * HyperkalemiaFusionTables.Weights[(int)electrode],
                        1000 * (Int128)FixedPointMath.Q32One)).ToArray())) }))).ToArray();
        return ElectrodeWaveformComposition.Restore(new(electrodes, [])).CaptureState().Electrodes;
    }

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands()
    {
        var electrodes = CreateElectrodes();
        var ra = electrodes[(int)EcgElectrode.RA].Bands[0];
        var ll = electrodes[(int)EcgElectrode.LL].Bands[0];
        return Array.AsReadOnly(new[] { ll with { TableQ32 = Array.AsReadOnly(ll.TableQ32.Zip(ra.TableQ32)
            .Select(p => checked(p.First - p.Second)).ToArray()) } });
    }
}
