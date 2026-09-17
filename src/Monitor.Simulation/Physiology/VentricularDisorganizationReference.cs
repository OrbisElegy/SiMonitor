// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Continuous ventricular electrical activity without organized QRS/ejection.
public static class VentricularDisorganizationReference
{
    public const string EvidenceId = "VentricularDisorganizationIllustrationDraft@1";
    public static bool IsPattern(AvConductionPattern pattern) => pattern is AvConductionPattern.VentricularFlutterIllustration
        or AvConductionPattern.VentricularFibrillationCoarseIllustration or AvConductionPattern.VentricularFibrillationFineIllustration;

    public static long SegmentDurationNs(AvConductionPattern pattern) => pattern switch
    {
        AvConductionPattern.VentricularFlutterIllustration => 250_000_000,
        AvConductionPattern.VentricularFibrillationCoarseIllustration or AvConductionPattern.VentricularFibrillationFineIllustration => 16_384_000_000,
        _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
    };

    public static RegularPhysiologyPlan CreatePlan(AvConductionPattern pattern)
    {
        _ = SegmentDurationNs(pattern);
        return new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000,
            CardiacActivity: CardiacActivity.VentricularOnly, VentricularMechanicalEnabled: false, ConductionPattern: pattern);
    }

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(AvConductionPattern pattern)
    {
        long duration = SegmentDurationNs(pattern);
        var table = pattern == AvConductionPattern.VentricularFlutterIllustration
            ? VentricularDisorganizationTables.Flutter : VentricularDisorganizationTables.Fibrillation;
        int gain = pattern == AvConductionPattern.VentricularFibrillationFineIllustration ? 200 : 1000;
        return Array.AsReadOnly(Enum.GetValues<EcgElectrode>().Select(electrode => new ElectrodeWaveformPlan(electrode,
            Array.AsReadOnly(new[] { new EventWaveformBand(PhysiologyCycleEventKind.VentricularDisorganizationSegment, 0, duration,
                Array.AsReadOnly(table.Select(value => checked((long)FixedPointMath.RoundDivideTiesToEven(
                    (Int128)value * VentricularDisorganizationTables.WeightsQ32[(int)electrode] * gain,
                    1_000_000 * (Int128)FixedPointMath.Q32One))).ToArray())) }))).ToArray());
    }

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(AvConductionPattern pattern)
    {
        var electrodes = CreateElectrodes(pattern);
        var ra = electrodes.Single(e => e.Electrode == EcgElectrode.RA).Bands[0];
        var ll = electrodes.Single(e => e.Electrode == EcgElectrode.LL).Bands[0];
        return Array.AsReadOnly(new[] { ll with
        {
            TableQ32 = Array.AsReadOnly(ll.TableQ32.Zip(ra.TableQ32).Select(p => checked(p.First - p.Second)).ToArray()),
        } });
    }
}
