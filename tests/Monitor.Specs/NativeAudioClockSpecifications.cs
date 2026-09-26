// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class NativeAudioClockSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(DeviceClockPositionUsesFrequencyRatherThanFormatRate), DeviceClockPositionUsesFrequencyRatherThanFormatRate),
        new(nameof(UnqualifiedClockReadingsCannotBecomeEngineFrames), UnqualifiedClockReadingsCannotBecomeEngineFrames),
    ];

    private static void DeviceClockPositionUsesFrequencyRatherThanFormatRate()
    {
        // Synthetic byte-valued device clock:192kHz stereo float,100ms elapsed.
        var sample = new NativeAudioClockSample(0, 0, 153600, 1536000, 987654321);
        Check.That(sample.Nominal48kElapsedFrames == 4800 && sample.Qpc100Nanoseconds == 987654321,
            "position/frequency yields elapsed time independently of device units; QPC100ns is preserved");
        var partial = new NativeAudioClockSample(0, 0, 1, 192000, 1);
        Check.That(partial.Nominal48kElapsedFrames == 0, "elapsed frame conversion floors rather than inventing a future frame");
        var large = new NativeAudioClockSample(0, 0, ulong.MaxValue, 48000, ulong.MaxValue);
        Check.That(large.Nominal48kElapsedFrames == ulong.MaxValue, "wide intermediate avoids multiplication overflow");
    }

    private static void UnqualifiedClockReadingsCannotBecomeEngineFrames()
    {
        foreach (var sample in new[]
        {
            new NativeAudioClockSample(1, 1, 48000, 48000, 1),
            new(-2, 0, 0, 0, 0), new(-4, 0, 48000, 48000, 1),
            new(0, 0, 1, 0, 1), new(0, 0x80004005, 48000, 48000, 1),
            new(0, 0, ulong.MaxValue, 1, 1),
        })
        { Check.That(sample.Nominal48kElapsedFrames is null, "reduced, unavailable, retired, invalid and overflowing readings stay unusable"); }
    }
}
