// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepSampleOffsetSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(HorizontalResizeRebuildsFromEvidence), HorizontalResizeRebuildsFromEvidence),
        new(nameof(HorizontalResizePreservesNoDataAndPinnedState), HorizontalResizePreservesNoDataAndPinnedState),
        new(nameof(HorizontalResizeRejectsMissingOrCorruptEvidence), HorizontalResizeRejectsMissingOrCorruptEvidence),
        new(nameof(HorizontalResizeCancellationAndLimitsPreserveSource), HorizontalResizeCancellationAndLimitsPreserveSource),
        new(nameof(SampleOffsetsUseExactHalfOpenGeometry), SampleOffsetsUseExactHalfOpenGeometry),
        new(nameof(OffsetAppendMapsBothAxesAndBreaksAtWrap), OffsetAppendMapsBothAxesAndBreaksAtWrap),
        new(nameof(OffsetEvidenceRejectsForgedPixelsAtomically), OffsetEvidenceRejectsForgedPixelsAtomically),
        new(nameof(OffsetCheckpointsRevalidateAfterResize), OffsetCheckpointsRevalidateAfterResize),
    ];

    private static SweepFrameReconstructionInput ResizeInput() => new(Frame(), new[] { Sample(0, 3), Sample(1, 4) });

    private static void HorizontalResizeRebuildsFromEvidence()
    {
        SweepFrameReconstructionInput input = ResizeInput();
        SweepFrameResizeResult result = SweepFrameHorizontalResize.Rebuild(input, 20, 30, 2, 2);
        Check.That(result.Frame.Segments[0].Segment.Start.X == new ExactPlotCoordinate(29, 1) &&
            result.Frame.Segments[0].Segment.End.X == new ExactPlotCoordinate(32, 1) &&
            result.Checkpoint.Frame.Presentation == input.Frame.Presentation &&
            result.Checkpoint.Frame.VerticalScale == input.Frame.VerticalScale && result.Checkpoint.Frame.Previous is null,
            "resize maps offsets into translated wider bounds without changing time or voltage scale");
        Check.That(result.Checkpoint.Samples[0] == input.Samples[0] with
        {
            Point = input.Samples[0].Point with { X = new(29, 0, 10) },
        }, "only X changes; source, quality, index, cycle and voltage evidence remain intact");
        SweepFrameResizeResult roundTrip = SweepFrameHorizontalResize.Rebuild(result.Checkpoint, 5, 10, 2, 2);
        Check.That(roundTrip.Checkpoint.Samples.SequenceEqual(input.Samples) &&
            result.Frame.Segments.SequenceEqual(SweepFrameReconstructor.Restore(2, 2, result.Checkpoint).Current!.Segments),
            "resize round trip and checkpoint restore introduce no pixel rounding drift");
    }

    private static void HorizontalResizePreservesNoDataAndPinnedState()
    {
        foreach (bool pinned in new[] { false, true })
        {
            SweepFrameReconstructionInput input = ResizeInput();
            SweepStateProjectionStateMachine machine = SweepStateProjectionStateMachine.Restore(input.Frame.Presentation);
            if (pinned) { machine.EnterReview("record.one", 0, 0, 0); }
            machine.SynchronizeContinuity(DataContinuityStateMachine.Restore(machine.CaptureState().ContinuityState).Disconnect(false, 1), 0, 0);
            machine.Advance(10, 0);
            input = input with { Frame = input.Frame with { Presentation = machine.CaptureState() } };
            SweepFrameResizeResult result = SweepFrameHorizontalResize.Rebuild(input, 5, 20, 2, 2);
            Check.That(result.Checkpoint.Frame.Presentation == machine.CaptureState() &&
                (pinned ? result.Frame.Geometry.Regions.Single().Kind == SweepTraceRegionKind.PinnedHistory && result.Frame.Segments.Count == 1
                    : result.Frame.Segments.Count == 0),
                "resize keeps the original Review range or current NoData suppression without advancing any clock");
        }
    }

    private static void HorizontalResizeRejectsMissingOrCorruptEvidence()
    {
        SweepFrameReconstructionInput input = ResizeInput();
        Check.That(Reason(() => SweepFrameHorizontalResize.Rebuild(input with
        {
            Samples = new[] { input.Samples[0], input.Samples[1] with { CycleOffsetNs = null } },
        }, 5, 20, 2, 2)) == "FrameResize.OffsetEvidenceRequired", "legacy pixels cannot be stretched without time evidence");
        Check.That(Reason(() => SweepFrameHorizontalResize.Rebuild(input with
        {
            Samples = new[] { input.Samples[0], input.Samples[1] with { CycleOffsetNs = 5 } },
        }, 5, 20, 2, 2)) == "SweepFrame.TimeMappingMismatch",
            "resize validates original evidence before remapping, rather than repairing forged input silently");
        SweepPathSample[] callerSamples = [input.Samples[0], input.Samples[1]];
        SweepFrameResizeResult result = SweepFrameHorizontalResize.Rebuild(input with { Samples = callerSamples }, 5, 20, 2, 2);
        callerSamples[1] = Sample(9, 9);
        Check.That(result.Checkpoint.Samples[1].SampleIndex == 1 && input.Samples[1].Point.X.WholePixels == 9,
            "output owns its samples and never edits caller coordinates");
    }

    private static void HorizontalResizeCancellationAndLimitsPreserveSource()
    {
        SweepFrameReconstructionInput input = ResizeInput();
        Check.That(Reason(() => SweepFrameHorizontalResize.Rebuild(input, 5, 20, 1, 2)) == "FrameReconstruction.SampleLimitExceeded" &&
            Reason(() => SweepFrameHorizontalResize.Rebuild(input, 5, 0, 2, 2)) == "SweepFrame.InvalidCheckpoint",
            "sample capacity and invalid target bounds fail explicitly");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try
        {
            _ = SweepFrameHorizontalResize.Rebuild(input, 5, 20, 2, 2, cancellation.Token);
            throw new InvalidOperationException("cancelled resize accepted");
        }
        catch (OperationCanceledException exception)
        {
            Check.That(exception.CancellationToken == cancellation.Token, "resize preserves cancellation identity");
        }
        Check.That(input.Frame == Frame() && input.Samples.SequenceEqual(ResizeInput().Samples),
            "failure and cancellation leave the original checkpoint intact");
    }

    private static SweepFramePathState Frame(int width = 10) => new(
        SweepStateProjectionStateMachine.Start(new("ecg", 4, 5, 0, 10, 2, 12), 1, 1, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.Disabled, 0).CaptureState(), 0, 0).CaptureState(),
        5, width, 0, 100, null, new(0, 100, 60, 20, 1));

    private static SweepSampleSource Source => new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"), Guid.Parse("22222222-2222-4222-8222-222222222222"),
        Guid.Parse("33333333-3333-4333-8333-333333333333"), 1, 2, 3, 4, 5, 500, 1);

    private static SweepPathSample Sample(ulong index, ulong offset, int width = 10)
    {
        SweepFramePathBuilder builder = SweepFramePathBuilder.Restore(Frame(width));
        builder.AppendVoltageAtOffset(Source, index, 0, true, offset, new(1000, 1));
        return builder.CaptureState().Previous!;
    }

    private static void SampleOffsetsUseExactHalfOpenGeometry()
    {
        Check.That(SweepPlotGeometry.MapSampleOffset(0, 10, 5, 13) == new SweepPixelPosition(5, 0, 10) &&
            SweepPlotGeometry.MapSampleOffset(9, 10, 5, 13) == new SweepPixelPosition(16, 7, 10),
            "left endpoint and fractional final position exclude the plot right endpoint");
        SweepPixelPosition extreme = SweepPlotGeometry.MapSampleOffset(ulong.MaxValue - 1, ulong.MaxValue, 0, int.MaxValue);
        Check.That(extreme.WholePixels == int.MaxValue - 1 &&
            extreme.FractionNumerator == ulong.MaxValue - int.MaxValue,
            "wide multiplication preserves exact geometry at integer limits");
        Check.That(Reason(() => SweepPlotGeometry.MapSampleOffset(10, 10, 5, 13)) == "SweepGeometry.InvalidSampleOffset" &&
            Reason(() => SweepPlotGeometry.MapSampleOffset(0, 0, 5, 13)) == "SweepGeometry.InvalidSampleOffset" &&
            Reason(() => SweepPlotGeometry.MapSampleOffset(0, 10, int.MaxValue, 1)) == "SweepGeometry.InvalidPlotBounds",
            "cycle end, zero duration and overflowed bounds reject explicitly");
    }

    private static void OffsetAppendMapsBothAxesAndBreaksAtWrap()
    {
        SweepFramePathBuilder builder = SweepFramePathBuilder.Restore(Frame());
        builder.AppendVoltageAtOffset(Source, 0, 0, true, 9, new(1000, 1));
        Check.That(builder.CaptureState().Previous == Sample(0, 9), "mapped sample retains both offset and voltage evidence");
        IReadOnlyList<SweepRegionPathResult> wrap = builder.AppendVoltageAtOffset(Source, 1, 1, true, 0, new(1000, 1));
        Check.That(wrap.All(result => result.Path.Segment is null && result.Path.ReasonCode == "SweepPath.CycleChanged"),
            "adjacent indices at a cycle boundary never connect right edge to left edge");
    }

    private static void OffsetEvidenceRejectsForgedPixelsAtomically()
    {
        SweepFramePathBuilder builder = SweepFramePathBuilder.Restore(Frame());
        builder.Append(Sample(0, 3));
        SweepFramePathState before = builder.CaptureState();
        SweepPathSample next = Sample(1, 4);
        Check.That(Reason(() => builder.Append(next with { CycleOffsetNs = 5 })) == "SweepFrame.TimeMappingMismatch" &&
            Reason(() => builder.Append(next with { Drawable = false, CycleOffsetNs = 10 })) == "SweepFrame.InvalidSampleOffset" &&
            builder.CaptureState() == before,
            "forged or out-of-range offsets reject even suppressed points without changing the frontier");
        Check.That(Reason(() => SweepFramePathBuilder.Restore(before with
        {
            Previous = before.Previous! with { CycleOffsetNs = 4 },
        })) == "SweepFrame.InvalidCheckpoint", "restored predecessor revalidates horizontal evidence");
    }

    private static void OffsetCheckpointsRevalidateAfterResize()
    {
        SweepFrameReconstructor reconstructor = new(2, 2);
        ReconstructedSweepFrame first = reconstructor.Replace(new(Frame(), new[] { Sample(0, 3), Sample(1, 4) }));
        SweepFrameReconstructionInput checkpoint = reconstructor.CaptureCheckpoint()!;
        Check.That(first.Segments.SequenceEqual(SweepFrameReconstructor.Restore(2, 2, checkpoint).Current!.Segments),
            "restore produces identical geometry with retained sample offsets");
        Check.That(Reason(() => reconstructor.Replace(checkpoint with { Frame = Frame(20) })) == "SweepFrame.TimeMappingMismatch" &&
            ReferenceEquals(first, reconstructor.Current) && ReferenceEquals(checkpoint, reconstructor.CaptureCheckpoint()),
            "resize cannot retain old X coordinates even with matching voltage");
        ReconstructedSweepFrame resized = reconstructor.Replace(new(Frame(20), new[] { Sample(0, 3, 20), Sample(1, 4, 20) }));
        Check.That(resized.Segments[0].Segment.Start.X == new ExactPlotCoordinate(11, 1) &&
            first.Segments[0].Segment.Start.X == new ExactPlotCoordinate(8, 1),
            "fresh mapping keeps time offsets while adopting resized geometry");
    }

    private static string? Reason(Action action)
    {
        try { action(); return null; }
        catch (SweepPlotGeometryException exception) { return exception.ReasonCode; }
        catch (SweepFramePathException exception) { return exception.ReasonCode; }
        catch (SweepFrameResizeException exception) { return exception.ReasonCode; }
        catch (SweepFrameReconstructionException exception) { return exception.ReasonCode; }
    }
}
