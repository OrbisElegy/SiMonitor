// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

// MSR1 transports a pinned recording. Only acquisition coordinates, channel order and MWS1
// blocks cross the boundary. Local case names, answers, annotations and authoring IDs stay private.
internal static class ClassroomArtifactRecord
{
    private const int HeaderBytes = 4 + 8 * 4 + 16 * WaveformRecordArchive.StandardEcgChannelCount;

    internal static byte[] Encode(WaveformRecordArchive record)
    {
        WaveformRecordArchiveState state = record.CaptureState();
        WaveformRecordArchivePlan plan = state.Plan;
        var releasedChannels = plan.ChannelIds.ToHashSet();
        byte[][] released = state.RawEnvelopes.Select(raw =>
        {
            WaveformEnvelope envelope = WaveformEnvelopeCodec.Decode(raw);
            return envelope.Planes.Count == releasedChannels.Count ? raw :
                WaveformEnvelopeCodec.EncodeRaw(envelope with
                {
                    Planes = envelope.Planes.Where(plane => releasedChannels.Contains(plane.ChannelId)).ToArray()
                });
        }).ToArray();
        int size = HeaderBytes + released.Sum(block => 4 + block.Length);
        if (size > ClassroomArtifactDescriptor.MaximumLengthBytes)
        {
            throw new ArgumentException("The study record exceeds the classroom transfer limit.", nameof(record));
        }
        // The first or last MWS1 block may contain samples outside a cropped record. Such a
        // crop needs a separately released recording before it may be sent to a learner.
        if (WaveformEnvelopeCodec.Decode(state.RawEnvelopes[0]).StartSimTimeNs != plan.RecordStartSimTimeNs ||
            WaveformEnvelopeCodec.Decode(state.RawEnvelopes[^1]).StartSimTimeNs != plan.RecordEndExclusiveSimTimeNs - WaveformBlockAssembler.BlockDurationNs)
        {
            throw new ArgumentException("Release a study record aligned to complete waveform blocks.", nameof(record));
        }
        byte[] encoded = new byte[size];
        "MSR1"u8.CopyTo(encoded);
        BinaryPrimitives.WriteUInt64LittleEndian(encoded.AsSpan(4), plan.SweepEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(encoded.AsSpan(12), plan.EpochAnchorSimTimeNs);
        BinaryPrimitives.WriteInt64LittleEndian(encoded.AsSpan(20), plan.RecordStartSimTimeNs);
        BinaryPrimitives.WriteInt64LittleEndian(encoded.AsSpan(28), plan.RecordEndExclusiveSimTimeNs);
        for (int i = 0; i < plan.ChannelIds.Count; i++)
        {
            plan.ChannelIds[i].TryWriteBytes(encoded.AsSpan(36 + 16 * i), bigEndian: true, out _);
        }
        int offset = HeaderBytes;
        foreach (byte[] block in released)
        {
            BinaryPrimitives.WriteInt32LittleEndian(encoded.AsSpan(offset), block.Length);
            block.CopyTo(encoded, offset + 4);
            offset += 4 + block.Length;
        }
        return encoded;
    }

    internal static WaveformRecordArchive Decode(ReadOnlySpan<byte> bytes, ClassroomArtifactDescriptor descriptor,
        Guid sessionId, Guid instanceId)
    {
        if (bytes.Length < HeaderBytes || !bytes[..4].SequenceEqual("MSR1"u8))
        {
            throw new InvalidDataException("Invalid study record header.");
        }
        ulong sweepEpoch = BinaryPrimitives.ReadUInt64LittleEndian(bytes[4..]);
        long anchor = BinaryPrimitives.ReadInt64LittleEndian(bytes[12..]);
        long start = BinaryPrimitives.ReadInt64LittleEndian(bytes[20..]);
        long end = BinaryPrimitives.ReadInt64LittleEndian(bytes[28..]);
        var channels = new Guid[WaveformRecordArchive.StandardEcgChannelCount];
        for (int i = 0; i < channels.Length; i++) { channels[i] = new Guid(bytes.Slice(36 + 16 * i, 16), bigEndian: true); }

        List<byte[]> blocks = [];
        int offset = HeaderBytes;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 4 || blocks.Count == WaveformRecordArchive.MaximumRecordBlockCount)
            {
                throw new InvalidDataException("Invalid study record block count or length.");
            }
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);
            offset += 4;
            if (size < WaveformEnvelopeCodec.PreludeSize || size > bytes.Length - offset ||
                size > WaveformEnvelopeCodec.PreludeSize + WaveformEnvelopeCodec.MaximumHeaderSize + WaveformEnvelopeCodec.MaximumPayloadSize)
            {
                throw new InvalidDataException("Invalid study record block size.");
            }
            byte[] raw = bytes.Slice(offset, size).ToArray();
            if (WaveformEnvelopeCodec.Decode(raw).Planes.Count != channels.Length)
            {
                throw new InvalidDataException("A released record may contain only its twelve declared channels.");
            }
            blocks.Add(raw);
            offset += size;
        }
        if (blocks.Count == 0) { throw new InvalidDataException("The study record is empty."); }
        WaveformEnvelope first = WaveformEnvelopeCodec.Decode(blocks[0]);
        WaveformEnvelope last = WaveformEnvelopeCodec.Decode(blocks[^1]);
        if (first.SessionId != sessionId || first.InstanceId != instanceId || start != first.StartSimTimeNs ||
            end < WaveformBlockAssembler.BlockDurationNs || end - WaveformBlockAssembler.BlockDurationNs != last.StartSimTimeNs)
        {
            throw new InvalidDataException("The record coordinates do not match the classroom.");
        }
        var plan = new WaveformRecordArchivePlan("ClassroomStudy", "record:" + descriptor.Sha256, sweepEpoch,
            sessionId, instanceId, first.TimebaseEpoch, anchor, first.StreamEpoch, first.ConfigurationRevision, start, end, channels);
        return WaveformRecordArchive.Create(plan, blocks);
    }
}
