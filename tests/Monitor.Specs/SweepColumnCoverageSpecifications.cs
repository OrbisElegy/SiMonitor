// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepColumnCoverageSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(SubpixelRegionsShareOneColumnExactly), SubpixelRegionsShareOneColumnExactly),
        new(nameof(ColumnCoverageConservesAreaAcrossWrapAndResize), ColumnCoverageConservesAreaAcrossWrapAndResize),
        new(nameof(WideAndPinnedPlotsUseBoundedRuns), WideAndPinnedPlotsUseBoundedRuns),
        new(nameof(CoverageRestoresAndRejectsInvalidInputsAtomically), CoverageRestoresAndRejectsInvalidInputsAtomically),
    ];

    private static SweepStateProjectionStateMachine Start(ulong duration = 10, ulong gap = 1) =>
        SweepStateProjectionStateMachine.Start(new NoDataSweepPlan("ecg", 1, 2, 0,
            duration, gap, checked(duration + gap)), 3, 4, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.DefaultDuration, 0).CaptureState(), 0, 0);

    private static void SubpixelRegionsShareOneColumnExactly()
    {
        SweepStateProjectionStateMachine machine = Start();
        machine.Advance(1, 42);
        SweepColumnCoverageSnapshot result = SweepColumnCoverage.Compose(machine.CaptureState(), 20, 1);
        Check.That(result.Runs.SequenceEqual(new[]
        {
            new SweepColumnCoverageRun(20, 21, SweepTraceRegionKind.RetainSourceTrace, 1, 10),
            new SweepColumnCoverageRun(20, 21, SweepTraceRegionKind.BackgroundEraseGap, 1, 10),
            new SweepColumnCoverageRun(20, 21, SweepTraceRegionKind.RetainSourceTrace, 8, 10),
        }), "three subpixel pieces share one column without dropping or expanding the erase gap");
    }

    private static void ColumnCoverageConservesAreaAcrossWrapAndResize()
    {
        SweepStateProjectionStateMachine machine = Start(gap: 2);
        machine.SynchronizeContinuity(DataContinuityStateMachine.Start(
            LocalContinuationPolicy.DefaultDuration, 0).Disconnect(false, 1), 0, 0);
        for (long time = 0; time <= 100; time++)
        {
            machine.Advance(time, 0);
            foreach (int width in new[] { 1, 3, 10, 17 })
            {
                SweepColumnCoverageSnapshot result = SweepColumnCoverage.Compose(machine.CaptureState(), 7, width);
                Check.That(result.Runs.Count <= 3 * result.Geometry.Regions.Count &&
                    result.Runs.All(run => run.StartColumn >= 7 && run.EndExclusiveColumn <= 7 + width &&
                        run.StartColumn < run.EndExclusiveColumn && run.CoverageNumerator > 0 &&
                        run.CoverageNumerator <= run.CoverageDenominator),
                    "coverage stays inside plot bounds with at most three runs per region");
                for (int column = 7; column < 7 + width; column++)
                {
                    ulong total = result.Runs.Where(run => run.StartColumn <= column && column < run.EndExclusiveColumn)
                        .Aggregate(0UL, (sum, run) => sum + run.CoverageNumerator);
                    Check.That(total == 10, "each pixel column has exactly unit coverage across every phase");
                }

                ulong gapArea = result.Runs.Where(run => run.Kind == SweepTraceRegionKind.BackgroundEraseGap)
                    .Aggregate(0UL, (sum, run) => sum + (ulong)(run.EndExclusiveColumn - run.StartColumn) * run.CoverageNumerator);
                Check.That(gapArea == (ulong)width * 2, "wrapped gap area is invariant under phase and scales exactly with width");
            }
        }
    }

    private static void WideAndPinnedPlotsUseBoundedRuns()
    {
        SweepStateProjectionStateMachine machine = Start(ulong.MaxValue, 0);
        SweepColumnCoverageSnapshot wide = SweepColumnCoverage.Compose(machine.CaptureState(), 0, int.MaxValue);
        Check.That(wide.Runs.SequenceEqual(new[]
        {
            new SweepColumnCoverageRun(0, int.MaxValue, SweepTraceRegionKind.RetainSourceTrace, ulong.MaxValue, ulong.MaxValue),
        }), "billions of full columns require one run with no per-column allocation or fraction overflow");
        machine.EnterFrozen(0, 0);
        Check.That(SweepColumnCoverage.Compose(machine.CaptureState(), int.MaxValue - 1, 1).Runs.SequenceEqual(new[]
        {
            new SweepColumnCoverageRun(int.MaxValue - 1, int.MaxValue, SweepTraceRegionKind.PinnedHistory, ulong.MaxValue, ulong.MaxValue),
        }), "pinned history covers the complete plot including the maximum exclusive edge");
    }

    private static void CoverageRestoresAndRejectsInvalidInputsAtomically()
    {
        SweepStateProjectionStateMachine machine = Start();
        machine.Advance(9, 123);
        SweepStateProjectionState state = machine.CaptureState();
        SweepColumnCoverageSnapshot expected = SweepColumnCoverage.Compose(state, 5, 13);
        Check.That(expected.Runs.SequenceEqual(SweepColumnCoverage.Compose(
            SweepStateProjectionStateMachine.Restore(state).CaptureState(), 5, 13).Runs),
            "checkpoint reconstruction preserves exact run contributions");
        try
        {
            SweepColumnCoverage.Compose(state, 5, 0);
            throw new InvalidOperationException("invalid bounds must reject");
        }
        catch (SweepPlotGeometryException exception)
        {
            Check.That(exception.ReasonCode == "SweepGeometry.InvalidPlotBounds", "coverage retains geometry validation");
        }
        try
        {
            SweepColumnCoverage.Compose(state with { LiveSweepClockNs = -1 }, 5, 13);
            throw new InvalidOperationException("invalid state must reject");
        }
        catch (SweepStateProjectionException exception)
        {
            Check.That(exception.ReasonCode == "SweepState.InvalidCheckpoint", "coverage revalidates source checkpoints");
        }
        Check.That(machine.CaptureState() == state && expected.Runs.SequenceEqual(
            SweepColumnCoverage.Compose(state, 5, 13).Runs), "failure cannot mutate source or published coverage");
    }
}
