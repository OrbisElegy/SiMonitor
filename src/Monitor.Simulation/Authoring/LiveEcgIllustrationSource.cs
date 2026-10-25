// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Simulation.Authoring;

public static class LiveEcgIllustrationSource
{
    public static LiveEcgProjection Create(PhysiologyIllustrationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var plan = configuration.ResolvePlan();
        var electrodes = configuration.EcgDefinition?.Electrodes ?? (configuration.Dextrocardia ? DextrocardiaReference.CreateElectrodes()
            : configuration.Pacing is { } pacing ? PacingReference.CreateElectrodes(pacing, configuration.PacingOutput)
            : configuration.ConductionPattern == AvConductionPattern.SinusArrestIllustration ? SinusArrestReference.CreateElectrodes()
            : configuration.ConductionPattern == AvConductionPattern.SinusArrhythmiaIllustration ? SinusArrhythmiaReference.CreateElectrodes()
            : configuration.AtrialEscape ? AtrialEscapeReference.CreateElectrodes()
            : configuration.Aar ? AcceleratedAtrialReference.CreateElectrodes()
            : configuration.Ajr ? AcceleratedJunctionalReference.CreateElectrodes()
            : configuration.Aivr ? AcceleratedVentricularReference.CreateElectrodes(configuration.AivrFusion, configuration.AivrCapture)
            : configuration.Vt ? VentricularTachycardiaReference.CreateElectrodes(configuration.VtFusion, configuration.VtCapture, configuration.VtBidirectional, configuration.VtTwisting)
            : configuration.Svt ? SupraventricularTachycardiaReference.CreateElectrodes(configuration.SvtRbbb, configuration.SvtLbbb)
            : configuration.NormalPrDelta ? NormalPrDeltaReference.CreateElectrodes(configuration.ProlongedPrDelta)
            : configuration.ShortPr ? ShortPrReference.CreateElectrodes()
            : configuration.Wpw ? WpwReference.CreateElectrodes(configuration.WpwNegativeV1, configuration.WpwSmallerDelta)
            : configuration.Quinidine != QuinidineIllustration.Reference ? QuinidineEffectReference.CreateElectrodes(configuration.Quinidine, configuration.QuinidineNotchedP)
            : configuration.DigitalisEffect ? DigitalisEffectReference.CreateElectrodes(configuration.DigitalisShape)
            : configuration.Calcium != CalciumIllustration.Reference
            ? CalciumRepolarizationReference.CreateElectrodes(configuration.Calcium)
            : configuration.HypokalemiaRepolarization
            ? HypokalemiaRepolarizationReference.CreateElectrodes(configuration.HypokalemiaTuFusion, configuration.HypokalemiaInvertedT, configuration.HypokalemiaConduction)
            : configuration.HyperkalemiaRepolarization
            ? configuration.HyperkalemiaFusion ? HyperkalemiaFusionReference.CreateElectrodes()
            : configuration.HyperkalemiaConduction ? HyperkalemiaConductionReference.CreateElectrodes(configuration.HyperkalemiaAbsentP) : HyperkalemiaRepolarizationReference.CreateElectrodes()
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
            : TextbookElectrodeReference.CreateElectrodes(timing: configuration.SeededRate?.Timing,
                infarction: configuration.Infarction, zones: configuration.Zones, atrial: configuration.AtrialShape,
                ventricular: configuration.VentricularShape, tContour: configuration.TContour));
        if (AtrialFibrillationReference.IsPattern(plan.ConductionPattern))
        {
            electrodes = Array.AsReadOnly(electrodes.Select(electrode => electrode with
            {
                Bands = Array.AsReadOnly(electrode.Bands.Select(band => band.AfBeatSelection is null
                    ? band : band with { AfTiming = plan }).ToArray())
            }).ToArray());
        }
        if (plan.RateAdjustment is { } adjustment)
        {
            electrodes = Array.AsReadOnly(electrodes.Select(electrode => electrode with
            { Bands = adjustment.AdjustBands(plan, electrode.Bands) }).ToArray());
        }
        return new LiveEcgProjection(plan, electrodes, configuration.EcgInterference);
    }
}
