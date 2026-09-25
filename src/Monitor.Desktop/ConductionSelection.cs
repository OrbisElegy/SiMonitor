// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class ConductionSelection
{
    internal static (int Atrial, int Conducted) Resolve(int index) => Monitor.Simulation.Authoring.AuthoredConductionSelection.Resolve(index);
    internal static AvConductionPattern Pattern(int index) => Monitor.Simulation.Authoring.AuthoredConductionSelection.Pattern(index);
    internal static int Index(int atrial, int conducted, AvConductionPattern pattern = AvConductionPattern.FixedPr) => Monitor.Simulation.Authoring.AuthoredConductionSelection.Index(atrial, conducted, pattern);
    internal static string Summary(int atrial, int conducted, AvConductionPattern pattern) =>
        pattern == AvConductionPattern.SinusArrestIllustration ? "窦性停搏：基础PP800ms，长PP2000ms（2.5倍），无逸搏固定例" :
        pattern == AvConductionPattern.SinusArrhythmiaIllustration ? "窦性心律不齐：PP/RR800/1000/600/800ms重复，PR160ms；未模拟呼吸耦合" :
        pattern == AvConductionPattern.AtrialFlutterIllustration && atrial == 1 ? "房扑1:1固定示意；房率/室率300次/分，QRS80/QT180ms；不显示可测PR" :
        pattern == AvConductionPattern.VariableAtrialFlutterIllustration ? "可变房扑示意；连续F300/min，RR400/600/800ms重复；QRS80/QT300ms" :
        VentricularDisorganizationReference.IsPattern(pattern) ? "无组织性QRS／有效射血" :
        pattern == AvConductionPattern.ShortCoupledRonTPvcIllustration ? "短联律R-on-T示意；窦性QT320ms、室早联律200ms、随后1400ms；室早QRS160/QT480ms" :
        pattern == AvConductionPattern.RonTLongQtPvcIllustration ? "R-on-T长QT示意；仅室早前一搏QT640/T440ms；联律500ms、随后1100ms；室早QRS160/QT480ms" :
        pattern == AvConductionPattern.PolymorphicVentricularCoupletIllustration ? "成对室早双形态示意；联律500ms、两室早间隔500ms、随后1400ms；A QRS160/QT480、B QRS180/QT500ms" :
        pattern == AvConductionPattern.VentricularCoupletIllustration ? "成对室早；首次联律500ms、两次室早相隔500ms、随后间歇1400ms；两次QRS160/QT480ms" :
        pattern == AvConductionPattern.InterpolatedPvcIllustration ? "插入性室早；基础窦性60次/分，联律500ms/至下一窦性QRS500ms，无代偿间歇；室早QRS160/QT480ms" :
        pattern == AvConductionPattern.PolymorphicPvcIllustration ? "多形室早示意；A/B形态交替，联律均500ms、间歇1100ms；A QRS160/QT480，B QRS180/QT500ms" :
        pattern == AvConductionPattern.MultifocalPvcIllustration ? "多源室早示意；A/B形态交替，联律500/600ms、间歇1100/1000ms；A QRS160/QT480，B QRS180/QT500ms" :
        pattern == AvConductionPattern.VentricularBigeminyIllustration ? "室早二联律（窦性/室早交替）；联律500/间歇1100ms；室早QRS160/QT480ms" :
        pattern == AvConductionPattern.VentricularTrigeminyIllustration ? "室早三联律（每2次窦性后1次室早）；联律500/间歇1100ms；室早QRS160/QT480ms" :
        pattern == AvConductionPattern.PrematureVentricularIllustration ? "单形室早；无相关P；联律500/间歇1100ms（完全代偿）；室早QRS160/QT480ms" :
        pattern == AvConductionPattern.PrematureJunctionalAfterQrsIllustration ? "交界性早搏；逆行P′在QRS起点后120ms；联律500/间歇1100ms（完全代偿）" :
        pattern == AvConductionPattern.PrematureJunctionalOverlappingIllustration ? "交界性早搏；逆行P′与QRS重叠；联律500/间歇1100ms（完全代偿）" :
        pattern == AvConductionPattern.PrematureJunctionalIllustration ? "交界性早搏；逆行P′R80ms；联律500/间歇1100ms（完全代偿）" :
        pattern == AvConductionPattern.BlockedPrematureAtrialIllustration ? "房早未下传；窦性RR为800/800/1500ms；P′无对应PR/QRS" :
        pattern == AvConductionPattern.AberrantPrematureAtrialIllustration ? "房早伴RBBB差异传导；窦性QRS80/QT320ms，房早QRS140/QT400ms" :
        pattern == AvConductionPattern.PrematureAtrialIllustration ? "房早均下传；PP/RR为800/800/500/1000ms" :
        AtrialFibrillationReference.IsPattern(pattern) ? "RR逐搏不规则（无固定传导比）" :
        pattern is AvConductionPattern.CompleteAvBlockJunctionalIllustration or AvConductionPattern.CompleteAvBlockVentricularIllustration ? "房室分离（无下传）" : $"传导 {atrial}:{conducted}";
}
