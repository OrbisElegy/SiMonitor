// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepColumnEnvelopeSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ColumnFrameExcludesExclusiveRightEdgeGeometry), ColumnFrameExcludesExclusiveRightEdgeGeometry),
        new(nameof(ColumnFrameRetainsLeftEdgeAndArrivingSegments), ColumnFrameRetainsLeftEdgeAndArrivingSegments),
        new(nameof(ColumnFrameBoundaryOwnershipRestoresAtCoordinateCeiling), ColumnFrameBoundaryOwnershipRestoresAtCoordinateCeiling),
        new(nameof(ClippedReductionRetainsPeaksAndJoinsOnlyContinuousPieces), ClippedReductionRetainsPeaksAndJoinsOnlyContinuousPieces),
        new(nameof(ClippedReductionRetainsCrossingIntersections), ClippedReductionRetainsCrossingIntersections),
        new(nameof(ClippedReductionKeepsGapsSeparate), ClippedReductionKeepsGapsSeparate),
        new(nameof(ClippedReductionRestoresAndRejectsWithoutTruncation), ClippedReductionRestoresAndRejectsWithoutTruncation),
        new(nameof(RegionReductionMasksBeforeSelectingExtrema), RegionReductionMasksBeforeSelectingExtrema),
        new(nameof(RegionReductionPreservesPinnedHistoryDuringNoData), RegionReductionPreservesPinnedHistoryDuringNoData),
        new(nameof(RegionReductionSeparatesGapsAndOwnsOutput), RegionReductionSeparatesGapsAndOwnsOutput),
        new(nameof(RegionReductionRejectsMaskedCorruptionAndOutputOverflow), RegionReductionRejectsMaskedCorruptionAndOutputOverflow),
        new(nameof(ColumnReductionRetainsNarrowPeaksAndValleys), ColumnReductionRetainsNarrowPeaksAndValleys),
        new(nameof(ColumnReductionSeparatesDiscontinuities), ColumnReductionSeparatesDiscontinuities),
        new(nameof(ColumnReductionKeepsExactExtremaAndColumnEdges), ColumnReductionKeepsExactExtremaAndColumnEdges),
        new(nameof(ColumnReductionValidatesAndOwnsResults), ColumnReductionValidatesAndOwnsResults),
    ];

    private static void ColumnFrameExcludesExclusiveRightEdgeGeometry()
    {
        foreach (int y in new[] { 40, 50 })
        {
            ReconstructedSweepColumnFrame result = new SweepColumnFrameReconstructor(2, 2, 1)
                .Replace(Input(Sample(0, 10, 50), Sample(1, 10, y)));
            Check.That(result.SourceFrame.Segments.Count == 1 && result.Pieces.Count == 0,
                "closed plot-edge vertical or point geometry owns no column beyond the half-open plot");
        }
        SweepFrameReconstructionInput input = Input(Sample(0, 4, 50), Sample(1, 4, 40));
        var machine = SweepStateProjectionStateMachine.Restore(input.Frame.Presentation);
        machine.Advance(40, 0);
        ReconstructedSweepColumnFrame masked = new SweepColumnFrameReconstructor(2, 2, 1).Replace(input with
        {
            Frame = input.Frame with { Presentation = machine.CaptureState() },
        });
        Check.That(masked.Pieces.Count == 0, "source right edge adjoining an erase gap cannot emit a gap-column line");
    }

    private static void ColumnFrameRetainsLeftEdgeAndArrivingSegments()
    {
        SweepPathSample a = Sample(0, 0, 50) with { Point = new(new(0, 1, 5), new(50, 1, VerticalPlotRelation.WithinPlot)) };
        SweepPathSample b = a with { SampleIndex = 1, Point = a.Point with { Y = new(40, 1, VerticalPlotRelation.WithinPlot) } };
        Check.That(new SweepColumnFrameReconstructor(2, 2, 1).Replace(Input(a, b)).Pieces.Single().Piece.ColumnPixels == 0,
            "fractional inclusive left edge still owns its vertical peak");
        SweepFrameReconstructionInput input = Input(Sample(0, 3, 50), Sample(1, 4, 40), Sample(2, 4, 60));
        var machine = SweepStateProjectionStateMachine.Restore(input.Frame.Presentation);
        machine.Advance(40, 0);
        ReconstructedSweepColumnFrame result = new SweepColumnFrameReconstructor(3, 3, 1).Replace(input with
        {
            Frame = input.Frame with { Presentation = machine.CaptureState() },
        });
        Check.That(result.Pieces.Count == 1 && result.Pieces[0].Piece.ColumnPixels == 3 &&
            result.Pieces[0].Piece.Segment.End.X == new ExactPlotCoordinate(4, 1),
            "arriving segment retains its boundary intersection; excluded vertical edge consumes no extra capacity");
    }

    private static void ColumnFrameBoundaryOwnershipRestoresAtCoordinateCeiling()
    {
        SweepFrameReconstructionInput input = Input(Sample(0, int.MaxValue, 50), Sample(1, int.MaxValue, 40));
        input = input with { Frame = input.Frame with { PlotLeftPixels = int.MaxValue - 10 } };
        SweepColumnFrameReconstructor reconstructor = new(2, 2, 1);
        ReconstructedSweepColumnFrame result = reconstructor.Replace(input);
        Check.That(result.Pieces.Count == 0 && SweepColumnFrameReconstructor.Restore(2, 2, 1, result.Checkpoint).Current!.Pieces.Count == 0,
            "exclusive coordinate ceiling is suppressed before subdivision and remains deterministic on restore");
        Check.That(SweepClippedColumnReduction.Reduce(result.Checkpoint, 2, 2, 1, 1).Envelopes.Count == 0,
            "excluded boundary-only geometry cannot return through extrema reduction");
    }

    private static ReducedSweepColumnFrame ReduceClipped(SweepFrameReconstructionInput input, int maximumEnvelopes = 8) =>
        SweepClippedColumnReduction.Reduce(input, 8, 8, 32, maximumEnvelopes);

    private static void ClippedReductionRetainsPeaksAndJoinsOnlyContinuousPieces()
    {
        ReducedSweepColumnFrame result = ReduceClipped(Input(Sample(0, 2, 50), Sample(1, 2, 1), Sample(2, 2, 99), Sample(3, 2, 50)));
        SweepClippedColumnEnvelope envelope = result.Envelopes.Single();
        Check.That(result.Frame.Pieces.Count == 3 && envelope.MinimumY.Y == new ExactPlotCoordinate(1, 1) &&
            envelope.MaximumY.Y == new ExactPlotCoordinate(99, 1) && envelope.FirstEndSampleIndex == 1 && envelope.LastEndSampleIndex == 3 &&
            envelope.First.Y == new ExactPlotCoordinate(50, 1) && envelope.Last.Y == new ExactPlotCoordinate(50, 1),
            "connected vertical column pieces retain both narrow extrema and full sample extent");
    }

    private static void ClippedReductionRetainsCrossingIntersections()
    {
        SweepFrameReconstructionInput input = Input(Sample(0, 0, 0), Sample(1, 1, 100));
        ReducedSweepColumnFrame result = ReduceClipped(input);
        SweepClippedColumnEnvelope envelope = result.Envelopes.Single();
        Check.That(envelope.First.X == new ExactPlotCoordinate(1, 5) && envelope.MinimumY.Y == new ExactPlotCoordinate(20, 1) &&
            envelope.MaximumY.Y == new ExactPlotCoordinate(100, 1),
            "erase-gap clipping contributes its interpolated intersection rather than the hidden source endpoint");
        Check.That(envelope.RegionIndex == result.Frame.SourceFrame.Segments[0].RegionIndex,
            "summaries retain exact source-region provenance for downstream masking");
    }

    private static void ClippedReductionKeepsGapsSeparate()
    {
        ReducedSweepColumnFrame result = ReduceClipped(Input(Sample(0, 2, 50), Sample(1, 2, 40),
            Sample(3, 2, 40), Sample(4, 2, 60)));
        Check.That(result.Envelopes.Count == 2 && result.Envelopes[0].Last == result.Envelopes[1].First,
            "coincident geometric endpoints cannot bridge a missing source sample");
        SweepPathSample first = Sample(0, 2, 50), second = Sample(1, 2, 40);
        SweepSampleSource changed = first.Source with { StreamEpoch = 9 };
        result = ReduceClipped(Input(first, second, Sample(0, 2, 40) with { Source = changed }, Sample(1, 2, 60) with { Source = changed }));
        Check.That(result.Envelopes.Count == 2 && result.Envelopes[0].Source != result.Envelopes[1].Source,
            "source changes remain separate even when columns and endpoints match");
    }

    private static void ClippedReductionRestoresAndRejectsWithoutTruncation()
    {
        SweepFrameReconstructionInput input = Input(Sample(0, 2, 50), Sample(1, 4, 40));
        ReducedSweepColumnFrame accepted = ReduceClipped(input);
        Check.That(accepted.Envelopes.SequenceEqual(ReduceClipped(accepted.Frame.Checkpoint).Envelopes),
            "owned checkpoint recomputation preserves clipped extrema and intersection provenance");
        try { _ = ReduceClipped(input, 1); throw new InvalidOperationException("truncated envelopes accepted"); }
        catch (SweepFrameReconstructionException exception)
        { Check.That(exception.ReasonCode == "ClippedEnvelope.OutputLimitExceeded", "summary capacity rejects rather than truncates"); }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try { _ = SweepClippedColumnReduction.Reduce(input, 8, 8, 32, 8, cancellation.Token); throw new InvalidOperationException("cancelled summary accepted"); }
        catch (OperationCanceledException exception) { Check.That(exception.CancellationToken == cancellation.Token, "cancellation retains identity"); }
        Check.That(accepted.Envelopes.Count == 2, "later failed operations leave accepted results untouched");
    }

    private static void RegionReductionMasksBeforeSelectingExtrema()
    {
        SweepPathSample hidden = Sample(0, 0, 1) with { Point = new(new(0, 1, 10), new(1, 1, VerticalPlotRelation.WithinPlot)) };
        SweepPathSample edge = Sample(1, 0, 50) with { Point = new(new(0, 2, 10), new(50, 1, VerticalPlotRelation.WithinPlot)) };
        SweepPathSample visible = Sample(2, 0, 60) with { Point = new(new(0, 3, 10), new(60, 1, VerticalPlotRelation.WithinPlot)) };
        IReadOnlyList<SweepRegionColumnEnvelope> result = SweepRegionColumnEnvelopeReduction.Reduce(Input(hidden, edge, visible, Sample(3, 10, 99)), 4, 4);
        Check.That(result.Count == 1 && result[0].Envelope.First == edge && result[0].Envelope.MinimumY == edge &&
            result[0].Envelope.MaximumY == visible,
            "subpixel erase-gap spike cannot contaminate the same column's source extrema; exact left edge is included and plot right excluded");
    }

    private static void RegionReductionPreservesPinnedHistoryDuringNoData()
    {
        SweepFrameReconstructionInput input = Input(Sample(0, 2, 50), Sample(1, 2, 40));
        var machine = SweepStateProjectionStateMachine.Restore(input.Frame.Presentation);
        machine.SynchronizeContinuity(DataContinuityStateMachine.Restore(machine.CaptureState().ContinuityState).Disconnect(false, 1), 0, 0);
        machine.Advance(100, 0);
        SweepFrameReconstructionInput live = input with { Frame = input.Frame with { Presentation = machine.CaptureState() } };
        Check.That(SweepRegionColumnEnvelopeReduction.Reduce(live, 2, 2).Count == 0, "full NoData coverage yields no source extrema");
        machine.EnterFrozen(100, 0);
        SweepFrameReconstructionInput pinned = input with { Frame = input.Frame with { Presentation = machine.CaptureState() } };
        Check.That(SweepRegionColumnEnvelopeReduction.Reduce(pinned, 2, 2).Single().Envelope.MinimumY == input.Samples[1],
            "pinned original data remains independent of Live NoData masking");
    }

    private static void RegionReductionSeparatesGapsAndOwnsOutput()
    {
        SweepPathSample[] samples = [Sample(0, 2, 50), Sample(2, 2, 40)];
        SweepFrameReconstructionInput input = Input(samples);
        IReadOnlyList<SweepRegionColumnEnvelope> result = SweepRegionColumnEnvelopeReduction.Reduce(input, 2, 2);
        Check.That(result.Count == 2 && result.SequenceEqual(SweepRegionColumnEnvelopeReduction.Reduce(input with
        {
            Frame = SweepFramePathBuilder.Restore(input.Frame).CaptureState(),
        }, 2, 2)), "masked reduction preserves source gaps and deterministic checkpoint recomputation");
        samples[1] = Sample(9, 2, 1);
        Check.That(result[1].Envelope.First.SampleIndex == 2, "caller array changes cannot alter returned provenance");
    }

    private static void RegionReductionRejectsMaskedCorruptionAndOutputOverflow()
    {
        try
        {
            _ = SweepRegionColumnEnvelopeReduction.Reduce(Input(Sample(0, 0, 50), Sample(0, 0, 40)), 2, 2);
            throw new InvalidOperationException("masked corruption accepted");
        }
        catch (SweepPathException exception) { Check.That(exception.ReasonCode == "SweepPath.FrontierReversed", "masking never bypasses evidence validation"); }
        try
        {
            _ = SweepRegionColumnEnvelopeReduction.Reduce(Input(Sample(0, 2, 50), Sample(1, 3, 40)), 2, 1);
            throw new InvalidOperationException("output overflow accepted");
        }
        catch (SweepFrameReconstructionException exception) { Check.That(exception.ReasonCode == "RegionEnvelope.OutputLimitExceeded", "output bound rejects rather than truncating extrema"); }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try { _ = SweepRegionColumnEnvelopeReduction.Reduce(Input(), 1, 1, cancellation.Token); throw new InvalidOperationException("cancelled masking accepted"); }
        catch (OperationCanceledException exception) { Check.That(exception.CancellationToken == cancellation.Token, "masking propagates cancellation without partial output"); }
    }

    private static SweepFrameReconstructionInput Input(params SweepPathSample[] samples) => new(new(
        SweepStateProjectionStateMachine.Start(new("ecg", 4, 5, 0, 100, 2, 102), 1, 1, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.Disabled, 0).CaptureState(), 0, 0).CaptureState(),
        0, 10, 0, 100, null), samples);

    private static SweepPathSample Sample(ulong index, int x, int y) => new(new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"), Guid.Parse("22222222-2222-4222-8222-222222222222"),
        Guid.Parse("33333333-3333-4333-8333-333333333333"), 1, 2, 3, 4, 5, 500, 1), index, 0, true,
        new(new(x, 0, 1), new(y, 1, VerticalPlotRelation.WithinPlot)));

    private static void ColumnReductionRetainsNarrowPeaksAndValleys()
    {
        SweepPathSample[] samples = [Sample(0, 2, 50), Sample(1, 2, 1), Sample(2, 2, 99), Sample(3, 2, 50)];
        SweepColumnEnvelope result = SweepColumnEnvelopeReduction.Reduce(Input(samples), 4).Single();
        Check.That(result.First == samples[0] && result.Last == samples[3] && result.MinimumY == samples[1] && result.MaximumY == samples[2],
            "single-sample peak and valley survive with their original provenance and endpoints");
        SweepFrameReconstructionInput input = Input(samples);
        Check.That(SweepColumnEnvelopeReduction.Reduce(input with
        {
            Frame = SweepFramePathBuilder.Restore(input.Frame).CaptureState(),
        }, 4).Single() == result, "restored frame evidence regenerates the same extrema summary");
    }

    private static void ColumnReductionSeparatesDiscontinuities()
    {
        SweepPathSample first = Sample(0, 2, 50);
        foreach (SweepPathSample next in new[]
        {
            Sample(2, 2, 40), Sample(1, 2, 40) with { CycleIndex = 1 },
            Sample(1, 2, 40) with { Source = first.Source with { StreamEpoch = 9 } },
        })
        {
            Check.That(SweepColumnEnvelopeReduction.Reduce(Input(first, next), 2).Count == 2,
                "gap, cycle and source changes remain separate even within one pixel column");
        }
        Check.That(SweepColumnEnvelopeReduction.Reduce(Input(first, Sample(1, 2, 0) with { Drawable = false }, Sample(2, 2, 40)), 3).Count == 2,
            "non-drawable data splits runs and never contributes extrema");
    }

    private static void ColumnReductionKeepsExactExtremaAndColumnEdges()
    {
        SweepPathSample first = Sample(0, 2, 1) with { Point = new(new(2, 9, 10), new(1, 3, VerticalPlotRelation.WithinPlot)) };
        SweepPathSample second = Sample(1, 2, 1) with { Point = new(new(2, 9, 10), new(1, 2, VerticalPlotRelation.WithinPlot)) };
        IReadOnlyList<SweepColumnEnvelope> result = SweepColumnEnvelopeReduction.Reduce(Input(first, second, Sample(2, 3, 1)), 3);
        Check.That(result.Count == 2 && result[0].MinimumY == first && result[0].MaximumY == second && result[1].ColumnPixels == 3,
            "rational extrema compare exactly and an integer X begins the next column");
        SweepPathSample tied = first with { SampleIndex = 1 };
        Check.That(SweepColumnEnvelopeReduction.Reduce(Input(first, tied), 2)[0].MinimumY == first,
            "equal extrema deterministically retain the first source sample");
    }

    private static void ColumnReductionValidatesAndOwnsResults()
    {
        SweepPathSample[] samples = [Sample(0, 2, 50), Sample(1, 2, 40)];
        SweepFrameReconstructionInput input = Input(samples);
        IReadOnlyList<SweepColumnEnvelope> result = SweepColumnEnvelopeReduction.Reduce(input, 2);
        samples[1] = Sample(9, 2, 99);
        Check.That(result[0].Last.SampleIndex == 1 && SweepColumnEnvelopeReduction.Reduce(Input(), 1).Count == 0,
            "returned summaries own immutable sample references and empty input invents no signal");
        try
        {
            _ = SweepColumnEnvelopeReduction.Reduce(Input(Sample(0, 2, 50), Sample(0, 2, 40)), 2);
            throw new InvalidOperationException("reversed frontier accepted");
        }
        catch (SweepPathException exception) { Check.That(exception.ReasonCode == "SweepPath.FrontierReversed", "all source paths are validated"); }
        try
        {
            _ = SweepColumnEnvelopeReduction.Reduce(input, 1);
            throw new InvalidOperationException("capacity exceeded");
        }
        catch (SweepFrameReconstructionException exception) { Check.That(exception.ReasonCode == "ColumnEnvelope.InvalidInput", "capacity rejects explicitly"); }
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        try { _ = SweepColumnEnvelopeReduction.Reduce(input, 2, cancellation.Token); throw new InvalidOperationException("cancelled reduction accepted"); }
        catch (OperationCanceledException exception) { Check.That(exception.CancellationToken == cancellation.Token, "cancellation preserves token identity"); }
        Check.That(result[0].Last.SampleIndex == 1, "later failures never alter an accepted summary");
    }
}
