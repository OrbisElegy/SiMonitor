// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

// One opened, not yet started OS render stream carrying 48 kHz frames. All
// members belong to the serialized owner thread; only IsRetired may be raised
// by device notifications on another thread.
internal interface IRenderEndpoint : IDisposable
{
    // Stream buffer and engine period, in 48 kHz frames; zero period is unknown.
    public int BufferFrames { get; }
    public int PeriodFrames { get; }
    // Device change, removal or invalidation; never cleared for this stream.
    public bool IsRetired { get; }
    // Frames still queued for the device. False means the stream is unusable.
    public bool TryGetPadding(out int queuedFrames);
    // Appends whole mono frames; the endpoint lays them out for its channels.
    public bool TryWrite(ReadOnlySpan<float> monoFrames);
    public bool Start();
    // True once the stream no longer plays; false keeps ownership for retry.
    public bool Stop();
    // Returns when the device consumed frames, the stream retired, or on timeout.
    public void Wait(int timeoutMilliseconds);
    public NativeAudioClockSample ReadClock();
}

// Opens a stream for an endpoint ID (null follows the default output) with a
// requested buffer of bufferFrames. Returns null when the device is unavailable.
internal delegate IRenderEndpoint? RenderEndpointOpener(string? deviceId, int bufferFrames);

internal static class RenderFrames
{
    // Mono content goes to the first two channels (front left/right in
    // WAVEFORMATEXTENSIBLE order) or the single channel; others stay silent.
    public static void ExpandMono(ReadOnlySpan<float> monoFrames, Span<float> interleaved, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        if (interleaved.Length != checked(monoFrames.Length * channels))
        { throw new ArgumentException("RenderFrames.LengthMismatch", nameof(interleaved)); }
        if (channels == 1)
        {
            monoFrames.CopyTo(interleaved);
            return;
        }
        interleaved.Clear();
        for (int frame = 0; frame < monoFrames.Length; frame++)
        {
            interleaved[frame * channels] = monoFrames[frame];
            interleaved[frame * channels + 1] = monoFrames[frame];
        }
    }
}
