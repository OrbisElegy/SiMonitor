// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class ConductionSelection
{
    internal static (int Atrial, int Conducted) Resolve(int index) => index switch
    {
        >= 0 and <= 3 => (index + 1, 1),
        4 or 16 or 18 => (3, 2),
        17 => (5, 4),
        5 or 6 or 19 => (4, 3),
        7 or 8 or 11 or 12 or 13 or 14 or 15 => (1, 1),
        9 => (2, 1),
        10 => (4, 1),
        _ => throw new ArgumentException("Invalid conduction selection."),
    };
    internal static AvConductionPattern Pattern(int index) => index switch { 6 => AvConductionPattern.WenckebachFourToThreeIllustration, 7 => AvConductionPattern.CompleteAvBlockJunctionalIllustration, 8 => AvConductionPattern.CompleteAvBlockVentricularIllustration, 9 or 10 => AvConductionPattern.AtrialFlutterIllustration, 11 => AvConductionPattern.AtrialFibrillationCoarseIllustration, 12 => AvConductionPattern.AtrialFibrillationFineIllustration, 13 => AvConductionPattern.VentricularFlutterIllustration, 14 => AvConductionPattern.VentricularFibrillationCoarseIllustration, 15 => AvConductionPattern.VentricularFibrillationFineIllustration, 16 => AvConductionPattern.WenckebachThreeToTwoIllustration, 17 => AvConductionPattern.WenckebachFiveToFourIllustration, 18 => AvConductionPattern.MobitzTwoThreeToTwoIllustration, 19 => AvConductionPattern.MobitzTwoFourToThreeIllustration, _ => AvConductionPattern.FixedPr };
    internal static string Summary(int atrial, int conducted, AvConductionPattern pattern) =>
        VentricularDisorganizationReference.IsPattern(pattern) ? "无组织性QRS／有效射血" :
        AtrialFibrillationReference.IsPattern(pattern) ? "RR逐搏不规则（无固定传导比）" :
        pattern is AvConductionPattern.CompleteAvBlockJunctionalIllustration or AvConductionPattern.CompleteAvBlockVentricularIllustration ? "房室分离（无下传）" : $"传导 {atrial}:{conducted}";
    internal static int Index(int atrial, int conducted, AvConductionPattern pattern = AvConductionPattern.FixedPr) => (atrial, conducted, pattern) switch
    {
        ( >= 1 and <= 4, 1, AvConductionPattern.FixedPr) => atrial - 1,
        (3, 2, AvConductionPattern.FixedPr) => 4,
        (4, 3, AvConductionPattern.FixedPr) => 5,
        (4, 3, AvConductionPattern.WenckebachFourToThreeIllustration) => 6,
        (1, 1, AvConductionPattern.CompleteAvBlockJunctionalIllustration) => 7,
        (1, 1, AvConductionPattern.CompleteAvBlockVentricularIllustration) => 8,
        (2, 1, AvConductionPattern.AtrialFlutterIllustration) => 9,
        (4, 1, AvConductionPattern.AtrialFlutterIllustration) => 10,
        (1, 1, AvConductionPattern.AtrialFibrillationCoarseIllustration) => 11,
        (1, 1, AvConductionPattern.AtrialFibrillationFineIllustration) => 12,
        (1, 1, AvConductionPattern.VentricularFlutterIllustration) => 13,
        (1, 1, AvConductionPattern.VentricularFibrillationCoarseIllustration) => 14,
        (1, 1, AvConductionPattern.VentricularFibrillationFineIllustration) => 15,
        (3, 2, AvConductionPattern.WenckebachThreeToTwoIllustration) => 16,
        (5, 4, AvConductionPattern.WenckebachFiveToFourIllustration) => 17,
        (3, 2, AvConductionPattern.MobitzTwoThreeToTwoIllustration) => 18,
        (4, 3, AvConductionPattern.MobitzTwoFourToThreeIllustration) => 19,
        _ => throw new ArgumentException("Invalid conduction selection."),
    };
}
