// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public enum EcgTContourTarget { Chest, I, II, III, AVR, AVL, AVF }

public enum EcgTContourShape { PositiveNegative = 1, NegativePositive, Notched }

// T targets carried through electrode projection; limb targets couple other limb leads.
public sealed record EcgTContourPlan(int ChestMask, EcgTContourShape Shape, int PeakMicrovolts, EcgTContourTarget Target = EcgTContourTarget.Chest, int? CrossingPositionPermille = null)
{
    private static readonly int[] LimbIndices = [0, 1, 3];
    public const string EvidenceId = "TContourIllustrationDraft@3";
    internal IReadOnlyList<ElectrodeWaveformPlan> Apply(IReadOnlyList<ElectrodeWaveformPlan> source)
    {
        if (ChestMask is < 1 or > 63 || !Enum.IsDefined(Target) || !Enum.IsDefined(Shape) || PeakMicrovolts is < 1 or > 4000)
        { throw new EventWaveformException("EcgTContour.InvalidPlan", "tContour"); }
        var phasePoints = CreatePhasePoints(source[0].Bands[2].DurationNs);
        var basis = Shape == EcgTContourShape.Notched ? TContourTables.Notched : TContourTables.Biphasic;
        int signedPeak = Shape == EcgTContourShape.NegativePositive ? -PeakMicrovolts : PeakMicrovolts;
        var table = Array.AsReadOnly(basis.Select(value => (long)FixedPointMath.RoundDivideTiesToEven((Int128)value * signedPeak, 1000)).ToArray());
        if (Target != EcgTContourTarget.Chest)
        {
            int[] weights = Target switch
            {
                EcgTContourTarget.I => [-1, 1, 0],
                EcgTContourTarget.II => [-1, 0, 1],
                EcgTContourTarget.III => [0, -1, 1],
                EcgTContourTarget.AVR => [2, -1, -1],
                EcgTContourTarget.AVL => [-1, 2, -1],
                _ => [-1, -1, 2],
            };
            bool augmented = Target >= EcgTContourTarget.AVR;
            int denominator = augmented ? 2 : 1;
            int driveDivisor = augmented ? 3 : 2;
            IReadOnlyList<long> Scale(IReadOnlyList<long> values, int numerator, int divisor) => Array.AsReadOnly(values.Select(value =>
                (long)FixedPointMath.RoundDivideTiesToEven((Int128)value * numerator, divisor)).ToArray());
            // delta = target - projected current T. Inject a zero-sum limb
            // drive; coupled limb leads change, Wilson/chest remain exact.
            List<EventWaveformBand> delta = [source[0].Bands[2] with { TableQ32 = table, PhasePoints = phasePoints }];
            for (int n = 0; n < LimbIndices.Length; n++)
            {
                if (weights[n] == 0) { continue; }
                var band = source[LimbIndices[n]].Bands[2];
                delta.Add(band with { TableQ32 = Scale(band.TableQ32, -weights[n], denominator) });
            }
            var drive = delta.Select(b => b with { TableQ32 = Scale(b.TableQ32, 1, driveDivisor) }).ToArray();
            var output = source.ToArray();
            for (int n = 0; n < LimbIndices.Length; n++)
            {
                var additions = weights[n] switch
                {
                    2 => drive.Concat(drive),
                    1 => drive,
                    -1 => drive.Select(b => b with { TableQ32 = Scale(b.TableQ32, -1, 1) }),
                    _ => Enumerable.Empty<EventWaveformBand>(),
                };
                int i = LimbIndices[n];
                output[i] = source[i] with { Bands = Array.AsReadOnly(source[i].Bands.Concat(additions).ToArray()) };
            }
            return Array.AsReadOnly(output);
        }
        var wilson = LimbIndices.Select(i => source[i].Bands[2] with
        {
            TableQ32 = Array.AsReadOnly(source[i].Bands[2].TableQ32.Select(value =>
                (long)FixedPointMath.RoundDivideTiesToEven(value, 3)).ToArray()),
        }).ToArray();
        return Array.AsReadOnly(source.Select((electrode, i) => i < 4 || (ChestMask & (1 << (i - 4))) == 0 ? electrode : electrode with
        {
            Bands = Array.AsReadOnly(electrode.Bands.Select((band, index) => index == 2 ? band with { TableQ32 = table, PhasePoints = phasePoints } : band)
                .Concat(wilson).ToArray()),
        }).ToArray());
    }
    private System.Collections.ObjectModel.ReadOnlyCollection<EventWaveformPhasePoint>? CreatePhasePoints(long durationNs)
    {
        if (CrossingPositionPermille is null) { return null; }
        if (Shape == EcgTContourShape.Notched || CrossingPositionPermille is < 1 or > 999)
        { throw new EventWaveformException("EcgTContour.InvalidCrossing", "tContour"); }
        if (CrossingPositionPermille == 500) { return null; }
        long crossing = (long)FixedPointMath.RoundDivideTiesToEven((Int128)durationNs * CrossingPositionPermille.Value, 1000);
        long halfSpan = Math.Min(crossing, durationNs - crossing) / 2;
        if (halfSpan <= 0 || crossing - halfSpan <= 0 || crossing + halfSpan >= durationNs)
        { throw new EventWaveformException("EcgTContour.InvalidCrossing", "tContour"); }
        // Warp at stationary extrema only. The crossing lies inside one linear
        // phase interval, avoiding a slope discontinuity at the baseline.
        return Array.AsReadOnly(new EventWaveformPhasePoint[]
        {
            new(0, 0), new(crossing - halfSpan, 32),
            new(crossing + halfSpan, 96), new(durationNs, 128),
        });
    }

}
