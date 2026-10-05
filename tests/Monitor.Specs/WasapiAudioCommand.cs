// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

// Engineering commands for comparing the managed WASAPI output with the native
// library on Windows hardware. They never claim physical latency evidence.
internal static class WasapiAudioCommand
{
    public static int Execute(string[] args, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
        if (args.Length is < 1 or > 2 || args[0] is not ("--audio-wasapi-audition" or "--audio-wasapi-diagnostics"))
        { error.WriteLine("Usage: --audio-wasapi-audition|--audio-wasapi-diagnostics [DEVICE_ID]"); return 2; }
        if (cancellation.IsCancellationRequested) { return 130; }
        string? deviceId = args.Length == 2 ? args[1] : null;
        using var factory = new WasapiAudioOutput();
        if (args[0] == "--audio-wasapi-diagnostics") { return Diagnose(deviceId, factory, output, error); }
        var lifecycle = new AudioOutputLifecycle(factory);
        int result = 1;
        try
        {
            result = NativeAudioCommand.Audition(deviceId, output, error, factory, lifecycle, () => Describe(factory), cancellation);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { result = 130; }
        finally
        {
            if (!lifecycle.Stop()) { error.WriteLine("Audio stop failed."); result = 1; }
        }
        return result;
    }

    // Opens without starting playback; unavailable devices report exit code 1.
    private static int Diagnose(string? deviceId, WasapiAudioOutput factory, TextWriter output, TextWriter error)
    {
        var device = factory.Open(deviceId, new AudioRenderSession(), 1);
        if (device is null)
        {
            error.WriteLine("WASAPI diagnostics: device unavailable.");
            return 1;
        }
        try
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Schema = "Monitor.WasapiOpenDiagnostics@1",
                RequestedDevice = deviceId ?? "system-default",
                PlaybackStarted = false,
                StreamPath = factory.StreamPath.ToString(),
                factory.BufferFrames,
                factory.PeriodFrames,
                factory.QueueTargetFrames,
                PhysicalLatencyAssessment = "NotAssessed",
            }));
            return 0;
        }
        finally { device.StopAndClose(); }
    }

    private static string Describe(WasapiAudioOutput factory) =>
        $"WASAPI {factory.StreamPath}, buffer {factory.BufferFrames} frames, period {factory.PeriodFrames} frames, target {factory.QueueTargetFrames} frames";
}
