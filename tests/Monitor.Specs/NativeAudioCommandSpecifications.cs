// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class NativeAudioCommandSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(NativeAuditionRejectsBadPathsAndCancelsBeforeLoading), NativeAuditionRejectsBadPathsAndCancelsBeforeLoading),
    ];

    private static void NativeAuditionRejectsBadPathsAndCancelsBeforeLoading()
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        Check.That(NativeAudioCommand.Execute(["--audio-native-audition", "relative.dll"], output, error, default) == 2,
            "audition requires explicit absolute library path");
        string missing = Path.Combine(Path.GetTempPath(), "monitor-absent-" + Guid.NewGuid().ToString("N"), "native.dll");
        Check.That(NativeAudioCommand.Execute(["--audio-native-audition", missing], output, error, new CancellationToken(true)) == 130,
            "cancelled audition never loads DLL");
        Check.That(NativeAudioCommand.Execute(["--audio-native-audition", missing], output, error, default) == 1 && output.ToString().Length == 0,
            "missing DLL reports failure without pretending output started");
        bool rejected = false;
        try { using var factory = new NativeAudioOutputFactory("relative.dll"); } catch (ArgumentException) { rejected = true; }
        Check.That(rejected, "library search-path fallback is forbidden");
        foreach (int invalid in new[] { 4, 101 })
        {
            bool invalidTarget = false;
            try { using var factory = new NativeAudioOutputFactory(missing, queueTargetMilliseconds: invalid); }
            catch (ArgumentOutOfRangeException) { invalidTarget = true; }
            Check.That(invalidTarget, "queue target outside 5-100 ms is rejected before loading");
        }
        string defaultLibrary = NativeAudioOutputFactory.DefaultLibraryPath;
        Check.That(Path.IsPathFullyQualified(defaultLibrary) && Path.GetDirectoryName(defaultLibrary) == Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory) &&
            Path.GetFileName(defaultLibrary) == (OperatingSystem.IsWindows() ? "sim_audio_native.dll" : OperatingSystem.IsMacOS() ? "libsim_audio_native.dylib" : "libsim_audio_native.so"),
            "default library is the platform-named file beside the application");
        Check.That(NativeAudioCommand.Execute(["--audio-native-diagnostics", missing], output, error, new CancellationToken(true)) == 130,
            "cancelled diagnostics cannot open device");
        Check.That(NativeAudioCommand.Execute(["--audio-native-unknown", missing], output, error, default) == 2,
            "unknown command cannot fall through to audible audition");
    }
}
