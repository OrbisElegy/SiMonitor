// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

public enum EcgAtrialIllustration
{
    Reference,
    LeftAtrialAbnormality,
    RightAtrialAbnormality,
    BiatrialAbnormality,
}

// Textbook-constrained teaching morphology, not a chamber/anatomy model.
public static class EcgAtrialIllustrations
{
    public const string EvidenceId = "LeftAtrialIllustrationDraft@1";
    public const long LeftPDurationNs = 140_000_000;
    public const string RightAndBiatrialEvidenceId = "RightAndBiatrialIllustrationDraft@1";

    public static long? PDurationNs(EcgAtrialIllustration illustration) => illustration switch
    {
        EcgAtrialIllustration.Reference => null,
        EcgAtrialIllustration.RightAtrialAbnormality => 100_000_000,
        EcgAtrialIllustration.LeftAtrialAbnormality or EcgAtrialIllustration.BiatrialAbnormality => LeftPDurationNs,
        _ => throw new EventWaveformException("EcgAtrial.InvalidIllustration", nameof(illustration)),
    };

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(EcgAtrialIllustration illustration)
    {
        var timing = TextbookEcgReference.Timing;
        if (PDurationNs(illustration) is { } duration) { timing = timing with { PDurationNs = duration }; }
        var electrodes = TextbookElectrodeReference.CreateElectrodes(timing: timing, atrial: illustration);
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Concat(
            electrodes[(int)EcgElectrode.RA].Bands.Select(band => band with
            { TableQ32 = Array.AsReadOnly(band.TableQ32.Select(value => checked(-value)).ToArray()) })).ToArray());
    }

    internal static EcgPWavePlan? Resolve(EcgAtrialIllustration illustration, EcgCycleTiming timing)
    {
        if (!Enum.IsDefined(illustration))
        { throw new EventWaveformException("EcgAtrial.InvalidIllustration", nameof(illustration)); }
        if (illustration == EcgAtrialIllustration.Reference) { return null; }
        // Retain PR and all ventricular timing. Reject an incompatible PR
        // rather than silently moving the ventricular event.
        if (timing.PDurationNs != PDurationNs(illustration) ||
            (illustration == EcgAtrialIllustration.LeftAtrialAbnormality &&
             (Int128)timing.PDurationNs * 5 <= (Int128)(timing.PrIntervalNs - timing.PDurationNs) * 8))
        { throw new EventWaveformException("EcgAtrial.InvalidTiming", "timing"); }
        if (illustration == EcgAtrialIllustration.RightAtrialAbnormality)
        {
            // Simultaneous lobes: one tall peak without prolonging P.
            // RA=LA and zero limb sum give a fixed +90 degree P axis.
            return new(Array.AsReadOnly(new EcgPWaveComponents?[]
            {
                new(-100, 0), new(-100, 0), null, new(200, 0),
                new(180, 0), new(150, 0), new(120, 0), new(100, 0), new(100, 0), new(100, 0),
            }), SeparationPermille: 0);
        }
        if (illustration == EcgAtrialIllustration.BiatrialAbnormality)
        {
            return new(Array.AsReadOnly(new EcgPWaveComponents?[]
            {
                new(-180, -180), new(30, 30), null, new(150, 150),
                new(220, -200), new(180, -80), new(160, 60), new(150, 150), new(150, 150), new(150, 150),
            }));
        }
        return new(Array.AsReadOnly(new EcgPWaveComponents?[]
        {
            new(-100, -100), new(100, 100), null, new(0, 0),
            new(80, -120), new(100, -40), new(100, 40), new(100, 100), new(100, 100), new(100, 100),
        }));
    }
}
