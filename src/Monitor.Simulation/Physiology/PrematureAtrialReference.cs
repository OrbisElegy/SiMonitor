// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Authored PAC: three sinus beats then an early ectopic P-prime.
// Conducted: coupling500/pause1000ms. Blocked:400/1100ms, P-prime on T.
public static class PrematureAtrialReference
{
    public const string EvidenceId = "PrematureAtrialIllustrationDraft@3";
    internal const long GroupDurationNs = 3_100_000_000;
    internal const long MinimumRrNs = 500_000_000;
    public static EcgCycleTiming Timing { get; } = new(MinimumRrNs, 100_000_000,
        160_000_000, 80_000_000, 320_000_000, 140_000_000);
    public static EcgCycleTiming BlockedTiming { get; } = Timing with { RrIntervalNs = 800_000_000 };
    // The aberrant beat is followed by1000ms, so its wider QT does not have to
    // use the shortest preceding RR. Sinus beats retain Timing and QT320ms.
    public static EcgCycleTiming AberrantTiming { get; } = RightBundleBlockReference.Timing with { RrIntervalNs = 1_000_000_000 };
    public static bool IsPattern(AvConductionPattern pattern) => pattern is AvConductionPattern.PrematureAtrialIllustration or AvConductionPattern.BlockedPrematureAtrialIllustration or AvConductionPattern.AberrantPrematureAtrialIllustration;
    public static RegularPhysiologyPlan CreateAberrantPlan() => CreatePlan() with { ConductionPattern = AvConductionPattern.AberrantPrematureAtrialIllustration };
    public static RegularPhysiologyPlan CreatePlan(bool blocked = false) => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000,
        ConductionPattern: blocked ? AvConductionPattern.BlockedPrematureAtrialIllustration : AvConductionPattern.PrematureAtrialIllustration);

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool blocked = false)
    {
        int[] amplitudes = [80, 20, 0, -100, 80, 60, 20, -40, -60, -60];
        long peak = TextbookEcgTables.P.Max();
        return Array.AsReadOnly(TextbookElectrodeReference.CreateElectrodes(timing: blocked ? BlockedTiming : Timing).Select((electrode, i) => electrode with
        {
            Bands = Array.AsReadOnly(electrode.Bands.Append(new EventWaveformBand(
                PhysiologyCycleEventKind.PrematureAtrialElectrical, 0, 80_000_000,
                Array.AsReadOnly(TextbookEcgTables.P.Select(value => checked((long)FixedPointMath.RoundDivideTiesToEven(
                    (Int128)value * amplitudes[i] * FixedPointMath.Q32One, peak))).ToArray()))).ToArray()),
        }).ToArray());
    }

    public static IReadOnlyList<ElectrodeWaveformPlan> CreateAberrantElectrodes()
    {
        var aberrant = RightBundleBlockReference.CreateElectrodes(AberrantTiming);
        return Array.AsReadOnly(CreateElectrodes().Select((electrode, i) => electrode with
        {
            Bands = Array.AsReadOnly(electrode.Bands.Select(band => band.Trigger == PhysiologyCycleEventKind.VentricularElectrical
                ? band with { VentricularCycles = new(4, 0b0111) } : band)
                .Concat(aberrant[i].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical)
                    .Select(band => band with { VentricularCycles = new(4, 0b1000) })).ToArray()),
        }).ToArray());
    }

    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool blocked = false) => LeadII(CreateElectrodes(blocked));
    public static IReadOnlyList<EventWaveformBand> CreateAberrantLeadIIBands() => LeadII(CreateAberrantElectrodes());

    private static System.Collections.ObjectModel.ReadOnlyCollection<EventWaveformBand> LeadII(IReadOnlyList<ElectrodeWaveformPlan> electrodes)
    {
        var ra = electrodes.Single(e => e.Electrode == EcgElectrode.RA);
        var ll = electrodes.Single(e => e.Electrode == EcgElectrode.LL);
        return Array.AsReadOnly(ll.Bands.Zip(ra.Bands).Select(pair => pair.First with
        {
            TableQ32 = Array.AsReadOnly(pair.First.TableQ32.Zip(pair.Second.TableQ32)
                .Select(values => checked(values.First - values.Second)).ToArray()),
        }).ToArray());
    }

    internal static void Visit(RegularPhysiologyPlan plan, PhysiologyCycleEventKind kind,
        long offset, long inclusive, Int128 exclusive, int maximumEvents,
        Action<PhysiologyCycleEvent> visitor, CancellationToken cancellationToken)
    {
        bool blocked = plan.ConductionPattern == AvConductionPattern.BlockedPrematureAtrialIllustration;
        Int128 Start(int slot) => (Int128)plan.EpochAnchorSimTimeNs + offset + (slot == 3 ? (blocked ? 2_000_000_000 : 2_100_000_000) : slot * 800_000_000L);
        bool Selected(int slot)
        {
            if (blocked && slot == 3 && kind is PhysiologyCycleEventKind.VentricularElectrical or PhysiologyCycleEventKind.VentricularMechanical) { return false; }
            return kind switch
            {
                PhysiologyCycleEventKind.AtrialElectrical => slot != 3,
                PhysiologyCycleEventKind.PrematureAtrialElectrical => slot == 3,
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
