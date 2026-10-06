// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Infrastructure.Audio;

public enum AudioOutputBackend { Native, Wasapi, Alsa }

// Linux plays through the system ALSA library. Windows keeps the native
// library until the managed WASAPI output is qualified on hardware. Set
// SIMONITOR_AUDIO_OUTPUT to native, wasapi or alsa before launch to override.
public static class AudioOutputSelection
{
    public const string EnvironmentVariable = "SIMONITOR_AUDIO_OUTPUT";

    public static AudioOutputBackend Current => Resolve(Environment.GetEnvironmentVariable(EnvironmentVariable));

    // Unset or unknown values select the platform default.
    public static AudioOutputBackend Resolve(string? value)
    {
        string selected = value?.Trim() ?? "";
        if (selected.Equals("native", StringComparison.OrdinalIgnoreCase)) { return AudioOutputBackend.Native; }
        if (selected.Equals("wasapi", StringComparison.OrdinalIgnoreCase)) { return AudioOutputBackend.Wasapi; }
        if (selected.Equals("alsa", StringComparison.OrdinalIgnoreCase)) { return AudioOutputBackend.Alsa; }
        return OperatingSystem.IsLinux() ? AudioOutputBackend.Alsa : AudioOutputBackend.Native;
    }

    public static IPumpedAudioOutput Create(AudioOutputBackend backend) => backend switch
    {
        AudioOutputBackend.Wasapi => new WasapiAudioOutput(),
        AudioOutputBackend.Alsa => new AlsaAudioOutput(),
        AudioOutputBackend.Native => new NativeAudioOutputFactory(NativeAudioOutputFactory.DefaultLibraryPath),
        _ => throw new ArgumentOutOfRangeException(nameof(backend)),
    };
}
