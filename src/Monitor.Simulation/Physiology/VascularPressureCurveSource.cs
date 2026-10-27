// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// All endpoint solves happen during preparation. Between breakpoints, interpolate
// two bounded reconstructions driven by the very same accepted ejections. This
// preserves pulse timing, missing beats, reservoir runoff, and acquisition order.
internal sealed class VascularPressureCurveSource
{
    private sealed record Segment(long FromNs, long ToNs, VascularPressureSource From, VascularPressureSource To);
    private readonly Segment[] _segments;

    internal VascularPressureCurveSource(RegularPhysiologyPlan physiology, VascularPressurePlan plan)
    {
        var curves = plan.TargetCurves!;
        ArgumentNullException.ThrowIfNull(curves.SystolicCentiMmHg);
        ArgumentNullException.ThrowIfNull(curves.DiastolicCentiMmHg);
        if (plan.Morphology is null) { throw new ArgumentException("Physiology.PressureTargetRequiresReservoirMorphology"); }
        long[] times = curves.SystolicCentiMmHg.Changes.Concat(curves.DiastolicCentiMmHg.Changes)
            .SelectMany(change => new[] { change.StartSimTimeNs, checked(change.StartSimTimeNs + change.DurationNs) })
            .Append(physiology.EpochAnchorSimTimeNs).Where(time => time >= physiology.EpochAnchorSimTimeNs)
            .Distinct().Order().ToArray();
        var sources = new Dictionary<(int Systolic, int Diastolic), VascularPressureSource>();
        _segments = new Segment[times.Length];
        for (int index = 0; index < times.Length; index++)
        {
            long from = times[index];
            long to = index + 1 < times.Length ? times[index + 1] : long.MaxValue;
            var start = Source(curves.SystolicCentiMmHg.At(from), curves.DiastolicCentiMmHg.At(from));
            var end = to == long.MaxValue ? start : Source(Before(curves.SystolicCentiMmHg, to), Before(curves.DiastolicCentiMmHg, to));
            _segments[index] = new(from, to, start, end);
        }

        VascularPressureSource Source(int systolic, int diastolic)
        {
            if (sources.TryGetValue((systolic, diastolic), out var cached)) { return cached; }
            var basis = plan with { TargetCurves = null };
            var solved = VascularPressureSource.SolveTarget(PacingReference.WithoutPulseDivision(physiology with { CardiacRateClock = null }), basis, systolic, diastolic)
                ?? throw new ArgumentException("Physiology.PressureTargetUnreachable");
            var source = VascularPressureSource.Create(physiology, basis with
            {
                InitialPressureCentiMmHg = diastolic,
                EjectionEquilibriumCentiMmHg = solved.EjectionEquilibriumCentiMmHg,
                Morphology = basis.Morphology! with { PulseHeightCentiMmHg = solved.PulseHeightCentiMmHg }
            });
            sources.Add((systolic, diastolic), source);
            return source;
        }
    }

    private static int Before(VitalValueCurve curve, long timeNs) =>
        new VitalValueCurve(curve.InitialValue, curve.Changes.Where(change => change.StartSimTimeNs < timeNs).ToArray()).At(timeNs);

    internal long EvaluateIntervalAt(long simTimeNs, long fromEventTimeNs, long toExclusiveEventTimeNs,
        bool includeInitialPressure, CancellationToken cancellationToken)
    {
        int low = 0, high = _segments.Length;
        while (low + 1 < high)
        {
            int middle = low + (high - low) / 2;
            if (_segments[middle].FromNs <= simTimeNs) { low = middle; }
            else { high = middle; }
        }
        var segment = _segments[low];
        long from = segment.From.EvaluateIntervalAt(simTimeNs, fromEventTimeNs, toExclusiveEventTimeNs,
            includeInitialPressure, cancellationToken);
        if (ReferenceEquals(segment.From, segment.To) || simTimeNs == segment.FromNs) { return from; }
        long to = segment.To.EvaluateIntervalAt(simTimeNs, fromEventTimeNs, toExclusiveEventTimeNs,
            includeInitialPressure, cancellationToken);
        return checked(from + (long)FixedPointMath.RoundDivideTiesToEven((Int128)(to - from) *
            (simTimeNs - segment.FromNs), segment.ToNs - segment.FromNs));
    }
}
