// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Shared repolarization phase map. Amplitude/polarity remain separate gains;
// changing the peak position never changes the finite T support or QT.
public sealed record EcgTWaveShapePlan(int PeakPositionPermille)
{
    internal IReadOnlyList<EventWaveformPhasePoint> CreatePhasePoints(EcgCycleTiming timing)
    {
        timing.Validate();
        if (PeakPositionPermille is < 100 or > 900) { throw Invalid(); }
        long peakNs = (long)FixedPointMath.RoundDivideTiesToEven((Int128)timing.TDurationNs * PeakPositionPermille, 1000);
        if (peakNs <= 0 || peakNs >= timing.TDurationNs) { throw Invalid(); }
        int peakIndex = Enumerable.Range(0, TextbookEcgTables.T.Count).MaxBy(index => TextbookEcgTables.T[index]);
        return Array.AsReadOnly(new EventWaveformPhasePoint[]
        {
            new(0, 0), new(peakNs, peakIndex), new(timing.TDurationNs, TextbookEcgTables.T.Count),
        });
    }

    private static EventWaveformException Invalid() => new("EcgTWave.InvalidShape", "tWave");
}
