// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public sealed partial class VascularPressureSource
{
    private const long FlowStepNs = 8_000_000;
    private const long FlowAnchorNs = 1_000_000_000;
    // Derived, immutable blocks are deliberately absent from checkpoints. A fork
    // may share this bounded memoization without sharing an integration cursor.
    private readonly PressureFlowBlock?[] _flowBlocks = new PressureFlowBlock[4];
    private sealed record FlowBeat(long TimeNs, long EjectionDurationNs, long MorphologyDurationNs, int GainPermille);
    private readonly record struct FlowEdge(long TimeNs, long InputDeltaQ32);
    private readonly record struct FlowState(long PressureQ32, long InputQ32, int EdgeIndex);

    internal void EvaluateUniform(long firstSimTimeNs, long samplePeriodNs, Span<long> output,
        CancellationToken cancellationToken = default) =>
        EvaluateIntervalUniform(firstSimTimeNs, samplePeriodNs, output, _physiology.EpochAnchorSimTimeNs,
            long.MaxValue, true, cancellationToken);

    internal void EvaluateIntervalUniform(long firstSimTimeNs, long samplePeriodNs, Span<long> output,
        long fromEventTimeNs, long toExclusiveEventTimeNs, bool includeInitialPressure,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        for (int index = 0; index < output.Length; index++)
        {
            output[index] = EvaluateIntervalAt(checked(firstSimTimeNs + index * samplePeriodNs),
                fromEventTimeNs, toExclusiveEventTimeNs, includeInitialPressure, cancellationToken);
        }
    }

    private PressureFlowBlock GetFlowBlock(long sourceTimeNs, long fromEventTimeNs, long toExclusiveEventTimeNs,
        bool includeInitialPressure, CancellationToken cancellationToken)
    {
        long blockIndex = (sourceTimeNs - _physiology.EpochAnchorSimTimeNs) / FlowAnchorNs;
        int slot = (int)(blockIndex % _flowBlocks.Length);
        var cached = Volatile.Read(ref _flowBlocks[slot]);
        if (cached is not null && cached.BlockIndex == blockIndex && cached.FromEventTimeNs == fromEventTimeNs &&
            cached.ToExclusiveEventTimeNs == toExclusiveEventTimeNs && cached.IncludeInitialPressure == includeInitialPressure)
        { return cached; }
        var block = new PressureFlowBlock(this, blockIndex, fromEventTimeNs, toExclusiveEventTimeNs,
            includeInitialPressure, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _flowBlocks[slot], block);
        return block;
    }

    private sealed class PressureFlowBlock
    {
        internal long BlockIndex { get; }
        internal long FromEventTimeNs { get; }
        internal long ToExclusiveEventTimeNs { get; }
        internal bool IncludeInitialPressure { get; }
        internal FlowBeat[] Beats { get; }
        private readonly long _anchorTimeNs;
        private readonly long _stepDecayQ62;
        private readonly FlowState[] _states;
        private readonly FlowEdge[] _edges;

        internal PressureFlowBlock(VascularPressureSource owner, long blockIndex, long fromEventTimeNs,
            long toExclusiveEventTimeNs, bool includeInitialPressure, CancellationToken cancellationToken)
        {
            BlockIndex = blockIndex;
            FromEventTimeNs = fromEventTimeNs;
            ToExclusiveEventTimeNs = toExclusiveEventTimeNs;
            IncludeInitialPressure = includeInitialPressure;
            _anchorTimeNs = checked(owner._physiology.EpochAnchorSimTimeNs + blockIndex * FlowAnchorNs);
            var endTimeNs = Int128.Min((Int128)_anchorTimeNs + FlowAnchorNs, (Int128)long.MaxValue + 1);
            _stepDecayQ62 = owner.Decay(FlowStepNs);
            Int128 pressureQ62 = (Int128)(includeInitialPressure
                ? owner._plan.InitialPressureCentiMmHg - owner._plan.AsymptoticPressureCentiMmHg : 0) *
                owner.Decay(_anchorTimeNs - owner._physiology.EpochAnchorSimTimeNs);
            long inputQ32 = 0;
            var beats = new List<FlowBeat>();
            var edges = new List<FlowEdge>();
            long historyFrom = (long)Int128.Max(fromEventTimeNs, Int128.Max(owner._physiology.EpochAnchorSimTimeNs,
                (Int128)_anchorTimeNs - owner._supportNs + 1));
            var historyTo = Int128.Min((Int128)_anchorTimeNs + 1, toExclusiveEventTimeNs);
            if (historyTo > historyFrom)
            {
                RegularPhysiologyTimeline.VisitVentricularMechanical(owner._physiology, historyFrom, historyTo,
                    MaximumEjectionCount, AddBeat, cancellationToken);
            }
            // Keep the historical and forward windows separately bounded. At the
            // maximum admitted event density their combined length can exceed
            // MaximumEjectionCount even though every individual query is valid.
            var futureFrom = Int128.Max((Int128)_anchorTimeNs + 1, fromEventTimeNs);
            var futureTo = Int128.Min(endTimeNs, toExclusiveEventTimeNs);
            if (futureTo > futureFrom)
            {
                RegularPhysiologyTimeline.VisitVentricularMechanical(owner._physiology, (long)futureFrom, futureTo,
                    MaximumEjectionCount, AddBeat, cancellationToken);
            }
            Beats = beats.ToArray();
            _edges = edges.OrderBy(edge => edge.TimeNs).ToArray();
            int count = (int)((endTimeNs - 1 - _anchorTimeNs) / FlowStepNs + 1);
            _states = new FlowState[count];
            var state = new FlowState((long)FixedPointMath.RoundShiftTiesToEven(pressureQ62, 30), inputQ32, 0);
            _states[0] = state;
            long timeNs = _anchorTimeNs;
            for (int sample = 1; sample < count; sample++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long nextTimeNs = checked(_anchorTimeNs + sample * FlowStepNs);
                state = Advance(owner, state, timeNs, nextTimeNs);
                _states[sample] = state;
                timeNs = nextTimeNs;
            }

            void AddBeat(PhysiologyCycleEvent beat)
            {
                int gain = owner.GainPermille(beat);
                if (gain == 0) { return; }
                long duration = owner._plan.UsePrematureBeatPerfusion
                    ? PrematureBeatPerfusion.DurationNs(owner._physiology.ConductionPattern, beat.CycleIndex, owner._plan.EjectionDurationNs)
                    : owner._plan.EjectionDurationNs;
                long morphologyDuration = owner._plan.Morphology?.DurationNs ?? 0;
                if (owner._plan.UsePrematureBeatPerfusion && morphologyDuration > 0)
                { morphologyDuration = PrematureBeatPerfusion.DurationNs(owner._physiology.ConductionPattern, beat.CycleIndex, morphologyDuration); }
                long age = _anchorTimeNs - beat.SimTimeNs;
                if (age >= 0)
                {
                    pressureQ62 += FixedPointMath.RoundDivideTiesToEven((Int128)owner._plan.EjectionEquilibriumCentiMmHg *
                        owner.EjectionCoefficient(age, duration) * gain, 1000);
                }
                if (owner._morphologyTable is not null && age < Math.Max(morphologyDuration, owner._morphologyPeriodNs))
                { beats.Add(new(beat.SimTimeNs, duration, morphologyDuration, gain)); }
                long magnitudeQ32 = (long)FixedPointMath.RoundDivideTiesToEven(
                    (Int128)owner._plan.EjectionEquilibriumCentiMmHg * FixedPointMath.Q32One * gain, 1000);
                Int128 end = (Int128)beat.SimTimeNs + duration;
                if (beat.SimTimeNs <= _anchorTimeNs && end > _anchorTimeNs)
                { inputQ32 = checked(inputQ32 + magnitudeQ32); }
                if (beat.SimTimeNs > _anchorTimeNs && beat.SimTimeNs < endTimeNs)
                { edges.Add(new(beat.SimTimeNs, magnitudeQ32)); }
                if (end > _anchorTimeNs && end < endTimeNs)
                { edges.Add(new((long)end, -magnitudeQ32)); }
            }
        }

        internal long ReservoirAt(long sourceTimeNs, VascularPressureSource owner)
        {
            int sample = (int)((sourceTimeNs - _anchorTimeNs) / FlowStepNs);
            long timeNs = _anchorTimeNs + sample * FlowStepNs;
            var state = _states[sample];
            if (timeNs != sourceTimeNs) { state = Advance(owner, state, timeNs, sourceTimeNs); }
            return checked(state.PressureQ32 + (long)owner._plan.AsymptoticPressureCentiMmHg * FixedPointMath.Q32One);
        }

        private FlowState Advance(VascularPressureSource owner, FlowState state, long fromTimeNs, long toTimeNs)
        {
            long pressureQ32 = state.PressureQ32;
            long inputQ32 = state.InputQ32;
            int edge = state.EdgeIndex;
            while (edge < _edges.Length && _edges[edge].TimeNs <= toTimeNs)
            {
                AdvancePressure(_edges[edge].TimeNs);
                inputQ32 = checked(inputQ32 + _edges[edge].InputDeltaQ32);
                edge++;
            }
            AdvancePressure(toTimeNs);
            return new(pressureQ32, inputQ32, edge);

            void AdvancePressure(long timeNs)
            {
                long durationNs = timeNs - fromTimeNs;
                if (durationNs == 0) { return; }
                long decayQ62 = durationNs == FlowStepNs ? _stepDecayQ62 : owner.Decay(durationNs);
                pressureQ32 = checked(inputQ32 + (long)FixedPointMath.RoundShiftTiesToEven(
                    (Int128)(pressureQ32 - inputQ32) * decayQ62, 62));
                fromTimeNs = timeNs;
            }
        }
    }
}
