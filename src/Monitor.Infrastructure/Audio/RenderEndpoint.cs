// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

internal enum RenderEndpointReadiness { Ready, Underrun, Unavailable }

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
    // Zero queued frames alone does not prove a gap: the engine may have just
    // taken the last packet. Underrun requires an explicit backend indication.
    public RenderEndpointReadiness ReadPadding(out int queuedFrames);
    // Appends whole mono frames; the endpoint lays them out for its channels.
    public bool TryWrite(ReadOnlySpan<float> monoFrames);
    public bool Start();
    // True once the stream no longer plays; false keeps ownership for retry.
    public bool Stop();
    // Wait may return early once no more than queuedFrames remain queued.
    // Event-driven endpoints that already wake every device period ignore it.
    public void SetWakeThreshold(int queuedFrames);
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
