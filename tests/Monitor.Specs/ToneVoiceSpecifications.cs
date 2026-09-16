// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class ToneVoiceSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ToneUsesSampleClockAndBoundedEnvelope), ToneUsesSampleClockAndBoundedEnvelope),
        new(nameof(ToneChunksAndRestorationAreBitIdenticalWithoutRenderAllocation), ToneChunksAndRestorationAreBitIdenticalWithoutRenderAllocation),
        new(nameof(ToneCancellationFadesAndInvalidStatesReject), ToneCancellationFadesAndInvalidStatesReject),
        new(nameof(AuditionWaveHasExactFramesAndCommandFailureSemantics), AuditionWaveHasExactFramesAndCommandFailureSemantics),
    ];

    private static void ToneUsesSampleClockAndBoundedEnvelope()
    {
        var preset = new TonePreset("Probe@1", 1_000_000, 1, 480, 1, 16384);
        float[] samples = new float[preset.TotalFrames + 20];
        var voice = new ToneVoice(preset);
        Check.That(voice.Render(samples) == preset.TotalFrames && voice.Finished && samples[0] == 0 && samples[481] == 0,
            "one-shot starts and ends at zero and stops at its exact frame count");
        Check.That(samples[12] == 0.5f && samples[24] == 0 && samples[36] == -0.5f,
            "1000Hz quadrants occur at exact 48kHz sample positions with headroom");
        Check.That(samples.Skip(1).Take(400).Zip(samples.Skip(49)).All(p => p.First == p.Second), "no phase drift between periods");
        Check.That(samples.All(v => float.IsFinite(v) && Math.Abs(v) <= 0.5f) && samples.Skip(482).All(v => v == 0),
            "output is finite mono float32 with bounded gain and silent tail");
        var silent = new ToneVoice(preset with { GainQ15 = 0 }); silent.Render(samples);
        Check.That(samples.All(v => v == 0), "zero gain stays silent");
    }

    private static void ToneChunksAndRestorationAreBitIdenticalWithoutRenderAllocation()
    {
        var preset = TonePreset.BeatAudition;
        float[] whole = new float[preset.TotalFrames]; new ToneVoice(preset).Render(whole);
        var source = new ToneVoice(preset); float[] chunks = new float[whole.Length];
        for (int offset = 0; offset < chunks.Length;)
        {
            int count = Math.Min(137, chunks.Length - offset);
            source.Render(chunks.AsSpan(offset, count)); offset += count;
            source = ToneVoice.Restore(source.CaptureState());
        }
        Check.That(whole.SequenceEqual(chunks), "callback partition and offline restoration preserve every PCM sample");
        Span<float> buffer = stackalloc float[256];
        var warm = new ToneVoice(preset); while (!warm.Finished) { warm.Render(buffer); }
        var measured = new ToneVoice(preset);
        long before = GC.GetAllocatedBytesForCurrentThread();
        while (!measured.Finished) { measured.Render(buffer); }
        Check.That(GC.GetAllocatedBytesForCurrentThread() == before, "steady Render path allocates no managed memory");
    }

    private static void ToneCancellationFadesAndInvalidStatesReject()
    {
        var voice = new ToneVoice(TonePreset.BeatAudition); voice.Render(new float[1000]); voice.Cancel(); voice.Cancel();
        var restored = ToneVoice.Restore(voice.CaptureState());
        float[] a = new float[512], b = new float[512];
        Check.That(voice.Render(a) == 240 && restored.Render(b) == 240 && a.SequenceEqual(b) && a[239] == 0 && a.Skip(240).All(v => v == 0),
            "cancellation is idempotent with a bounded 5ms fade and no replay");
        var neverStarted = new ToneVoice(TonePreset.BeatAudition); neverStarted.Cancel();
        Check.That(neverStarted.Render(a) == 0 && a.All(v => v == 0), "cancel before start cannot begin a sound");
        var early = new ToneVoice(TonePreset.BeatAudition); early.Render(new float[5]); early.Cancel(); early.Render(a);
        Check.That(a.All(v => Math.Abs(v) <= 0.25f * 5 / 240), "cancellation during attack must not continue increasing its envelope");
        foreach (var preset in new[] { TonePreset.BeatAudition with { Id = "" }, TonePreset.BeatAudition with { FrequencyMilliHz = 0 },
            TonePreset.BeatAudition with { AttackFrames = 0 }, TonePreset.BeatAudition with { HoldFrames = int.MaxValue },
            TonePreset.BeatAudition with { ReleaseFrames = 0 }, TonePreset.BeatAudition with { GainQ15 = 16385 } })
        {
            bool rejected = false; try { _ = new ToneVoice(preset); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "invalid tone parameters reject before rendering");
        }
        foreach (var state in new[] { new ToneVoiceState(TonePreset.BeatAudition, -1, null), new(TonePreset.BeatAudition, 6000, null),
            new(TonePreset.BeatAudition, 300, 0), new(TonePreset.BeatAudition, 0, 1) })
        {
            bool rejected = false; try { _ = ToneVoice.Restore(state); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "malformed voice positions and cancellation state reject");
        }
    }

    private static void AuditionWaveHasExactFramesAndCommandFailureSemantics()
    {
        using MemoryStream output = new(); using StringWriter error = new();
        Check.That(AudioFixtureCommand.Execute(["--audio-tone-fixture"], output, error, default) == 0, "audition command succeeds");
        byte[] bytes = output.ToArray(); int total = 4 * 38_400 + TonePreset.BeatAudition.TotalFrames;
        Check.That(bytes.Length == 44 + total * 2 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24)) == 48_000 && BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22)) == 1 &&
            BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34)) == 16 && BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40)) == total * 2,
            "export is complete mono 48kHz PCM16 WAV");
        int toneBytes = TonePreset.BeatAudition.TotalFrames * 2;
        for (int beat = 1; beat < 5; beat++)
        {
            Check.That(bytes.AsSpan(44, toneBytes).SequenceEqual(bytes.AsSpan(44 + beat * 38_400 * 2, toneBytes)), "audition cadence has exact integer positions");
            Check.That(bytes.AsSpan(44 + (beat - 1) * 38_400 * 2 + toneBytes, 38_400 * 2 - toneBytes).IndexOfAnyExcept((byte)0) == -1, "gaps are silent");
        }
        using MemoryStream cancelled = new();
        Check.That(AudioFixtureCommand.Execute(["--audio-tone-fixture"], cancelled, error, new CancellationToken(true)) == 130 && cancelled.Length == 0,
            "cancelled export cannot publish a header");
        Check.That(AudioFixtureCommand.Execute(["--audio-tone-fixture", "extra"], cancelled, error, default) == 2 && cancelled.Length == 0,
            "malformed command cannot publish audio");
        using var broken = new BrokenStream();
        Check.That(AudioFixtureCommand.Execute(["--audio-tone-fixture"], broken, error, default) == 1, "write failure is reported without retry");
    }

    private sealed class BrokenStream : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("fixture");
    }
}
