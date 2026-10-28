// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public sealed partial class PlethRunoffSource
{
    private const long FlowStepNs = 8_000_000;
    private const long FlowAnchorNs = 1_000_000_000;
    private readonly PlethFlowBlock?[] _flowBlocks = new PlethFlowBlock[4];
    private sealed record FlowPulse(long TimeNs, long DurationNs, long JoinNs, int GainPermille);
    private readonly record struct FlowEdge(long TimeNs, long AmplitudeQ32);
    private readonly record struct FlowState(long BaseQ32, long WeightedQ32, int EdgeIndex);

    internal void EvaluateUniform(long firstSimTimeNs, long samplePeriodNs, Span<long> output,
        CancellationToken cancellationToken = default) =>
        EvaluateIntervalUniform(firstSimTimeNs, samplePeriodNs, output, _physiology.EpochAnchorSimTimeNs,
            long.MaxValue, cancellationToken);

    internal void EvaluateIntervalUniform(long firstSimTimeNs, long samplePeriodNs, Span<long> output,
        long fromEventTimeNs, long toExclusiveEventTimeNs, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        for (int index = 0; index < output.Length; index++)
        {
            output[index] = EvaluateIntervalAt(checked(firstSimTimeNs + index * samplePeriodNs),
                fromEventTimeNs, toExclusiveEventTimeNs, cancellationToken);
        }
    }

    private PlethFlowBlock GetFlowBlock(long sourceTimeNs, long fromEventTimeNs, long toExclusiveEventTimeNs,
        CancellationToken cancellationToken)
    {
        long blockIndex = (sourceTimeNs - _physiology.EpochAnchorSimTimeNs) / FlowAnchorNs;
        int slot = (int)(blockIndex % _flowBlocks.Length);
        var cached = Volatile.Read(ref _flowBlocks[slot]);
        if (cached is not null && cached.BlockIndex == blockIndex && cached.FromEventTimeNs == fromEventTimeNs &&
            cached.ToExclusiveEventTimeNs == toExclusiveEventTimeNs)
        { return cached; }
        var block = new PlethFlowBlock(this, blockIndex, fromEventTimeNs, toExclusiveEventTimeNs, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _flowBlocks[slot], block);
        return block;
    }

    private sealed class PlethFlowBlock
    {
        internal long BlockIndex { get; }
        internal long FromEventTimeNs { get; }
        internal long ToExclusiveEventTimeNs { get; }
        private readonly long _anchorTimeNs;
        private readonly long _stepDecayQ62;
        private readonly FlowState[] _states;
        private readonly FlowEdge[] _edges;
        private readonly FlowPulse[] _heads;

        internal PlethFlowBlock(PlethRunoffSource owner, long blockIndex, long fromEventTimeNs,
            long toExclusiveEventTimeNs, CancellationToken cancellationToken)
        {
            BlockIndex = blockIndex;
            FromEventTimeNs = fromEventTimeNs;
            ToExclusiveEventTimeNs = toExclusiveEventTimeNs;
            _anchorTimeNs = checked(owner._physiology.EpochAnchorSimTimeNs + blockIndex * FlowAnchorNs);
            var endTimeNs = Int128.Min((Int128)_anchorTimeNs + FlowAnchorNs, (Int128)long.MaxValue + 1);
            _stepDecayQ62 = owner.Decay(FlowStepNs);
            long baseQ32 = 0;
            long weightedQ32 = 0;
            var heads = new List<FlowPulse>();
            var edges = new List<FlowEdge>();
            long historyFrom = (long)Int128.Max(fromEventTimeNs, Int128.Max(owner._physiology.EpochAnchorSimTimeNs,
                (Int128)_anchorTimeNs - owner.SupportNs + 1));
            var historyTo = Int128.Min((Int128)_anchorTimeNs + 1, toExclusiveEventTimeNs);
            if (historyTo > historyFrom)
            {
                RegularPhysiologyTimeline.VisitVentricularMechanical(owner._physiology, historyFrom, historyTo,
                    MaximumEventCount, AddBeat, cancellationToken);
            }
            var futureFrom = Int128.Max((Int128)_anchorTimeNs + 1, fromEventTimeNs);
            var futureTo = Int128.Min(endTimeNs, toExclusiveEventTimeNs);
            if (futureTo > futureFrom)
            {
                RegularPhysiologyTimeline.VisitVentricularMechanical(owner._physiology, (long)futureFrom, futureTo,
                    MaximumEventCount, AddBeat, cancellationToken);
            }
            _heads = heads.ToArray();
            _edges = edges.OrderBy(edge => edge.TimeNs).ToArray();
            int count = (int)((endTimeNs - 1 - _anchorTimeNs) / FlowStepNs + 1);
            _states = new FlowState[count];
            var state = new FlowState(baseQ32, weightedQ32, 0);
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
                    ? PrematureBeatPerfusion.DurationNs(owner._physiology.ConductionPattern, beat.CycleIndex, owner._plan.PulseDurationNs)
                    : owner._plan.PulseDurationNs;
                long join = (long)((Int128)duration * owner._joinIndex / 128);
                long amplitude = (long)FixedPointMath.RoundDivideTiesToEven((Int128)owner._table[owner._joinIndex] * gain, 1000);
                Int128 joinTime = (Int128)beat.SimTimeNs + join;
                if (joinTime <= _anchorTimeNs)
                {
                    long age = (long)(_anchorTimeNs - joinTime);
                    long decayed = (long)FixedPointMath.RoundShiftTiesToEven((Int128)amplitude * owner.Decay(age), 62);
                    baseQ32 = checked(baseQ32 + decayed);
                    weightedQ32 = checked(weightedQ32 + (long)FixedPointMath.RoundDivideTiesToEven(
                        (Int128)decayed * age, owner._plan.TailTimeConstantNs));
                }
                else
                {
                    heads.Add(new(beat.SimTimeNs, duration, join, gain));
                    if (joinTime < endTimeNs) { edges.Add(new((long)joinTime, amplitude)); }
                }
            }
        }

        internal long EvaluateAt(long sourceTimeNs, PlethRunoffSource owner)
        {
            int sample = (int)((sourceTimeNs - _anchorTimeNs) / FlowStepNs);
            long timeNs = _anchorTimeNs + sample * FlowStepNs;
            var state = _states[sample];
            if (timeNs != sourceTimeNs) { state = Advance(owner, state, timeNs, sourceTimeNs); }
            long result = checked(state.BaseQ32 + state.WeightedQ32);
            foreach (var pulse in _heads)
            {
                long age = sourceTimeNs - pulse.TimeNs;
                if (age < 0) { break; }
                if (age >= pulse.JoinNs) { continue; }
                long value = PeriodicLutLinear.Interpolate(owner._table,
                    (ulong)(((UInt128)age << 64) / (ulong)pulse.DurationNs)).Value;
                result = checked(result + (long)FixedPointMath.RoundDivideTiesToEven((Int128)value * pulse.GainPermille, 1000));
            }
            return result;
        }

        private FlowState Advance(PlethRunoffSource owner, FlowState state, long fromTimeNs, long toTimeNs)
        {
            long baseQ32 = state.BaseQ32;
            long weightedQ32 = state.WeightedQ32;
            int edge = state.EdgeIndex;
            while (edge < _edges.Length && _edges[edge].TimeNs <= toTimeNs)
            {
                AdvanceTail(_edges[edge].TimeNs);
                baseQ32 = checked(baseQ32 + _edges[edge].AmplitudeQ32);
                edge++;
            }
            AdvanceTail(toTimeNs);
            return new(baseQ32, weightedQ32, edge);

            void AdvanceTail(long timeNs)
            {
                long durationNs = timeNs - fromTimeNs;
                if (durationNs == 0) { return; }
                long decayQ62 = durationNs == FlowStepNs ? _stepDecayQ62 : owner.Decay(durationNs);
                long grownQ32 = checked(weightedQ32 + (long)FixedPointMath.RoundDivideTiesToEven(
                    (Int128)baseQ32 * durationNs, owner._plan.TailTimeConstantNs));
                weightedQ32 = (long)FixedPointMath.RoundShiftTiesToEven((Int128)grownQ32 * decayQ62, 62);
                baseQ32 = (long)FixedPointMath.RoundShiftTiesToEven((Int128)baseQ32 * decayQ62, 62);
                fromTimeNs = timeNs;
            }
        }
    }
}
