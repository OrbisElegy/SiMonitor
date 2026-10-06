// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public static class AtrialFibrillationReference
{
    public const string EvidenceId = "AtrialFibrillationIllustrationDraft@1";
    public const long MinimumRrNs = 440_000_000;
    internal const long GridNs = 800_000_000;
    internal const long MaximumJitterNs = 360_000_000;
    public static long SegmentDurationNs => AtrialFibrillationTables.Cycle.Count * 4_000_000L;
    public static EcgCycleTiming Timing { get; } = new(MinimumRrNs, 40_000_000, 80_000_000, 80_000_000, 300_000_000, 140_000_000);
    public static bool IsPattern(AvConductionPattern pattern) => pattern is
        AvConductionPattern.AtrialFibrillationCoarseIllustration or AvConductionPattern.AtrialFibrillationFineIllustration;

    public static RegularPhysiologyPlan CreatePlan(bool fine = false) => new(0, GridNs, 80_000_000,
        80_000_000, 160_000_000, 3_750_000_000, 1_875_000_000,
        ConductionPattern: fine ? AvConductionPattern.AtrialFibrillationFineIllustration : AvConductionPattern.AtrialFibrillationCoarseIllustration);

    // Authored selector, not a bundle refractory-period model.
    public static bool IsLongShortBeat(ulong ordinal)
    {
        if (ordinal < 2) { return false; }
        long previous = Jitter(ordinal - 1);
        return GridNs + Jitter(ordinal) - previous < 600_000_000 &&
            GridNs + previous - Jitter(ordinal - 2) >= 900_000_000;
    }

    public static bool IsLongShortBeat(RegularPhysiologyPlan plan, ulong ordinal)
    {
        if (ordinal < 2) { return false; }
        long previous = plan.RhythmSchedule?.AfJitterNs(ordinal - 1) ?? Jitter(ordinal - 1);
        return GridNs + (plan.RhythmSchedule?.AfJitterNs(ordinal) ?? Jitter(ordinal)) - previous < 600_000_000 &&
            GridNs + previous - (plan.RhythmSchedule?.AfJitterNs(ordinal - 2) ?? Jitter(ordinal - 2)) >= 900_000_000;
    }

    internal static long PrecedingIntervalNs(RegularPhysiologyPlan plan, ulong ordinal)
    {
        Int128 end = (Int128)ordinal * GridNs + (plan.RhythmSchedule?.AfJitterNs(ordinal) ?? Jitter(ordinal));
        Int128 start = ordinal == 0 ? end - GridNs : (Int128)(ordinal - 1) * GridNs + (plan.RhythmSchedule?.AfJitterNs(ordinal - 1) ?? Jitter(ordinal - 1));
        return checked((long)(plan.RateAdjustment is { } rate ? rate.Map(plan, false, end) - rate.Map(plan, false, start) : end - start));
    }

    // Stateless integer mixing of beat identity. No mutable PRNG or origin scan;
    // every event and pressure reconstruction uses exactly the same timestamps.
    internal static long Jitter(ulong beat)
    {
        if (beat == 0) { return 0; }
        unchecked
        {
            ulong value = beat + 0x9e3779b97f4a7c15UL;
            value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
            value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
            value ^= value >> 31;
            return (long)(value % 361) * 1_000_000;
        }
    }

    internal static void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind,
        long offset, long inclusive, Int128 exclusive, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        Int128 start = (Int128)plan.EpochAnchorSimTimeNs + offset;
        var first = Int128.Max(0, ((Int128)inclusive - start - MaximumJitterNs + GridNs - 1) / GridNs);
        var end = Int128.Max(0, (exclusive - start + GridNs - 1) / GridNs);
        if (end - first > (Int128)maximumEvents + 2) { throw Limit(); }
        int count = 0;
        for (Int128 index = first; index < end; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Int128 time = At(index);
            if (time >= inclusive && time < exclusive && ++count > maximumEvents) { throw Limit(); }
        }
        for (Int128 index = first; index < end; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Int128 time = At(index);
            if (time >= inclusive && time < exclusive) { visitor(new((long)time, kind, (ulong)index)); }
        }
        Int128 At(Int128 index) => start + index * GridNs + (plan.RhythmSchedule?.AfJitterNs((ulong)index) ?? Jitter((ulong)index));
        PhysiologyTimelineException Limit() => new("PhysiologyTimeline.EventLimitExceeded", nameof(maximumEvents));
    }

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool fine = false, bool aberrancy = false) =>
        aberrancy ? CreateAberrantElectrodes(fine) :
        Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: Timing).Select((electrode, i) => electrode with
        {
            Bands = Array.AsReadOnly(electrode.Bands.Select((band, index) => index == 0
                ? new EventWaveformBand(PhysiologyCycleEventKind.AtrialFibrillationSegment, 0, SegmentDurationNs,
                    Array.AsReadOnly(AtrialFibrillationTables.Cycle.Select(value => checked((long)FixedPointMath.RoundDivideTiesToEven(
                        (Int128)value * AtrialFibrillationTables.WeightsQ32[i] * (fine ? 300 : 1000),
                        1_000_000 * (Int128)FixedPointMath.Q32One))).ToArray()))
                : band).ToArray()),
        }).ToArray());

    private static System.Collections.ObjectModel.ReadOnlyCollection<ElectrodeWaveformPlan> CreateAberrantElectrodes(bool fine)
    {
        // Factory P/PR only constructs bands; P is discarded. The actual
        // ventricular QT400ms fits even the shortest AF RR440ms.
        var wide = RightBundleBlockReference.CreateElectrodes(RightBundleBlockReference.Timing with
        { PDurationNs = 40_000_000, PrIntervalNs = 80_000_000 });
        return Array.AsReadOnly(CreateElectrodes(fine).Select((electrode, i) => electrode with
        {
            Bands = Array.AsReadOnly(electrode.Bands.Select(band => band.Trigger == PhysiologyCycleEventKind.VentricularElectrical
                ? band with { AfBeatSelection = AtrialFibrillationBeatSelection.Ordinary } : band)
                .Concat(wide[i].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical)
                    .Select(band => band with { AfBeatSelection = AtrialFibrillationBeatSelection.LongShort })).ToArray()),
        }).ToArray());
    }

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool fine = false, bool aberrancy = false)
    {
        var electrodes = CreateElectrodes(fine, aberrancy);
        var ra = electrodes.Single(e => e.Electrode == EcgElectrode.RA);
        var ll = electrodes.Single(e => e.Electrode == EcgElectrode.LL);
        return Array.AsReadOnly(ll.Bands.Zip(ra.Bands).Select(pair => pair.First with
        {
            TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32)
                .Select(values => checked(values.First - values.Second)).ToArray()),
        }).ToArray());
    }
}
