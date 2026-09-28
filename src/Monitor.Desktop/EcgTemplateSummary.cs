// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Describes authored waveform supports, never measured intervals or live values.
internal static class EcgTemplateSummary
{
    internal static string Describe(ProjectedEcgDemoConfiguration config)
    {
        var pattern = config.ConductionPattern;
        if (config.CardiacActivity == CardiacActivity.Absent)
        { return "无心房或心室电活动；P、PR、QRS、QT 不适用。"; }
        if (VentricularDisorganizationReference.IsPattern(pattern))
        { return "无可独立标注的 P、PR、QRS、QT；采用室扑／室颤复合波形。"; }
        var timing = config.ResolveTiming();
        string Ms(long ns) => (ns / 1_000_000m).ToString("0.###", CultureInfo.InvariantCulture);
        if (config.CardiacActivity == CardiacActivity.AtrialOnly)
        { return $"P {Ms(timing.PDurationNs)} ms；无心室电活动，PR、QRS、QT 不适用。"; }
        string ventricular = $"QRS {Ms(timing.QrsDurationNs)} ms · QT {Ms(timing.QtIntervalNs)} ms · T {Ms(timing.TDurationNs)} ms";
        if (config.HypokalemiaRepolarization)
        {
            string atrialTiming = $"P {Ms(timing.PDurationNs)} ms · PR {Ms(timing.PrIntervalNs)} ms · QRS {Ms(timing.QrsDurationNs)} ms";
            return config.HypokalemiaTuFusion
                ? atrialTiming + $"；T–U 融合，QT 不单独标注；QU {Ms(HypokalemiaRepolarizationReference.QuIntervalNs)} ms"
                : $"P {Ms(timing.PDurationNs)} ms · PR {Ms(timing.PrIntervalNs)} ms · " + ventricular +
                    $"；U 波增高，QU {Ms(HypokalemiaRepolarizationReference.QuIntervalNs)} ms";
        }
        if (config.HyperkalemiaAbsentP)
        { return "无 P 波；PR 不适用；" + ventricular; }
        if (AtrialFibrillationReference.IsPattern(pattern))
        {
            string aberrancy = config.IllustrateAfAberrancy
                ? $"；差异传导搏动 QRS {Ms(RightBundleBlockReference.Timing.QrsDurationNs)} ms · QT {Ms(RightBundleBlockReference.Timing.QtIntervalNs)} ms" : "";
            return "无正常 P 波；PR 不适用；普通搏动 " + ventricular + aberrancy;
        }
        if (AtrialFlutterReference.IsPattern(pattern))
        { return "连续 F 波；无正常 P 波，PR 不适用；" + ventricular; }
        if (config.Svt)
        { return "逆行 P′与 QRS 重叠；PR 不可单独测量；" + ventricular; }
        if (config.Vt || config.Aivr || config.Ajr || config.IndependentVentricularPeriodMilliseconds is not null)
        {
            string variants = config.VtFusion || config.VtCapture || config.AivrFusion || config.AivrCapture
                ? "；融合／夺获搏动采用独立形态，以上为普通搏动时限" : "";
            return $"独立 P {Ms(timing.PDurationNs)} ms；无固定 PR；" + ventricular + variants;
        }
        bool premature = PrematureVentricularReference.IsPattern(pattern) ||
            PrematureAtrialReference.IsPattern(pattern) || PrematureJunctionalReference.IsPattern(pattern);
        bool wenckebach = pattern is AvConductionPattern.WenckebachThreeToTwoIllustration or
            AvConductionPattern.WenckebachFourToThreeIllustration or AvConductionPattern.WenckebachFiveToFourIllustration;
        string pr = wenckebach ? "PR 逐搏延长至脱漏" : $"PR {Ms(timing.PrIntervalNs)} ms（下传搏动）";
        string atrial = config.Aar || config.AtrialEscape ? "P′" : "P";
        return (premature ? "窦性搏动：" : "") + $"{atrial} {Ms(timing.PDurationNs)} ms · {pr} · " + ventricular +
            (premature ? "；早搏及相关搏动采用独立时序／形态" : "");
    }
}
