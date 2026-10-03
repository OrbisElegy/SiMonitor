// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public static class AtrialFlutterReference
{
    public const string EvidenceId = "AtrialFlutterIllustrationDraft@3";
    public const string OneToOneEvidenceId = "AtrialFlutterOneToOneIllustration@1";
    public static bool IsPattern(AvConductionPattern pattern) => pattern is AvConductionPattern.AtrialFlutterIllustration or AvConductionPattern.VariableAtrialFlutterIllustration;
    public static RegularPhysiologyPlan CreateVariablePlan() => CreatePlan(2) with { ConductionPattern = AvConductionPattern.VariableAtrialFlutterIllustration };

    internal const long VariableGroupDurationNs = 1_800_000_000;
    internal static ReadOnlySpan<long> VariableCycleOffsetsNs => [0, 400_000_000, 1_000_000_000];

    internal static void VisitVariable(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind,
        long offset, long inclusive, Int128 exclusive, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        const long groupDuration = VariableGroupDurationNs;
        ReadOnlySpan<long> slots = VariableCycleOffsetsNs;
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
                if (time >= inclusive && time < exclusive) { visitor(new((long)time, kind, (ulong)(group * 3 + slot))); }
            }
        }
    }

    public static EcgCycleTiming Timing(int ratio)
    {
        if (ratio is not (1 or 2 or 3 or 4)) { throw new ArgumentOutOfRangeException(nameof(ratio)); }
        // At1:1 use short authored repolarization support, without changing
        // the80ms ventricular event offset. P/PR20 are builder placeholders.
        if (ratio == 1) { return new(200_000_000, 20_000_000, 20_000_000, 80_000_000, 180_000_000, 80_000_000); }
        // P/PR here are construction placeholders; the P band is replaced in full.
        return new(ratio * 200_000_000L, 40_000_000, 80_000_000, 80_000_000, 300_000_000, 140_000_000);
    }

    public static RegularPhysiologyPlan CreatePlan(int ratio = 4)
    {
        _ = Timing(ratio);
        return new(0, 200_000_000, 80_000_000, 80_000_000, 160_000_000,
            3_750_000_000, 1_875_000_000, VentricularConductionRatio: ratio,
            ConductionPattern: AvConductionPattern.AtrialFlutterIllustration);
    }

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(int ratio = 4) =>
        Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: Timing(ratio)).Select((electrode, i) => electrode with
        {
            Bands = Array.AsReadOnly(electrode.Bands.Select((band, index) => index == 0
                ? new EventWaveformBand(PhysiologyCycleEventKind.AtrialElectrical, 0, 200_000_000,
                    Array.AsReadOnly(AtrialFlutterTables.Cycle.Select(value => checked((long)FixedPointMath.RoundDivideTiesToEven(
                        (Int128)value * AtrialFlutterTables.AmplitudesQ32[i], 1000 * (Int128)FixedPointMath.Q32One))).ToArray()))
                : band).ToArray()),
        }).ToArray());

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(int ratio = 4)
    {
        var electrodes = CreateElectrodes(ratio);
        var ra = electrodes.Single(e => e.Electrode == EcgElectrode.RA);
        var ll = electrodes.Single(e => e.Electrode == EcgElectrode.LL);
        return Array.AsReadOnly(ll.Bands.Zip(ra.Bands).Select(pair => pair.First with
        {
            TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32)
                .Select(values => checked(values.First - values.Second)).ToArray()),
        }).ToArray());
    }
}
