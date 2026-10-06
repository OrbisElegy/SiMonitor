// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Fixed pause without escape: PP2000ms is not a multiple of baseline800ms.
// Timing is authored, not a sinus-node or automaticity model.
public static class SinusArrestReference
{
    public const string EvidenceId = "SinusArrestIllustration@1";
    public static EcgCycleTiming Timing { get; } = new(800_000_000, 100_000_000,
        160_000_000, 80_000_000, 400_000_000, 180_000_000);
    public static RegularPhysiologyPlan CreatePlan() => new(0, 800_000_000,
        160_000_000, 80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000,
        ConductionPattern: AvConductionPattern.SinusArrestIllustration);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes() =>
        TextbookElectrodeReference.CreateElectrodes(timing: Timing);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands()
    {
        var electrodes = CreateElectrodes();
        return Array.AsReadOnly(electrodes[(int)EcgElectrode.LL].Bands.Zip(electrodes[(int)EcgElectrode.RA].Bands)
            .Select(pair => pair.First with { TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32).Select(v => checked(v.First - v.Second)).ToArray()) }).ToArray());
    }

    internal const long GroupDurationNs = 4_400_000_000;
    internal static ReadOnlySpan<long> CycleOffsetsNs => [0, 800_000_000, 1_600_000_000, 3_600_000_000];

    internal static void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind,
        long offset, long inclusive, Int128 exclusive, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        if (plan.RhythmSchedule is { } rhythm)
        {
            rhythm.Visit(plan, kind, offset, inclusive, exclusive, maximumEvents, visitor, cancellationToken);
            return;
        }
        IndexedCardiacSchedule.Visit(plan, kind, offset, inclusive, exclusive,
            maximumEvents, visitor, GroupDurationNs, CycleOffsetsNs, cancellationToken);
    }
}
