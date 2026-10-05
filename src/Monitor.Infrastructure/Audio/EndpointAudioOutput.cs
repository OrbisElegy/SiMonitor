// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

// Writes the render session straight into an OS stream buffer: no native ring
// and no callback thread of ours. All members belong to the serialized
// scheduler/control owner thread.
internal sealed class EndpointAudioOutput : IPumpedAudioOutput
{
    private readonly RenderEndpointOpener _openEndpoint;
    private readonly int _queueTargetMilliseconds;
    private Device? _device;
    private bool _disposed;

    public EndpointAudioOutput(RenderEndpointOpener openEndpoint, int queueTargetMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(openEndpoint);
        ArgumentOutOfRangeException.ThrowIfLessThan(queueTargetMilliseconds, 5);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(queueTargetMilliseconds, 100);
        _openEndpoint = openEndpoint;
        _queueTargetMilliseconds = queueTargetMilliseconds;
    }

    // Queue level that Pump maintains for the open stream, in 48 kHz frames.
    public int? QueueTargetFrames => _device?.QueueTargetFrames;
    public int? BufferFrames => _device?.Endpoint.BufferFrames;
    public int? PeriodFrames => _device?.Endpoint.PeriodFrames;

    public IAudioOutputDevice? Open(string? deviceId, AudioRenderSession session, long generation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(session);
        if (_device is not null) { throw new InvalidOperationException("AudioEndpoint.DeviceStillOwned"); }
        if (deviceId?.Contains('\0', StringComparison.Ordinal) == true) { throw new ArgumentException("AudioEndpoint.InvalidId", nameof(deviceId)); }
        if (session.RequiresReplacement) { return null; }
        var endpoint = _openEndpoint(deviceId, session.CapacityFrames);
        if (endpoint is null) { return null; }
        int limitFrames = Math.Min(session.CapacityFrames, endpoint.BufferFrames);
        _device = new Device(this, endpoint, session, AudioQueueTarget.Frames(_queueTargetMilliseconds, endpoint.PeriodFrames, limitFrames));
        return _device;
    }

    // Bounded work: at most one session capacity each invocation.
    public bool Pump() => _device?.Pump() == true;

    public void WaitForQueueSpace(int timeoutMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMilliseconds, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeoutMilliseconds, 1000);
        if (_device is null) { Thread.Sleep(1); return; }
        _device.Endpoint.Wait(timeoutMilliseconds);
    }

    public NativeAudioClockSample ReadClock() => _device?.Endpoint.ReadClock() ?? new(-2, 0, 0, 0, 0);

    // No library unload hazard here: close a still-open stream best-effort.
    // A stream that fails to stop stays owned by its device for a later retry.
    public void Dispose()
    {
        if (_disposed) { return; }
        _device?.StopAndClose();
        _disposed = true;
    }

    private sealed class Device(EndpointAudioOutput owner, IRenderEndpoint endpoint, AudioRenderSession session, int queueTargetFrames) : IAudioOutputDevice
    {
        private const int MaximumProduceFrames = 240;
        private readonly float[] _scratch = new float[session.CapacityFrames];
        private bool _started;
        private bool _closed;

        public IRenderEndpoint Endpoint => endpoint;
        public int QueueTargetFrames => queueTargetFrames;

        public bool Start()
        {
            if (_closed || !Pump()) { return false; }
            _started = endpoint.Start();
            return _started;
        }

        public bool Pump()
        {
            if (_closed || session.RequiresReplacement) { return false; }
            if (endpoint.IsRetired)
            {
                session.Retire();
                return false;
            }
            for (int budget = session.CapacityFrames; budget > 0;)
            {
                if (!endpoint.TryGetPadding(out int queuedFrames))
                {
                    session.Retire();
                    return false;
                }
                // A running shared stream at zero padding has already played
                // silence. The silent span is not measured; report a lower bound.
                if (_started && queuedFrames == 0)
                {
                    session.ReportNativeUnderrun(1);
                    return false;
                }
                int frames = Math.Min(endpoint.BufferFrames - queuedFrames, budget);
                if (session.BufferedFrames == 0)
                {
                    // New PCM only tops the stream up to its target; staged PCM always drains.
                    frames = Math.Min(frames, Math.Min(queueTargetFrames - queuedFrames, MaximumProduceFrames));
                    if (frames <= 0) { return true; }
                    if (!session.TryProduce(frames)) { return false; }
                }
                if (frames <= 0) { return true; }
                frames = Math.Min(frames, session.BufferedFrames);
                if (session.Read(_scratch.AsSpan(0, frames)) != frames || !endpoint.TryWrite(_scratch.AsSpan(0, frames)))
                {
                    session.Retire();
                    return false;
                }
                budget -= frames;
            }
            return true;
        }

        public bool StopAndClose()
        {
            if (_closed) { return true; }
            session.Retire();
            if (!endpoint.Stop()) { return false; }
            endpoint.Dispose();
            _closed = true;
            owner._device = null;
            return true;
        }
    }
}
