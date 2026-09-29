// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public enum EcgVentricularIllustration { Reference, LeftHypertrophyWithStrain, RightHypertrophyWithStrain, BiventricularCombinedSigns, SevereRightQr, PulmonaryHeartSigns }

// Explicit teaching coefficients, not chamber mass or anatomical calibration.
public static class EcgVentricularIllustrations
{
    public const string EvidenceId = "LeftVentricularIllustrationDraft@1";
    public const string BiventricularEvidenceId = "BiventricularIllustrationDraft@1";
    public const string SevereRightEvidenceId = "SevereRightVentricularIllustrationDraft@1";
    public const string PulmonaryHeartEvidenceId = "PulmonaryHeartIllustrationDraft@1";
    public const string RightEvidenceId = "RightVentricularIllustrationDraft@1";
    public static long? QrsDurationNs(EcgVentricularIllustration illustration) => illustration switch
    {
        EcgVentricularIllustration.Reference => null,
        EcgVentricularIllustration.LeftHypertrophyWithStrain => 100_000_000,
        EcgVentricularIllustration.BiventricularCombinedSigns => 100_000_000,
        EcgVentricularIllustration.RightHypertrophyWithStrain or EcgVentricularIllustration.SevereRightQr or EcgVentricularIllustration.PulmonaryHeartSigns => 80_000_000,
        _ => throw new EventWaveformException("EcgVentricular.InvalidIllustration", nameof(illustration)),
    };

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(EcgVentricularIllustration illustration)
    {
        var electrodes = TextbookElectrodeReference.CreateElectrodes(ventricular: illustration);
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Concat(
            electrodes[(int)EcgElectrode.RA].Bands.Select(band => band with
            { TableQ32 = Array.AsReadOnly(band.TableQ32.Select(value => checked(-value)).ToArray()) })).ToArray());
    }

    internal static IReadOnlyList<ElectrodeWaveformPlan> Apply(EcgVentricularIllustration illustration,
        IReadOnlyList<ElectrodeWaveformPlan> source, EcgCycleTiming timing)
    {
        if (QrsDurationNs(illustration) is not { } duration) { return source; }
        bool preserveRepolarization = illustration is EcgVentricularIllustration.BiventricularCombinedSigns or EcgVentricularIllustration.PulmonaryHeartSigns;
        if (timing.QrsDurationNs != duration || (timing.StDurationNs <= 0 && !preserveRepolarization))
        { throw new EventWaveformException("EcgVentricular.InvalidTiming", "timing"); }
        if (preserveRepolarization)
        {
            IReadOnlyList<long>[] qrs = illustration == EcgVentricularIllustration.PulmonaryHeartSigns
                ? [PulmonaryHeartQrsTables.RA, PulmonaryHeartQrsTables.LA, PulmonaryHeartQrsTables.RL, PulmonaryHeartQrsTables.LL,
                    PulmonaryHeartQrsTables.C1, PulmonaryHeartQrsTables.C2, PulmonaryHeartQrsTables.C3, PulmonaryHeartQrsTables.C4, PulmonaryHeartQrsTables.C5, PulmonaryHeartQrsTables.C6]
                : [BiventricularQrsTables.RA, BiventricularQrsTables.LA,
                BiventricularQrsTables.RL, BiventricularQrsTables.LL, BiventricularQrsTables.C1,
                BiventricularQrsTables.C2, BiventricularQrsTables.C3, BiventricularQrsTables.C4,
                BiventricularQrsTables.C5, BiventricularQrsTables.C6];
            return Array.AsReadOnly(source.Select((electrode, i) => electrode with
            {
                Bands = Array.AsReadOnly(electrode.Bands.Select((band, index) =>
                    index == 1 ? band with { TableQ32 = qrs[i] } : band).ToArray()),
            }).ToArray());
        }
        bool right = illustration is EcgVentricularIllustration.RightHypertrophyWithStrain or EcgVentricularIllustration.SevereRightQr;
        IReadOnlyList<long>[]? rightQrs = illustration == EcgVentricularIllustration.SevereRightQr
            ? [SevereRightVentricularQrsTables.RA, SevereRightVentricularQrsTables.LA, SevereRightVentricularQrsTables.RL, SevereRightVentricularQrsTables.LL,
                SevereRightVentricularQrsTables.C1, SevereRightVentricularQrsTables.C2, SevereRightVentricularQrsTables.C3, SevereRightVentricularQrsTables.C4, SevereRightVentricularQrsTables.C5, SevereRightVentricularQrsTables.C6]
            : right ? [RightVentricularQrsTables.RA, RightVentricularQrsTables.LA,
            RightVentricularQrsTables.RL, RightVentricularQrsTables.LL, RightVentricularQrsTables.C1,
            RightVentricularQrsTables.C2, RightVentricularQrsTables.C3, RightVentricularQrsTables.C4,
            RightVentricularQrsTables.C5, RightVentricularQrsTables.C6] : null;
        int[] t = right ? [-40, 80, 0, -40, -300, -250, -100, 100, 200, 200]
            : [100, -200, 0, 100, 300, 250, 100, -100, -350, -300];
        var st = (right ? new EcgStSegmentPlan([0, 0, 0, 0, -60, -50, 0, 0, 0, 0],
            [0, 0, 0, 0, -120, -100, 0, 0, 0, 0]) : new EcgStSegmentPlan([20, -40, 0, 20, 0, 0, 0, -40, -60, -60],
            [40, -80, 0, 40, 0, 0, 0, -80, -120, -120])).CreateBands(timing);
        long peak = TextbookEcgTables.T.Max();
        return Array.AsReadOnly(source.Select((electrode, i) => electrode with
        {
            Bands = Array.AsReadOnly(electrode.Bands.Select((band, index) => index switch
            {
                // Threefold limb QRS and twofold projected chest QRS. Carry
                // the changed Wilson reference into each chest electrode.
                1 => band with
                {
                    TableQ32 = rightQrs?[i] ?? Array.AsReadOnly(band.TableQ32.Select((value, k) => i < 4
                    ? checked(value * 3)
                    : checked(value * 2 + (long)FixedPointMath.RoundDivideTiesToEven(
                        (Int128)source[0].Bands[1].TableQ32[k] + source[1].Bands[1].TableQ32[k] + source[3].Bands[1].TableQ32[k], 3))).ToArray())
                },
                2 => band with
                {
                    TableQ32 = Array.AsReadOnly(TextbookEcgTables.T.Select(value =>
                    checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)value * t[i] * FixedPointMath.Q32One, peak))).ToArray())
                },
                _ => band,
            }).Concat(st[i] ?? []).ToArray()),
        }).ToArray());
    }
}
