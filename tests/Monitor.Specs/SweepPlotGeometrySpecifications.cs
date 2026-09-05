// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepPlotGeometrySpecifications
{
    public static Specification[] All =>
    [
        new(nameof(SubpixelGapsRemainExactAcrossResize), SubpixelGapsRemainExactAcrossResize),
        new(nameof(WrappedGeometryStaysInsidePlotBounds), WrappedGeometryStaysInsidePlotBounds),
        new(nameof(GeometryRebuildPreservesPinnedAndNoDataRegions), GeometryRebuildPreservesPinnedAndNoDataRegions),
        new(nameof(GeometryHandlesWideProductsAndRejectsInvalidBounds), GeometryHandlesWideProductsAndRejectsInvalidBounds),
    ];

    private static SweepStateProjectionStateMachine Start(ulong duration = 10, ulong gap = 1) =>
        SweepStateProjectionStateMachine.Start(new NoDataSweepPlan("ecg", 1, 2, 0,
            duration, gap, checked(duration + gap)), 3, 4, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.DefaultDuration, 0).CaptureState(), 0, 0);

    private static void SubpixelGapsRemainExactAcrossResize()
    {
        SweepStateProjectionStateMachine machine = Start();
        machine.Advance(1, 123);
        SweepStateProjectionState state = machine.CaptureState();
        SweepPlotGeometrySnapshot narrow = SweepPlotGeometry.Compose(state, 20, 3);
        SweepPlotRegion gap = narrow.Regions.Single(region => region.Kind == SweepTraceRegionKind.BackgroundEraseGap);
        Check.That(gap.StartX == new SweepPixelPosition(20, 3, 10) &&
            gap.EndExclusiveX == new SweepPixelPosition(20, 6, 10),
            "a subpixel gap must neither vanish through rounding nor expand to a whole pixel");
        SweepPlotGeometrySnapshot wide = SweepPlotGeometry.Compose(state, 40, 30);
        SweepPlotRegion wideGap = wide.Regions.Single(region => region.Kind == SweepTraceRegionKind.BackgroundEraseGap);
        Check.That(wideGap.StartX == new SweepPixelPosition(43, 0, 10) &&
            wideGap.EndExclusiveX == new SweepPixelPosition(46, 0, 10) &&
            narrow.SweepEpoch == wide.SweepEpoch && narrow.VisibleDurationNs == wide.VisibleDurationNs &&
            narrow.PresentationClockRevision == wide.PresentationClockRevision && machine.CaptureState() == state,
            "resize changes only geometry, preserving source identity, duration, phase and patient frontier");
    }

    private static void WrappedGeometryStaysInsidePlotBounds()
    {
        SweepStateProjectionStateMachine machine = Start(gap: 2);
        machine.Advance(9, 5);
        SweepPlotGeometrySnapshot geometry = SweepPlotGeometry.Compose(machine.CaptureState(), 25, 100);
        Check.That(geometry.Regions.SequenceEqual(new[]
        {
            new SweepPlotRegion(new(25, 0, 10), new(35, 0, 10), SweepTraceRegionKind.BackgroundEraseGap),
            new SweepPlotRegion(new(35, 0, 10), new(115, 0, 10), SweepTraceRegionKind.RetainSourceTrace),
            new SweepPlotRegion(new(115, 0, 10), new(125, 0, 10), SweepTraceRegionKind.BackgroundEraseGap),
        }), "wrap produces separate plot-clipped pieces and never enters the left calibration gutter");
    }

    private static void GeometryRebuildPreservesPinnedAndNoDataRegions()
    {
        SweepStateProjectionStateMachine machine = Start();
        machine.SynchronizeContinuity(DataContinuityStateMachine.Start(
            LocalContinuationPolicy.DefaultDuration, 0).Disconnect(false, 1), 0, 0);
        machine.Advance(4, 0);
        SweepStateProjectionState checkpoint = machine.CaptureState();
        SweepPlotGeometrySnapshot first = SweepPlotGeometry.Compose(checkpoint, 7, 13);
        SweepPlotGeometrySnapshot restored = SweepPlotGeometry.Compose(
            SweepStateProjectionStateMachine.Restore(checkpoint).CaptureState(), 7, 13);
        Check.That(first.Regions.SequenceEqual(restored.Regions) &&
            first.Regions.Select(region => region.Kind).SequenceEqual(new[]
            {
                SweepTraceRegionKind.NoDataBaseline, SweepTraceRegionKind.BackgroundEraseGap,
                SweepTraceRegionKind.RetainSourceTrace,
            }) && first.Regions.Zip(first.Regions.Skip(1)).All(pair => pair.First.EndExclusiveX == pair.Second.StartX),
            "restored NoData geometry preserves semantics and exact shared boundaries");
        foreach (bool review in new[] { false, true })
        {
            var pinned = SweepStateProjectionStateMachine.Restore(checkpoint);
            if (review) { pinned.EnterReview("record.old", 0, 4, 0); }
            else { pinned.EnterFrozen(4, 0); }
            pinned.Advance(30, 0);
            Check.That(SweepPlotGeometry.Compose(pinned.CaptureState(), 7, 13).Regions.SequenceEqual(new[]
            {
                new SweepPlotRegion(new(7, 0, 10), new(20, 0, 10), SweepTraceRegionKind.PinnedHistory),
            }), "pinned geometry remains full-width despite background NoData coverage");
        }
    }

    private static void GeometryHandlesWideProductsAndRejectsInvalidBounds()
    {
        SweepStateProjectionState state = Start(ulong.MaxValue, 0).CaptureState();
        SweepPlotGeometrySnapshot large = SweepPlotGeometry.Compose(state, 0, int.MaxValue);
        Check.That(large.Regions.Single().EndExclusiveX == new SweepPixelPosition(int.MaxValue, 0, ulong.MaxValue),
            "duration times width uses wide integer arithmetic without overflow");
        foreach ((int left, int width) in new[] { (-1, 10), (0, 0), (0, -1), (int.MaxValue, 1) })
        {
            try
            {
                SweepPlotGeometry.Compose(state, left, width);
                throw new InvalidOperationException("invalid plot bounds must reject");
            }
            catch (SweepPlotGeometryException exception)
            {
                Check.That(exception.ReasonCode == "SweepGeometry.InvalidPlotBounds", "stable plot-bound reason");
            }
        }

        try
        {
            SweepPlotGeometry.Compose(state with { LiveSweepClockNs = -1 }, 0, 1);
            throw new InvalidOperationException("invalid source must reject");
        }
        catch (SweepStateProjectionException exception)
        {
            Check.That(exception.ReasonCode == "SweepState.InvalidCheckpoint", "source checkpoint is revalidated");
        }

        Check.That(SweepStateProjectionStateMachine.Restore(state).CaptureState() == state,
            "geometry failures do not mutate source state");
    }
}
