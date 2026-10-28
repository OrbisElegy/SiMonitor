// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Audio;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Performance;

internal static class Program
{
    private static readonly RegularPhysiologyPlan Physiology = new(
        0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    private static readonly Guid EcgChannel = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid PlethChannel = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid PressureChannel = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static ulong _sink;

    public static int Main(string[] args)
    {
        bool verifyOnly = false;
        string label = "current";
        string? expectedPath = null;
        var casePrefixes = new List<string>();
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--verify-only":
                    verifyOnly = true;
                    break;
                case "--label" when index + 1 < args.Length:
                    label = args[++index];
                    break;
                case "--expect-fingerprints" when index + 1 < args.Length:
                    expectedPath = args[++index];
                    break;
                case "--case-prefix" when index + 1 < args.Length:
                    casePrefixes.Add(args[++index]);
                    break;
                default:
                    Console.Error.WriteLine("Usage: [--verify-only] [--label NAME] [--expect-fingerprints JSONL_PATH] [--case-prefix PREFIX]...");
                    return 2;
            }
        }

        try
        {
            Write(new
            {
                kind = "environment",
                label,
                host = Environment.MachineName,
                runtime = RuntimeInformation.FrameworkDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                os = RuntimeInformation.OSDescription,
                processorCount = Environment.ProcessorCount,
                tieredCompilation = RuntimeSetting("TieredCompilation"),
                tieredPgo = RuntimeSetting("TieredPGO"),
                readyToRun = RuntimeSetting("ReadyToRun"),
                hardwareIntrinsics = RuntimeSetting("EnableHWIntrinsic"),
                avx2 = RuntimeSetting("EnableAVX2"),
                avx512 = RuntimeSetting("EnableAVX512"),
                warmupMilliseconds = IsTieredCompilationDisabled ? 150 : 3000,
                compilationSettleMilliseconds = IsTieredCompilationDisabled ? 0 : 1000,
                calibrationMilliseconds = IsTieredCompilationDisabled ? 150 : 500,
                measurementBatches = 7,
                casePrefixes,
            });
            Dictionary<string, string> expected = ReadExpectedFingerprints(expectedPath);
            VerifyFingerprints(label, expected);
            if (!verifyOnly)
            {
                MeasurePipelines(label, casePrefixes);
            }

            Write(new { kind = "completed", label, verifyOnly, sink = _sink });
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static bool IsTieredCompilationDisabled => RuntimeSetting("TieredCompilation") == "0";

    private static string RuntimeSetting(string name) =>
        Environment.GetEnvironmentVariable("DOTNET_" + name) ??
        Environment.GetEnvironmentVariable("COMPlus_" + name) ?? "default";

    private static Dictionary<string, string> ReadExpectedFingerprints(string? path)
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        if (path is null)
        {
            return expected;
        }

        foreach (string line in File.ReadLines(path, System.Text.Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.GetProperty("kind").GetString() == "fingerprint")
            {
                expected.Add(root.GetProperty("name").GetString()!, root.GetProperty("sha256").GetString()!);
            }
        }

        if (expected.Count != 6)
        {
            throw new ArgumentException("Expected six pipeline fingerprints in the comparison JSONL file.", nameof(path));
        }

        return expected;
    }

    private static void VerifyFingerprints(string label, Dictionary<string, string> expected)
    {
        foreach (bool morphology in new[] { false, true })
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            PhysiologyWaveformGroup group = CreateGroup(morphology);
            int blocks = 0;
            for (int blockIndex = 1; blockIndex <= 150; blockIndex++)
            {
                foreach (byte[] wire in group.AdvanceTo(blockIndex * 200_000_000L, 50, 1, 100))
                {
                    hash.AppendData(wire);
                    _ = WaveformEnvelopeCodec.Decode(wire);
                    blocks++;
                }
            }

            if (blocks != 150)
            {
                throw new InvalidOperationException("The 30-second waveform workload must produce 150 envelopes.");
            }

            EmitFingerprint($"group/{(morphology ? "morphology" : "plain")}/30s", hash);
        }

        using var eventHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        PhysiologySignalGenerator generator = CreateEcgGenerator();
        _ = generator.GenerateBefore(20_000_000_000, 5000, 1000);
        Span<byte> sampleBytes = stackalloc byte[18];
        foreach (PhysiologySignalSample sample in generator.GenerateBefore(24_000_000_000, 1000, 100))
        {
            BinaryPrimitives.WriteInt64LittleEndian(sampleBytes, sample.Tick.SimTimeNs);
            BinaryPrimitives.WriteInt64LittleEndian(sampleBytes[8..], sample.ValueQ32);
            BinaryPrimitives.WriteInt16LittleEndian(sampleBytes[16..], sample.NormalizedValue);
            eventHash.AppendData(sampleBytes);
        }

