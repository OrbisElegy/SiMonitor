// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Authored sequences under the textbook's progressively lengthening PR rule.
// These delays are not a calibrated AV-node model.
internal static class WenckebachIllustration
{
    internal static int GroupSize(AvConductionPattern pattern) => pattern switch
    {
        AvConductionPattern.WenckebachThreeToTwoIllustration => 3,
        AvConductionPattern.WenckebachFourToThreeIllustration => 4,
        AvConductionPattern.WenckebachFiveToFourIllustration => 5,
        _ => 0,
    };
    internal static long ExtraDelayNs(int slot) => slot switch
    {
        0 => 0,
        1 => 80_000_000,
        2 => 120_000_000,
        3 => 140_000_000,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };
}
