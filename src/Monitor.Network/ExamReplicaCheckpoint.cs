// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Network;

// The primary sends generator state and the next block's digest. Each restoring
// node generates that block independently; the journal never contains its samples.
public static class ExamReplicaCheckpoint
{
    private sealed record Image(string Format, PhysiologyWaveformGroupState Patient, string NextBlockSha256);
    private const string CurrentFormat = "ExamWaveformCheckpoint@1";
    private static readonly JsonSerializerOptions Options = new(WaveformCheckpointJson.Options)
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 48
    };

    public static byte[] Capture(PhysiologyWaveformGroup source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var state = source.CaptureState();
        ValidateBoundary(state);
        var checkpoint = new Image(CurrentFormat, state, ReplayDigest(source.Fork(), state));
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(checkpoint, Options);
        if (payload.Length > ExamReplicaEntry.MaxPayloadBytes) { throw new InvalidDataException("Exam checkpoint exceeds the replication limit."); }
        return payload;
    }

    public static PhysiologyWaveformGroup Restore(byte[] payload)
    {
        if (payload is not { Length: > 0 and <= ExamReplicaEntry.MaxPayloadBytes })
        { throw new InvalidDataException("Invalid exam checkpoint length."); }
        try
        {
            var checkpoint = JsonSerializer.Deserialize<Image>(payload, Options);
            if (checkpoint?.Format != CurrentFormat || !ClassroomArtifactDescriptor.IsContentHash(checkpoint.NextBlockSha256))
            { throw new InvalidDataException("Unsupported exam checkpoint or digest."); }
            ValidateBoundary(checkpoint.Patient);
            var source = PhysiologyWaveformGroup.Restore(WaveformCheckpointJson.Normalize(checkpoint.Patient));
            if (ReplayDigest(source.Fork(), checkpoint.Patient) != checkpoint.NextBlockSha256)
            { throw new InvalidDataException("Standby waveform replay differs from the checkpoint."); }
            return source;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or ArgumentException or OverflowException or
            InvalidOperationException or NullReferenceException or KeyNotFoundException or IndexOutOfRangeException)
        { throw new InvalidDataException("Invalid exam waveform checkpoint.", e); }
    }

    private static void ValidateBoundary(PhysiologyWaveformGroupState state)
    {
        if (state?.Assembler?.Planes is not { Count: > 0 and <= WaveformEnvelopeCodec.MaximumPlaneCount } planes ||
            state.Channels is null || state.Channels.Count != planes.Count ||
            planes.Any(p => p?.PendingSamples is not { Count: 0 }) ||
            state.Channels.Any(c => c?.Generator?.Clock?.CursorSimTimeNs != state.Assembler.NextBlockStartSimTimeNs))
        { throw new InvalidDataException("A waveform checkpoint requires a complete sample block boundary."); }
    }

    private static string ReplayDigest(PhysiologyWaveformGroup source, PhysiologyWaveformGroupState state)
    {
        long end = checked(state.Assembler.NextBlockStartSimTimeNs + WaveformBlockAssembler.BlockDurationNs);
        byte[] block = source.AdvanceTo(end, 250, 1, 256).Single();
        return Convert.ToHexStringLower(block.AsSpan(WaveformEnvelopeCodec.ContentSha256Offset, 32));
    }
}
