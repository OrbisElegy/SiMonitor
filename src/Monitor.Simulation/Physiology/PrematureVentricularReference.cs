// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// One authored monomorphic PVC with full compensation, not a focus model.
public static class PrematureVentricularReference
{
    public const string EvidenceId = "PrematureVentricularIllustrationDraft@3";
    public static bool IsPattern(AvConductionPattern pattern) => pattern is AvConductionPattern.PrematureVentricularIllustration or
        AvConductionPattern.VentricularBigeminyIllustration or AvConductionPattern.VentricularTrigeminyIllustration or
        AvConductionPattern.PolymorphicPvcIllustration or AvConductionPattern.MultifocalPvcIllustration;
    public static int BeatsPerGroup(AvConductionPattern pattern) => pattern switch
    {
        AvConductionPattern.PrematureVentricularIllustration => 4,
        AvConductionPattern.VentricularBigeminyIllustration => 2,
        AvConductionPattern.VentricularTrigeminyIllustration => 3,
        AvConductionPattern.PolymorphicPvcIllustration or AvConductionPattern.MultifocalPvcIllustration => 8,
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
        bool diverse = count == 8;
        ulong ectopic = diverse ? 8UL : 1UL << (count - 1);
        ulong sinus = diverse ? 0b01110111UL : ectopic - 1;
        var ventricular = CompleteAvBlockVentricularReference.CreateElectrodes();
        return Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: Timing).Select((e, i) => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Select(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical
                ? b with { VentricularCycles = new(count, sinus) } : b)
                .Concat(ventricular[i].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical)
                    .Select(b => b with { VentricularCycles = new(count, ectopic) }))
                .Concat(diverse ? ventricular[i].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical).Select(SecondMorphology) : []).ToArray()),
        }).ToArray());
    }

    // Authored second ventricular vector: opposite polarity, QRS180/QT500.
    // This deliberately illustrative transform is not an anatomical focus model.
    private static EventWaveformBand SecondMorphology(EventWaveformBand band) => band with
    {
        VentricularCycles = new(8, 128),
        DurationNs = band.DelayNs == 0 ? 180_000_000 : band.DurationNs,
        DelayNs = band.DelayNs == 0 ? 0 : band.DelayNs + 20_000_000,
        TableQ32 = Array.AsReadOnly(band.TableQ32.Select(v => checked(-v)).ToArray()),
    };

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
        bool Ectopic(int slot) => countPerGroup == 8 ? slot % 4 == 3 : slot == countPerGroup - 1;
        Int128 Start(int slot) => (Int128)plan.EpochAnchorSimTimeNs + offset + (Ectopic(slot)
            ? (slot - 1) * 800_000_000L + (plan.ConductionPattern == AvConductionPattern.MultifocalPvcIllustration && slot == 7 ? 600_000_000 : 500_000_000)
            : slot * 800_000_000L);
        bool Selected(int slot) => !Ectopic(slot) || kind is PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical;
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
