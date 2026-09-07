// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;

namespace Monitor.Specs;

internal static class SweepColumnEnvelopeSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ColumnReductionRetainsNarrowPeaksAndValleys), ColumnReductionRetainsNarrowPeaksAndValleys),
        new(nameof(ColumnReductionSeparatesDiscontinuities), ColumnReductionSeparatesDiscontinuities),
        new(nameof(ColumnReductionKeepsExactExtremaAndColumnEdges), ColumnReductionKeepsExactExtremaAndColumnEdges),
        new(nameof(ColumnReductionValidatesAndOwnsResults), ColumnReductionValidatesAndOwnsResults),
    ];

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
