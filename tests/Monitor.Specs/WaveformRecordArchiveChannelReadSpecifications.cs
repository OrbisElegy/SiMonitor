// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static partial class WaveformRecordArchiveSpecifications
{
    private static void ChannelReadClipsSamplesAndQualityAcrossBlocks()
    {
        byte[] QualityWire(ulong sequence, long start)
        {
            WaveformEnvelope envelope = WaveformEnvelopeCodec.Decode(Wire(sequence, start));
            return WaveformEnvelopeCodec.EncodeRaw(envelope with
            {
                Planes = envelope.Planes.Select(plane => plane with
                {
                    ScaleNumerator = -3,
                    ScaleDenominator = 7,
                    OffsetNumerator = 5,
                    OffsetDenominator = 11,
                    QualityEncoding = WaveformQualityEncoding.Ranges,
                    QualityRanges = new WaveformQualityRange[] { new(0, 2, 0x80000000), new(98, 2, 7) },
                }).ToArray(),
            });
        }
        var archive = WaveformRecordArchive.Create(
            Plan(endExclusiveSimTimeNs: 400_000_000), [QualityWire(100, 0), QualityWire(101, 200_000_000)]);
        ArchivedWaveformChannelRead read = archive.ReadChannel(ChannelIds[1], 198_000_000, 202_000_000, 2);
        WaveformPlane first = read.Blocks[0].Plane;
        WaveformPlane second = read.Blocks[1].Plane;
        Check.That(read.Blocks.Count == 2 && first.FirstSampleIndex == 99 && second.FirstSampleIndex == 100 &&
            first.Samples.SequenceEqual(new short[] { 101 }) && second.Samples.SequenceEqual(new short[] { 102 }) &&
            first.QualityRanges.SequenceEqual(new WaveformQualityRange[] { new(0, 1, 7) }) &&
            second.QualityRanges.SequenceEqual(new WaveformQualityRange[] { new(0, 1, 0x80000000) }) &&
            first.ScaleNumerator == -3 && first.ScaleDenominator == 7 &&
            first.OffsetNumerator == 5 && first.OffsetDenominator == 11 &&
            read.Blocks[1].OriginalStartSimTimeNs == 200_000_000 &&
            read.Blocks[0].OriginalContentSha256 == archive.ReadBlocks()[0].ContentSha256,
            "trimmed planes must retain exact samples, opaque flags, scale and original block provenance");
    }

    private static void ChannelReadUsesExactHalfOpenSampleTimes()
    {
        WaveformEnvelope fractional = WaveformEnvelopeCodec.Decode(Wire(100, 0, sampleRateNumerator: 15));
        byte[] fractionalWire = WaveformEnvelopeCodec.EncodeRaw(fractional with
        {
            Planes = fractional.Planes.Select(plane => plane with
            { SampleRateNumerator = 30, SampleRateDenominator = 2 }).ToArray(),
        });
        var archive = WaveformRecordArchive.Create(
            Plan(startSimTimeNs: 1, endExclusiveSimTimeNs: 199_999_999),
            [fractionalWire]);
        ArchivedWaveformChannelRead before = archive.ReadChannel(ChannelIds[0], 66_666_666, 66_666_667, 1);
        Check.That(before.Blocks.Single().Plane.FirstSampleIndex == 1 &&
            before.Blocks.Single().Plane.Samples.Count == 1 &&
            archive.ReadChannel(ChannelIds[0], 1, 66_666_666, 1).Blocks.Count == 0 &&
            archive.ReadChannel(ChannelIds[0], 66_666_667, 133_333_333, 1).Blocks.Count == 0,
            "fractional nanosecond sample times must be compared exactly without floor rounding or padding");
        var ordinary = WaveformRecordArchive.Create(
            Plan(endExclusiveSimTimeNs: 200_000_000), [Wire(100, 0)]);
        Check.That(ordinary.ReadChannel(ChannelIds[0], 0, 2_000_000, 1).Blocks.Single().Plane.FirstSampleIndex == 0 &&
            ordinary.ReadChannel(ChannelIds[0], 2_000_000, 4_000_000, 1).Blocks.Single().Plane.FirstSampleIndex == 1,
            "a sample at a shared page boundary must occur only in the next page");
        const long anchor = 1_000_000_000;
        var shifted = WaveformRecordArchive.Create(
            Plan(startSimTimeNs: anchor, endExclusiveSimTimeNs: anchor + 200_000_000) with
            { EpochAnchorSimTimeNs = anchor },
            [WaveformEnvelopeCodec.EncodeRaw(fractional with { StartSimTimeNs = anchor })]);
        Check.That(shifted.ReadChannel(ChannelIds[0], anchor + 66_666_666, anchor + 66_666_667, 1)
            .Blocks.Single().Plane.FirstSampleIndex == 1,
            "source time must use the recorded epoch anchor rather than treating it as zero");
    }

    private static void ChannelReadRejectsWithoutChangingArchive()
    {
        var archive = WaveformRecordArchive.Create(
            Plan(endExclusiveSimTimeNs: 400_000_000), [Wire(100, 0), Wire(101, 200_000_000)]);
        IReadOnlyList<ArchivedWaveformBlock> before = archive.ReadBlocks();
        Check.That(Reason(() => archive.ReadChannel(Guid.Empty, 0, 1, 1)) == "WaveformRecordArchive.UnknownChannel" &&
            Reason(() => archive.ReadChannel(ChannelIds[0], -1, 1, 1)) == "WaveformRecordArchive.InvalidReadRange" &&
            Reason(() => archive.ReadChannel(ChannelIds[0], 1, 1, 1)) == "WaveformRecordArchive.InvalidReadRange" &&
            Reason(() => archive.ReadChannel(ChannelIds[0], 0, 400_000_001, 1)) == "WaveformRecordArchive.InvalidReadRange" &&
            Reason(() => archive.ReadChannel(ChannelIds[0], 0, 1, 0)) == "WaveformRecordArchive.InvalidSampleLimit" &&
            Reason(() => archive.ReadChannel(ChannelIds[0], 0, 400_000_000, 199)) == "WaveformRecordArchive.SampleLimitExceeded",
            "invalid and late over-budget reads must fail closed");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool cancelled = false;
        try { archive.ReadChannel(ChannelIds[0], 0, 400_000_000, 200, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Check.That(cancelled && Equivalent(before, archive.ReadBlocks()) &&
            archive.ReadChannel(ChannelIds[0], 0, 400_000_000, 200).Blocks.Sum(block => block.Plane.Samples.Count) == 200,
            "failure and cancellation must preserve the complete archive and allow an exact-limit retry");
    }

    private static void ChannelReadRestoresAndOwnsOutput()
    {
        var archive = WaveformRecordArchive.Create(
            Plan(endExclusiveSimTimeNs: 200_000_000), [Wire(100, 0)]);
        WaveformRecordArchiveState state = archive.CaptureState();
        var restored = WaveformRecordArchive.Restore(state);
        ArchivedWaveformChannelRead read = restored.ReadChannel(ChannelIds[0], 0, 200_000_000, 100);
        state.RawEnvelopes[0][0] ^= 0xff;
        bool immutable = false;
        try { ((IList<short>)read.Blocks[0].Plane.Samples)[0] = -1; }
        catch (NotSupportedException) { immutable = true; }
        Check.That(immutable && read.Blocks[0].Plane.Samples[0] == 100 &&
            read.ArchivePlan == restored.CapturePlan() with { ChannelIds = read.ArchivePlan.ChannelIds } &&
            read.Blocks[0].Plane.Samples.SequenceEqual(archive.ReadChannel(ChannelIds[0], 0, 200_000_000, 100).Blocks[0].Plane.Samples) &&
            Reason(() => WaveformRecordArchive.Restore(state)) == "WaveformRecordArchive.InvalidCheckpoint",
            "reads must own immutable samples and reproduce from revalidated checkpoints without trusting corrupt wire");
    }
}
