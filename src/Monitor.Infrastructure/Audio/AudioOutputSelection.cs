// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

public enum AudioOutputBackend { Native, Wasapi }

// The native library stays the product default until the managed WASAPI
// output is qualified on Windows hardware. Set SIMONITOR_AUDIO_OUTPUT=wasapi
// before launch to compare; any other value keeps the native output.
public static class AudioOutputSelection
{
    public const string EnvironmentVariable = "SIMONITOR_AUDIO_OUTPUT";

    public static AudioOutputBackend Current => Parse(Environment.GetEnvironmentVariable(EnvironmentVariable));

    public static AudioOutputBackend Parse(string? value) =>
        string.Equals(value?.Trim(), "wasapi", StringComparison.OrdinalIgnoreCase) ? AudioOutputBackend.Wasapi : AudioOutputBackend.Native;

    public static IPumpedAudioOutput Create(AudioOutputBackend backend) => backend switch
    {
        AudioOutputBackend.Wasapi => new WasapiAudioOutput(),
        AudioOutputBackend.Native => new NativeAudioOutputFactory(NativeAudioOutputFactory.DefaultLibraryPath),
        _ => throw new ArgumentOutOfRangeException(nameof(backend)),
    };
}