        EmitFingerprint("generator/ecg/4s", eventHash);
        PhysiologyWaveformGroup continuation = CreateEcgContinuation();
        foreach (byte[] wire in continuation.AdvanceTo(24_200_000_000, 1000, 20, 100))
        {
            eventHash.AppendData(wire);
            _ = WaveformEnvelopeCodec.Decode(wire);
        }

        EmitFingerprint("group/ecg-continuation/4s", eventHash);

        using var audioHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (int pitch in new[] { 70, 85, 97 })
        {
            var renderer = new SampleToneRenderer();
            RequireAccepted(renderer.Schedule(1, SelectedMonitorTones.Heartbeat(37, pitch), 0, long.MaxValue));
            RequireAccepted(renderer.Schedule(2, SelectedMonitorTones.Alarm(MonitorNoticeLevel.Warning, 29), 31, long.MaxValue));
            float[] samples = new float[257];
            for (int blockIndex = 0; blockIndex < 32; blockIndex++)
            {
                if (blockIndex == 5)
                {
                    renderer.Cancel(1);
                }

                renderer.Render(samples);
                AppendAudio(audioHash, samples);
            }
        }

        EmitFingerprint("audio/cancel/pitch", audioHash);
        SampleToneRenderer eightVoices = CreateAudioRenderer(8);
        float[] output = new float[256];
        for (int blockIndex = 0; blockIndex < 256; blockIndex++)
        {
            eightVoices.Render(output);
            AppendAudio(audioHash, output);
        }

        EmitFingerprint("audio/8voices/65536frames", audioHash);

        void EmitFingerprint(string name, IncrementalHash hash)
        {
            string sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (expected.Count != 0 && (!expected.TryGetValue(name, out string? reference) ||
                !string.Equals(reference, sha256, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Pipeline fingerprint mismatch: {name}; actual SHA-256 {sha256}.");
            }

            Write(new { kind = "fingerprint", label, name, sha256, compared = expected.Count != 0 });
        }
    }

    private static void AppendAudio(IncrementalHash hash, ReadOnlySpan<float> samples)
    {
        Span<byte> bytes = stackalloc byte[samples.Length * sizeof(float)];
        for (int index = 0; index < samples.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes[(index * sizeof(float))..], BitConverter.SingleToInt32Bits(samples[index]));
        }

        hash.AppendData(bytes);
    }

    private static void MeasurePipelines(string label, List<string> casePrefixes)
    {
        foreach (bool morphology in new[] { false, true })
        {
            PhysiologyWaveformGroup prepared = CreateGroup(morphology);
            _ = prepared.AdvanceTo(20_000_000_000, 5000, 100, 1000);
            MeasureCase($"group/{(morphology ? "morphology" : "plain")}/200ms", 100, "samples", () =>
            {
                PhysiologyWaveformGroup trial = prepared.Fork();
                IReadOnlyList<byte[]> wire = trial.AdvanceTo(20_200_000_000, 50, 1, 100);
                ulong checksum = (ulong)wire.Count;
                foreach (byte[] block in wire)
                {
                    checksum += block[^1];
                }

                return checksum;
            });
        }

        foreach (int voices in new[] { 1, 8 })
        {
            SampleToneRenderer renderer = CreateAudioRenderer(voices);
            float[] output = new float[256];
            MeasureCase($"audio/{voices}voices/256frames", 256, "frames", () =>
            {
                // Reset before the 168000-frame clip ends; reset cost is included
                // and amortized over 625 render calls in both comparison builds.
                if (renderer.Position >= 160000)
                {
                    renderer = CreateAudioRenderer(voices);
                }

                renderer.Render(output);
                return unchecked((uint)BitConverter.SingleToInt32Bits(output[0]));
            });
        }

        PhysiologySignalGenerator generator = CreateEcgGenerator();
        _ = generator.GenerateBefore(20_000_000_000, 5000, 1000);
        long generatorToExclusiveSimTimeNs = 20_000_000_000;
        MeasureCase("generator/ecg/4s", 1000, "samples", () =>
        {
            generatorToExclusiveSimTimeNs += 4_000_000_000;
            IReadOnlyList<PhysiologySignalSample> samples = generator.GenerateBefore(generatorToExclusiveSimTimeNs, 1000, 100);
            return unchecked((ulong)samples[^1].ValueQ32);
        });

        PhysiologyWaveformGroup continuation = CreateEcgContinuation();
        MeasureCase("group/ecg-continuation/200ms", 50, "samples", () =>
        {
            PhysiologyWaveformGroup trial = continuation.Fork();
            IReadOnlyList<byte[]> wire = trial.AdvanceTo(20_400_000_000, 50, 1, 100);
            return (ulong)wire.Count + wire[0][^1];
        });

        void MeasureCase(string name, int units, string unit, Func<ulong> operation)
        {
            if (casePrefixes.Count == 0 || casePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                Measure(label, name, units, unit, operation);
            }
        }
    }

