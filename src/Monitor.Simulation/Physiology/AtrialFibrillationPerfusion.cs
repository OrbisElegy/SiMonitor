// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Explicit teaching strengths. RR alone does not determine clinical stroke volume.
public static class AtrialFibrillationPerfusion
{
    public const string EvidenceId = "AtrialFibrillationPerfusionIllustration@2";

    public static int GainPermille(AvConductionPattern pattern, ulong ordinal, bool systemicPulseDeficit = false)
    {
        if (!AtrialFibrillationReference.IsPattern(pattern))
        { throw new EventWaveformException("AtrialFibrillationPerfusion.InvalidPattern", nameof(pattern)); }
        if (ordinal == 0) { return 800; }
        long precedingRr = AtrialFibrillationReference.GridNs + AtrialFibrillationReference.Jitter(ordinal) -
            AtrialFibrillationReference.Jitter(ordinal - 1);
        if (systemicPulseDeficit && AtrialFibrillationReference.IsLongShortBeat(ordinal)) { return 0; }
        return precedingRr < 600_000_000 ? 400 : precedingRr < 900_000_000 ? 800 : 1000;
    }
}
