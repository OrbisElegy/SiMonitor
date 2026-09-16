// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

public enum EcgAtrialIllustration
{
    Reference,
    LeftAtrialAbnormality,
}

// Textbook-constrained teaching morphology, not a chamber/anatomy model.
public static class EcgAtrialIllustrations
{
    public const string EvidenceId = "LeftAtrialIllustrationDraft@1";
    public const long LeftPDurationNs = 140_000_000;

    internal static EcgPWavePlan? Resolve(EcgAtrialIllustration illustration, EcgCycleTiming timing)
    {
        if (!Enum.IsDefined(illustration))
        { throw new EventWaveformException("EcgAtrial.InvalidIllustration", "atrial"); }
        if (illustration == EcgAtrialIllustration.Reference) { return null; }
        // Retain PR and all ventricular timing. Reject an incompatible PR
        // rather than silently moving the ventricular event.
        if (timing.PDurationNs != LeftPDurationNs ||
            (Int128)timing.PDurationNs * 5 <= (Int128)(timing.PrIntervalNs - timing.PDurationNs) * 8)
        { throw new EventWaveformException("EcgAtrial.InvalidTiming", "timing"); }
        return new(Array.AsReadOnly(new EcgPWaveComponents?[]
        {
            new(-100, -100), new(100, 100), null, new(0, 0),
            new(80, -120), new(100, -40), new(100, 40), new(100, 100), new(100, 100), new(100, 100),
        }));
    }
}
