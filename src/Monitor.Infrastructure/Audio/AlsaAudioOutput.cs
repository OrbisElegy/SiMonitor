// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

// Managed Linux output through the system alsa-lib: no project native
// library. Open returns null on other platforms or without libasound. All
// members belong to the serialized scheduler/control owner thread.
public sealed class AlsaAudioOutput : IPumpedAudioOutput
{
    private readonly EndpointAudioOutput _output;

    public AlsaAudioOutput(int queueTargetMilliseconds = NativeAudioOutputFactory.DefaultQueueTargetMilliseconds) =>
        _output = new EndpointAudioOutput(OpenEndpoint, queueTargetMilliseconds);

    // Queue level Pump maintains, stream buffer and period, in 48 kHz frames.
    public int? QueueTargetFrames => _output.QueueTargetFrames;
    public int? BufferFrames => _output.BufferFrames;
    public int? PeriodFrames => _output.PeriodFrames;

    public IAudioOutputDevice? Open(string? deviceId, AudioRenderSession session, long generation) => _output.Open(deviceId, session, generation);
    public bool Pump() => _output.Pump();
    public void WaitForQueueSpace(int timeoutMilliseconds) => _output.WaitForQueueSpace(timeoutMilliseconds);
    public NativeAudioClockSample ReadClock() => _output.ReadClock();
    public void Dispose() => _output.Dispose();

    private static AlsaRenderEndpoint? OpenEndpoint(string? deviceId, int bufferFrames) =>
        OperatingSystem.IsLinux() ? AlsaRenderEndpoint.Open(deviceId, bufferFrames) : null;
}
