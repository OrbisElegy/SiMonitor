// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public enum EcgTContourShape { PositiveNegative = 1, NegativePositive, Notched }

// Absolute chest-lead T targets carried through electrode/Wilson projection.
public sealed record EcgTContourPlan(int ChestMask, EcgTContourShape Shape, int PeakMicrovolts)
{
    private static readonly int[] LimbIndices = [0, 1, 3];
    public const string EvidenceId = "TContourIllustrationDraft@1";
    internal IReadOnlyList<ElectrodeWaveformPlan> Apply(IReadOnlyList<ElectrodeWaveformPlan> source)
    {
        if (ChestMask is < 1 or > 63 || !Enum.IsDefined(Shape) || PeakMicrovolts is < 1 or > 4000)
        { throw new EventWaveformException("EcgTContour.InvalidPlan", "tContour"); }
        var basis = Shape == EcgTContourShape.Notched ? TContourTables.Notched : TContourTables.Biphasic;
        int signedPeak = Shape == EcgTContourShape.NegativePositive ? -PeakMicrovolts : PeakMicrovolts;
        var table = Array.AsReadOnly(basis.Select(value => (long)FixedPointMath.RoundDivideTiesToEven((Int128)value * signedPeak, 1000)).ToArray());
        var wilson = LimbIndices.Select(i => source[i].Bands[2] with
        {
            TableQ32 = Array.AsReadOnly(source[i].Bands[2].TableQ32.Select(value =>
                (long)FixedPointMath.RoundDivideTiesToEven(value, 3)).ToArray()),
        }).ToArray();
        return Array.AsReadOnly(source.Select((electrode, i) => i < 4 || (ChestMask & (1 << (i - 4))) == 0 ? electrode : electrode with
        {
            Bands = Array.AsReadOnly(electrode.Bands.Select((band, index) => index == 2 ? band with { TableQ32 = table } : band)
                .Concat(wilson).ToArray()),
        }).ToArray());
    }
}
