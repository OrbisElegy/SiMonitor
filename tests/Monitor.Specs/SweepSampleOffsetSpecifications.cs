// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepSampleOffsetSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(SampleOffsetsUseExactHalfOpenGeometry), SampleOffsetsUseExactHalfOpenGeometry),
        new(nameof(OffsetAppendMapsBothAxesAndBreaksAtWrap), OffsetAppendMapsBothAxesAndBreaksAtWrap),
        new(nameof(OffsetEvidenceRejectsForgedPixelsAtomically), OffsetEvidenceRejectsForgedPixelsAtomically),
        new(nameof(OffsetCheckpointsRevalidateAfterResize), OffsetCheckpointsRevalidateAfterResize),
    ];

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
    }
}
