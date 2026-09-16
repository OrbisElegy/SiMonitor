// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;

namespace Monitor.Infrastructure.Audio;

// Offline audition export only. Real device callbacks must not call this writer.
public static class ToneWaveFixture
{
    public static void Write(Stream output, TonePreset preset, int beatCount, int periodFrames, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(preset);
        preset.Validate();
        long total = (long)(beatCount - 1) * periodFrames + preset.TotalFrames;
        if (!output.CanWrite || beatCount is < 1 or > 128 || periodFrames < preset.TotalFrames || total > 60L * ToneVoice.SampleRate)
        { throw new ArgumentException("AudioTone.InvalidFixture"); }
        cancellationToken.ThrowIfCancellationRequested();
        int byteCount = checked((int)total * 2);
        Span<byte> header = stackalloc byte[44]; header.Clear();
        "RIFF"u8.CopyTo(header); BinaryPrimitives.WriteInt32LittleEndian(header[4..], byteCount + 36);
        "WAVEfmt "u8.CopyTo(header[8..]); BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1); BinaryPrimitives.WriteInt16LittleEndian(header[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], ToneVoice.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], ToneVoice.SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], 2); BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]); BinaryPrimitives.WriteInt32LittleEndian(header[40..], byteCount);
        output.Write(header);
        Span<float> frames = stackalloc float[256]; Span<byte> pcm = stackalloc byte[512];
        for (int beat = 0; beat < beatCount; beat++)
        {
            var voice = new ToneVoice(preset);
            int remaining = beat == beatCount - 1 ? preset.TotalFrames : periodFrames;
            while (remaining != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(remaining, frames.Length);
                voice.Render(frames[..count]);
                for (int i = 0; i < count; i++)
                { BinaryPrimitives.WriteInt16LittleEndian(pcm[(i * 2)..], checked((short)MathF.Round(frames[i] * 32768, MidpointRounding.ToEven))); }
                output.Write(pcm[..(count * 2)]); remaining -= count;
            }
        }
        output.Flush();
    }
}
