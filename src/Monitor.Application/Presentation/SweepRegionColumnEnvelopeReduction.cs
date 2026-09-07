// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed record SweepRegionColumnEnvelope(int RegionIndex, SweepColumnEnvelope Envelope);

public static class SweepRegionColumnEnvelopeReduction
{
    public static IReadOnlyList<SweepRegionColumnEnvelope> Reduce(SweepFrameReconstructionInput input,
        int maximumSamples, int maximumEnvelopes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(input);
        int count = input.Samples?.Count ?? -1;
        if (maximumSamples <= 0 || maximumEnvelopes <= 0 || count < 0 || count > maximumSamples)
        {
            throw new SweepFrameReconstructionException("RegionEnvelope.InvalidInput", nameof(input));
        }
        var samples = new SweepPathSample[count];
        for (int index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            samples[index] = input.Samples![index];
        }
        SweepFrameReconstructionInput owned = input with { Samples = Array.AsReadOnly(samples) };
        // Validate all evidence, including samples that a mask will suppress.
        _ = SweepColumnEnvelopeReduction.Reduce(owned, maximumSamples, cancellationToken);
        SweepPlotGeometrySnapshot geometry = SweepFramePathBuilder.Restore(owned.Frame).Geometry;
        List<SweepRegionColumnEnvelope> output = [];
        for (int regionIndex = 0; regionIndex < geometry.Regions.Count; regionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SweepPlotRegion region = geometry.Regions[regionIndex];
            if (region.Kind is SweepTraceRegionKind.NoDataBaseline or SweepTraceRegionKind.BackgroundEraseGap) { continue; }
            List<SweepPathSample> selected = [];
            foreach (SweepPathSample sample in samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Compare(sample.Point.X, region.StartX) >= 0 && Compare(sample.Point.X, region.EndExclusiveX) < 0)
                { selected.Add(sample); }
            }
            foreach (SweepColumnEnvelope envelope in SweepColumnEnvelopeReduction.Reduce(
                owned with { Samples = selected.AsReadOnly() }, maximumSamples, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (output.Count == maximumEnvelopes)
                {
                    throw new SweepFrameReconstructionException("RegionEnvelope.OutputLimitExceeded", nameof(maximumEnvelopes));
                }
                output.Add(new(regionIndex, envelope));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly(output.ToArray());
    }

    private static int Compare(SweepPixelPosition left, SweepPixelPosition right) =>
        (((BigInteger)left.WholePixels * left.FractionDenominator + left.FractionNumerator) * right.FractionDenominator).CompareTo(
            ((BigInteger)right.WholePixels * right.FractionDenominator + right.FractionNumerator) * left.FractionDenominator);
}
