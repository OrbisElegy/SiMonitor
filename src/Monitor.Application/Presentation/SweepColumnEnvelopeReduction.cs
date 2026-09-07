// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

// Source evidence summary, not a drawable polyline: region masking and clipping
// must still apply before rasterization. Min/Max refer to screen Y, not voltage.
public sealed record SweepColumnEnvelope(int ColumnPixels,
    SweepPathSample First, SweepPathSample Last, SweepPathSample MinimumY, SweepPathSample MaximumY);

public static class SweepColumnEnvelopeReduction
{
    public static IReadOnlyList<SweepColumnEnvelope> Reduce(SweepFrameReconstructionInput input,
        int maximumSamples, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(input);
        int count = input.Samples?.Count ?? -1;
        if (maximumSamples <= 0 || count < 0 || count > maximumSamples ||
            input.Frame is null || input.Frame.Previous is not null)
        {
            throw new SweepFrameReconstructionException("ColumnEnvelope.InvalidInput", nameof(input));
        }
        var validator = SweepFramePathBuilder.Restore(input.Frame);
        List<SweepColumnEnvelope> output = [];
        SweepPathSample? first = null, last = null, minimum = null, maximum = null;
        int column = 0;
        void Flush()
        {
            if (first is not null) { output.Add(new(column, first, last!, minimum!, maximum!)); }
            first = last = minimum = maximum = null;
        }
        for (int index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SweepPathSample sample = input.Samples![index];
            _ = validator.Append(sample, cancellationToken);
            if (!sample.Drawable) { Flush(); continue; }
            bool adjacent = last is not null && last.Source == sample.Source && last.CycleIndex == sample.CycleIndex &&
                last.SampleIndex < sample.SampleIndex && sample.SampleIndex - last.SampleIndex == 1;
            if (first is not null && (!adjacent || sample.Point.X.WholePixels != column)) { Flush(); }
            if (first is null)
            {
                column = sample.Point.X.WholePixels;
                first = minimum = maximum = sample;
            }
            else
            {
                if (Compare(sample.Point.Y, minimum!.Point.Y) < 0) { minimum = sample; }
                if (Compare(sample.Point.Y, maximum!.Point.Y) > 0) { maximum = sample; }
            }
            last = sample;
        }
        Flush();
        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly(output.ToArray());
    }

    private static int Compare(EcgVerticalPosition left, EcgVerticalPosition right) =>
        ((BigInteger)left.PixelNumerator * (BigInteger)right.PixelDenominator).CompareTo(
            (BigInteger)right.PixelNumerator * (BigInteger)left.PixelDenominator);
}
