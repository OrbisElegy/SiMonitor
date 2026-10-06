// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

internal static class AudioQueueTarget
{
    public const int FramesPerMillisecond = ToneVoice.SampleRate / 1000;

    // Keep at least two device periods queued so one late producer wake-up
    // cannot starve the device, and never more than the stream can hold.
    public static int Frames(int targetMilliseconds, long periodFrames48k, int limitFrames)
    {
        long targetFrames = Math.Max((long)targetMilliseconds * FramesPerMillisecond, 2 * periodFrames48k);
        return (int)Math.Min(targetFrames, limitFrames);
    }

    // Device periods are reported at the device rate; round up to 48 kHz frames.
    public static long PeriodFrames48k(uint periodFrames, uint sampleRate) =>
        sampleRate == 0 ? 0 : ((long)periodFrames * ToneVoice.SampleRate + sampleRate - 1) / sampleRate;
}
