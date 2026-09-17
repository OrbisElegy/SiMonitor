// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class ConductionSelection
{
    internal static (int Atrial, int Conducted) Resolve(int index) => index switch
    {
        >= 0 and <= 3 => (index + 1, 1),
        4 => (3, 2),
        5 or 6 => (4, 3),
        7 or 8 => (1, 1),
        9 => (2, 1),
        10 => (4, 1),
        _ => throw new ArgumentException("Invalid conduction selection."),
    };
    internal static AvConductionPattern Pattern(int index) => index switch { 6 => AvConductionPattern.WenckebachFourToThreeIllustration, 7 => AvConductionPattern.CompleteAvBlockJunctionalIllustration, 8 => AvConductionPattern.CompleteAvBlockVentricularIllustration, 9 or 10 => AvConductionPattern.AtrialFlutterIllustration, _ => AvConductionPattern.FixedPr };
    internal static string Summary(int atrial, int conducted, AvConductionPattern pattern) =>
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
        _ => throw new ArgumentException("Invalid conduction selection."),
    };
}
