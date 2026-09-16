// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class AudioFixtureCommand
{
    internal static int Execute(string[] args, Stream output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length != 1 || args[0] != "--audio-tone-fixture")
        { error.WriteLine("Usage: Monitor.Specs --audio-tone-fixture > beat.wav"); return 2; }
        try
        {
            // Five fixed 75bpm audition tones, not detected QRS events.
            ToneWaveFixture.Write(output, TonePreset.BeatAudition, 5, 38_400, cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { error.WriteLine("Audio fixture cancelled; output may be incomplete."); return 130; }
        catch (IOException)
        { error.WriteLine("Audio fixture I/O failed; output may be incomplete."); return 1; }
    }
}
