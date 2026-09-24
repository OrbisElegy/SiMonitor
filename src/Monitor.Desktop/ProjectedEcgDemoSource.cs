// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed record ProjectedEcgDemoConfiguration(int HeartRateBpm, int QtcMilliseconds, string? MethodId, int VentricularConductionRatio = 1,
    int PDurationMilliseconds = 100, int PrIntervalMilliseconds = 160,
    int QrsDurationMilliseconds = 80, int TDurationMilliseconds = 180,
    ProjectedEcgUConfiguration? UWave = null, CardiacActivity CardiacActivity = CardiacActivity.AtrialAndVentricular, EcgLimbPlacement Placement = EcgLimbPlacement.Standard, int? IndependentVentricularPeriodMilliseconds = null, int? IndependentVentricularOffsetMilliseconds = null, ProjectedEcgTConfiguration? TWave = null, int ChestJMicrovolts = 0, int ChestStEndMicrovolts = 0, EcgPWaveComponents? ChestP = null, int ChestStArchMicrovolts = 0, ProjectedEcgFusionConfiguration? Fusion = null, EcgChestInfarctionPlan? Infarction = null, EcgInfarctionZones? Zones = null, EcgAtrialIllustration Atrial = EcgAtrialIllustration.Reference, EcgVentricularIllustration Ventricular = EcgVentricularIllustration.Reference, EcgTContourPlan? TContour = null, int ConductedBeatsPerGroup = 1, AvConductionPattern ConductionPattern = AvConductionPattern.FixedPr, EcgBundleBlockIllustration BundleBlock = EcgBundleBlockIllustration.Reference, bool IllustrateAfAberrancy = false, bool HyperkalemiaRepolarization = false, bool HypokalemiaRepolarization = false, bool HypokalemiaTuFusion = false, bool HyperkalemiaConduction = false, bool HyperkalemiaAbsentP = false, bool HyperkalemiaFusion = false, bool HypokalemiaInvertedT = false, bool HypokalemiaConduction = false, CalciumIllustration Calcium = CalciumIllustration.Reference, bool DigitalisEffect = false, DigitalisTShape DigitalisShape = DigitalisTShape.FishHook, QuinidineIllustration Quinidine = QuinidineIllustration.Reference, bool QuinidineNotchedP = false, bool Wpw = false, bool WpwNegativeV1 = false, bool ShortPr = false, bool NormalPrDelta = false, bool WpwSmallerDelta = false, bool ProlongedPrDelta = false, bool Svt = false, bool Vt = false, bool VtFusion = false, bool VtCapture = false, bool VtBidirectional = false, bool VtTwisting = false, bool Aivr = false, bool Ajr = false, bool Aar = false, bool SvtRbbb = false, bool SvtLbbb = false)
{
    internal static ProjectedEcgDemoConfiguration Default { get; } = new(75, 400, null);

    internal static ProjectedEcgDemoConfiguration CalciumPreset(CalciumIllustration mode)
    {
        var timing = CalciumRepolarizationReference.Timing(mode);
        return mode == CalciumIllustration.Reference ? Default : Default with
        { Calcium = mode, HeartRateBpm = 60, TDurationMilliseconds = (int)(timing.TDurationNs / 1_000_000) };
    }

    internal static ProjectedEcgDemoConfiguration QuinidinePreset(QuinidineIllustration mode, bool notchedP = false)
    {
        if (!Enum.IsDefined(mode)) { throw new EventWaveformException("Quinidine.InvalidMode", nameof(mode)); }
        if (mode == QuinidineIllustration.Reference && notchedP)
        { throw new EventWaveformException("Quinidine.ConflictingModes", nameof(notchedP)); }
        return mode == QuinidineIllustration.Reference ? Default : Default with
        {
            Quinidine = mode,
            QuinidineNotchedP = notchedP,
            HeartRateBpm = 60,
            PDurationMilliseconds = 120,
            PrIntervalMilliseconds = 200,
            QrsDurationMilliseconds = (int)(QuinidineEffectReference.ResolveTiming(mode).QrsDurationNs / 1_000_000)
        };
    }

    internal static ProjectedEcgDemoConfiguration AarPreset { get; } = Default with
    { Aar = true, HeartRateBpm = 100, PDurationMilliseconds = 80 };

    internal static ProjectedEcgDemoConfiguration AjrPreset { get; } = Default with
    { Ajr = true, PrIntervalMilliseconds = 100 };

    internal static ProjectedEcgDemoConfiguration AivrPreset { get; } = Default with
    { Aivr = true, PrIntervalMilliseconds = 100, QrsDurationMilliseconds = 160 };

    internal static ProjectedEcgDemoConfiguration VtPreset { get; } = Default with
    { Vt = true, PrIntervalMilliseconds = 100, QrsDurationMilliseconds = 160, TDurationMilliseconds = 100 };

    internal static ProjectedEcgDemoConfiguration SvtPreset { get; } = Default with
    { Svt = true, HeartRateBpm = 200, PDurationMilliseconds = 40, PrIntervalMilliseconds = 40, TDurationMilliseconds = 100 };
    internal static ProjectedEcgDemoConfiguration SvtLeftBundlePreset { get; } = SvtPreset with
    { SvtLbbb = true, QrsDurationMilliseconds = 140 };
    internal static ProjectedEcgDemoConfiguration SvtRightBundlePreset { get; } = SvtPreset with
    { SvtRbbb = true, QrsDurationMilliseconds = 140 };
    internal static ProjectedEcgDemoConfiguration NormalPrDeltaPreset { get; } = Default with
    { NormalPrDelta = true, QrsDurationMilliseconds = 140 };
    internal static ProjectedEcgDemoConfiguration ShortPrPreset { get; } = Default with
    { ShortPr = true, PDurationMilliseconds = 80, PrIntervalMilliseconds = 100 };
    internal static ProjectedEcgDemoConfiguration WpwPreset { get; } = Default with
    { Wpw = true, PDurationMilliseconds = 80, PrIntervalMilliseconds = 100, QrsDurationMilliseconds = 140 };

    internal static ProjectedEcgDemoConfiguration Digitalis { get; } = Default with
    { DigitalisEffect = true, HeartRateBpm = 60, TDurationMilliseconds = 120 };

    internal static ProjectedEcgDemoConfiguration Hypokalemia { get; } = Default with
    { HypokalemiaRepolarization = true, HeartRateBpm = 60 };

    internal static ProjectedEcgDemoConfiguration HypokalemiaWithConduction { get; } = Hypokalemia with
    { HypokalemiaConduction = true, QrsDurationMilliseconds = 120 };

    internal static ProjectedEcgDemoConfiguration Hyperkalemia { get; } = Default with
    { HyperkalemiaRepolarization = true, TDurationMilliseconds = 120 };

    internal static ProjectedEcgDemoConfiguration HyperkalemiaWithConduction { get; } = Hyperkalemia with
    {
        HyperkalemiaConduction = true,
        HeartRateBpm = 60,
        PDurationMilliseconds = 140,
        PrIntervalMilliseconds = 240,
        QrsDurationMilliseconds = 140,
        TDurationMilliseconds = 160
    };

    internal static ProjectedEcgDemoConfiguration HyperkalemiaWithoutP { get; } = HyperkalemiaWithConduction with
    { HyperkalemiaAbsentP = true, CardiacActivity = CardiacActivity.VentricularOnly };

    internal static ProjectedEcgDemoConfiguration HyperkalemiaWithFusion { get; } = HyperkalemiaWithoutP with
    { HyperkalemiaFusion = true, QrsDurationMilliseconds = 320, TDurationMilliseconds = 400 };

    internal static ProjectedEcgDemoConfiguration PrematureAtrial { get; } = Default with
    { TDurationMilliseconds = 140, ConductionPattern = AvConductionPattern.PrematureAtrialIllustration };

    internal static ProjectedEcgDemoConfiguration BlockedPrematureAtrial { get; } = PrematureAtrial with
    { ConductionPattern = AvConductionPattern.BlockedPrematureAtrialIllustration };

    internal static ProjectedEcgDemoConfiguration AberrantPrematureAtrial { get; } = PrematureAtrial with
    { ConductionPattern = AvConductionPattern.AberrantPrematureAtrialIllustration };

    internal static ProjectedEcgDemoConfiguration PrematureVentricular { get; } = PrematureAtrial with
    { ConductionPattern = AvConductionPattern.PrematureVentricularIllustration };

    internal static ProjectedEcgDemoConfiguration Pvc(AvConductionPattern pattern)
    {
        _ = PrematureVentricularReference.BeatsPerGroup(pattern);
        return PrematureVentricular with { ConductionPattern = pattern, HeartRateBpm = pattern == AvConductionPattern.InterpolatedPvcIllustration ? 60 : 75 };
    }

    internal static ProjectedEcgDemoConfiguration PrematureJunctional { get; } = PrematureAtrial with
    { ConductionPattern = AvConductionPattern.PrematureJunctionalIllustration };

    internal static ProjectedEcgDemoConfiguration JunctionalEscape { get; } = Default with
    {
        ConductionPattern = AvConductionPattern.CompleteAvBlockJunctionalIllustration,
        IndependentVentricularPeriodMilliseconds = 1200,
        IndependentVentricularOffsetMilliseconds = 400,
    };

    internal static ProjectedEcgDemoConfiguration VentricularEscape { get; } = Default with
    {
        ConductionPattern = AvConductionPattern.CompleteAvBlockVentricularIllustration,
        IndependentVentricularPeriodMilliseconds = 2000,
        IndependentVentricularOffsetMilliseconds = 400,
        QrsDurationMilliseconds = 160,
        TDurationMilliseconds = 220,
    };

    internal static ProjectedEcgDemoConfiguration VariableFlutter => Flutter(2) with { ConductionPattern = AvConductionPattern.VariableAtrialFlutterIllustration };

    internal static ProjectedEcgDemoConfiguration Flutter(int ratio) => Default with
    {
        HeartRateBpm = 300,
        PDurationMilliseconds = 40,
        PrIntervalMilliseconds = 80,
        TDurationMilliseconds = 140,
        VentricularConductionRatio = ratio,
        ConductionPattern = AvConductionPattern.AtrialFlutterIllustration,
    };

    internal static ProjectedEcgDemoConfiguration Disorganized(AvConductionPattern pattern) => Default with
    { CardiacActivity = CardiacActivity.VentricularOnly, ConductionPattern = pattern };

    internal static ProjectedEcgDemoConfiguration Fibrillation(bool fine = false) => Default with
    {
        PDurationMilliseconds = 40,
        PrIntervalMilliseconds = 80,
        TDurationMilliseconds = 140,
        ConductionPattern = fine ? AvConductionPattern.AtrialFibrillationFineIllustration : AvConductionPattern.AtrialFibrillationCoarseIllustration,
    };

    internal long ResolveAtrialPeriodNs()
    {
        if (AtrialFlutterReference.IsPattern(ConductionPattern)) { return 200_000_000; }
        if (HeartRateBpm is < 30 or > 200) { throw new ArgumentException("Invalid base rate."); }
        return (long)Monitor.Simulation.Determinism.FixedPointMath.RoundDivideTiesToEven(60_000_000_000, HeartRateBpm);
    }

    internal EcgCycleTiming ResolveTiming()
    {
        if (((SvtRbbb || SvtLbbb) && !Svt) || (SvtRbbb && SvtLbbb)) { throw new EventWaveformException("Svt.ConflictingModes", "configuration"); }
        if (Aar)
        {
            if (this != AarPreset) { throw new EventWaveformException("Aar.ConflictingModes", "configuration"); }
            return AcceleratedAtrialReference.Timing;
        }
        if (Ajr)
        {
            if (this != AjrPreset) { throw new EventWaveformException("Ajr.ConflictingModes", "configuration"); }
            return AcceleratedJunctionalReference.Timing;
        }
        if (Aivr)
        {
            if (this != AivrPreset) { throw new EventWaveformException("Aivr.ConflictingModes", "configuration"); }
            return AcceleratedVentricularReference.Timing;
        }
        if (((VtFusion || VtCapture || VtBidirectional || VtTwisting) && !Vt) || (VtBidirectional && (VtFusion || VtCapture)) || (VtTwisting && (VtBidirectional || VtFusion || VtCapture))) { throw new EventWaveformException("Vt.ConflictingModes", "configuration"); }
        if (Vt)
        {
            if ((this with { VtFusion = false, VtCapture = false, VtBidirectional = false, VtTwisting = false }) != VtPreset) { throw new EventWaveformException("Vt.ConflictingModes", "configuration"); }
            return VentricularTachycardiaReference.Timing;
        }
        if (Svt)
        {
            if (this != (SvtLbbb ? SvtLeftBundlePreset : SvtRbbb ? SvtRightBundlePreset : SvtPreset)) { throw new EventWaveformException("Svt.ConflictingModes", "configuration"); }
            return SupraventricularTachycardiaReference.ResolveTiming(SvtRbbb, SvtLbbb);
        }
        if (ProlongedPrDelta && !NormalPrDelta) { throw new EventWaveformException("NormalPrDelta.ConflictingModes", "configuration"); }
        if (NormalPrDelta)
        {
            if (this != (NormalPrDeltaPreset with { ProlongedPrDelta = ProlongedPrDelta, PrIntervalMilliseconds = ProlongedPrDelta ? 240 : 160 })) { throw new EventWaveformException("NormalPrDelta.ConflictingModes", "configuration"); }
            return NormalPrDeltaReference.ResolveTiming(ProlongedPrDelta);
        }
        if (ShortPr)
        {
            if (this != ShortPrPreset) { throw new EventWaveformException("ShortPr.ConflictingModes", "configuration"); }
            return ShortPrReference.Timing;
        }
        if ((WpwNegativeV1 || WpwSmallerDelta) && !Wpw) { throw new EventWaveformException("Wpw.ConflictingModes", "configuration"); }
        if (Wpw)
        {
            if (this with { WpwNegativeV1 = false } != (WpwPreset with { WpwSmallerDelta = WpwSmallerDelta, QrsDurationMilliseconds = WpwSmallerDelta ? 110 : 140 })) { throw new EventWaveformException("Wpw.ConflictingModes", "configuration"); }
            return WpwReference.ResolveTiming(WpwSmallerDelta);
        }
        if (!Enum.IsDefined(Quinidine)) { throw new EventWaveformException("Quinidine.InvalidMode", "configuration"); }
        if (Quinidine == QuinidineIllustration.Reference && QuinidineNotchedP)
        { throw new EventWaveformException("Quinidine.ConflictingModes", "configuration"); }
        if (Quinidine != QuinidineIllustration.Reference)
        {
            if (!Enum.IsDefined(Placement) || this with { Placement = EcgLimbPlacement.Standard } != QuinidinePreset(Quinidine, QuinidineNotchedP))
            { throw new EventWaveformException("Quinidine.ConflictingModes", "configuration"); }
            return QuinidineEffectReference.ResolveTiming(Quinidine);
        }
        if (!Enum.IsDefined(DigitalisShape) || (!DigitalisEffect && DigitalisShape != DigitalisTShape.FishHook))
        { throw new EventWaveformException("Digitalis.ConflictingModes", "configuration"); }
        if (DigitalisEffect)
        {
            if (!Enum.IsDefined(Placement) || this with { Placement = EcgLimbPlacement.Standard, DigitalisShape = DigitalisTShape.FishHook } != Digitalis)
            { throw new EventWaveformException("Digitalis.ConflictingModes", "configuration"); }
            return DigitalisEffectReference.Timing;
        }
        if (!Enum.IsDefined(Calcium)) { throw new EventWaveformException("Calcium.InvalidMode", "configuration"); }
        if (Calcium != CalciumIllustration.Reference)
        {
            if (!Enum.IsDefined(Placement) || this with { Placement = EcgLimbPlacement.Standard } != CalciumPreset(Calcium))
            { throw new EventWaveformException("Calcium.ConflictingModes", "configuration"); }
            return CalciumRepolarizationReference.Timing(Calcium);
        }
        if (HyperkalemiaFusion && !HyperkalemiaAbsentP)
        { throw new EventWaveformException("Hyperkalemia.ConflictingModes", "configuration"); }
        if (HyperkalemiaAbsentP && (!HyperkalemiaConduction || !HyperkalemiaRepolarization))
        { throw new EventWaveformException("Hyperkalemia.ConflictingModes", "configuration"); }
        if (HyperkalemiaConduction && !HyperkalemiaRepolarization)
        { throw new EventWaveformException("Hyperkalemia.ConflictingModes", "configuration"); }
        if ((HypokalemiaTuFusion || HypokalemiaInvertedT || HypokalemiaConduction) && !HypokalemiaRepolarization)
        { throw new EventWaveformException("Hypokalemia.ConflictingModes", "configuration"); }
        if (HypokalemiaRepolarization)
        {
            if (!Enum.IsDefined(Placement) || this with { Placement = EcgLimbPlacement.Standard, HypokalemiaTuFusion = false, HypokalemiaInvertedT = false } != (HypokalemiaConduction ? HypokalemiaWithConduction : Hypokalemia))
            { throw new EventWaveformException("Hypokalemia.ConflictingModes", "configuration"); }
            return HypokalemiaConduction ? HypokalemiaRepolarizationReference.ConductionTiming : HypokalemiaRepolarizationReference.Timing;
        }
        if (HyperkalemiaRepolarization)
        {
            if (!Enum.IsDefined(Placement) || this with { Placement = EcgLimbPlacement.Standard } != (HyperkalemiaFusion ? HyperkalemiaWithFusion : HyperkalemiaAbsentP ? HyperkalemiaWithoutP : HyperkalemiaConduction ? HyperkalemiaWithConduction : Hyperkalemia))
            { throw new EventWaveformException("Hyperkalemia.ConflictingModes", "configuration"); }
            return HyperkalemiaFusion ? HyperkalemiaFusionReference.Timing : HyperkalemiaConduction ? HyperkalemiaConductionReference.Timing : HyperkalemiaRepolarizationReference.Timing;
        }
        if (IllustrateAfAberrancy && !AtrialFibrillationReference.IsPattern(ConductionPattern))
        { throw new ArgumentException("AF aberrancy requires an AF rhythm."); }
        if (!Enum.IsDefined(BundleBlock)) { throw new EventWaveformException("EcgBundleBlock.InvalidMode", "BundleBlock"); }
        if (BundleBlock != EcgBundleBlockIllustration.Reference)
        {
            if (this != BundleBlockPreset.Ecg(BundleBlock)) { throw new EventWaveformException("EcgBundleBlock.ConflictingModes", "configuration"); }
            return BundleBlockReference.Timing(BundleBlock);
        }
        if (PrematureVentricularReference.IsPattern(ConductionPattern))
        {
            if (this != Pvc(ConductionPattern)) { throw new ArgumentException("PVC requires its authored schedule and morphology."); }
            return PrematureVentricularReference.Timing;
        }
        if (PrematureJunctionalReference.IsPattern(ConductionPattern))
        {
            if (this != PrematureJunctional with { ConductionPattern = ConductionPattern }) { throw new ArgumentException("PJC requires its authored schedule and morphology."); }
            return PrematureJunctionalReference.Timing;
        }
        if (PrematureAtrialReference.IsPattern(ConductionPattern))
        {
            if (this != (ConductionPattern == AvConductionPattern.BlockedPrematureAtrialIllustration ? BlockedPrematureAtrial : ConductionPattern == AvConductionPattern.AberrantPrematureAtrialIllustration ? AberrantPrematureAtrial : PrematureAtrial)) { throw new ArgumentException("PAC requires its authored schedule and morphology."); }
            return ConductionPattern == AvConductionPattern.BlockedPrematureAtrialIllustration ? PrematureAtrialReference.BlockedTiming : PrematureAtrialReference.Timing;
        }
        if (ConductionPattern == AvConductionPattern.MobitzTwoLbbbFourToThreeIllustration)
        {
            if (this != SecondDegreeBlockPreset.Ecg(7)) { throw new ArgumentException("LBBB illustration requires its authored morphology and timing."); }
            return LeftBundleBlockReference.Timing;
        }
        if (ConductionPattern == AvConductionPattern.MobitzTwoRbbbFourToThreeIllustration)
        {
            if (this != SecondDegreeBlockPreset.Ecg(6)) { throw new ArgumentException("RBBB illustration requires its authored morphology and timing."); }
            return RightBundleBlockReference.Timing;
        }
        if (ConductionPattern is AvConductionPattern.MobitzTwoThreeToTwoIllustration or AvConductionPattern.MobitzTwoFourToThreeIllustration)
        {
            int preset = ConductionPattern == AvConductionPattern.MobitzTwoThreeToTwoIllustration ? 3 : 4;
            if (this != SecondDegreeBlockPreset.Ecg(preset)) { throw new ArgumentException("Mobitz II illustration requires its fixed reference shape and timing."); }
            return TextbookEcgReference.Timing;
        }
        if (VentricularDisorganizationReference.IsPattern(ConductionPattern))
        {
            if (this != Disorganized(ConductionPattern)) { throw new ArgumentException("Ventricular disorganization requires its authored preset."); }
            return TextbookEcgReference.Timing; // Construction bounds only; no P/QRS/T bands.
        }
        if (AtrialFibrillationReference.IsPattern(ConductionPattern))
        {
            if (this != (Fibrillation(ConductionPattern == AvConductionPattern.AtrialFibrillationFineIllustration) with { IllustrateAfAberrancy = IllustrateAfAberrancy }))
            { throw new ArgumentException("AF illustration requires the authored irregular schedule and morphology."); }
            return AtrialFibrillationReference.Timing;
        }
        if (AtrialFlutterReference.IsPattern(ConductionPattern))
        {
            if (this != (ConductionPattern == AvConductionPattern.VariableAtrialFlutterIllustration ? VariableFlutter : Flutter(VentricularConductionRatio)))
            { throw new ArgumentException("Flutter illustration requires its authored morphology/timing; select a supported fixed or variable flutter example."); }
            return AtrialFlutterReference.Timing(VentricularConductionRatio);
        }
        if (ConductionPattern == AvConductionPattern.CompleteAvBlockVentricularIllustration)
        {
            if (this with { IndependentVentricularPeriodMilliseconds = 2000, IndependentVentricularOffsetMilliseconds = 400 } != VentricularEscape ||
                IndependentVentricularPeriodMilliseconds is not (>= 1500 and <= 3000))
            { throw new ArgumentException("Ventricular escape requires the authored shape/timing and a 1500-3000ms escape period."); }
            return CompleteAvBlockVentricularReference.Timing with { RrIntervalNs = IndependentVentricularPeriodMilliseconds.Value * 1_000_000L };
        }
        if (!Enum.IsDefined(Atrial)) { throw new ArgumentException("Invalid atrial illustration."); }
        if (ConductionPattern == AvConductionPattern.CompleteAvBlockJunctionalIllustration &&
            this with { IndependentVentricularPeriodMilliseconds = 1200, IndependentVentricularOffsetMilliseconds = 400 } != JunctionalEscape)
        { throw new ArgumentException("Junctional illustration requires the reference morphology and timing; only escape period/phase are editable."); }
        var timing = ResolveBaseTiming();
        if (EcgAtrialIllustrations.PDurationNs(Atrial) is { } pDuration)
        { timing = timing with { PDurationNs = pDuration }; }
        if (EcgVentricularIllustrations.QrsDurationNs(Ventricular) is { } qrsDuration)
        { timing = timing with { QrsDurationNs = qrsDuration }; }
        timing.Validate();
        return timing;
    }

    private EcgCycleTiming ResolveBaseTiming()
    {
        _ = ConductionSelection.Index(VentricularConductionRatio, ConductedBeatsPerGroup, ConductionPattern);
        if (!Enum.IsDefined(Placement) || VentricularConductionRatio is < 1 or > 5 || !Enum.IsDefined(CardiacActivity)) { throw new ArgumentException("Invalid conduction ratio or cardiac activity."); }
        if (ChestStArchMicrovolts is < -1000 or > 1000 || ChestJMicrovolts is < -1000 or > 1000 || ChestStEndMicrovolts is < -1000 or > 1000) { throw new ArgumentException("Invalid chest ST offsets."); }
        if (ChestP is { } p && (p.EarlyMicrovolts is < -1000 or > 1000 || p.LateMicrovolts is < -1000 or > 1000))
        { throw new ArgumentException("Invalid C1 P components."); }
        long atrialPeriod = ResolveAtrialPeriodNs();
        if (IndependentVentricularPeriodMilliseconds is { } independent &&
            (independent is < 800 or > 3200 || independent * 1_000_000L < atrialPeriod || VentricularConductionRatio != 1))
        { throw new ArgumentException("Invalid independent ventricular period."); }
        long rr = IndependentVentricularPeriodMilliseconds is { } period ? period * 1_000_000L : atrialPeriod * (ConductedBeatsPerGroup > 1 ? 1 : VentricularConductionRatio);
        if (MethodId is null)
        {
            if (this with { VentricularConductionRatio = 1, ConductedBeatsPerGroup = 1, ConductionPattern = AvConductionPattern.FixedPr, UWave = null, CardiacActivity = CardiacActivity.AtrialAndVentricular, Placement = EcgLimbPlacement.Standard, IndependentVentricularPeriodMilliseconds = null, IndependentVentricularOffsetMilliseconds = null, TWave = null, ChestJMicrovolts = 0, ChestStEndMicrovolts = 0, ChestP = null, ChestStArchMicrovolts = 0, Fusion = null, Infarction = null, Zones = null, Atrial = EcgAtrialIllustration.Reference, Ventricular = EcgVentricularIllustration.Reference, TContour = null } != Default) { throw new ArgumentException("Invalid fixed reference configuration."); }
            return TextbookEcgReference.Timing with { RrIntervalNs = rr };
        }
        if (HeartRateBpm is < 30 or > 200 || QtcMilliseconds is < 1 or > 1000 ||
            PDurationMilliseconds is < 1 or > 1000 || PrIntervalMilliseconds is < 1 or > 1000 ||
            QrsDurationMilliseconds is < 1 or > 1000 || TDurationMilliseconds is < 1 or > 1000)
        { throw new ArgumentException("Demo parameter outside supported input bounds."); }
        long qt = new EcgQtCorrection(MethodId, QtcMilliseconds * 1_000_000L, rr).ResolveQtIntervalNs();
        // Validate the complete requested timing, not the adult reference's
        // fixed PR/QRS/T durations against a shorter requested RR interval.
        var timing = new EcgCycleTiming(rr, PDurationMilliseconds * 1_000_000L,
            PrIntervalMilliseconds * 1_000_000L, QrsDurationMilliseconds * 1_000_000L,
            qt, TDurationMilliseconds * 1_000_000L);
        timing.Validate();
        return timing;
    }
}

