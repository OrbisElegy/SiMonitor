// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class SampleToneRendererSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ToneSchedulePreservesExactOnsetAcrossRenderPartitions), ToneSchedulePreservesExactOnsetAcrossRenderPartitions),
        new(nameof(ToneScheduleBoundsCommandsAndExpiresAtExclusiveDeadline), ToneScheduleBoundsCommandsAndExpiresAtExclusiveDeadline),
        new(nameof(ToneScheduleCancelsPendingAndFadesActiveVoices), ToneScheduleCancelsPendingAndFadesActiveVoices),
        new(nameof(ToneScheduleDiscardsOldClockAndAllocatesNothingWhileRendering), ToneScheduleDiscardsOldClockAndAllocatesNothingWhileRendering),
    ];

    private static void ToneSchedulePreservesExactOnsetAcrossRenderPartitions()
    {
        var whole = new SampleToneRenderer(); var chunks = new SampleToneRenderer();
        foreach (var renderer in new[] { whole, chunks })
        {
            Check.That(renderer.Schedule(1, TonePreset.BeatAudition, 317, 12317) == ToneScheduleResult.Accepted, "future cue accepted");
            renderer.Schedule(2, TonePreset.BeatAudition, 510, 12510);
        }
        float[] a = new float[7000], b = new float[7000]; whole.Render(a);
        for (int offset = 0; offset < b.Length; offset += 137) { chunks.Render(b.AsSpan(offset, Math.Min(137, b.Length - offset))); }
        float[] reference = new float[TonePreset.BeatAudition.TotalFrames]; new ToneVoice(TonePreset.BeatAudition).Render(reference);
        Check.That(a.SequenceEqual(b) && whole.Position == 7000 && chunks.Position == 7000, "partition size cannot move onset or change PCM");
        for (int i = 0; i < a.Length; i++)
        {
            float expected = (i >= 317 && i < 317 + reference.Length ? reference[i - 317] : 0) +
                (i >= 510 && i < 510 + reference.Length ? reference[i - 510] : 0);
            Check.That(a[i] == expected, "two independently offset voices mix at exact sample indices");
        }
    }

    private static void ToneScheduleBoundsCommandsAndExpiresAtExclusiveDeadline()
    {
        var renderer = new SampleToneRenderer(12000);
        Check.That(renderer.Schedule(1, TonePreset.BeatAudition, 0, 12000) == ToneScheduleResult.Expired, "250ms beat example expires exactly at deadline");
        Check.That(renderer.Schedule(1, TonePreset.BeatAudition, 0, 12001) == ToneScheduleResult.Accepted, "valid late cue starts at current frontier");
        Check.That(renderer.Schedule(1, TonePreset.BeatAudition, 12000, 24000) == ToneScheduleResult.Duplicate, "active command key is not queued twice");
        for (int i = 2; i <= 32; i++) { renderer.Schedule(i, TonePreset.BeatAudition, 12000, 24000); }
        Check.That(renderer.Schedule(33, TonePreset.BeatAudition, 12000, 24000) == ToneScheduleResult.Full, "thirty-third voice rejects without evicting existing cues");
        float[] mixed = new float[7000]; renderer.Render(mixed);
        Check.That(mixed.All(v => float.IsFinite(v) && Math.Abs(v) <= 1) && mixed.Any(v => Math.Abs(v) == 1), "overlap saturates safely");
        Check.That(renderer.Schedule(33, TonePreset.BeatAudition, 19000, 31000) == ToneScheduleResult.Accepted, "finished voices release bounded slots");
        long before = renderer.Position; bool rejected = false;
        try { renderer.Schedule(10, TonePreset.BeatAudition, 20000, 20000); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check.That(rejected && renderer.Position == before, "invalid deadline cannot mutate clock");
    }

    private static void ToneScheduleCancelsPendingAndFadesActiveVoices()
    {
        var renderer = new SampleToneRenderer();
        renderer.Schedule(1, TonePreset.BeatAudition, 100, 12100); renderer.Cancel(1); renderer.Cancel(1);
        float[] silent = new float[1000]; renderer.Render(silent);
        Check.That(silent.All(v => v == 0), "pending cancellation never starts a cue");
        renderer.Schedule(2, TonePreset.BeatAudition, 1000, 13000);
        float[] first = new float[1000]; renderer.Render(first);
        var reference = new ToneVoice(TonePreset.BeatAudition); reference.Render(new float[1000]); reference.Cancel();
        renderer.Cancel(2); renderer.Cancel(2);
        float[] expected = new float[500], actual = new float[500]; reference.Render(expected); renderer.Render(actual);
        Check.That(actual.SequenceEqual(expected) && actual.Skip(239).All(v => v == 0), "active cancellation matches <=5ms voice fade exactly");
    }

    private static void ToneScheduleDiscardsOldClockAndAllocatesNothingWhileRendering()
    {
        var renderer = new SampleToneRenderer();
        renderer.Schedule(1, TonePreset.BeatAudition, 0, 12000); renderer.Schedule(2, TonePreset.BeatAudition, 20000, 32000);
        renderer.Render(new float[500]); renderer.DiscardForDiscontinuity(10000);
        float[] data = new float[24000]; renderer.Render(data);
        Check.That(data.All(v => v == 0) && renderer.Position == 34000, "clock jump cannot replay active or future old commands");
        bool rejected = false;
        try { renderer.DiscardForDiscontinuity(0); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check.That(rejected && renderer.Position == 34000, "rewind rejects atomically; new stream needs new owner");
        renderer.Schedule(3, TonePreset.BeatAudition, 34000, 46000); renderer.Render(data);
        renderer.Schedule(4, TonePreset.BeatAudition, renderer.Position, renderer.Position + 12000);
        long before = GC.GetAllocatedBytesForCurrentThread(); renderer.Render(data);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.That(allocated == 0 && data.Any(v => v != 0), "prepared voices render without managed allocations");
        var exhausted = new SampleToneRenderer(long.MaxValue);
        rejected = false;
        try { exhausted.Render(data); } catch (OverflowException) { rejected = true; }
        Check.That(rejected && exhausted.Position == long.MaxValue, "frame overflow rejects before changing state");
    }
}
