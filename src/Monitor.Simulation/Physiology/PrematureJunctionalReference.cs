// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Authored PJC with retrograde P-prime before, after or within QRS. No automatic focus or
// refractoriness model: the group explicitly selects one compensatory pause.
public static class PrematureJunctionalReference
{
    public const string EvidenceId = "PrematureJunctionalIllustrationDraft@2";
    internal const long GroupDurationNs = 3_200_000_000;
    public static EcgCycleTiming Timing => PrematureAtrialReference.Timing;
    public static bool IsPattern(AvConductionPattern pattern) => pattern is AvConductionPattern.PrematureJunctionalIllustration or
        AvConductionPattern.PrematureJunctionalAfterQrsIllustration or AvConductionPattern.PrematureJunctionalOverlappingIllustration;
    public static long RetrogradeOffsetNs(AvConductionPattern pattern) => pattern switch
    {
        AvConductionPattern.PrematureJunctionalIllustration => 2_180_000_000,
        AvConductionPattern.PrematureJunctionalAfterQrsIllustration => 2_380_000_000,
        AvConductionPattern.PrematureJunctionalOverlappingIllustration => 2_260_000_000,
        _ => throw new PhysiologyTimelineException("PhysiologyTimeline.InvalidJunctionalPattern", nameof(pattern)),
    };
    internal static long MinimumAtrialIntervalNs(AvConductionPattern pattern) => RetrogradeOffsetNs(pattern) - 1_600_000_000;
    public static RegularPhysiologyPlan CreatePlan(AvConductionPattern pattern = AvConductionPattern.PrematureJunctionalIllustration)
    {
        _ = RetrogradeOffsetNs(pattern);
        return PrematureAtrialReference.CreatePlan() with { ConductionPattern = pattern };
    }

    // The existing ectopic vector gives inferior negative / aVR positive P.
    // Reuse only its shape: retrograde atrial activation has its own event time.
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes() =>
        Array.AsReadOnly(PrematureAtrialReference.CreateElectrodes().Select(e => e with
        { Bands = Array.AsReadOnly(e.Bands.Select(Retrograde).ToArray()) }).ToArray());

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands() =>
        Array.AsReadOnly(PrematureAtrialReference.CreateLeadIIBands().Select(Retrograde).ToArray());

    private static EventWaveformBand Retrograde(EventWaveformBand band) =>
        band.Trigger == PhysiologyCycleEventKind.PrematureAtrialElectrical
        ? band with { Trigger = PhysiologyCycleEventKind.RetrogradeAtrialElectrical } : band;

    internal static void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind,
        long offset, long inclusive, Int128 exclusive, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        Int128 Start(int slot) => (Int128)plan.EpochAnchorSimTimeNs + offset + (slot == 3
            ? (kind is PhysiologyCycleEventKind.RetrogradeAtrialElectrical or PhysiologyCycleEventKind.AtrialMechanical ? RetrogradeOffsetNs(plan.ConductionPattern) : 2_100_000_000)
            : slot * 800_000_000L);
        bool Selected(int slot)
        {
            return kind switch
            {
                PhysiologyCycleEventKind.AtrialElectrical => slot != 3,
                PhysiologyCycleEventKind.RetrogradeAtrialElectrical => slot == 3,
                _ => true,
            };
        }
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