// Desktop binding for the shared textbook-constrained electrode reference.
internal static class ProjectedEcgDemoSource
{
    internal static string[] LeadNames => ["I", "II", "III", "aVR", "aVL", "aVF", "V1", "V2", "V3", "V4", "V5", "V6"];
    internal static Guid ChannelId(EcgLead lead) => Guid.Parse($"00000000-0000-4000-8000-{12 - (int)lead:D12}");

    internal static ElectrodeWaveformGroup Create(ProjectedEcgDemoConfiguration? configuration = null)
    {
        configuration ??= ProjectedEcgDemoConfiguration.Default;
        var timing = configuration.ResolveTiming();
        long offset = configuration.Svt ? 0 : DemoVentricularTiming.ResolveOffset(configuration.IndependentVentricularPeriodMilliseconds, configuration.IndependentVentricularOffsetMilliseconds, timing.PrIntervalNs);
        EcgStSegmentPlan? st = configuration.ChestJMicrovolts == 0 && configuration.ChestStEndMicrovolts == 0 && configuration.ChestStArchMicrovolts == 0 ? null : new(
            Enumerable.Range(0, 10).Select(i => i < 4 ? 0 : configuration.ChestJMicrovolts).ToArray(),
            Enumerable.Range(0, 10).Select(i => i < 4 ? 0 : configuration.ChestStEndMicrovolts).ToArray(),
            Enumerable.Range(0, 10).Select(i => i < 4 ? 0 : configuration.ChestStArchMicrovolts).ToArray());
        EcgPWavePlan? pWave = configuration.ChestP is { } p
            ? new(Enumerable.Range(0, 10).Select(i => i == (int)EcgElectrode.C1 ? p : null).ToArray()) : null;
        var electrodes = configuration.Aar ? AcceleratedAtrialReference.CreateElectrodes() : configuration.Ajr ? AcceleratedJunctionalReference.CreateElectrodes() : configuration.Aivr ? AcceleratedVentricularReference.CreateElectrodes() : configuration.Vt ? VentricularTachycardiaReference.CreateElectrodes(configuration.VtFusion, configuration.VtCapture, configuration.VtBidirectional, configuration.VtTwisting) : configuration.Svt ? SupraventricularTachycardiaReference.CreateElectrodes(configuration.SvtRbbb, configuration.SvtLbbb) : configuration.NormalPrDelta ? NormalPrDeltaReference.CreateElectrodes(configuration.ProlongedPrDelta) : configuration.ShortPr ? ShortPrReference.CreateElectrodes() : configuration.Wpw ? WpwReference.CreateElectrodes(configuration.WpwNegativeV1, configuration.WpwSmallerDelta) : configuration.Quinidine != QuinidineIllustration.Reference ? QuinidineEffectReference.CreateElectrodes(configuration.Quinidine, configuration.QuinidineNotchedP)
            : configuration.DigitalisEffect ? DigitalisEffectReference.CreateElectrodes(configuration.DigitalisShape)
            : configuration.Calcium != CalciumIllustration.Reference
            ? CalciumRepolarizationReference.CreateElectrodes(configuration.Calcium)
            : configuration.HypokalemiaRepolarization
            ? HypokalemiaRepolarizationReference.CreateElectrodes(configuration.HypokalemiaTuFusion, configuration.HypokalemiaInvertedT, configuration.HypokalemiaConduction)
            : configuration.HyperkalemiaRepolarization
            ? configuration.HyperkalemiaFusion ? HyperkalemiaFusionReference.CreateElectrodes() : configuration.HyperkalemiaConduction ? HyperkalemiaConductionReference.CreateElectrodes(configuration.HyperkalemiaAbsentP) : HyperkalemiaRepolarizationReference.CreateElectrodes()
            : configuration.BundleBlock != EcgBundleBlockIllustration.Reference
            ? BundleBlockReference.CreateElectrodes(configuration.BundleBlock)
            : configuration.ConductionPattern == AvConductionPattern.MobitzTwoLbbbFourToThreeIllustration
            ? LeftBundleBlockReference.CreateElectrodes()
            : configuration.ConductionPattern == AvConductionPattern.MobitzTwoRbbbFourToThreeIllustration
            ? RightBundleBlockReference.CreateElectrodes()
            : PrematureVentricularReference.IsPattern(configuration.ConductionPattern)
            ? PrematureVentricularReference.CreateElectrodes(configuration.ConductionPattern)
            : PrematureJunctionalReference.IsPattern(configuration.ConductionPattern)
            ? PrematureJunctionalReference.CreateElectrodes()
            : configuration.ConductionPattern == AvConductionPattern.AberrantPrematureAtrialIllustration
            ? PrematureAtrialReference.CreateAberrantElectrodes()
            : PrematureAtrialReference.IsPattern(configuration.ConductionPattern)
            ? PrematureAtrialReference.CreateElectrodes(configuration.ConductionPattern == AvConductionPattern.BlockedPrematureAtrialIllustration)
            : VentricularDisorganizationReference.IsPattern(configuration.ConductionPattern)
            ? VentricularDisorganizationReference.CreateElectrodes(configuration.ConductionPattern)
            : AtrialFibrillationReference.IsPattern(configuration.ConductionPattern)
            ? AtrialFibrillationReference.CreateElectrodes(configuration.ConductionPattern == AvConductionPattern.AtrialFibrillationFineIllustration, configuration.IllustrateAfAberrancy)
            : AtrialFlutterReference.IsPattern(configuration.ConductionPattern)
            ? AtrialFlutterReference.CreateElectrodes(configuration.VentricularConductionRatio)
            : configuration.ConductionPattern == AvConductionPattern.CompleteAvBlockVentricularIllustration
            ? CompleteAvBlockVentricularReference.CreateElectrodes()
            : configuration.ConductionPattern == AvConductionPattern.CompleteAvBlockJunctionalIllustration
            ? CompleteAvBlockJunctionalReference.CreateElectrodes()
            : configuration.Zones is { } zones
            ? TextbookElectrodeReference.CreateElectrodes(configuration.UWave?.Resolve(timing), timing, pWave: pWave, zones: zones, atrial: configuration.Atrial, ventricular: configuration.Ventricular, tContour: configuration.TContour)
            : TextbookElectrodeReference.CreateElectrodes(configuration.UWave?.Resolve(timing), timing, configuration.TWave?.Resolve(), configuration.TWave?.ResolveShape(), st, pWave, configuration.Fusion?.Resolve(), configuration.Infarction, atrial: configuration.Atrial, ventricular: configuration.Ventricular, tContour: configuration.TContour);
        RegularPhysiologyPlan plan = new(0, configuration.ResolveAtrialPeriodNs(), offset,
            80_000_000, offset + 80_000_000, 3_750_000_000, 1_875_000_000,
            VentricularConductionRatio: configuration.VentricularConductionRatio, CardiacActivity: configuration.CardiacActivity,
            VentricularMechanicalEnabled: !VentricularDisorganizationReference.IsPattern(configuration.ConductionPattern),
            IndependentVentricularPeriodNs: configuration.IndependentVentricularPeriodMilliseconds is { } period ? period * 1_000_000L : null, ConductedBeatsPerGroup: configuration.ConductedBeatsPerGroup, ConductionPattern: configuration.ConductionPattern);
        if (configuration.Aar) { plan = AcceleratedAtrialReference.CreatePlan(); }
        if (configuration.Ajr) { plan = AcceleratedJunctionalReference.CreatePlan(); }
        if (configuration.Aivr) { plan = AcceleratedVentricularReference.CreatePlan(); }
        if (configuration.Vt) { plan = VentricularTachycardiaReference.CreatePlan(configuration.VtCapture); }
        if (configuration.Svt) { plan = SupraventricularTachycardiaReference.CreatePlan(); }
        return ElectrodeWaveformGroup.Start(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), 1, 1, 1, 0, 16, plan, electrodes,
            Enum.GetValues<EcgLead>().Select(lead => new ElectrodeChannelPlan(lead, ChannelId(lead), 10, 0)).ToArray(), configuration.Placement);
    }
}
