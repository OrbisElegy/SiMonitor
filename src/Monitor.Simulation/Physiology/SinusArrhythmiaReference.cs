// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Authored PP sequence, not respiratory coupling or sinus-node physiology.
public static class SinusArrhythmiaReference
{
    public const string EvidenceId = "SinusArrhythmiaIllustration@1";
    public static EcgCycleTiming Timing { get; } = new(600_000_000, 100_000_000,
        160_000_000, 80_000_000, 400_000_000, 180_000_000);
    public static RegularPhysiologyPlan CreatePlan() => new(0, 800_000_000,
        160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000,
        ConductionPattern: AvConductionPattern.SinusArrhythmiaIllustration);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes() =>
        TextbookElectrodeReference.CreateElectrodes(timing: Timing);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands()
    {
        var electrodes = CreateElectrodes();
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Zip(electrodes[(int)EcgElectrode.RA].Bands)
            .Select(pair => pair.First with { TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32).Select(v => checked(v.First - v.Second)).ToArray()) }).ToArray());
    }

    internal static void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind,
        long offset, long inclusive, Int128 exclusive, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        const long groupDuration = 3_200_000_000;
        ReadOnlySpan<long> slots = [0, 800_000_000, 1_800_000_000, 2_400_000_000];
        Int128 firstGroup = Int128.MaxValue, lastGroup = -1, count = 0;
        for (int slot = 0; slot < slots.Length; slot++)
        {
            Int128 start = (Int128)plan.EpochAnchorSimTimeNs + offset + slots[slot];
            Int128 first = inclusive <= start ? 0 : ((Int128)inclusive - start + groupDuration - 1) / groupDuration;
            if (exclusive <= start + first * groupDuration) { continue; }
            Int128 last = (exclusive - 1 - start) / groupDuration;
            count += last - first + 1;
            firstGroup = Int128.Min(firstGroup, first); lastGroup = Int128.Max(lastGroup, last);
        }
        if (count > maximumEvents)
        { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
        for (Int128 group = firstGroup; group <= lastGroup; group++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int slot = 0; slot < slots.Length; slot++)
            {
                Int128 time = (Int128)plan.EpochAnchorSimTimeNs + offset + group * groupDuration + slots[slot];
                if (time >= inclusive && time < exclusive) { visitor(new((long)time, kind, (ulong)(group * 4 + slot))); }
            }
        }
    }

}
