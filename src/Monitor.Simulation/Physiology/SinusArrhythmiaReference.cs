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

    internal const long GroupDurationNs = 3_200_000_000;
    internal static ReadOnlySpan<long> CycleOffsetsNs => [0, 800_000_000, 1_800_000_000, 2_400_000_000];

    internal static void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind,
        long offset, long inclusive, Int128 exclusive, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        IndexedCardiacSchedule.Visit(plan, kind, offset, inclusive, exclusive,
            maximumEvents, visitor, GroupDurationNs, CycleOffsetsNs, cancellationToken);
    }
}
