// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Explicit authored physical calibration, independent of impedance/pressure/PI.
// Volumes are BTPS here; the oxygen solver owns conversion to gas amounts STPD.
public sealed record VentilationTransportPlan(int TidalVolumeMicrolitersBtps,
    int DeadSpaceMicrolitersBtps, int InspiredOxygenMillionths, bool AirwayOpen = true);

public enum StrokeVolumeResponse { Constant, CardiacFilling, PrematureBeat, AtrialFibrillation, ConductedFlutter, SeededRate }

public sealed record BloodFlowTransportPlan(int ReferenceStrokeVolumeMicroliters,
    long EjectionDurationNs, StrokeVolumeResponse Response = StrokeVolumeResponse.Constant,
    bool IllustrateAfSystemicPulseDeficit = false);

public readonly record struct PhysiologyTransportInterval(long FromSimTimeNs, long ToExclusiveSimTimeNs,
    long DeliveredVolumeNanolitersBtps, long AlveolarVentilationNanolitersBtps,
    long EffectiveBloodVolumeNanoliters, int InspiredOxygenMillionths, bool AirwayOpen);

public sealed record PhysiologyTransportState(RegularPhysiologyPlan Physiology,
    VentilationTransportPlan Ventilation, BloodFlowTransportPlan BloodFlow, string ModelId = "PhysiologyTransportInputs@1");

// Indexed integration over source events, with no moving-average flow tail.
// Prefix differences preserve volume exactly across arbitrary caller partitions.
public sealed class PhysiologyTransportSource
{
    public const string ModelId = "PhysiologyTransportInputs@1";
    public const int MaximumEvents = 4096;
    public const long MaximumIntervalNs = 60_000_000_000;
    private readonly PhysiologyTransportState _state;

    private PhysiologyTransportSource(PhysiologyTransportState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _ = RegularPhysiologyTimeline.Start(state.Physiology);
        if (state.ModelId != ModelId || state.Ventilation is not { } ventilation || state.BloodFlow is not { } flow ||
            ventilation.TidalVolumeMicrolitersBtps is < 0 or > 3_000_000 ||
            ventilation.DeadSpaceMicrolitersBtps is < 0 or > 1_000_000 ||
            ventilation.InspiredOxygenMillionths is < 100_000 or > 1_000_000 ||
            flow.ReferenceStrokeVolumeMicroliters is < 0 or > 250_000 ||
            flow.EjectionDurationNs is < 1_000_000 or > 1_000_000_000 || !Enum.IsDefined(flow.Response) ||
            flow.Response == StrokeVolumeResponse.CardiacFilling && !CardiacFillingPerfusion.Supports(state.Physiology) ||
            flow.Response == StrokeVolumeResponse.PrematureBeat && !PrematureBeatPerfusion.IsPattern(state.Physiology.ConductionPattern) ||
            flow.Response == StrokeVolumeResponse.AtrialFibrillation && !AtrialFibrillationReference.IsPattern(state.Physiology.ConductionPattern) ||
            flow.Response == StrokeVolumeResponse.ConductedFlutter && !ConductedFlutterPerfusion.Supports(state.Physiology) ||
            flow.Response == StrokeVolumeResponse.SeededRate && state.Physiology.SeededRate is null ||
            flow.IllustrateAfSystemicPulseDeficit && flow.Response != StrokeVolumeResponse.AtrialFibrillation)
        { throw new ArgumentException("Transport.InvalidPlan", nameof(state)); }
        _state = state;
    }

    public static PhysiologyTransportSource Create(RegularPhysiologyPlan physiology,
        VentilationTransportPlan ventilation, BloodFlowTransportPlan bloodFlow) => new(new(physiology, ventilation, bloodFlow));

    public PhysiologyTransportState CaptureState() => _state;
    public static PhysiologyTransportSource Restore(PhysiologyTransportState state) => new(state);

