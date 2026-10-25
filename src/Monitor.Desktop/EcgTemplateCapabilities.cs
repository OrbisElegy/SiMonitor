// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed record EcgTemplateCapabilities(bool Timing, bool PDuration, bool PrInterval, bool QrsDuration,
    bool Ventricular, bool PWave, bool StT, bool UWave, bool Placement, bool Escape, bool Fibrillation)
{
    internal static EcgTemplateCapabilities For(ProjectedEcgDemoConfiguration config)
    {
        // Only the reference-electrode path accepts arbitrary component edits.
        bool reference = config.ConductionPattern is AvConductionPattern.FixedPr or
            AvConductionPattern.WenckebachThreeToTwoIllustration or AvConductionPattern.WenckebachFiveToFourIllustration or
            AvConductionPattern.WenckebachFourToThreeIllustration &&
            config.Pacing is null && !config.Dextrocardia && !config.AtrialEscape && !config.Aar && !config.Ajr && !config.Aivr &&
            !config.Vt && !config.Svt && !config.Wpw && !config.ShortPr && !config.NormalPrDelta &&
            config.Quinidine == QuinidineIllustration.Reference && !config.DigitalisEffect && config.Calcium == CalciumIllustration.Reference &&
            !config.HypokalemiaRepolarization && !config.HyperkalemiaRepolarization && config.BundleBlock == EcgBundleBlockIllustration.Reference;
        bool atrial = config.CardiacActivity is CardiacActivity.AtrialAndVentricular or CardiacActivity.AtrialOnly;
        bool ventricular = config.CardiacActivity is CardiacActivity.AtrialAndVentricular or CardiacActivity.VentricularOnly;
        bool manualP = reference && atrial && config.Atrial == EcgAtrialIllustration.Reference;
        bool manualStT = reference && ventricular && config.Ventricular == EcgVentricularIllustration.Reference &&
            config.TContour is null && config.Infarction is null && config.Zones is null;
        bool placement = reference || config.Pacing is not null || config.Dextrocardia ||
            config.Quinidine != QuinidineIllustration.Reference || config.DigitalisEffect || config.Calcium != CalciumIllustration.Reference ||
            config.HypokalemiaRepolarization || config.HyperkalemiaRepolarization;
        return new(reference && (atrial || ventricular), manualP, reference && atrial && ventricular,
            reference && ventricular && config.Ventricular == EcgVentricularIllustration.Reference,
            reference && ventricular, manualP, manualStT, reference && ventricular,
            placement && config.CardiacActivity != CardiacActivity.Absent,
            config.ConductionPattern is AvConductionPattern.CompleteAvBlockJunctionalIllustration or AvConductionPattern.CompleteAvBlockVentricularIllustration,
            AtrialFibrillationReference.IsPattern(config.ConductionPattern));
    }
}
