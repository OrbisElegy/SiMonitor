// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// A rotating polymorphic contour illustration, not a complete TdP episode.
// Integer authored coefficients; no runtime trigonometry or anatomical model.
public static class TwistingVtReference
{
    public const string EvidenceId = "TwistingVtMorphologyIllustration@1";
    private static readonly (int A, int B)[] Weights =
        [(1000, 0), (707, 707), (0, 1000), (-707, 707), (-1000, 0), (-707, -707), (0, -1000), (707, -707)];

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes()
    {
        var source = VentricularTachycardiaReference.CreateElectrodes();
        return Array.AsReadOnly(source.Select((electrode, index) => electrode with
        {
            Bands = Array.AsReadOnly(electrode.Bands.Take(1).Concat(
                Weights.SelectMany((weight, phase) => electrode.Bands.Skip(1).Select((band, component) => band with
                {
                    VentricularCycles = new(8, 1UL << phase),
                    TableQ32 = Array.AsReadOnly(band.TableQ32.Select((a, sample) =>
                        Divide((Int128)a * weight.A + (Int128)SecondBasis(index, component + 1, sample) * weight.B, 1000)).ToArray())
                }))).ToArray())
        }).ToArray());

        long SecondBasis(int electrode, int band, int sample)
        {
            long At(int index) => source[index].Bands[band].TableQ32[sample];
            long ra = At(0), la = At(1), ll = At(3);
            long wilson = Divide((Int128)ra + la + ll, 3);
            // Zero-sum limb basis approximates a quadrature vector. Chest
            // channels form three independent paired axes around Wilson.
            return electrode switch
            {
                0 => Divide(((Int128)ll - la) * 577, 1000),
                1 => Divide(((Int128)ra - ll) * 577, 1000),
                2 => 0,
                3 => Divide(((Int128)la - ra) * 577, 1000),
                >= 4 and <= 6 => At(electrode + 3) - wilson,
                _ => -(At(electrode - 3) - wilson),
            };
        }
    }

    private static long Divide(Int128 value, long divisor) =>
        checked((long)FixedPointMath.RoundDivideTiesToEven(value, divisor));
}
