// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class AudioRenderSessionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(AudioClockMapsFractionalTicksAndObservedDrift), AudioClockMapsFractionalTicksAndObservedDrift),
        new(nameof(AudioPumpPreservesToneWhenFullAndAcrossWrap), AudioPumpPreservesToneWhenFullAndAcrossWrap),
        new(nameof(AudioUnderrunRetiresOldPcmAndCommands), AudioUnderrunRetiresOldPcmAndCommands),
        new(nameof(AudioPumpAndCallbackAllocateNothing), AudioPumpAndCallbackAllocateNothing),
    ];

    private static void AudioClockMapsFractionalTicksAndObservedDrift()
    {
        var clock = new AudioClockBridge(10_000_000, 10_000_000, 48_000);
        Check.That(clock.MapToFrame(10_000_001) == 48_001 && clock.MapToFrame(9_999_999) == 48_000 &&
            clock.MapToFrame(20_000_000) == 96_000, "first frame at/after requested monotonic tick, including negative offsets");
        clock.Observe(20_000_000, 96_048);
        Check.That(clock.MapToFrame(30_000_000) == 144_096, "paired observations account for a synthetic 1000ppm slope");
        bool rejected = false;
        try { clock.Observe(20_000_000, 100_000); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check.That(rejected && clock.MapToFrame(30_000_000) == 144_096, "duplicate ticks reject without changing mapping");
        rejected = false;
        try { clock.Observe(30_000_000, 96_047); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check.That(rejected && clock.MapToFrame(30_000_000) == 144_096, "backward device cursor requires a new clock bridge");
        var stream = new AudioRenderSession(48_000);
        var nominal = new AudioClockBridge(1000, 1000, 48_000);
        Check.That(stream.Schedule(1, TonePreset.BeatAudition, nominal.MapToFrame(1000), nominal.MapToFrame(1250)) == ToneScheduleResult.Accepted,
            "mapped exclusive250ms deadline is accepted at the same engine origin");
        var overflow = new AudioClockBridge(1, 0, long.MaxValue);
        rejected = false;
        try { overflow.MapToFrame(1); } catch (OverflowException) { rejected = true; }
        Check.That(rejected, "mapping cannot wrap the sample clock");
    }

    private static void AudioPumpPreservesToneWhenFullAndAcrossWrap()
    {
        var stream = new AudioRenderSession();
        stream.Schedule(1, TonePreset.BeatAudition, 317, 12317);
        var reference = new SampleToneRenderer(); reference.Schedule(1, TonePreset.BeatAudition, 317, 12317);
        float[] expected = new float[7000]; reference.Render(expected);
        float[] actual = new float[7000];
        Check.That(stream.TryProduce(1920) && !stream.TryProduce(1) && stream.RenderedThroughFrame == 1920,
            "full transport does not consume a tone frame");
        Check.That(stream.Read(actual.AsSpan(0, 1000)) == 1000 && stream.TryProduce(1000), "producer fills freed ring space");
        Check.That(stream.Read(actual.AsSpan(1000, 1920)) == 1920, "wrapped queued frames delivered");
        for (int offset = 2920; offset < actual.Length;)
        {
            int count = Math.Min(137, actual.Length - offset);
            Check.That(stream.TryProduce(count) && stream.Read(actual.AsSpan(offset, count)) == count, "pump stays ahead of callback");
            offset += count;
        }
        Check.That(actual.SequenceEqual(expected) && !stream.RequiresReplacement, "transport preserves scheduled PCM sample for sample");
    }

    private static void AudioUnderrunRetiresOldPcmAndCommands()
    {
        var stream = new AudioRenderSession();
        stream.Schedule(1, TonePreset.BeatAudition, 0, 12000); stream.TryProduce(240);
        float[] output = new float[480];
        Check.That(stream.Read(output) == 240 && output.Skip(240).All(v => v == 0) && stream.UnderrunFrames == 240,
            "first underrun retains valid prefix and counts missing frames");
        long position = stream.RenderedThroughFrame;
        Check.That(stream.RequiresReplacement && !stream.TryProduce(480) && stream.RenderedThroughFrame == position &&
            stream.Schedule(2, TonePreset.BeatAudition, 240, 12240) is null, "late producer and new commands cannot revive retired timeline");
        output.AsSpan().Fill(1);
        Check.That(stream.Read(output) == 0 && output.All(v => v == 0) && stream.UnderrunFrames == 240,
            "retired callbacks stay silent without inflating first-fault count");
        var replacement = new AudioRenderSession(48000);
        replacement.TryProduce(480);
        Check.That(replacement.Read(output) == 480 && output.All(v => v == 0), "replacement does not inherit old cues");
        replacement.Schedule(3, TonePreset.BeatAudition, 48480, 60480); replacement.TryProduce(480); replacement.Retire();
        Check.That(replacement.Read(output) == 0 && output.All(v => v == 0), "explicit retirement fences already queued PCM");
    }

    private static void AudioPumpAndCallbackAllocateNothing()
    {
        var stream = new AudioRenderSession();
        Span<float> output = stackalloc float[240];
        stream.TryProduce(240); stream.Read(output);
        stream.Schedule(1, TonePreset.BeatAudition, 240, 12240);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { stream.TryProduce(240); stream.Read(output); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.That(allocated == 0 && !stream.RequiresReplacement, "render/pump/callback paths allocate no managed memory");
    }
}
