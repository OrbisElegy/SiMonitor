// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Specs;

internal static class AcceleratedVentricularFusionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(AcceleratedVentricularFusionSelectsOnlyOneAtrialAlignedBeatPerGroup), AcceleratedVentricularFusionSelectsOnlyOneAtrialAlignedBeatPerGroup),
        new(nameof(AcceleratedVentricularFusionPreservesProjectionAndRestoresWithinSelectedBeat), AcceleratedVentricularFusionPreservesProjectionAndRestoresWithinSelectedBeat),
    ];

    private static void AcceleratedVentricularFusionSelectsOnlyOneAtrialAlignedBeatPerGroup() =>
        AcceleratedVentricularChecks.VerifyMorphology(capture: false);

    private static void AcceleratedVentricularFusionPreservesProjectionAndRestoresWithinSelectedBeat() =>
        AcceleratedVentricularChecks.VerifyProjectionAndRecovery(capture: false);
}
