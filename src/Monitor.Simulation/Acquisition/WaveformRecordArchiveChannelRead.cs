// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Acquisition;

// The hash identifies the original complete block, not the trimmed plane.
public sealed record ArchivedWaveformPlaneBlock(
    ulong BlockSequence,
    long OriginalStartSimTimeNs,
    string OriginalContentSha256,
    WaveformPlane Plane);

public sealed record ArchivedWaveformChannelRead(
    WaveformRecordArchivePlan ArchivePlan,
    Guid ChannelId,
    long StartSimTimeNs,
    long EndExclusiveSimTimeNs,
    IReadOnlyList<ArchivedWaveformPlaneBlock> Blocks);

public sealed partial class WaveformRecordArchive
{
    // Local raw-data projection only: no unit inference, quality interpretation,
    // interpolation, predecessor padding, or permission to display the result.
    public ArchivedWaveformChannelRead ReadChannel(
        Guid channelId,
        long startSimTimeNs,
        long endExclusiveSimTimeNs,
        int maximumSamples,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_plan.ChannelIds.Contains(channelId))
        { throw Error("WaveformRecordArchive.UnknownChannel", nameof(channelId)); }
        if (startSimTimeNs < RecordStartSimTimeNs ||
            endExclusiveSimTimeNs > RecordEndExclusiveSimTimeNs ||
            startSimTimeNs >= endExclusiveSimTimeNs)
        { throw Error("WaveformRecordArchive.InvalidReadRange", nameof(startSimTimeNs)); }
        if (maximumSamples <= 0)
        { throw Error("WaveformRecordArchive.InvalidSampleLimit", nameof(maximumSamples)); }

        List<ArchivedWaveformPlaneBlock> result = [];
        int total = 0;
        foreach (StoredBlock block in _blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (block.StartSimTimeNs >= endExclusiveSimTimeNs) { break; }
            if (block.StartSimTimeNs + WaveformBlockAssembler.BlockDurationNs <= startSimTimeNs) { continue; }
            WaveformPlane plane = WaveformEnvelopeCodec.Decode(block.RawEnvelope).Planes
                .Single(candidate => candidate.ChannelId == channelId);
            int first = OffsetAtOrAfter(startSimTimeNs, block.StartSimTimeNs, plane);
            int end = OffsetAtOrAfter(endExclusiveSimTimeNs, block.StartSimTimeNs, plane);
            int count = end - first;
            if (count == 0) { continue; }
            if (count > maximumSamples - total)
            { throw Error("WaveformRecordArchive.SampleLimitExceeded", nameof(maximumSamples)); }
            total += count;
            short[] samples = new short[count];
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                samples[index] = plane.Samples[first + index];
            }
            List<WaveformQualityRange> ranges = [];
            foreach (WaveformQualityRange range in plane.QualityRanges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long left = Math.Max(first, range.FirstSampleOffset);
                long right = Math.Min(end, (long)range.FirstSampleOffset + range.Count);
                if (left < right)
                { ranges.Add(new(checked((uint)(left - first)), checked((uint)(right - left)), range.QualityFlags)); }
            }
            WaveformPlane trimmed = plane with
            {
                FirstSampleIndex = checked(plane.FirstSampleIndex + (uint)first),
                Samples = Array.AsReadOnly(samples),
                QualityRanges = ranges.AsReadOnly(),
            };
            result.Add(new(block.BlockSequence, block.StartSimTimeNs, block.ContentSha256, trimmed));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(CapturePlan(), channelId, startSimTimeNs, endExclusiveSimTimeNs, result.AsReadOnly());
    }

    private static int OffsetAtOrAfter(long time, long blockStart, WaveformPlane plane)
    {
        if (time <= blockStart) { return 0; }
        Int128 numerator = (Int128)(time - blockStart) * plane.SampleRateNumerator;
        Int128 denominator = (Int128)plane.SampleRateDenominator * 1_000_000_000;
        Int128 ceiling = (numerator + denominator - 1) / denominator;
        return ceiling >= plane.Samples.Count ? plane.Samples.Count : (int)ceiling;
    }
}
