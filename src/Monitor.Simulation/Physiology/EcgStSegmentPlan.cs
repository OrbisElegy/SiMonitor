// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Explicit additive electrode potentials. Smooth terminal-QRS onset, independent
// J and ST-end levels, then return to baseline across the existing T support.
// This changes the baseline under T, not its intrinsic amplitude parameter.
public sealed record EcgStSegmentPlan(IReadOnlyList<int> JMicrovolts, IReadOnlyList<int> EndMicrovolts)
{
    public const string EvidenceId = "StSegmentIllustrationDraft@1";

    internal IReadOnlyList<EventWaveformBand>?[] CreateBands(EcgCycleTiming timing)
    {
        timing.Validate();
        if (JMicrovolts is null || EndMicrovolts is null || JMicrovolts.Count != 10 || EndMicrovolts.Count != 10) { throw Invalid(); }
        int[] j = JMicrovolts.ToArray(), end = EndMicrovolts.ToArray();
        if (j.Concat(end).Any(value => value is < -4000 or > 4000)) { throw Invalid(); }
        var output = new IReadOnlyList<EventWaveformBand>?[10];
        if (j.Concat(end).All(value => value == 0)) { return output; }
        long transition = timing.QrsDurationNs / 4;
        if (transition <= 0 || timing.StDurationNs <= 0) { throw Invalid(); }
        long start = timing.QrsDurationNs - transition;
        var phases = Array.AsReadOnly(new EventWaveformPhasePoint[]
        {
            new(0, 0), new(transition, 32), new(timing.TOffsetFromQrsNs - start, 64), new(timing.QtIntervalNs - start, 128),
        });
        for (int i = 0; i < 10; i++)
        {
            if (j[i] == 0 && end[i] == 0) { continue; }
            long[] table = Enumerable.Range(0, 128).Select(index => checked(StSegmentTables.J[index] * j[i] + StSegmentTables.End[index] * end[i])).ToArray();
            output[i] = Array.AsReadOnly(new[] { new EventWaveformBand(PhysiologyCycleEventKind.VentricularElectrical,
                start, timing.QtIntervalNs - start, Array.AsReadOnly(table), phases) });
        }
        return output;
    }

    private static EventWaveformException Invalid() => new("EcgSt.InvalidPlan", "stSegment");
}
