// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// One authored monomorphic PVC with full compensation, not a focus model.
public static class PrematureVentricularReference
{
    public const string EvidenceId = "PrematureVentricularIllustrationDraft@2";
    public static bool IsPattern(AvConductionPattern pattern) => pattern is AvConductionPattern.PrematureVentricularIllustration or
        AvConductionPattern.VentricularBigeminyIllustration or AvConductionPattern.VentricularTrigeminyIllustration;
    public static int BeatsPerGroup(AvConductionPattern pattern) => pattern switch
    {
        AvConductionPattern.PrematureVentricularIllustration => 4,
        AvConductionPattern.VentricularBigeminyIllustration => 2,
        AvConductionPattern.VentricularTrigeminyIllustration => 3,
        _ => throw new PhysiologyTimelineException("PhysiologyTimeline.InvalidPvcPattern", nameof(pattern)),
    };
    public static EcgCycleTiming Timing => PrematureAtrialReference.Timing;
    public static RegularPhysiologyPlan CreatePlan(AvConductionPattern pattern = AvConductionPattern.PrematureVentricularIllustration)
    {
        _ = BeatsPerGroup(pattern);
        return PrematureAtrialReference.CreatePlan() with { ConductionPattern = pattern };
    }

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(AvConductionPattern pattern = AvConductionPattern.PrematureVentricularIllustration)
    {
        int count = BeatsPerGroup(pattern);
        ulong ectopic = 1UL << (count - 1);
        var ventricular = CompleteAvBlockVentricularReference.CreateElectrodes();
        return Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: Timing).Select((e, i) => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Select(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical
                ? b with { VentricularCycles = new(count, ectopic - 1) } : b)
                .Concat(ventricular[i].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical)
                    .Select(b => b with { VentricularCycles = new(count, ectopic) })).ToArray()),
        }).ToArray());
    }

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(AvConductionPattern pattern = AvConductionPattern.PrematureVentricularIllustration)
    {
        var electrodes = CreateElectrodes(pattern);
        var ra = electrodes.Single(e => e.Electrode == EcgElectrode.RA);
        var ll = electrodes.Single(e => e.Electrode == EcgElectrode.LL);
        return Array.AsReadOnly(ll.Bands.Zip(ra.Bands).Select(pair => pair.First with
        { TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32).Select(v => checked(v.First - v.Second)).ToArray()) }).ToArray());
    }

    internal static void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind,
        long offset, long inclusive, Int128 exclusive, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        int countPerGroup = BeatsPerGroup(plan.ConductionPattern);
        long groupDurationNs = countPerGroup * 800_000_000L;
        Int128 Start(int slot) => (Int128)plan.EpochAnchorSimTimeNs + offset + (slot == countPerGroup - 1 ? (countPerGroup - 2) * 800_000_000L + 500_000_000 : slot * 800_000_000L);
        bool Selected(int slot) => slot != countPerGroup - 1 || kind is PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical;
        Int128 firstGroup = Int128.MaxValue, lastGroup = -1, count = 0;
        for (int slot = 0; slot < countPerGroup; slot++)
        {
            if (!Selected(slot)) { continue; }
            Int128 start = Start(slot);
            Int128 first = inclusive <= start ? 0 : ((Int128)inclusive - start + groupDurationNs - 1) / groupDurationNs;
            if (exclusive <= start + first * groupDurationNs) { continue; }
            Int128 last = (exclusive - 1 - start) / groupDurationNs;
            count += last - first + 1;
            firstGroup = Int128.Min(firstGroup, first);
            lastGroup = Int128.Max(lastGroup, last);
        }
        if (count > maximumEvents)
        { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
        for (Int128 group = firstGroup; group <= lastGroup; group++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int slot = 0; slot < countPerGroup; slot++)
            {
                Int128 time = Start(slot) + group * groupDurationNs;
                if (Selected(slot) && time >= inclusive && time < exclusive)
                { visitor(new((long)time, kind, (ulong)(group * countPerGroup + slot))); }
            }
        }
    }
}
