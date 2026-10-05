// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Monitor.Application.Localization;
using Monitor.Infrastructure.Localization;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Describes authored waveform supports, never measured intervals or live values.
internal static class EcgTemplateSummary
{
    private static readonly ITextLocalizer Chinese = BuiltInLocalizations.Create("zh-CN");

    // The default wording, in Chinese.
    internal static string Describe(ProjectedEcgDemoConfiguration config) => Describe(Chinese, config);

    internal static string Describe(ITextLocalizer text, ProjectedEcgDemoConfiguration config)
    {
        var pattern = config.ConductionPattern;
        if (config.CardiacActivity == CardiacActivity.Absent) { return text.GetString("summary.noActivity"); }
        if (VentricularDisorganizationReference.IsPattern(pattern)) { return text.GetString("summary.disorganized"); }
        if (config.HyperkalemiaFusion) { return text.GetString("summary.hyperkalemiaFusion"); }
        var timing = config.ResolveTiming();
        if (config.CardiacActivity == CardiacActivity.AtrialOnly) { return text.Format("summary.atrialOnly", Ms(timing.PDurationNs)); }
        string ventricular = text.Format("summary.ventricular", Ms(timing.QrsDurationNs), Ms(timing.QtIntervalNs), Ms(timing.TDurationNs));
        if (config.Zones is { } zones)
        {
            string Region(EcgInfarctionRegion region) => InfarctionZoneSelection.Name(text, InfarctionZoneSelection.Index(region));
            return DescribeComponents(text, zones.Components, text.Format("summary.zones", Region(zones.Ischemia), Region(zones.Injury), Region(zones.Necrosis)),
                timing.QtIntervalNs + (zones.Ischemia.Territory == InfarctionTerritory.CustomChest && zones.Ischemia.ChestMask == 0 ? 0 : zones.RepolarizationDelayNs));
        }
        if (config.Infarction is { } infarction)
        {
            string local = infarction.Stage is InfarctionIllustrationStage.HyperacuteInjury or InfarctionIllustrationStage.AcuteMonophasic
                ? text.Format("summary.fusedSt", Ms(timing.QtIntervalNs + infarction.RepolarizationDelayNs),
                    Ms(infarction.Stage == InfarctionIllustrationStage.HyperacuteInjury ? timing.QrsDurationNs * 6 / 5 : timing.QrsDurationNs))
                : text.Format("summary.regional", Ms(timing.QrsDurationNs), Ms(timing.QtIntervalNs + infarction.RepolarizationDelayNs),
                    Ms(timing.TDurationNs + infarction.RepolarizationDelayNs));
            string region = infarction.Territory == InfarctionTerritory.CustomChest
                ? ChestLeads(text, infarction.ChestMask)
                : InfarctionProductPreset.TerritoryName(text, infarction.Territory);
            if (infarction.Components is { } parts)
            {
                return DescribeComponents(text, parts, region, timing.QtIntervalNs + infarction.RepolarizationDelayNs);
            }
            return text.Format("summary.infarctionSnapshot", region, Ms(timing.PDurationNs), Ms(timing.PrIntervalNs), local);
        }
        if (config.TContour is { } contour)
        {
            string crossing = contour.Shape is EcgTContourShape.PositiveNegative or EcgTContourShape.NegativePositive
                ? text.Format("summary.tContourCrossing", ((contour.CrossingPositionPermille ?? 500) / 10m).ToString("0.#", CultureInfo.InvariantCulture),
                    Number(contour.SecondPeakMicrovolts ?? contour.PeakMicrovolts))
                : "";
            string target = contour.Target == EcgTContourTarget.Chest ? ChestLeads(text, contour.ChestMask) : contour.Target.ToString();
            return text.Format("summary.tContour", Ms(timing.PDurationNs), Ms(timing.PrIntervalNs), ventricular, target, Number(contour.PeakMicrovolts), crossing);
        }
        if (config.Quinidine != QuinidineIllustration.Reference)
        {
            return text.Format("summary.quinidine", Ms(timing.PDurationNs), config.QuinidineNotchedP ? text.GetString("summary.notched") : "",
                Ms(timing.PrIntervalNs), ventricular, Ms(QuinidineEffectReference.ResolveQuIntervalNs(config.Quinidine)));
        }
        if (config.DigitalisEffect) { return text.Format("summary.digitalis", Ms(timing.PDurationNs), Ms(timing.PrIntervalNs), Ms(timing.QtIntervalNs)); }
        if (config.HypokalemiaRepolarization)
        {
            return config.HypokalemiaTuFusion
                ? text.Format("summary.tuFusion", text.Format("summary.atrialTiming", Ms(timing.PDurationNs), Ms(timing.PrIntervalNs), Ms(timing.QrsDurationNs)),
                    Ms(HypokalemiaRepolarizationReference.QuIntervalNs))
                : text.Format("summary.tallU", Ms(timing.PDurationNs), Ms(timing.PrIntervalNs), ventricular, Ms(HypokalemiaRepolarizationReference.QuIntervalNs));
        }
        if (config.HyperkalemiaAbsentP) { return text.Format("summary.absentP", ventricular); }
        if (AtrialFibrillationReference.IsPattern(pattern))
        {
            string aberrancy = config.IllustrateAfAberrancy
                ? text.Format("summary.aberrancy", Ms(RightBundleBlockReference.Timing.QrsDurationNs), Ms(RightBundleBlockReference.Timing.QtIntervalNs)) : "";
            return text.Format("summary.fibrillation", ventricular, aberrancy);
        }
        if (AtrialFlutterReference.IsPattern(pattern)) { return text.Format("summary.flutter", ventricular); }
        if (config.Svt) { return text.Format("summary.svt", ventricular); }
        if (config.Vt || config.Aivr || config.Ajr || config.IndependentVentricularPeriodMilliseconds is not null)
        {
            string variants = config.VtFusion || config.VtCapture || config.AivrFusion || config.AivrCapture
                ? text.GetString("summary.independentVariants") : "";
            return text.Format("summary.independent", Ms(timing.PDurationNs), ventricular, variants);
        }
        bool premature = PrematureVentricularReference.IsPattern(pattern) ||
            PrematureAtrialReference.IsPattern(pattern) || PrematureJunctionalReference.IsPattern(pattern);
        bool wenckebach = pattern is AvConductionPattern.WenckebachThreeToTwoIllustration or
            AvConductionPattern.WenckebachFourToThreeIllustration or AvConductionPattern.WenckebachFiveToFourIllustration;
        string pr = wenckebach ? text.GetString("summary.wenckebach") : text.Format("summary.conductedPr", Ms(timing.PrIntervalNs));
        string atrial = config.Aar || config.AtrialEscape ? "P′" : "P";
        return text.Format("summary.default", premature ? text.GetString("summary.sinusPrefix") : "", atrial, Ms(timing.PDurationNs), pr, ventricular,
            premature ? text.GetString("summary.prematureSuffix") : "");
    }

    private static string DescribeComponents(ITextLocalizer text, EcgInfarctionComponents parts, string region, long qtNs)
    {
        string qrs = parts.Necrosis switch
        {
            NecrosisIllustrationShape.QWithReducedR => text.GetString("summary.necrosisQ"),
            NecrosisIllustrationShape.QS => "QS",
            _ => text.GetString("summary.referenceQrs"),
        };
        string t = parts.TPeakMicrovolts is { } peak ? text.Format("summary.tPeak", Number(peak)) : text.GetString("summary.referenceT");
        return text.Format("summary.components", region, qrs, (parts.QrsTemplatePermille / 10m).ToString("0.#", CultureInfo.InvariantCulture),
            Number(parts.JMicrovolts), Number(parts.StEndMicrovolts), Number(parts.StArchMicrovolts), t, Ms(qtNs));
    }

    private static string Ms(long ns) => (ns / 1_000_000m).ToString("0.###", CultureInfo.InvariantCulture);
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string ChestLeads(ITextLocalizer text, int mask) =>
        string.Join(text.GetString("summary.leadSeparator"), Enumerable.Range(0, 6).Where(i => (mask & (1 << i)) != 0).Select(i => $"V{i + 1}"));
}
