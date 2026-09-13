// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using Monitor.Domain.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Application.Presentation;

public sealed record CapturedRecordWaveformHorizontalBlock(ArchivedWaveformPlaneBlock Source,
    IReadOnlyList<ExactPlotCoordinate> SampleX);

public sealed record CapturedRecordWaveformHorizontalPageDisplay(CapturedRecordWaveformPageDisplay Content,
    IReadOnlyList<CapturedRecordWaveformHorizontalBlock> Blocks);

// Internal composition consumes only the fresh, bounded archive read produced by
// the study view. Public records are output models, not accepted evidence inputs.
internal static class CapturedRecordWaveformHorizontalProjection
{
    internal static CapturedRecordWaveformHorizontalPageDisplay Build(
        CapturedRecordWaveformPageDisplay content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content.Waveform is not { } waveform)
        { return new(content, Array.Empty<CapturedRecordWaveformHorizontalBlock>()); }
        RecordCursorViewport viewport = content.Content.Viewport!;
        long duration = viewport.EndExclusiveDataTimeNs - viewport.StartDataTimeNs;
        List<CapturedRecordWaveformHorizontalBlock> blocks = [];
        foreach (ArchivedWaveformPlaneBlock block in waveform.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaveformPlane plane = block.Plane;
            var positions = new ExactPlotCoordinate[plane.Samples.Count];
            BigInteger denominator = (BigInteger)duration * plane.SampleRateNumerator;
            BigInteger origin = ((BigInteger)waveform.ArchivePlan.EpochAnchorSimTimeNs -
                viewport.StartDataTimeNs) * plane.SampleRateNumerator;
            BigInteger period = (BigInteger)plane.SampleRateDenominator * 1_000_000_000;
            for (int index = 0; index < positions.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BigInteger offset = origin + ((BigInteger)plane.FirstSampleIndex + index) * period;
                // Archive selection proves 0 <= offset < duration * rate numerator.
                BigInteger numerator = viewport.PlotLeftPixels * denominator + offset * viewport.PlotWidthPixels;
                var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
                positions[index] = new(numerator / divisor, denominator / divisor);
            }
            blocks.Add(new(block, Array.AsReadOnly(positions)));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(content, blocks.AsReadOnly());
    }
}
