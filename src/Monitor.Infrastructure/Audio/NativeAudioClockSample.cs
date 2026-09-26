// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

public readonly record struct NativeAudioClockSample(int Result, uint HResult,
    ulong DevicePosition, ulong DeviceFrequency, ulong Qpc100Nanoseconds)
{
    // Nominal elapsed engine frames, not a renderer frontier or resampler
    // offset calibration. Reduced-accuracy readings must not steer scheduling.
    public ulong? Nominal48kElapsedFrames
    {
        get
        {
            if (Result != 0 || HResult != 0 || DeviceFrequency == 0) { return null; }
            UInt128 frames = (UInt128)DevicePosition * ToneVoice.SampleRate / DeviceFrequency;
            return frames <= ulong.MaxValue ? (ulong)frames : null;
        }
    }
}
