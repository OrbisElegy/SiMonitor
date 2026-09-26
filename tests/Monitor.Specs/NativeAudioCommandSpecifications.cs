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
    }
}
