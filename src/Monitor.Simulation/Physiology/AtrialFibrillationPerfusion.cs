// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// RR-dependent passive filling without an organized atrial contribution.
// Contractile restitution and loading feedback are not calibrated by this model.
public static class AtrialFibrillationPerfusion
{
    public static int GainPermille(RegularPhysiologyPlan plan, ulong ordinal, bool systemicPulseDeficit = false)
    {
        if (!AtrialFibrillationReference.IsPattern(plan.ConductionPattern))
        { throw new EventWaveformException("AtrialFibrillationPerfusion.InvalidPattern", nameof(plan)); }
        if (systemicPulseDeficit && AtrialFibrillationReference.IsLongShortBeat(plan, ordinal)) { return 0; }
        return CardiacFillingPerfusion.StrokeVolumePermille(AtrialFibrillationReference.PrecedingIntervalNs(plan, ordinal), 0);
    }

    public const string EvidenceId = "AtrialFibrillationPerfusionIllustration@3";

    public static int GainPermille(AvConductionPattern pattern, ulong ordinal, bool systemicPulseDeficit = false)
    {
        if (!AtrialFibrillationReference.IsPattern(pattern))
        { throw new EventWaveformException("AtrialFibrillationPerfusion.InvalidPattern", nameof(pattern)); }
        long precedingRr = ordinal == 0 ? AtrialFibrillationReference.GridNs :
            AtrialFibrillationReference.GridNs + AtrialFibrillationReference.Jitter(ordinal) -
            AtrialFibrillationReference.Jitter(ordinal - 1);
        if (systemicPulseDeficit && AtrialFibrillationReference.IsLongShortBeat(ordinal)) { return 0; }
        return CardiacFillingPerfusion.StrokeVolumePermille(precedingRr, 0);
    }
}
