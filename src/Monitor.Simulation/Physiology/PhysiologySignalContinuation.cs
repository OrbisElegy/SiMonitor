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
    private readonly EventWaveformComposition? _composition;
    private readonly bool _hasContinuousActivity;
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
            _composition = EventWaveformComposition.Restore(new(segment.Bands, []));
            var owned = _composition.CaptureState().Bands;
            segment = segment with { Bands = owned };
            _hasContinuousActivity = owned.Any(b => IsContinuousActivity(b.Trigger));
            SupportNs = owned.Max(b => b.MaximumSupportNs);
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
        // Atrial f waves and disorganized ventricular activity occupy the
        // source's sample interval. Reconstruct the current segment immediately
        // and stop it at the switch boundary, preserving only triggered beat tails.
        bool includeContinuousActivity = _hasContinuousActivity && timeNs < Segment.ToExclusiveEventTimeNs;
        long from = Math.Max(includeContinuousActivity ? Segment.Plan.EpochAnchorSimTimeNs : Segment.FromEventTimeNs,
            timeNs - SupportNs);
        long to = Math.Min(checked(timeNs + 1), Segment.ToExclusiveEventTimeNs);
        long baseline = includeBaseline ? (long)Segment.PressureBaselineCentiMmHg * FixedPointMath.Q32One : 0;
        if (from >= to) { return baseline; }
        var timeline = RegularPhysiologyTimeline.Restore(new(Segment.Plan, from));
        var events = timeline.AdvanceBefore(to, EventWaveformComposition.MaximumEventCount, cancellationToken)
            .Where(item => IsContinuousActivity(item.Kind)
                ? includeContinuousActivity : item.SimTimeNs >= Segment.FromEventTimeNs).ToArray();
        return checked(baseline + _composition!.WithEvents(events).EvaluateAt(timeNs, cancellationToken));
    }

    internal void EvaluateUniform(long firstSimTimeNs, long samplePeriodNs, Span<long> output,
        bool includeBaseline, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        output.Clear();
        if (output.IsEmpty) { return; }
        if (_pressure is not null || _pleth is not null)
        {
            for (int sample = 0; sample < output.Length; sample++)
            { output[sample] = EvaluateAt(checked(firstSimTimeNs + sample * samplePeriodNs), includeBaseline, cancellationToken); }
            return;
        }
        int first = EventWaveformComposition.FirstSampleAtOrAfter(Segment.FromEventTimeNs,
            firstSimTimeNs, samplePeriodNs, output.Length);
        int last = EventWaveformComposition.FirstSampleAtOrAfter((Int128)Segment.ToExclusiveEventTimeNs + SupportNs,
            firstSimTimeNs, samplePeriodNs, output.Length);
        if (first >= last) { return; }
        int continuousEnd = _hasContinuousActivity ? Math.Clamp(EventWaveformComposition.FirstSampleAtOrAfter(
            Segment.ToExclusiveEventTimeNs, firstSimTimeNs, samplePeriodNs, output.Length), first, last) : first;
        if (continuousEnd > first)
        {
            EvaluateEventWindow(checked(firstSimTimeNs + first * samplePeriodNs), samplePeriodNs,
                output[first..continuousEnd], true, cancellationToken);
        }
        if (last > continuousEnd)
        {
            EvaluateEventWindow(checked(firstSimTimeNs + continuousEnd * samplePeriodNs), samplePeriodNs,
                output[continuousEnd..last], false, cancellationToken);
        }
        if (includeBaseline && Segment.PressureBaselineCentiMmHg != 0)
        {
            long baseline = (long)Segment.PressureBaselineCentiMmHg * FixedPointMath.Q32One;
            for (int sample = first; sample < last; sample++)
            { output[sample] = checked(output[sample] + baseline); }
        }
    }

    private void EvaluateEventWindow(long firstSimTimeNs, long samplePeriodNs, Span<long> output,
        bool includeContinuousActivity, CancellationToken cancellationToken)
    {
        long from = Math.Max(includeContinuousActivity ? Segment.Plan.EpochAnchorSimTimeNs : Segment.FromEventTimeNs,
            firstSimTimeNs - SupportNs);
        long to = Math.Min(checked(firstSimTimeNs + (output.Length - 1L) * samplePeriodNs + 1), Segment.ToExclusiveEventTimeNs);
        if (from >= to) { return; }
        var timeline = RegularPhysiologyTimeline.Restore(new(Segment.Plan, from));
        IReadOnlyList<PhysiologyCycleEvent> events;
        try { events = timeline.AdvanceBefore(to, EventWaveformComposition.MaximumEventCount, cancellationToken); }
        catch (PhysiologyTimelineException exception) when (output.Length > 1 &&
            exception.ReasonCode == "PhysiologyTimeline.EventLimitExceeded")
        {
            // A long batch may exceed the bounded event window even when every
            // individual sample is valid. Splitting changes only reconstruction
            // cost; the exact absolute-time waveform remains identical.
            int middle = output.Length / 2;
            EvaluateEventWindow(firstSimTimeNs, samplePeriodNs, output[..middle], includeContinuousActivity, cancellationToken);
            EvaluateEventWindow(checked(firstSimTimeNs + middle * samplePeriodNs), samplePeriodNs,
                output[middle..], includeContinuousActivity, cancellationToken);
            return;
        }
        _composition!.WithEvents(events.Where(item => IsContinuousActivity(item.Kind)
            ? includeContinuousActivity : item.SimTimeNs >= Segment.FromEventTimeNs).ToArray())
            .EvaluateUniform(firstSimTimeNs, samplePeriodNs, output, cancellationToken);
    }

    private static bool IsContinuousActivity(PhysiologyCycleEventKind kind) => kind is
        PhysiologyCycleEventKind.AtrialFibrillationSegment or PhysiologyCycleEventKind.VentricularDisorganizationSegment;
}