    public PhysiologyTransportInterval Integrate(long fromSimTimeNs, long toExclusiveSimTimeNs,
        int maximumEvents = MaximumEvents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RegularPhysiologyPlan physiology = _state.Physiology;
        if (fromSimTimeNs < physiology.EpochAnchorSimTimeNs || toExclusiveSimTimeNs < fromSimTimeNs ||
            toExclusiveSimTimeNs - fromSimTimeNs > MaximumIntervalNs)
        { throw new ArgumentOutOfRangeException(nameof(toExclusiveSimTimeNs), "Transport.InvalidInterval"); }
        if (maximumEvents is < 1 or > MaximumEvents) { throw new ArgumentOutOfRangeException(nameof(maximumEvents)); }
        long delivered = 0, alveolar = 0, blood = 0;
        int remaining = maximumEvents;
        var ventilation = _state.Ventilation;
        if (fromSimTimeNs != toExclusiveSimTimeNs && ventilation.AirwayOpen)
        {
            ulong first = (ulong)((fromSimTimeNs - physiology.EpochAnchorSimTimeNs) / physiology.BreathPeriodNs);
            ulong last = (ulong)((toExclusiveSimTimeNs - 1 - physiology.EpochAnchorSimTimeNs) / physiology.BreathPeriodNs);
            if (last - first >= (ulong)remaining) { throw new ArgumentException("Transport.EventBudgetExceeded", nameof(maximumEvents)); }
            long flowDurationNs = physiology.InspirationDurationNs - physiology.InspiratoryPauseNs;
            for (ulong cycle = first; cycle <= last; cycle++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                remaining--;
                bool altered = physiology.ActivityAfterBreaths is not { } after || cycle >= after &&
                    (physiology.ActivityDurationBreaths is not { } duration || cycle - after < duration);
                if (altered && physiology.RespiratoryActivity != RespiratoryActivity.Breathing) { continue; }
                int depth = RespiratoryPatternDepth.At(physiology.RespiratoryPattern, cycle);
                // The explicit reference VT gives the relative source depth its
                // physical scale; display amplitude/polarity is never consulted.
                long tidalNanoliters = (long)ventilation.TidalVolumeMicrolitersBtps * depth;
                long alveolarNanoliters = Math.Max(0, tidalNanoliters - (long)ventilation.DeadSpaceMicrolitersBtps * 1000);
                long startNs = checked(physiology.EpochAnchorSimTimeNs + (long)cycle * physiology.BreathPeriodNs);
                delivered = checked(delivered + Portion(tidalNanoliters, startNs, flowDurationNs));
                alveolar = checked(alveolar + Portion(alveolarNanoliters, startNs, flowDurationNs));
            }
        }
        if (fromSimTimeNs != toExclusiveSimTimeNs)
        {
            var flow = _state.BloodFlow;
            long begin = Math.Max(physiology.EpochAnchorSimTimeNs, fromSimTimeNs - flow.EjectionDurationNs);
            RegularPhysiologyTimeline.VisitVentricularMechanical(physiology, begin, toExclusiveSimTimeNs, remaining, beat =>
            {
                int gain = flow.Response switch
                {
                    StrokeVolumeResponse.CardiacFilling => CardiacFillingPerfusion.GainPermille(physiology, beat.CycleIndex),
                    StrokeVolumeResponse.PrematureBeat => PrematureBeatPerfusion.GainPermille(physiology.ConductionPattern, beat.CycleIndex),
                    StrokeVolumeResponse.AtrialFibrillation => AtrialFibrillationPerfusion.GainPermille(physiology.ConductionPattern, beat.CycleIndex, flow.IllustrateAfSystemicPulseDeficit),
                    StrokeVolumeResponse.ConductedFlutter => ConductedFlutterPerfusion.GainPermille(physiology, beat.CycleIndex),
                    StrokeVolumeResponse.SeededRate => physiology.SeededRate!.EjectionGainPermille(beat.CycleIndex),
                    _ => 1000,
                };
                long durationNs = flow.Response == StrokeVolumeResponse.PrematureBeat
                    ? PrematureBeatPerfusion.DurationNs(physiology.ConductionPattern, beat.CycleIndex, flow.EjectionDurationNs)
                    : flow.EjectionDurationNs;
                blood = checked(blood + Portion((long)flow.ReferenceStrokeVolumeMicroliters * gain, beat.SimTimeNs, durationNs));
            }, cancellationToken);
        }
        return new(fromSimTimeNs, toExclusiveSimTimeNs, delivered, alveolar, blood,
            ventilation.InspiredOxygenMillionths, ventilation.AirwayOpen);

        long Portion(long volumeNanoliters, long startNs, long durationNs)
        {
            Int128 Prefix(long timeNs) => (Int128)volumeNanoliters * Math.Clamp(timeNs - startNs, 0, durationNs) / durationNs;
            return (long)(Prefix(toExclusiveSimTimeNs) - Prefix(fromSimTimeNs));
        }
    }
}
