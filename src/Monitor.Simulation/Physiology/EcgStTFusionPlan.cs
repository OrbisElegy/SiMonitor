// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public sealed record EcgStTFusionContour(int JMicrovolts, int PeakMicrovolts, int PeakPositionPermille);

// A continuous replacement contour, not an additional ST hump plus a T wave.
// Null entries keep original repolarization. Chest contours are relative to
// Wilson repolarization; limb contours remain electrode potentials before projection.
public sealed record EcgStTFusionPlan(IReadOnlyList<EcgStTFusionContour?> Electrodes)
{
    public const string EvidenceId = "StTFusionIllustrationDraft@1";

    internal EventWaveformBand?[] CreateBands(EcgCycleTiming timing)
    {
        timing.Validate();
        if (Electrodes is null || Electrodes.Count != 10) { throw Invalid(); }
        var contours = Electrodes.ToArray();
        var result = new EventWaveformBand?[10];
        for (int index = 0; index < contours.Length; index++)
        {
            if (contours[index] is not { } contour) { continue; }
            if (contour.JMicrovolts is < -4000 or > 4000 || contour.PeakMicrovolts is < -4000 or > 4000 ||
                contour.PeakPositionPermille is < 1 or > 999) { throw Invalid(); }
            long transition = timing.QrsDurationNs / 4;
            long span = timing.QtIntervalNs - timing.QrsDurationNs;
            long peak = (long)FixedPointMath.RoundDivideTiesToEven((Int128)span * contour.PeakPositionPermille, 1000);
            if (transition <= 0 || peak <= 0 || peak >= span) { throw Invalid(); }
            long[] table = Enumerable.Range(0, 128).Select(i => checked(
                StSegmentTables.J[i] * contour.JMicrovolts + StSegmentTables.End[i] * contour.PeakMicrovolts)).ToArray();
            result[index] = new(PhysiologyCycleEventKind.VentricularElectrical,
                timing.QrsDurationNs - transition, transition + span, Array.AsReadOnly(table),
                Array.AsReadOnly(new EventWaveformPhasePoint[]
                {
                    new(0, 0), new(transition, 32), new(transition + peak, 64), new(transition + span, 128),
                }));
        }
        return result;
    }

    private static EventWaveformException Invalid() => new("EcgStTFusion.InvalidPlan", "fusion");
}
