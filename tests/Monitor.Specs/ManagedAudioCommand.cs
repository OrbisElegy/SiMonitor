// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

// Engineering commands for the managed outputs: WASAPI for comparison with the
// native library on Windows, ALSA on Linux. They never claim latency evidence.
internal static class ManagedAudioCommand
{
    public static readonly string[] Commands =
        ["--audio-wasapi-audition", "--audio-wasapi-diagnostics", "--audio-alsa-audition", "--audio-alsa-diagnostics"];

    public static int Execute(string[] args, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
        if (args.Length is < 1 or > 2 || !Commands.Contains(args[0]))
        { error.WriteLine("Usage: --audio-wasapi-audition|--audio-wasapi-diagnostics|--audio-alsa-audition|--audio-alsa-diagnostics [DEVICE_ID]"); return 2; }
        if (cancellation.IsCancellationRequested) { return 130; }
        string? deviceId = args.Length == 2 ? args[1] : null;
        using var stream = args[0].StartsWith("--audio-wasapi-", StringComparison.Ordinal)
            ? ManagedStream.Wasapi(new WasapiAudioOutput())
            : ManagedStream.Alsa(new AlsaAudioOutput());
        if (args[0].EndsWith("-diagnostics", StringComparison.Ordinal)) { return Diagnose(deviceId, stream, output, error); }
        var lifecycle = new AudioOutputLifecycle(stream.Output);
        int result = 1;
        try
        {
            result = NativeAudioCommand.Audition(deviceId, output, error, stream.Output, lifecycle, stream.Describe, cancellation);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { result = 130; }
        finally
        {
            if (!lifecycle.Stop()) { error.WriteLine("Audio stop failed."); result = 1; }
        }
        return result;
    }

    // Opens without starting playback; unavailable devices report exit code 1.
    private static int Diagnose(string? deviceId, ManagedStream stream, TextWriter output, TextWriter error)
    {
        var device = stream.Output.Open(deviceId, new AudioRenderSession(), 1);
        if (device is null)
        {
            error.WriteLine("Managed audio diagnostics: device unavailable.");
            return 1;
        }
        try
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                stream.Schema,
                RequestedDevice = deviceId ?? "system-default",
                PlaybackStarted = false,
                stream.StreamPath,
                BufferFrames = stream.BufferFrames(),
                PeriodFrames = stream.PeriodFrames(),
                QueueTargetFrames = stream.QueueTargetFrames(),
                PhysicalLatencyAssessment = "NotAssessed",
            }));
            return 0;
        }
        finally { device.StopAndClose(); }
    }

    private sealed record ManagedStream(IPumpedAudioOutput Output, string Schema, Func<string?> Path,
        Func<int?> BufferFrames, Func<int?> PeriodFrames, Func<int?> QueueTargetFrames) : IDisposable
    {
        public string? StreamPath => Path();

        public static ManagedStream Wasapi(WasapiAudioOutput output) => new(output, "Monitor.WasapiOpenDiagnostics@1",
            () => output.StreamPath.ToString(), () => output.BufferFrames, () => output.PeriodFrames, () => output.QueueTargetFrames);

        public static ManagedStream Alsa(AlsaAudioOutput output) => new(output, "Monitor.AlsaOpenDiagnostics@1",
            () => null, () => output.BufferFrames, () => output.PeriodFrames, () => output.QueueTargetFrames);

        public string Describe() =>
            $"{Schema} path {StreamPath ?? "-"}, buffer {BufferFrames()} frames, period {PeriodFrames()} frames, target {QueueTargetFrames()} frames";

        public void Dispose() => Output.Dispose();
    }
}
