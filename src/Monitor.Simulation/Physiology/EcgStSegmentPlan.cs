// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Explicit additive electrode potentials. Smooth terminal-QRS onset, independent
// J and ST-end levels, then return to baseline across the existing T support.
// This changes the baseline under T, not its intrinsic amplitude parameter.
public sealed record EcgStSegmentPlan(IReadOnlyList<int> JMicrovolts, IReadOnlyList<int> EndMicrovolts, IReadOnlyList<int>? ArchMicrovolts = null)
{
    public const string EvidenceId = "StSegmentIllustrationDraft@2";

    internal IReadOnlyList<EventWaveformBand>?[] CreateBands(EcgCycleTiming timing)
    {
        timing.Validate();
        if (JMicrovolts is null || EndMicrovolts is null || JMicrovolts.Count != 10 || EndMicrovolts.Count != 10) { throw Invalid(); }
        int[] j = JMicrovolts.ToArray(), end = EndMicrovolts.ToArray();
        if (ArchMicrovolts is not null && ArchMicrovolts.Count != 10) { throw Invalid(); }
        int[] arch = ArchMicrovolts?.ToArray() ?? new int[10];
        if (j.Concat(end).Concat(arch).Any(value => value is < -4000 or > 4000)) { throw Invalid(); }
        var output = new IReadOnlyList<EventWaveformBand>?[10];
        if (j.Concat(end).Concat(arch).All(value => value == 0)) { return output; }
        long transition = timing.QrsDurationNs / 4;
        if (transition <= 0 || timing.StDurationNs <= 0) { throw Invalid(); }
        long start = timing.QrsDurationNs - transition;
        var phases = Array.AsReadOnly(new EventWaveformPhasePoint[]
        {
            new(0, 0), new(transition, 32), new(timing.TOffsetFromQrsNs - start, 64), new(timing.QtIntervalNs - start, 128),
        });
        for (int i = 0; i < 10; i++)
        {
            List<EventWaveformBand> bands = [];
            if (j[i] != 0 || end[i] != 0)
            {
                long[] table = Enumerable.Range(0, 128).Select(index => checked(StSegmentTables.J[index] * j[i] + StSegmentTables.End[index] * end[i])).ToArray();
                bands.Add(new(PhysiologyCycleEventKind.VentricularElectrical,
                    start, timing.QtIntervalNs - start, Array.AsReadOnly(table), phases));
            }
            if (arch[i] != 0)
            {
                bands.Add(new(PhysiologyCycleEventKind.VentricularElectrical, timing.QrsDurationNs,
                    timing.StDurationNs, Array.AsReadOnly(StSegmentTables.Arch.Select(value => checked(value * arch[i])).ToArray())));
            }
            if (bands.Count != 0) { output[i] = Array.AsReadOnly(bands.ToArray()); }
        }
        return output;
    }

    private static EventWaveformException Invalid() => new("EcgSt.InvalidPlan", "stSegment");
}
