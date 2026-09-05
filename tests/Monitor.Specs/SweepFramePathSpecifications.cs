// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepFramePathSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(FramePathsSplitAroundTheEraseGap), FramePathsSplitAroundTheEraseGap),
        new(nameof(FailedFrameAppendCannotAdvanceSharedFrontier), FailedFrameAppendCannotAdvanceSharedFrontier),
        new(nameof(FrameRestoreRevalidatesIdentityAndReconstructsOutput), FrameRestoreRevalidatesIdentityAndReconstructsOutput),
        new(nameof(FramePathsRespectNoDataAndPinnedHistory), FramePathsRespectNoDataAndPinnedHistory),
    ];
    private static SweepStateProjectionStateMachine Presentation(long phase = 4) => Advance(
        SweepStateProjectionStateMachine.Start(new("ecg", 4, 5, 0, 10, 2, 12), 1, 1,
            SessionRunState.Running, Continuity().CaptureState(), 0, 0), phase);
    private static SweepStateProjectionStateMachine Advance(SweepStateProjectionStateMachine machine, long time)
    {
        machine.Advance(time, 0);
        return machine;
    }
    private static DataContinuityStateMachine Continuity() => DataContinuityStateMachine.Start(LocalContinuationPolicy.DefaultDuration, 0);
    private static SweepFramePathBuilder Start(SweepStateProjectionStateMachine? machine = null) =>
        SweepFramePathBuilder.Start((machine ?? Presentation()).CaptureState(), 0, 10, 0, 10);
    private static SweepPathSample Sample(ulong index, int x) => new(
        new(Guid.Parse("11111111-1111-4111-8111-111111111111"), Guid.Parse("22222222-2222-4222-8222-222222222222"),
            Guid.Parse("33333333-3333-4333-8333-333333333333"), 1, 2, 3, 4, 5, 500, 1),
        index, 0, true, new(new(x, 0, 1), new(5, 1, VerticalPlotRelation.WithinPlot)));

    private static void FramePathsSplitAroundTheEraseGap()
    {
        SweepFramePathBuilder frame = Start();
        frame.Append(Sample(10, 0));
        IReadOnlyList<SweepRegionPathResult> results = frame.Append(Sample(11, 10));
        Check.That(results.Count == 3 && results[0].Path.Segment?.End.X == new ExactPlotCoordinate(4, 1) &&
            results[1].Path.Segment is null && results[2].Path.Segment?.Start.X == new ExactPlotCoordinate(6, 1) &&
            frame.CaptureState().Previous == Sample(11, 10),
            "one source segment is split at gap edges and every region shares one accepted frontier");
    }

    private static void FailedFrameAppendCannotAdvanceSharedFrontier()
    {
        SweepFramePathBuilder frame = Start(Presentation(0));
        frame.Append(Sample(10, 5));
        SweepFramePathState before = frame.CaptureState();
        try
        {
            frame.Append(Sample(11, 4));
            throw new InvalidOperationException("reversed segment must reject");
        }
        catch (SweepSegmentClipException exception)
        {
            Check.That(exception.ReasonCode == "SweepClip.ReversedSegment" && frame.CaptureState() == before,
                "a rejected frame append cannot publish any region frontier or output");
        }
        Check.That(frame.Append(Sample(11, 6)).Any(result => result.Path.Segment is not null),
            "retry with a valid sample uses the original predecessor");
    }

    private static void FrameRestoreRevalidatesIdentityAndReconstructsOutput()
    {
        SweepFramePathBuilder frame = Start();
        frame.Append(Sample(10, 0));
        SweepFramePathState state = frame.CaptureState();
        var restored = SweepFramePathBuilder.Restore(state);
        Check.That(frame.Append(Sample(11, 10)).SequenceEqual(restored.Append(Sample(11, 10))),
            "split-run reconstruction returns the same ordered region results");
        foreach (SweepFramePathState invalid in new[]
        {
            state with { PlotWidthPixels = 0 }, state with { PlotHeightPixels = 0 },
            state with { Previous = state.Previous! with { Source = state.Previous.Source with { SweepEpoch = 99 } } },
            state with { Previous = state.Previous! with { Source = state.Previous.Source with { PresentationClockRevision = 99 } } },
        })
        {
            Check.That(Reason(() => SweepFramePathBuilder.Restore(invalid)) == "SweepFrame.InvalidCheckpoint",
                "restore checks plot bounds and retained sample agreement");
        }
        SweepFramePathState before = frame.CaptureState();
        Check.That(Reason(() => frame.Append(Sample(12, 10) with { Source = Sample(12, 10).Source with { SweepEpoch = 99 } })) ==
            "SweepFrame.PresentationIdentityMismatch" && frame.CaptureState() == before,
            "wrong presentation epoch rejects without changing accepted state");
    }

    private static void FramePathsRespectNoDataAndPinnedHistory()
    {
        SweepStateProjectionStateMachine machine = Presentation(0);
        machine.SynchronizeContinuity(Continuity().Disconnect(false, 1), 0, 0);
        machine.Advance(10, 0);
        SweepFramePathBuilder noData = Start(machine);
        noData.Append(Sample(10, 0));
        Check.That(noData.Append(Sample(11, 10)).All(result => result.Path.Segment is null),
            "fully replaced Live frame cannot expose source geometry through NoData");
        machine.EnterFrozen(10, 0);
        SweepFramePathBuilder pinned = Start(machine);
        pinned.Append(Sample(10, 0));
        Check.That(pinned.Append(Sample(11, 10)).Single().Path.Segment is not null,
            "pinned reconstruction retains historical source geometry independently from Live erasure");
    }

    private static string? Reason(Action action)
    {
        try { action(); return null; }
        catch (SweepFramePathException exception) { return exception.ReasonCode; }
    }
}
