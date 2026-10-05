// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public sealed record PhysiologySignalSegment(RegularPhysiologyPlan Plan, IReadOnlyList<EventWaveformBand> Bands,
    VascularPressurePlan? Pressure, PlethRunoffPlan? Pleth, long FromEventTimeNs,
    long ToExclusiveEventTimeNs, bool IncludeInitialPressure, int PressureBaselineCentiMmHg = 0);

// Retain only responses already triggered by the old source. New events use the
// new definition, on the existing clock; no second waveform is precomputed.
internal sealed class PhysiologySignalContinuation
{
    internal PhysiologySignalSegment Segment { get; }
    private readonly VascularPressureSource? _pressure;
    private readonly PlethRunoffSource? _pleth;
    internal long SupportNs { get; }

    internal PhysiologySignalContinuation(PhysiologySignalSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        _ = RegularPhysiologyTimeline.Start(segment.Plan);
        if (segment.PressureBaselineCentiMmHg is < short.MinValue or > short.MaxValue ||
            segment.PressureBaselineCentiMmHg != 0 && (segment.Pressure is not null || segment.Pleth is not null) ||
            segment.Pressure is not null && segment.Pleth is not null ||
            segment.IncludeInitialPressure && segment.FromEventTimeNs != segment.Plan.EpochAnchorSimTimeNs)
        { throw new ArgumentException("PhysiologySignal.InvalidSegment"); }
        if (segment.FromEventTimeNs < segment.Plan.EpochAnchorSimTimeNs ||
            segment.ToExclusiveEventTimeNs <= segment.FromEventTimeNs)
        { throw new ArgumentException("PhysiologySignal.InvalidSegment"); }
        if (segment.Pressure is { } pressure)
        {
            _pressure = VascularPressureSource.Create(segment.Plan, pressure);
            SupportNs = checked(pressure.TransitDelayNs + pressure.EjectionDurationNs + 64 * pressure.TimeConstantNs);
        }
        else if (segment.Pleth is { } pleth)
        {
            _pleth = PlethRunoffSource.Create(segment.Plan, pleth);
            SupportNs = checked(pleth.TransitDelayNs + _pleth.SupportNs);
        }
        else
        {
            var owned = EventWaveformComposition.Restore(new(segment.Bands, [])).CaptureState().Bands;
            segment = segment with { Bands = owned };
            SupportNs = owned.Max(b => checked(b.DelayNs + b.DurationNs));
        }
        Segment = segment;
    }

    internal long EvaluateAt(long timeNs, bool includeBaseline, CancellationToken cancellationToken = default)
    {
        if (timeNs < Segment.FromEventTimeNs || timeNs - Segment.ToExclusiveEventTimeNs >= SupportNs) { return 0; }
        if (_pressure is not null)
        {
            long value = _pressure.EvaluateIntervalAt(timeNs, Segment.FromEventTimeNs,
                Segment.ToExclusiveEventTimeNs, Segment.IncludeInitialPressure, cancellationToken);
            return includeBaseline ? value : checked(value - (long)Segment.Pressure!.AsymptoticPressureCentiMmHg * FixedPointMath.Q32One);
        }
        if (_pleth is not null)
        { return _pleth.EvaluateIntervalAt(timeNs, Segment.FromEventTimeNs, Segment.ToExclusiveEventTimeNs, cancellationToken); }
        long from = Math.Max(Segment.FromEventTimeNs, timeNs - SupportNs);
        long to = Math.Min(checked(timeNs + 1), Segment.ToExclusiveEventTimeNs);
        long baseline = includeBaseline ? (long)Segment.PressureBaselineCentiMmHg * FixedPointMath.Q32One : 0;
        if (from >= to) { return baseline; }
        var timeline = RegularPhysiologyTimeline.Restore(new(Segment.Plan, from));
        var events = timeline.AdvanceBefore(to, EventWaveformComposition.MaximumEventCount, cancellationToken);
        return checked(baseline + EventWaveformComposition.Restore(new(Segment.Bands, events)).EvaluateAt(timeNs, cancellationToken));
    }
}
