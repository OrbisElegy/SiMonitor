// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Simulation.Authoring;

public sealed record PhysiologyIllustrationConfiguration(int BreathPeriodMilliseconds,
    int InspirationMilliseconds, int RespAmplitudeCounts, int? Co2PlateauStartCentiMmHg = null,
    int Co2BaselineMmHg = 0, int Co2EndExpiratoryMmHg = 40, int Co2DeadSpaceMilliseconds = 125,
    int Co2RiseMilliseconds = 250, int Co2FallMilliseconds = 200, int Co2TransportDelayMilliseconds = 0, int Co2DispersionStepMilliseconds = 0,
    int InspiratoryPauseMilliseconds = 0, int ExpiratoryPauseMilliseconds = 0, int RespCardiacArtifactCounts = 0,
    RespiratoryActivity RespiratoryActivity = RespiratoryActivity.Breathing, int? ActivityAfterBreaths = null, int? ActivityDurationBreaths = null, int VentricularConductionRatio = 1, CardiacActivity CardiacActivity = CardiacActivity.AtrialAndVentricular, bool VentricularMechanicalEnabled = true, int? MechanicalAfterCycles = null, int? MechanicalDurationCycles = null, int MechanicalEveryCycles = 1, bool UseVascularReservoir = false, int? IndependentVentricularPeriodMilliseconds = null, int? IndependentVentricularOffsetMilliseconds = null, RespiratoryPattern RespiratoryPattern = RespiratoryPattern.Regular, int ConductedBeatsPerGroup = 1, AvConductionPattern ConductionPattern = AvConductionPattern.FixedPr, EcgBundleBlockIllustration BundleBlock = EcgBundleBlockIllustration.Reference, bool IllustrateAfSystemicPulseDeficit = false, bool IllustrateAfAberrancy = false, bool Wpw = false, bool WpwNegativeV1 = false, bool ShortPr = false, bool NormalPrDelta = false, bool WpwSmallerDelta = false, bool ProlongedPrDelta = false, bool Svt = false, bool Vt = false, bool VtFusion = false, bool VtCapture = false, bool VtBidirectional = false, bool VtTwisting = false, bool Aivr = false, bool Ajr = false, bool Aar = false, bool SvtRbbb = false, bool SvtLbbb = false, bool AivrFusion = false, bool AivrCapture = false, bool AtrialEscape = false)
{
    public CalciumIllustration Calcium { get; init; }
    public bool HypokalemiaRepolarization { get; init; }
    public bool HypokalemiaInvertedT { get; init; }
    public bool HypokalemiaTuFusion { get; init; }
    public bool HypokalemiaConduction { get; init; }
    public bool HyperkalemiaRepolarization { get; init; }
    public bool HyperkalemiaConduction { get; init; }
    public bool HyperkalemiaAbsentP { get; init; }
    public int AbpPulsePermille { get; init; } = 1000;
    public int PaPulsePermille { get; init; } = 1000;
    public int CvpBaselineCentiMmHg { get; init; } = 600;
    public SeededExpirationPressure? SeededCo2 { get; init; }
    public SeededCardiacRate? SeededRate { get; init; }
    public static PhysiologyIllustrationConfiguration Default { get; } = new(3750, 1875, 1000, UseVascularReservoir: true);

    public static PhysiologyIllustrationConfiguration SinusArrhythmiaPreset { get; } = Default with { ConductionPattern = AvConductionPattern.SinusArrhythmiaIllustration };
    public static PhysiologyIllustrationConfiguration SinusArrestPreset { get; } = Default with { ConductionPattern = AvConductionPattern.SinusArrestIllustration };
    public static PhysiologyIllustrationConfiguration AtrialEscapePreset { get; } = Default with { AtrialEscape = true };
    public static PhysiologyIllustrationConfiguration AarPreset { get; } = Default with { Aar = true };
    public static PhysiologyIllustrationConfiguration AjrPreset { get; } = Default with { Ajr = true };
    public static PhysiologyIllustrationConfiguration AivrPreset { get; } = Default with { Aivr = true };
    public static PhysiologyIllustrationConfiguration VtPreset { get; } = Default with { Vt = true };
    public static PhysiologyIllustrationConfiguration SvtPreset { get; } = Default with { Svt = true };
    public static PhysiologyIllustrationConfiguration NormalPrDeltaPreset { get; } = Default with { NormalPrDelta = true };
    public static PhysiologyIllustrationConfiguration ShortPrPreset { get; } = Default with { ShortPr = true };
    public static PhysiologyIllustrationConfiguration WpwPreset { get; } = Default with { Wpw = true };

    public static PhysiologyIllustrationConfiguration PrematureAtrial { get; } = Default with
    { ConductionPattern = AvConductionPattern.PrematureAtrialIllustration };

    public static PhysiologyIllustrationConfiguration BlockedPrematureAtrial { get; } = PrematureAtrial with
    { ConductionPattern = AvConductionPattern.BlockedPrematureAtrialIllustration };

    public static PhysiologyIllustrationConfiguration AberrantPrematureAtrial { get; } = PrematureAtrial with
    { ConductionPattern = AvConductionPattern.AberrantPrematureAtrialIllustration };

    public static PhysiologyIllustrationConfiguration PrematureVentricular { get; } = PrematureAtrial with
    { ConductionPattern = AvConductionPattern.PrematureVentricularIllustration };

    public static PhysiologyIllustrationConfiguration PrematureJunctional { get; } = Default with
    { ConductionPattern = AvConductionPattern.PrematureJunctionalIllustration };

    public static PhysiologyIllustrationConfiguration JunctionalEscape { get; } = Default with
    {
        ConductionPattern = AvConductionPattern.CompleteAvBlockJunctionalIllustration,
        IndependentVentricularPeriodMilliseconds = 1200,
        IndependentVentricularOffsetMilliseconds = 400,
    };

    public static PhysiologyIllustrationConfiguration VentricularEscape { get; } = Default with
    {
        ConductionPattern = AvConductionPattern.CompleteAvBlockVentricularIllustration,
        IndependentVentricularPeriodMilliseconds = 2000,
        IndependentVentricularOffsetMilliseconds = 400,
    };

    public static PhysiologyIllustrationConfiguration VariableFlutter => Flutter(2) with { ConductionPattern = AvConductionPattern.VariableAtrialFlutterIllustration };

    public static PhysiologyIllustrationConfiguration Flutter(int ratio) => Default with
    { VentricularConductionRatio = ratio, ConductionPattern = AvConductionPattern.AtrialFlutterIllustration };

    public static PhysiologyIllustrationConfiguration Disorganized(AvConductionPattern pattern) => Default with
    { CardiacActivity = CardiacActivity.VentricularOnly, VentricularMechanicalEnabled = false, UseVascularReservoir = true, ConductionPattern = pattern };

    public static PhysiologyIllustrationConfiguration Fibrillation(bool fine = false) => Default with
    { ConductionPattern = fine ? AvConductionPattern.AtrialFibrillationFineIllustration : AvConductionPattern.AtrialFibrillationCoarseIllustration };

    public CapnogramPlan ResolveCapnogram()
    {
        // Match the demo's fixed0..80mmHg display range; source limits are separate.
        if (Co2BaselineMmHg is < 0 or > 80 || Co2EndExpiratoryMmHg is < 0 or > 80)
        { throw new ArgumentException("PhysiologyDemo.InvalidCo2Pressure"); }
        if (Co2TransportDelayMilliseconds is < 0 or > 5000)
        { throw new ArgumentException("PhysiologyDemo.InvalidCo2TransportDelay"); }
        if (Co2DispersionStepMilliseconds is < 0 or > 500)
        { throw new ArgumentException("PhysiologyDemo.InvalidCo2Dispersion"); }
        return new(Co2DeadSpaceMilliseconds * 1_000_000L, Co2RiseMilliseconds * 1_000_000L,
            Co2FallMilliseconds * 1_000_000L, Co2BaselineMmHg, Co2EndExpiratoryMmHg, Co2PlateauStartCentiMmHg, Co2TransportDelayMilliseconds * 1_000_000L, Co2DispersionStepMilliseconds * 1_000_000L)
        { SeededPressure = SeededCo2 };
    }

    public RegularPhysiologyPlan ResolvePlan()
    {
        if (!Enum.IsDefined(Calcium)) { throw new EventWaveformException("Calcium.InvalidMode", "configuration"); }
        if (Calcium != CalciumIllustration.Reference && (HyperkalemiaRepolarization || HyperkalemiaConduction || HyperkalemiaAbsentP ||
            HypokalemiaRepolarization || HypokalemiaInvertedT || HypokalemiaTuFusion || HypokalemiaConduction))
        { throw new EventWaveformException("Calcium.ConflictingModes", "configuration"); }
        if ((HypokalemiaInvertedT || HypokalemiaTuFusion || HypokalemiaConduction) && !HypokalemiaRepolarization ||
            HypokalemiaRepolarization && (HyperkalemiaRepolarization || HyperkalemiaConduction || HyperkalemiaAbsentP))
        { throw new EventWaveformException("HypokalemiaRepolarization.ConflictingModes", "configuration"); }
        if (HyperkalemiaConduction && !HyperkalemiaRepolarization || HyperkalemiaAbsentP && !HyperkalemiaConduction)
        { throw new EventWaveformException("HyperkalemiaConduction.ConflictingModes", "configuration"); }
        if ((HyperkalemiaRepolarization || HypokalemiaRepolarization || Calcium != CalciumIllustration.Reference) && (ConductionPattern != AvConductionPattern.FixedPr ||
            VentricularConductionRatio != 1 || ConductedBeatsPerGroup != 1 || CardiacActivity != (HyperkalemiaAbsentP ? CardiacActivity.VentricularOnly : CardiacActivity.AtrialAndVentricular) ||
            IndependentVentricularPeriodMilliseconds is not null || IndependentVentricularOffsetMilliseconds is not null ||
            BundleBlock != EcgBundleBlockIllustration.Reference || SeededRate is not null ||
            Wpw || WpwNegativeV1 || WpwSmallerDelta || ShortPr || NormalPrDelta || ProlongedPrDelta ||
            Svt || SvtRbbb || SvtLbbb || Vt || VtFusion || VtCapture || VtBidirectional || VtTwisting ||
            Aivr || AivrFusion || AivrCapture || Ajr || Aar || AtrialEscape || IllustrateAfAberrancy || IllustrateAfSystemicPulseDeficit))
        { throw new EventWaveformException(Calcium != CalciumIllustration.Reference ? "Calcium.ConflictingModes" : HypokalemiaRepolarization ? "HypokalemiaRepolarization.ConflictingModes" : "HyperkalemiaRepolarization.ConflictingModes", "configuration"); }
        if (((AivrFusion || AivrCapture) && !Aivr) || (AivrFusion && AivrCapture)) { throw new EventWaveformException("Aivr.ConflictingModes", "configuration"); }
        if (((SvtRbbb || SvtLbbb) && !Svt) || (SvtRbbb && SvtLbbb)) { throw new EventWaveformException("Svt.ConflictingModes", "configuration"); }
        if (AtrialEscape && (Aar || Ajr || Aivr || Vt || VtFusion || VtCapture || VtBidirectional || VtTwisting || Svt ||
            NormalPrDelta || ProlongedPrDelta || ShortPr || Wpw || WpwNegativeV1 || WpwSmallerDelta ||
            BundleBlock != EcgBundleBlockIllustration.Reference || ConductionPattern != AvConductionPattern.FixedPr ||
            VentricularConductionRatio != 1 || ConductedBeatsPerGroup != 1 || CardiacActivity != CardiacActivity.AtrialAndVentricular ||
            IndependentVentricularPeriodMilliseconds is not null || IndependentVentricularOffsetMilliseconds is not null ||
            IllustrateAfSystemicPulseDeficit || IllustrateAfAberrancy || !UseVascularReservoir))
        { throw new EventWaveformException("AtrialEscape.ConflictingModes", "configuration"); }
        if (Aar && (Ajr || Aivr || Vt || VtFusion || VtCapture || VtBidirectional || VtTwisting || Svt ||
            NormalPrDelta || ProlongedPrDelta || ShortPr || Wpw || WpwNegativeV1 || WpwSmallerDelta ||
            BundleBlock != EcgBundleBlockIllustration.Reference || ConductionPattern != AvConductionPattern.FixedPr ||
            VentricularConductionRatio != 1 || ConductedBeatsPerGroup != 1 || CardiacActivity != CardiacActivity.AtrialAndVentricular ||
            IndependentVentricularPeriodMilliseconds is not null || IndependentVentricularOffsetMilliseconds is not null ||
            IllustrateAfSystemicPulseDeficit || IllustrateAfAberrancy || !UseVascularReservoir))
        { throw new EventWaveformException("Aar.ConflictingModes", "configuration"); }
        if (Ajr && (Aivr || Vt || VtFusion || VtCapture || VtBidirectional || VtTwisting || Svt ||
            NormalPrDelta || ProlongedPrDelta || ShortPr || Wpw || WpwNegativeV1 || WpwSmallerDelta ||
            BundleBlock != EcgBundleBlockIllustration.Reference || ConductionPattern != AvConductionPattern.FixedPr ||
            VentricularConductionRatio != 1 || ConductedBeatsPerGroup != 1 || CardiacActivity != CardiacActivity.AtrialAndVentricular ||
            IndependentVentricularPeriodMilliseconds is not null || IndependentVentricularOffsetMilliseconds is not null ||
            IllustrateAfSystemicPulseDeficit || IllustrateAfAberrancy || !UseVascularReservoir))
        { throw new EventWaveformException("Ajr.ConflictingModes", "configuration"); }
        if (Aivr && (Vt || VtFusion || VtCapture || VtBidirectional || VtTwisting || Svt ||
            NormalPrDelta || ProlongedPrDelta || ShortPr || Wpw || WpwNegativeV1 || WpwSmallerDelta ||
            BundleBlock != EcgBundleBlockIllustration.Reference || ConductionPattern != AvConductionPattern.FixedPr ||
            VentricularConductionRatio != 1 || ConductedBeatsPerGroup != 1 || CardiacActivity != CardiacActivity.AtrialAndVentricular ||
            IndependentVentricularPeriodMilliseconds is not null || IndependentVentricularOffsetMilliseconds is not null ||
            IllustrateAfSystemicPulseDeficit || IllustrateAfAberrancy || !UseVascularReservoir))
        { throw new EventWaveformException("Aivr.ConflictingModes", "configuration"); }
        if (((VtFusion || VtCapture || VtBidirectional || VtTwisting) && !Vt) || (VtBidirectional && (VtFusion || VtCapture)) || (VtTwisting && (VtBidirectional || VtFusion || VtCapture))) { throw new EventWaveformException("Vt.ConflictingModes", "configuration"); }
        if (Vt && (Svt || NormalPrDelta || ProlongedPrDelta || ShortPr || Wpw || WpwNegativeV1 || WpwSmallerDelta ||
            BundleBlock != EcgBundleBlockIllustration.Reference || ConductionPattern != AvConductionPattern.FixedPr ||
            VentricularConductionRatio != 1 || ConductedBeatsPerGroup != 1 || CardiacActivity != CardiacActivity.AtrialAndVentricular ||
            IndependentVentricularPeriodMilliseconds is not null || IndependentVentricularOffsetMilliseconds is not null ||
            IllustrateAfSystemicPulseDeficit || IllustrateAfAberrancy || !UseVascularReservoir))
        { throw new EventWaveformException("Vt.ConflictingModes", "configuration"); }
        if (Svt && (NormalPrDelta || ProlongedPrDelta || ShortPr || Wpw || WpwNegativeV1 || WpwSmallerDelta ||
            BundleBlock != EcgBundleBlockIllustration.Reference || ConductionPattern != AvConductionPattern.FixedPr ||
            VentricularConductionRatio != 1 || ConductedBeatsPerGroup != 1 || CardiacActivity != CardiacActivity.AtrialAndVentricular ||
            IndependentVentricularPeriodMilliseconds is not null || IndependentVentricularOffsetMilliseconds is not null ||
            IllustrateAfSystemicPulseDeficit || IllustrateAfAberrancy || !UseVascularReservoir))
        { throw new EventWaveformException("Svt.ConflictingModes", "configuration"); }
        if (ProlongedPrDelta && !NormalPrDelta) { throw new EventWaveformException("NormalPrDelta.ConflictingModes", "configuration"); }
        if (NormalPrDelta && (ShortPr || Wpw || WpwNegativeV1 || BundleBlock != EcgBundleBlockIllustration.Reference ||
            ConductionPattern != AvConductionPattern.FixedPr || VentricularConductionRatio != 1 || ConductedBeatsPerGroup != 1 ||
            CardiacActivity != CardiacActivity.AtrialAndVentricular || IndependentVentricularPeriodMilliseconds is not null ||
            IndependentVentricularOffsetMilliseconds is not null || IllustrateAfSystemicPulseDeficit || IllustrateAfAberrancy))
        { throw new EventWaveformException("NormalPrDelta.ConflictingModes", "configuration"); }
        if (ShortPr && (Wpw || WpwNegativeV1 || BundleBlock != EcgBundleBlockIllustration.Reference ||
            ConductionPattern != AvConductionPattern.FixedPr || VentricularConductionRatio != 1 || ConductedBeatsPerGroup != 1 ||
            CardiacActivity != CardiacActivity.AtrialAndVentricular || IndependentVentricularPeriodMilliseconds is not null ||
            IndependentVentricularOffsetMilliseconds is not null || IllustrateAfSystemicPulseDeficit || IllustrateAfAberrancy))
        { throw new EventWaveformException("ShortPr.ConflictingModes", "configuration"); }
        if ((WpwNegativeV1 || WpwSmallerDelta) && !Wpw) { throw new EventWaveformException("Wpw.ConflictingModes", "configuration"); }
        if (Wpw && (BundleBlock != EcgBundleBlockIllustration.Reference || ConductionPattern != AvConductionPattern.FixedPr ||
            VentricularConductionRatio != 1 || ConductedBeatsPerGroup != 1 || CardiacActivity != CardiacActivity.AtrialAndVentricular ||
            IndependentVentricularPeriodMilliseconds is not null || IndependentVentricularOffsetMilliseconds is not null ||
            IllustrateAfSystemicPulseDeficit || IllustrateAfAberrancy))
        { throw new EventWaveformException("Wpw.ConflictingModes", "configuration"); }
        if (!Enum.IsDefined(BundleBlock)) { throw new EventWaveformException("EcgBundleBlock.InvalidMode", "BundleBlock"); }
        if (BundleBlock != EcgBundleBlockIllustration.Reference &&
            (ConductionPattern != AvConductionPattern.FixedPr || VentricularConductionRatio != 1 || ConductedBeatsPerGroup != 1 ||
             CardiacActivity != CardiacActivity.AtrialAndVentricular || IndependentVentricularPeriodMilliseconds is not null || IndependentVentricularOffsetMilliseconds is not null))
        { throw new EventWaveformException("EcgBundleBlock.ConflictingModes", "configuration"); }
        if (AtrialFlutterReference.IsPattern(ConductionPattern) && VentricularConductionRatio == 1 && !UseVascularReservoir)
        { throw new EventWaveformException("Flutter.OneToOneRequiresReservoir", "configuration"); }
        if (ConductionPattern == AvConductionPattern.SinusArrhythmiaIllustration && !UseVascularReservoir)
        { throw new EventWaveformException("SinusArrhythmia.RequiresReservoir", "configuration"); }
        if (ConductionPattern == AvConductionPattern.SinusArrestIllustration && !UseVascularReservoir)
        { throw new EventWaveformException("SinusArrest.RequiresReservoir", "configuration"); }
        _ = AuthoredConductionSelection.Index(VentricularConductionRatio, ConductedBeatsPerGroup, ConductionPattern);
        // Demo input/display bounds, not physiological normal ranges.
        if ((IndependentVentricularPeriodMilliseconds is { } independent && (independent is < 800 or > 3200 || VentricularConductionRatio != 1)) ||
            MechanicalEveryCycles is < 1 or > 4 || (MechanicalAfterCycles is { } cycles && (cycles is < 1 or > 100 || VentricularMechanicalEnabled || CardiacActivity is not (CardiacActivity.AtrialAndVentricular or CardiacActivity.VentricularOnly))) ||
            (MechanicalDurationCycles is { } mechanicalDuration && (mechanicalDuration is < 1 or > 100 || MechanicalAfterCycles is null)) ||
            !Enum.IsDefined(CardiacActivity) || VentricularConductionRatio is < 1 or > 5 || BreathPeriodMilliseconds is < 1000 or > 10000 || InspirationMilliseconds <= 0 ||
            InspirationMilliseconds >= BreathPeriodMilliseconds || RespAmplitudeCounts is < -1000 or > 1000 ||
            InspiratoryPauseMilliseconds < 0 || InspiratoryPauseMilliseconds >= InspirationMilliseconds ||
            ExpiratoryPauseMilliseconds < 0 || ExpiratoryPauseMilliseconds >= BreathPeriodMilliseconds - InspirationMilliseconds ||
            RespCardiacArtifactCounts is < -200 or > 200 || !Enum.IsDefined(RespiratoryActivity) ||
            (ActivityAfterBreaths is { } breaths && (breaths is < 1 or > 100 || RespiratoryActivity == RespiratoryActivity.Breathing)) ||
            (ActivityDurationBreaths is { } duration && (duration is < 1 or > 100 || ActivityAfterBreaths is null)))
        { throw new ArgumentException("PhysiologyDemo.InvalidConfiguration"); }
        if ((VentricularDisorganizationReference.IsPattern(ConductionPattern) || PrematureBeatPerfusion.IsPattern(ConductionPattern) || AtrialFibrillationReference.IsPattern(ConductionPattern)) && !UseVascularReservoir)
        { throw new ArgumentException("This rhythm illustration requires the vascular reservoir source."); }
        if (IllustrateAfSystemicPulseDeficit && !AtrialFibrillationReference.IsPattern(ConductionPattern))
        { throw new ArgumentException("AF pulse deficit requires an AF rhythm."); }
        if (IllustrateAfAberrancy && !AtrialFibrillationReference.IsPattern(ConductionPattern))
        { throw new ArgumentException("AF aberrancy requires an AF rhythm."); }
        bool flutter = AtrialFlutterReference.IsPattern(ConductionPattern);
        bool fibrillation = AtrialFibrillationReference.IsPattern(ConductionPattern);
        bool prematureBeat = PrematureAtrialReference.IsPattern(ConductionPattern) || PrematureJunctionalReference.IsPattern(ConductionPattern) || PrematureVentricularReference.IsPattern(ConductionPattern);
        var timing = Calcium != CalciumIllustration.Reference ? CalciumRepolarizationReference.Timing(Calcium) : HypokalemiaRepolarization ? (HypokalemiaConduction ? HypokalemiaRepolarizationReference.ConductionTiming : HypokalemiaRepolarizationReference.Timing) : HyperkalemiaConduction ? HyperkalemiaConductionReference.Timing : AtrialEscape ? AtrialEscapeReference.Timing : Aar ? AcceleratedAtrialReference.Timing : Svt ? SupraventricularTachycardiaReference.ResolveTiming(SvtRbbb, SvtLbbb) : NormalPrDelta ? NormalPrDeltaReference.ResolveTiming(ProlongedPrDelta) : ShortPr ? ShortPrReference.Timing : Wpw ? WpwReference.ResolveTiming(WpwSmallerDelta) : prematureBeat ? (ConductionPattern == AvConductionPattern.BlockedPrematureAtrialIllustration ? PrematureAtrialReference.BlockedTiming : PrematureAtrialReference.Timing) : fibrillation ? AtrialFibrillationReference.Timing : flutter ? AtrialFlutterReference.Timing(VentricularConductionRatio) : TextbookEcgReference.Timing;
        if (SeededRate is { } seeded)
        {
            if (ConductionPattern != AvConductionPattern.FixedPr || IndependentVentricularPeriodMilliseconds is not null ||
                VentricularConductionRatio != 1 || ConductedBeatsPerGroup != 1 || BundleBlock != EcgBundleBlockIllustration.Reference ||
                Wpw || ShortPr || NormalPrDelta || Svt || Vt || Aivr || Ajr || Aar || AtrialEscape)
            { throw new ArgumentException("SeededRate.RequiresReferenceSinus"); }
            timing = seeded.Timing;
        }
        long offset = Ajr || Aivr || Vt ? 120_000_000 : Svt ? 0 : IllustrationVentricularTiming.ResolveOffset(IndependentVentricularPeriodMilliseconds, IndependentVentricularOffsetMilliseconds, flutter ? 80_000_000 : timing.PrIntervalNs);
        return new RegularPhysiologyPlan(0, SeededRate?.PeriodNs ?? (Ajr || Aivr || Vt ? 800_000_000 : ConductionPattern == AvConductionPattern.InterpolatedPvcIllustration ? 1_000_000_000 : fibrillation || prematureBeat ? 800_000_000 : flutter ? 200_000_000 : timing.RrIntervalNs), offset, 80_000_000,
            offset + 80_000_000, BreathPeriodMilliseconds * 1_000_000L, InspirationMilliseconds * 1_000_000L,
            InspiratoryPauseMilliseconds * 1_000_000L, ExpiratoryPauseMilliseconds * 1_000_000L, RespiratoryActivity, ActivityAfterBreaths is { } count ? (ulong)count : null,
            ActivityDurationBreaths is { } durationCount ? (ulong)durationCount : null, VentricularConductionRatio, CardiacActivity, VentricularMechanicalEnabled, MechanicalAfterCycles is { } mechanicalCycles ? (ulong)mechanicalCycles : null, MechanicalDurationCycles is { } durationCycles ? (ulong)durationCycles : null, MechanicalEveryCycles, Ajr ? 600_000_000 : Aivr ? 750_000_000 : Vt ? 375_000_000 : IndependentVentricularPeriodMilliseconds is { } period ? period * 1_000_000L : null, RespiratoryPattern, ConductedBeatsPerGroup, Ajr ? AvConductionPattern.AcceleratedJunctionalIllustration : Aivr ? AvConductionPattern.AcceleratedVentricularIllustration : Vt ? (VtCapture ? AvConductionPattern.VtCaptureIllustration : AvConductionPattern.MonomorphicVtIllustration) : Svt ? AvConductionPattern.NarrowComplexSvtIllustration : ConductionPattern)
        { SeededRate = SeededRate };
    }
}
