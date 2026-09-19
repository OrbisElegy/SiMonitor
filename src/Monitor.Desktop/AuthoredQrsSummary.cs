// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class AuthoredQrsSummary
{
    internal static string Create(ElectrodeWaveformGroup source, CardiacActivity activity)
    {
        if (activity is CardiacActivity.Absent or CardiacActivity.AtrialOnly)
        { return "QRS 形态核验：无心室事件，不核验。"; }
        var state = source.CaptureState().Generator;
        if (VentricularDisorganizationReference.IsPattern(state.Timeline.Plan.ConductionPattern))
        { return "QRS 形态核验：室扑／室颤仍有室性电活动，但无独立 QRS 分量，不核验。"; }
        if (state.Electrodes.SelectMany(e => e.Bands).Any(b =>
            b.Trigger == PhysiologyCycleEventKind.VentricularElectrical && b.DelayNs == 0 && b.DurationNs < 2_000_000))
        { return "QRS 形态核验不可用：QRS 分量短于 2 ms，1 ms 网格不足；波形配置仍可应用。"; }
        if (state.Electrodes.SelectMany(e => e.Bands).Any(b => b.AfBeatSelection is not null))
        { return "QRS源形态核验：当前房颤含80ms普通QRS及140ms差异传导QRS，单一形态摘要不适用；请核对对应搏动。"; }
        var measurements = AuthoredQrsMeasurements.Project(state.Electrodes, state.Placement);
        return (PrematureVentricularReference.IsPattern(state.Timeline.Plan.ConductionPattern) ? "当前含两种QRS；以下仅核验窦性槽0，不代表室早各型宽QRS。\n" : "") + (state.Timeline.Plan.ConductionPattern == AvConductionPattern.AberrantPrematureAtrialIllustration ? "当前含两种QRS；以下仅核验窦性槽0，不代表房早的140ms宽QRS。\n" : "") + "QRS 源形态核验（1 ms 网格，隔离 QRS 分量；不含 P/ST/T/u，不是屏幕或采集信号测量）：\n" +
            string.Join("\n", measurements.Select((m, index) => Format(ProjectedEcgDemoSource.LeadNames[index], m))) +
            "\n条件仅核对 Q 时限≥30 ms 且 |Q|≥R/4；QS 单列，不计算 Q/R，不据此诊断。";
    }

    private static string Format(string lead, AuthoredQrsMeasurement m)
    {
        string kind = m.Kind switch
        {
            AuthoredQrsKind.Flat => "无偏转",
            AuthoredQrsKind.RFirst => "无起始 Q",
            AuthoredQrsKind.QS => "QS（无 R）",
            _ => m.MeetsQIllustrationCriteria ? "达到所列 Q 条件" : "未达到所列 Q 条件",
        };
        string duration = m.Kind == AuthoredQrsKind.QThenR ? (m.QDurationNs / 1_000_000m).ToString("0.###", CultureInfo.InvariantCulture) : "—";
        string ratio = m.Kind == AuthoredQrsKind.QThenR ? (m.QDepthQ32 * 100m / m.RPeakQ32).ToString("0.###", CultureInfo.InvariantCulture) + "%" : "—";
        return string.Create(CultureInfo.InvariantCulture,
            $"{lead}：{kind}；Q 时限 {duration} ms；{(m.Kind == AuthoredQrsKind.QS ? "QS深度" : "Q深度")} {m.QDepthQ32 / (decimal)FixedPointMath.Q32One:0.###} μV；R {m.RPeakQ32 / (decimal)FixedPointMath.Q32One:0.###} μV；Q/R {ratio}");
    }
}
