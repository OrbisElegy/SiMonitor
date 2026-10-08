// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.ObjectModel;
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Fixed teaching examples, not a programmable implant/device model.
public enum PacingIllustration
{
    AtrialAai,
    RightVentricularVvi,
    DualChamberDdd,
    BiventricularCrt,
    SelectiveHis,
    LeftBundleBranchArea,
    LeadlessRightVentricular,
    LeftVentricularEpicardial,
    TemporaryTransvenous,
    AtrialNoncapture,
    VentricularNoncapture,
    VentricularOutputFailure,
    VentricularUndersensing,
    VentricularOversensing,
    IntermittentVentricularNoncapture,
}

public static class PacingReference
{
    public const string EvidenceId = "PacingIllustration@1";
    // Display support at 250 Hz. This is NOT the physical implant pulse width.
    public const long DisplayPulseDurationNs = 8_000_000;
    private const long PeriodNs = 1_000_000_000;

    private static bool IndependentAtrium(PacingIllustration mode) => mode is
        PacingIllustration.RightVentricularVvi or PacingIllustration.LeadlessRightVentricular or
        PacingIllustration.TemporaryTransvenous;

    public static EcgCycleTiming Timing(PacingIllustration mode)
    {
        ValidateMode(mode);
        long qrs = mode switch
        {
            PacingIllustration.AtrialAai or PacingIllustration.SelectiveHis or PacingIllustration.VentricularUndersensing => 80_000_000,
            PacingIllustration.LeftBundleBranchArea => 110_000_000,
            PacingIllustration.BiventricularCrt => 140_000_000,
            _ => 160_000_000,
        };
        return new(PeriodNs, 100_000_000, 200_000_000, qrs, 420_000_000, 180_000_000);
    }

    public static RegularPhysiologyPlan CreatePlan(PacingIllustration mode)
    {
        ValidateMode(mode);
        return new(0, IndependentAtrium(mode) ? 800_000_000 : PeriodNs,
            208_000_000, 88_000_000, 288_000_000, 3_750_000_000, 1_875_000_000,
            IndependentVentricularPeriodNs: IndependentAtrium(mode) ? PeriodNs : null)
        { Pacing = mode };
    }

    internal static void Validate(RegularPhysiologyPlan plan)
    {
        if (plan.Pacing is not { } mode) { return; }
        var expected = CreatePlan(mode);
        if (plan.HeartPeriodNs != expected.HeartPeriodNs || plan.VentricularElectricalOffsetNs != expected.VentricularElectricalOffsetNs ||
            plan.AtrialMechanicalOffsetNs != expected.AtrialMechanicalOffsetNs || plan.VentricularMechanicalOffsetNs != expected.VentricularMechanicalOffsetNs ||
            plan.IndependentVentricularPeriodNs != expected.IndependentVentricularPeriodNs || plan.ConductionPattern != AvConductionPattern.FixedPr ||
            plan.VentricularConductionRatio != 1 || plan.ConductedBeatsPerGroup != 1 || plan.CardiacActivity != CardiacActivity.AtrialAndVentricular ||
            plan.MechanicalEveryCycles != 1 || plan.MechanicalAfterCycles is not null || plan.MechanicalDurationCycles is not null ||
            plan.SeededRate is not null || plan.RateAdjustment is not null || plan.RhythmSchedule is not null)
        { throw new PhysiologyTimelineException("Pacing.ConflictingModes", nameof(plan)); }
    }

    internal static bool IsCardiac(PhysiologyCycleEventKind kind) => kind is
        PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.AtrialMechanical or
        PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical or
        PhysiologyCycleEventKind.AtrialPacingPulse or PhysiologyCycleEventKind.VentricularPacingPulse;

