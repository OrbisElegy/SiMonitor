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
        _ => throw new ArgumentException("Invalid conduction selection."),
    };
    internal static AvConductionPattern Pattern(int index) => index == 6 ? AvConductionPattern.WenckebachFourToThreeIllustration : AvConductionPattern.FixedPr;
    internal static int Index(int atrial, int conducted, AvConductionPattern pattern = AvConductionPattern.FixedPr) => (atrial, conducted, pattern) switch
    {
        ( >= 1 and <= 4, 1, AvConductionPattern.FixedPr) => atrial - 1,
        (3, 2, AvConductionPattern.FixedPr) => 4,
        (4, 3, AvConductionPattern.FixedPr) => 5,
        (4, 3, AvConductionPattern.WenckebachFourToThreeIllustration) => 6,
        _ => throw new ArgumentException("Invalid conduction selection."),
    };
}
