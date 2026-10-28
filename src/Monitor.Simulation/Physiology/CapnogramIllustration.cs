// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public enum CapnogramIllustration
{
    Reference,
    EsophagealIntubation,
    RightMainstemIntubation,
    SpontaneousVentilation,
    IntermittentMandatoryVentilation,
    Obstructive,
    Emphysema,
    CurareCleft,
    Rebreathing,
    ExhaustedAbsorber,
    CardiogenicOscillations,
    MechanicalObstruction,
    Pigtail
}

// Authored landmarks describe selected examples, not diagnostic signatures.
// EndExpiratoryMmHg remains the terminal concentration; the emphysema example
// can have an earlier, higher peak. Rebreathing/absorber presets share the same
// shape: their positive inspired baseline is a separate, visible parameter.
internal static class CapnogramMorphology
{
    private static readonly IReadOnlyList<int> GastricWashoutGains = Array.AsReadOnly(new[] { 1000, 450, 180, 50 });
    internal static long[] Create(CapnogramIllustration illustration, long peakCounts,
        IReadOnlyList<EventWaveformPhasePoint> phases, long? plateauStartCounts = null)
    {
        (int Index, int Permille)[]? landmarks = illustration switch
        {
            CapnogramIllustration.EsophagealIntubation => [(0, 0), (32, 0), (96, 260), (260, 300), (480, 200), (512, 0)],
            CapnogramIllustration.RightMainstemIntubation => [(0, 0), (32, 0), (96, 720), (220, 730), (280, 650), (360, 980), (480, 1000), (512, 0)],
            CapnogramIllustration.SpontaneousVentilation => [(0, 0), (32, 0), (70, 350), (96, 700), (160, 870), (320, 960), (480, 1000), (500, 300), (512, 0)],
            CapnogramIllustration.Obstructive or CapnogramIllustration.MechanicalObstruction => [(0, 0), (32, 0), (96, 320), (180, 580), (300, 800), (480, 1000), (512, 0)],
            CapnogramIllustration.Emphysema => [(0, 0), (32, 0), (96, 1250), (240, 1160), (480, 1000), (512, 0)],
            CapnogramIllustration.CurareCleft => [(0, 0), (32, 0), (96, 930), (250, 960), (288, 560), (326, 980), (480, 1000), (512, 0)],
            CapnogramIllustration.Pigtail => [(0, 0), (32, 0), (96, 670), (420, 700), (450, 740), (480, 1000), (512, 0)],
            _ => null
        };
        if (landmarks is null)
        {
            // Retain the established nominal C/D pressure ratio, while replacing
            // the imported staircase with a continuous authored contour.
            long startQ32 = plateauStartCounts is { } start ? start * FixedPointMath.Q32One :
                (long)FixedPointMath.RoundDivideTiesToEven((Int128)peakCounts * 13 * FixedPointMath.Q32One, 14);
            return SmoothWaveformTable.Create(512,
                [(0, 0), (32, 0), (96, startQ32), (480, peakCounts * FixedPointMath.Q32One), (512, 0)], phases);
        }
        // Derivatives use physical phase times: a smooth curve in table-index
        // space alone would still kink where rise and plateau timing differ.
        var points = landmarks.Select(point => (point.Index, ValueQ32:
            (long)FixedPointMath.RoundDivideTiesToEven((Int128)point.Permille * peakCounts * FixedPointMath.Q32One, 1000))).ToArray();
        if (points.Any(point => point.ValueQ32 > short.MaxValue * FixedPointMath.Q32One))
        { throw new EventWaveformException("Capnogram.Co2ResponseOutOfRange", "EndExpiratoryMmHg"); }
        return SmoothWaveformTable.Create(512, points, phases);
    }

    internal static IReadOnlyList<int> CombineGains(IReadOnlyList<int>? existing, IReadOnlyList<int> authored)
    {
        int length = authored.Count;
        if (existing is not null)
        {
            while (length % existing.Count != 0) { length += authored.Count; }
        }
        if (length > 64) { throw new EventWaveformException("Capnogram.IncompatibleCyclePatterns", "plan"); }
        return Array.AsReadOnly(Enumerable.Range(0, length).Select(index => checked((int)FixedPointMath.RoundDivideTiesToEven(
            (existing?[index % existing.Count] ?? 1000) * authored[index % authored.Count], 1000))).ToArray());
    }

    internal static IReadOnlyList<EventWaveformBand> ApplyBands(IReadOnlyList<EventWaveformBand> bands,
        CapnogramIllustration illustration)
    {
        if (illustration == CapnogramIllustration.EsophagealIntubation)
        {
            // Never wrap the washout sequence back into another apparent breath.
            // Four finite contributions describe gastric CO2 washout, not apnea.
            return Array.AsReadOnly(bands.Select(band => band with
            {
                ExpirationActivationGainsPermille = GastricWashoutGains
            }).ToArray());
        }
        if (illustration == CapnogramIllustration.IntermittentMandatoryVentilation)
        {
            // One mandatory breath and three spontaneous breaths. These modest
            // concentration differences are authored independently of RESP depth.
            List<EventWaveformBand> mixed = [];
            foreach (var band in bands)
            {
                mixed.Add(band with { ExpirationCycleGainsPermille = CombineGains(band.ExpirationCycleGainsPermille, [1000, 0, 0, 0]) });
                // Preserve the dispersion path's share when replacing its shape.
                long pathPeak = checked((long)FixedPointMath.RoundShiftTiesToEven(band.TableQ32[480], 32));
                long[] spontaneous = Create(CapnogramIllustration.SpontaneousVentilation, pathPeak, band.PhasePoints!);
                mixed.Add(band with
                {
                    TableQ32 = Array.AsReadOnly(spontaneous),
                    ExpirationCycleGainsPermille = CombineGains(band.ExpirationCycleGainsPermille, [0, 920, 950, 900])
                });
            }
            return mixed.AsReadOnly();
        }
        if (illustration == CapnogramIllustration.CardiogenicOscillations)
        {
            return Array.AsReadOnly(bands.Select(band => band with
            {
                CardiogenicRippleDurationNs = Math.Min(200_000_000, band.DurationNs),
                CardiogenicRipplePermille = 70
            }).ToArray());
        }
        return bands;
    }
}