    internal static void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind,
        long inclusiveSimTimeNs, Int128 exclusiveSimTimeNs, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        var mode = plan.Pacing!.Value;
        bool atrial = kind is PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.AtrialMechanical;
        bool atrialPulse = kind == PhysiologyCycleEventKind.AtrialPacingPulse;
        bool ventricularPulse = kind == PhysiologyCycleEventKind.VentricularPacingPulse;
        bool intrinsic = mode == PacingIllustration.VentricularUndersensing;
        if (atrialPulse && (IndependentAtrium(mode) || intrinsic) ||
            ventricularPulse && mode is PacingIllustration.AtrialAai or PacingIllustration.VentricularOutputFailure ||
            atrial && mode == PacingIllustration.AtrialNoncapture ||
            !atrial && !atrialPulse && !ventricularPulse && mode is PacingIllustration.VentricularNoncapture or PacingIllustration.VentricularOutputFailure)
        { return; }
        long period = atrial && IndependentAtrium(mode) ? 800_000_000 : PeriodNs;
        long offset = kind switch
        {
            PhysiologyCycleEventKind.AtrialPacingPulse => 0,
            PhysiologyCycleEventKind.AtrialElectrical => IndependentAtrium(mode) || intrinsic ? 0 : 8_000_000,
            PhysiologyCycleEventKind.AtrialMechanical => IndependentAtrium(mode) || intrinsic ? 80_000_000 : 88_000_000,
            PhysiologyCycleEventKind.VentricularPacingPulse => intrinsic ? 240_000_000 : mode == PacingIllustration.SelectiveHis ? 168_000_000 : 200_000_000,
            PhysiologyCycleEventKind.VentricularElectrical => 208_000_000,
            _ => 288_000_000,
        };
        // Four-beat repeating pauses: oversensing inhibits output; noncapture
        // preserves pulses. Both remove ventricular electrical/mechanical events.
        bool pause = !atrial && !atrialPulse && (mode == PacingIllustration.VentricularOversensing ||
            mode == PacingIllustration.IntermittentVentricularNoncapture && !ventricularPulse);
        IndexedCardiacSchedule.Visit(plan, kind, offset, inclusiveSimTimeNs, exclusiveSimTimeNs,
            maximumEvents, visitor, pause ? 4 * PeriodNs : period,
            pause ? [0, PeriodNs] : [0], cancellationToken);
    }

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(PacingIllustration mode)
    {
        var timing = Timing(mode);
        // Existing authored component contours are reused as illustrative
        // activation patterns, not evidence of a particular lead position.
        var source = mode switch
        {
            PacingIllustration.AtrialAai or PacingIllustration.SelectiveHis or PacingIllustration.VentricularUndersensing => TextbookElectrodeReference.CreateElectrodes(timing: timing),
            PacingIllustration.BiventricularCrt or PacingIllustration.LeftBundleBranchArea or PacingIllustration.LeftVentricularEpicardial => RightBundleBlockReference.CreateElectrodes(timing),
            _ => LeftBundleBlockReference.CreateElectrodes(timing),
        };
        if (mode is not (PacingIllustration.AtrialAai or PacingIllustration.SelectiveHis or
            PacingIllustration.VentricularUndersensing or PacingIllustration.BiventricularCrt or
            PacingIllustration.LeftBundleBranchArea or PacingIllustration.LeftVentricularEpicardial))
        { source = WithApicalAxis(source); }
        int amplitude = mode == PacingIllustration.LeadlessRightVentricular ? 200 : 1200;
        int[] vector = [0, 1, 0, 1, -1, -1, 0, 1, 1, 1];
        var electrodes = source.Select(e => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Concat(new[]
            {
                Pulse(PhysiologyCycleEventKind.AtrialPacingPulse, amplitude * vector[(int)e.Electrode] / 2),
                Pulse(PhysiologyCycleEventKind.VentricularPacingPulse, amplitude * vector[(int)e.Electrode]),
            }).ToArray())
        }).ToArray();
        return ElectrodeWaveformComposition.Restore(new(electrodes, [])).CaptureState().Electrodes;
    }

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(PacingIllustration mode)
    {
        var electrodes = CreateElectrodes(mode);
        var ra = electrodes.Single(e => e.Electrode == EcgElectrode.RA);
        var ll = electrodes.Single(e => e.Electrode == EcgElectrode.LL);
        return Array.AsReadOnly(ll.Bands.Zip(ra.Bands).Select(pair => pair.First with
        {
            TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32)
                .Select(v => checked(v.First - v.Second)).ToArray())
        }).ToArray());
    }

    private static EventWaveformBand Pulse(PhysiologyCycleEventKind kind, int microvolts) =>
        new(kind, 0, DisplayPulseDurationNs, Array.AsReadOnly(new long[]
        { 0, microvolts * FixedPointMath.Q32One, microvolts * FixedPointMath.Q32One, 0 }));

    // RV apical illustration: superior ventricular axis (negative inferior
    // QRS), retaining LBBB-like chest activation. Equal/opposite RA and LL
    // changes preserve Wilson's terminal and all chest projections. Atrial
    // depolarization retains its own axis; ST/T follows the ventricular change.
    private static ReadOnlyCollection<ElectrodeWaveformPlan> WithApicalAxis(IReadOnlyList<ElectrodeWaveformPlan> source)
    {
        var ra = source.Single(e => e.Electrode == EcgElectrode.RA);
        var ll = source.Single(e => e.Electrode == EcgElectrode.LL);
        return Array.AsReadOnly(source.Select(e => e.Electrode is EcgElectrode.RA or EcgElectrode.LL ? e with
        {
            Bands = Array.AsReadOnly(e.Bands.Select((band, index) => band.Trigger != PhysiologyCycleEventKind.VentricularElectrical ? band : band with
            {
                TableQ32 = Array.AsReadOnly(band.TableQ32.Select((value, point) => checked(value +
                    (e.Electrode == EcgElectrode.RA ? 1 : -1) * (long)FixedPointMath.RoundDivideTiesToEven(
                        (Int128)(ll.Bands[index].TableQ32[point] - ra.Bands[index].TableQ32[point]) * 3, 2))).ToArray())
            }).ToArray())
        } : e).ToArray());
    }

    private static void ValidateMode(PacingIllustration mode)
    {
        if (!Enum.IsDefined(mode)) { throw new ArgumentOutOfRangeException(nameof(mode)); }
    }
}
