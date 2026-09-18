// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// One authored monomorphic PVC with full compensation, not a focus model.
public static class PrematureVentricularReference
{
    public const string EvidenceId = "PrematureVentricularIllustrationDraft@1";
    internal const long GroupDurationNs = 3_200_000_000;
    public static EcgCycleTiming Timing => PrematureAtrialReference.Timing;
    public static RegularPhysiologyPlan CreatePlan() => PrematureAtrialReference.CreatePlan() with
    { ConductionPattern = AvConductionPattern.PrematureVentricularIllustration };

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes()
    {
        var ventricular = CompleteAvBlockVentricularReference.CreateElectrodes();
        return Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: Timing).Select((e, i) => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Select(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical
                ? b with { VentricularCycles = new(4, 7) } : b)
                .Concat(ventricular[i].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical)
                    .Select(b => b with { VentricularCycles = new(4, 8) })).ToArray()),
        }).ToArray());
    }

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands()
    {
        var electrodes = CreateElectrodes();
        var ra = electrodes.Single(e => e.Electrode == EcgElectrode.RA);
        var ll = electrodes.Single(e => e.Electrode == EcgElectrode.LL);
        return Array.AsReadOnly(ll.Bands.Zip(ra.Bands).Select(pair => pair.First with
        { TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32).Select(v => checked(v.First - v.Second)).ToArray()) }).ToArray());
    }

    internal static void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind,
        long offset, long inclusive, Int128 exclusive, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        Int128 Start(int slot) => (Int128)plan.EpochAnchorSimTimeNs + offset + (slot == 3 ? 2_100_000_000 : slot * 800_000_000L);
        bool Selected(int slot) => slot != 3 || kind is PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical;
        Int128 firstGroup = Int128.MaxValue, lastGroup = -1, count = 0;
        for (int slot = 0; slot < 4; slot++)
        {
            if (!Selected(slot)) { continue; }
            Int128 start = Start(slot);
            Int128 first = inclusive <= start ? 0 : ((Int128)inclusive - start + GroupDurationNs - 1) / GroupDurationNs;
            if (exclusive <= start + first * GroupDurationNs) { continue; }
            Int128 last = (exclusive - 1 - start) / GroupDurationNs;
            count += last - first + 1;
            firstGroup = Int128.Min(firstGroup, first);
            lastGroup = Int128.Max(lastGroup, last);
        }
        if (count > maximumEvents)
        { throw new PhysiologyTimelineException("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents)); }
        for (Int128 group = firstGroup; group <= lastGroup; group++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int slot = 0; slot < 4; slot++)
            {
                Int128 time = Start(slot) + group * GroupDurationNs;
                if (Selected(slot) && time >= inclusive && time < exclusive)
                { visitor(new((long)time, kind, (ulong)(group * 4 + slot))); }
            }
        }
    }
}
