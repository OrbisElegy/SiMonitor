// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

// Managed WASAPI shared-mode output: no native library or build step. Open
// returns null on other platforms. All members belong to the serialized
// scheduler/control owner thread, which also receives MMCSS scheduling.
public sealed class WasapiAudioOutput : IPumpedAudioOutput
{
    private readonly EndpointAudioOutput _output;
    private WasapiStreamPath _path;

    public WasapiAudioOutput(int queueTargetMilliseconds = NativeAudioOutputFactory.DefaultQueueTargetMilliseconds)
    {
        int queueTargetFrames = queueTargetMilliseconds * AudioQueueTarget.FramesPerMillisecond;
        _output = new EndpointAudioOutput(OpenEndpoint, queueTargetMilliseconds);

        IRenderEndpoint? OpenEndpoint(string? deviceId, int bufferFrames)
        {
            if (!OperatingSystem.IsWindows()) { return null; }
            var endpoint = WasapiRenderEndpoint.Open(deviceId, bufferFrames, queueTargetFrames);
            _path = endpoint?.Path ?? WasapiStreamPath.None;
            return endpoint;
        }
    }

    // Queue level Pump maintains, stream buffer and engine period, in 48 kHz frames.
    public int? QueueTargetFrames => _output.QueueTargetFrames;
    public int? BufferFrames => _output.BufferFrames;
    public int? PeriodFrames => _output.PeriodFrames;
    public WasapiStreamPath StreamPath => _output.QueueTargetFrames is null ? WasapiStreamPath.None : _path;

    public IAudioOutputDevice? Open(string? deviceId, AudioRenderSession session, long generation) => _output.Open(deviceId, session, generation);
    public bool Pump() => _output.Pump();
    public void WaitForQueueSpace(int timeoutMilliseconds) => _output.WaitForQueueSpace(timeoutMilliseconds);
    public NativeAudioClockSample ReadClock() => _output.ReadClock();
    public void Dispose() => _output.Dispose();
}
