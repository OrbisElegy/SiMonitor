// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AcceleratedVentricularCaptureSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(CaptureAndFusionCannotOccupyTheSameSlot), CaptureAndFusionCannotOccupyTheSameSlot),
        new(nameof(AcceleratedVentricularModesUseSharedEventGrid), AcceleratedVentricularModesUseSharedEventGrid),
        new(nameof(AcceleratedVentricularCaptureSelectsOnlyOneAtrialAlignedBeatPerGroup), AcceleratedVentricularCaptureSelectsOnlyOneAtrialAlignedBeatPerGroup),
        new(nameof(AcceleratedVentricularCapturePreservesProjectionAndRestoresWithinSelectedBeat), AcceleratedVentricularCapturePreservesProjectionAndRestoresWithinSelectedBeat),
    ];

    private static void AcceleratedVentricularModesUseSharedEventGrid() =>
        AcceleratedVentricularChecks.VerifySharedEventGrid();

    private static void AcceleratedVentricularCaptureSelectsOnlyOneAtrialAlignedBeatPerGroup() =>
        AcceleratedVentricularChecks.VerifyMorphology(capture: true);

    private static void AcceleratedVentricularCapturePreservesProjectionAndRestoresWithinSelectedBeat() =>
        AcceleratedVentricularChecks.VerifyProjectionAndRecovery(capture: true);

    private static void CaptureAndFusionCannotOccupyTheSameSlot()
    {
        foreach (Action create in new Action[] {
            () => AcceleratedVentricularReference.CreateElectrodes(true,true),
            () => AcceleratedVentricularReference.CreateLeadIIBands(true,true) })
        {
            try { create(); }
            catch (EventWaveformException e) when (e.ReasonCode == "Aivr.ConflictingModes") { continue; }
            throw new InvalidOperationException("Conflicting capture/fusion accepted.");
        }
    }

}
