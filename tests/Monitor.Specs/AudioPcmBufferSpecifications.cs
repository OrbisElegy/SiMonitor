// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Infrastructure.Audio;

namespace Monitor.Specs;

internal static class AudioPcmBufferSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PcmQueueWrapsWithoutOverwritingAndSilencesUnderruns), PcmQueueWrapsWithoutOverwritingAndSilencesUnderruns),
        new(nameof(PcmQueueRejectsInvalidBlocksAtomicallyAndIsolatesStreams), PcmQueueRejectsInvalidBlocksAtomicallyAndIsolatesStreams),
        new(nameof(PcmQueuePreservesToneAcrossConcurrentPartitions), PcmQueuePreservesToneAcrossConcurrentPartitions),
        new(nameof(PcmQueueSteadyPathsAllocateNothing), PcmQueueSteadyPathsAllocateNothing),
    ];

    private static void PcmQueueWrapsWithoutOverwritingAndSilencesUnderruns()
    {
        var queue = new AudioPcmBuffer(channels: 2);
        Check.That(queue.SampleRate == 48_000 && queue.CapacityFrames == 1920, "default capacity is 40ms independently of waveform buffering");
        float[] block = Enumerable.Range(0, 3000).Select(i => (i % 99 - 49) / 50f).ToArray();
        Check.That(queue.TryWrite(block), "stereo frames accepted");
        float[] first = new float[2000];
        Check.That(queue.Read(first) == 1000 && first.SequenceEqual(block.Take(2000)), "channels and frame order preserved");
        Check.That(queue.TryWrite(block.AsSpan(0, 2000)), "write wraps physical storage");
        Check.That(!queue.TryWrite(block), "full queue rejects complete submission without overwrite");
        float[] tail = Enumerable.Repeat(0.75f, 4000).ToArray();
        Check.That(queue.Read(tail) == 1500 && tail.Take(3000).SequenceEqual(block.Skip(2000).Concat(block.Take(2000))) &&
            tail.Skip(3000).All(v => v == 0), "wrapped read is exact and underrun tail is silent");
        Check.That(queue.Read(tail) == 0 && tail.All(v => v == 0), "empty callback cannot repeat old samples");
    }

    private static void PcmQueueRejectsInvalidBlocksAtomicallyAndIsolatesStreams()
    {
        var old = new AudioPcmBuffer(channels: 2);
        Check.That(old.TryWrite([0.25f, -0.25f]), "initial frame accepted");
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, 1.01f, -1.01f })
        { Check.That(!old.TryWrite([0, 0, value, 0]), "invalid PCM rejects even after a valid prefix"); }
        Check.That(!old.TryWrite([0.1f]), "partial interleaved frame rejects");
        float[] invalid = [1, 1, 1];
        Check.That(old.Read(invalid) == 0 && invalid.All(v => v == 0), "invalid callback geometry is silent");
        float[] output = new float[4];
        Check.That(old.Read(output) == 1 && output.SequenceEqual(new[] { 0.25f, -0.25f, 0, 0 }), "rejections preserve unread PCM");
        old.TryWrite([0.5f, 0.5f]);
        var replacement = new AudioPcmBuffer(channels: 2);
        Check.That(replacement.Read(output) == 0 && output.All(v => v == 0), "new stream owns fresh storage with no stale audio");
        foreach (var args in new[] { (7999, 1, 40), (192001, 1, 40), (48000, 0, 40), (48000, 9, 40), (48000, 1, 19), (48000, 1, 101) })
        {
            bool rejected = false;
            try { _ = new AudioPcmBuffer(args.Item1, args.Item2, args.Item3); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check.That(rejected, "invalid geometry fails before callback setup");
        }
    }

    private static void PcmQueuePreservesToneAcrossConcurrentPartitions()
    {
        float[] tone = new float[TonePreset.BeatAudition.TotalFrames]; new ToneVoice(TonePreset.BeatAudition).Render(tone);
        float[] source = Enumerable.Range(0, 200).SelectMany(_ => tone).ToArray();
        float[] result = new float[source.Length];
        var queue = new AudioPcmBuffer(capacityMilliseconds: 20);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var producer = Task.Run(() =>
        {
            for (int position = 0; position < source.Length;)
            {
                deadline.Token.ThrowIfCancellationRequested();
                int count = Math.Min(137, source.Length - position);
                if (queue.TryWrite(source.AsSpan(position, count))) { position += count; }
                else { Thread.Yield(); }
            }
        });
        var consumer = Task.Run(() =>
        {
            float[] buffer = new float[251];
            for (int position = 0; position < result.Length;)
            {
                deadline.Token.ThrowIfCancellationRequested();
                int count = queue.Read(buffer.AsSpan(0, Math.Min(buffer.Length, result.Length - position)));
                buffer.AsSpan(0, count).CopyTo(result.AsSpan(position)); position += count;
                if (count == 0) { Thread.Yield(); }
            }
        });
        Task.WaitAll(producer, consumer);
        Check.That(source.SequenceEqual(result), "over one million PCM samples survive concurrent publication and wrap without loss or reordering");
    }

    private static void PcmQueueSteadyPathsAllocateNothing()
    {
        var queue = new AudioPcmBuffer();
        Span<float> data = stackalloc float[240]; data.Fill(0.25f);
        Span<float> output = stackalloc float[480];
        for (int i = 0; i < 100; i++) { queue.TryWrite(data); queue.Read(output); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) { queue.TryWrite(data); queue.Read(output); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.That(allocated == 0, "producer and consumer allocate zero bytes after construction");
    }
}