    private static void Measure(string label, string name, int units, string unit, Func<ulong> operation)
    {
        int warmupMilliseconds = IsTieredCompilationDisabled ? 150 : 3000;
        long start = Stopwatch.GetTimestamp();
        do
        {
            _sink ^= operation();
        }
        while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < warmupMilliseconds);

        // With a single CPU affinity, a busy warmup competes with background
        // tier compilation. Give that worker a quiet interval, then exercise
        // the newly compiled code again before choosing measurement iterations.
        // This measures warmed throughput, not cold-start/callback latency.
        if (!IsTieredCompilationDisabled) { Thread.Sleep(1000); }
        int calibrationMilliseconds = IsTieredCompilationDisabled ? 150 : 500;
        start = Stopwatch.GetTimestamp();
        int calibrationIterations = 0;
        do
        {
            _sink ^= operation();
            calibrationIterations++;
        }
        while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < calibrationMilliseconds);

        double estimateNs = Stopwatch.GetElapsedTime(start).TotalNanoseconds / calibrationIterations;
        int iterations = (int)Math.Clamp(50_000_000 / Math.Max(estimateNs, 1), 1, 1_000_000);
        double[] timingsNs = new double[7];
        double[] allocatedBytes = new double[7];
        for (int batch = 0; batch < timingsNs.Length; batch++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            start = Stopwatch.GetTimestamp();
            ulong checksum = 0;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                checksum = unchecked(checksum + operation());
            }

            timingsNs[batch] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
            allocatedBytes[batch] = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / (double)iterations;
            _sink ^= checksum;
        }

        double[] sortedTimingsNs = timingsNs.Order().ToArray();
        Write(new
        {
            kind = "measurement",
            label,
            name,
            units,
            unit,
            iterations,
            batches = timingsNs.Length,
            medianNs = sortedTimingsNs[3],
            minNs = sortedTimingsNs[0],
            maxNs = sortedTimingsNs[^1],
            medianAllocatedBytes = allocatedBytes.Order().ElementAt(3),
            timingsNs,
        });
    }

    private static SampleToneRenderer CreateAudioRenderer(int voices)
    {
        var renderer = new SampleToneRenderer();
        for (int voice = 0; voice < voices; voice++)
        {
            RequireAccepted(renderer.Schedule(voice, SelectedMonitorTones.Alarm(MonitorNoticeLevel.Critical, 5), 0, long.MaxValue));
        }

        return renderer;
    }

    private static PhysiologySignalGenerator CreateEcgGenerator() =>
        PhysiologySignalGenerator.Start(Physiology, "AcqECGMonitor250@1", 1, TextbookEcgReference.CreateBands());

    private static PhysiologyWaveformGroup CreateEcgContinuation()
    {
        PhysiologyWaveformGroup group = EcgGroup(Physiology);
        _ = group.AdvanceTo(20_200_000_000, 5050, 101, 1000);
        group.ContinueWith(EcgGroup(Physiology with { HeartPeriodNs = 1_000_000_000 }));
        return group;

        static PhysiologyWaveformGroup EcgGroup(RegularPhysiologyPlan plan) =>
            PhysiologyWaveformGroup.Start(EcgChannel, PlethChannel, 1, 1, 1, 0, 128,
                [new(plan, new(EcgChannel, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 0)]);
    }

    private static void RequireAccepted(ToneScheduleResult result)
    {
        if (result != ToneScheduleResult.Accepted)
        {
            throw new InvalidOperationException($"Benchmark audio scheduling failed: {result}.");
        }
    }

    private static PhysiologyWaveformGroup CreateGroup(bool morphology) =>
        PhysiologyWaveformGroup.Start(EcgChannel, PlethChannel, 1, 1, 1, 0, 128,
        [
            new(Physiology, new(EcgChannel, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 0),
            new(Physiology, new(PlethChannel, "AcqPleth125@1", 1, 1, 0, 1), [], 0,
                PlethRunoff: new(80_000_000, 512_000_000, 1000)),
            new VascularPressurePlan(80_000_001, 240_000_000, 1_500_000_000, 10000, 1200, 26000,
                Morphology: morphology ? new(VascularPressureMorphologyKind.Arterial, 600_000_000, 4000) : null)
                .CreateChannel(Physiology, PressureChannel, 0),
        ]);

    private static void Write<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value));
}
