// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class AudioOutputDeviceSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(WindowsCollectionUsesTheSdkInterfaceIdentity), WindowsCollectionUsesTheSdkInterfaceIdentity),
        new(nameof(AlsaEnumerationReturnsSelectablePlaybackDevices), AlsaEnumerationReturnsSelectablePlaybackDevices),
    ];

    private static void WindowsCollectionUsesTheSdkInterfaceIdentity()
    {
        // IID_IMMDeviceCollection from the Windows SDK mmdeviceapi.h ABI contract.
        Check.That(typeof(IMMDeviceCollection).GUID == new Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
            "the generated COM wrapper must query the SDK's device collection interface");
    }

    private static void AlsaEnumerationReturnsSelectablePlaybackDevices()
    {
        if (!OperatingSystem.IsLinux() || !NativeLibrary.TryLoad("libasound.so.2", out nint library)) { return; }
        NativeLibrary.Free(library);
        string directory = Path.Combine(Path.GetTempPath(), "monitor-alsa-devices-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string configuration = Path.Combine(directory, "alsa.conf");
        try
        {
            // Isolated ALSA config: no host sound cards, global configuration changes or audible output.
            File.WriteAllText(configuration, """
                pcm.monitor_speaker { type null hint.show off }
                pcm.monitor_duplex { type null hint.show off }
                pcm.monitor_unnamed { type null hint.show off }
                namehint.pcm [
                    "monitor_speaker|USB 扬声器
                Analog output|IOIDOutput"
                    "monitor_duplex|Headset"
                    "monitor_input|Microphone|IOIDInput"
                    "monitor_unnamed"
                    "monitor_speaker|Duplicate|IOIDOutput"
                    "default|Default"
                    "null|Discard"
                ]
                """, new UTF8Encoding(false));
            var devices = JsonSerializer.Deserialize<AudioOutputDeviceInfo[]>(Run(configuration, "--audio-output-devices"))!;
            Check.That(devices.SequenceEqual(new AudioOutputDeviceInfo[]
                {
                    new("monitor_speaker", "USB 扬声器 · Analog output"),
                    new("monitor_duplex", "Headset"),
                    new("monitor_unnamed", "monitor_unnamed")
                }), "enumeration reads real ALSA hints, excludes capture/default/null, deduplicates IDs and preserves UTF-8 names");
            using var opened = JsonDocument.Parse(Run(configuration, "--audio-alsa-diagnostics", devices[0].Id));
            Check.That(opened.RootElement.GetProperty("RequestedDevice").GetString() == devices[0].Id &&
                opened.RootElement.GetProperty("BufferFrames").GetInt32() > 0 &&
                !opened.RootElement.GetProperty("PlaybackStarted").GetBoolean(),
                "the enumerated ID opens the selected ALSA PCM without substituting the default or playing audio");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string Run(string configuration, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(typeof(AudioOutputDeviceSpecifications).Assembly.Location);
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        start.Environment["ALSA_CONFIG_PATH"] = configuration;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new InvalidOperationException("ALSA device fixture timed out.");
        }
        string text = output.GetAwaiter().GetResult();
        Check.That(process.ExitCode == 0, "ALSA device fixture: " + error.GetAwaiter().GetResult());
        return text;
    }
}
