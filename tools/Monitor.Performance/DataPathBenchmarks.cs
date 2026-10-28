// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Performance;

internal static class DataPathBenchmarks
{
    private const string Variant = "current";
    private static ulong _sink;
    private static bool _verifyOnly;
    private static readonly RegularPhysiologyPlan Plan = new(
        0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    private static readonly Guid EcgChannel = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid PlethChannel = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid PressureChannel = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static bool IsTieredCompilationEnabled => Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") != "0";
    private static void Write(object row) => Console.WriteLine(JsonSerializer.Serialize(row));

    public static int Execute(string[] args)
    {
        if (args is not ([] or ["--verify-only"]))
        {
            Console.Error.WriteLine("Usage: --data-paths [--verify-only]");
            return 2;
        }

        _verifyOnly = args.Length != 0;
        try
        {
            Run();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Run()
    {
        Write(new
        {
            kind = "environment",
            variant = Variant,
            runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            tiered = IsTieredCompilationEnabled,
            vectorBits = Vector<byte>.Count * 8,
            host = Environment.MachineName,
        });
        MeasureFixedPoint();
        MeasureOptical();
        MeasurePipelines();
        MeasureStreamPipelines();
        MeasureOxygen();
        MeasureSession();
        Write(new { kind = "completed", variant = Variant, sink = _sink });
    }

    private static void Fingerprint(string name, byte[] data) =>
        Write(new { kind = "fingerprint", name, sha256 = Convert.ToHexString(SHA256.HashData(data)) });

    private static void Fingerprint(string name, object data) => Fingerprint(name, JsonSerializer.SerializeToUtf8Bytes(data));

    private static void MeasureFixedPoint()
    {
        long[] left = new long[4096], right = new long[4096];
        var random = new Random(1452);
        for (int i = 0; i < left.Length; i++)
        {
            left[i] = random.NextInt64(-FixedPointMath.Q62One, FixedPointMath.Q62One);
            right[i] = random.NextInt64(-32767L * FixedPointMath.Q32One, 32767L * FixedPointMath.Q32One);
        }

        foreach (int shift in new[] { 32, 62 })
        {
            var results = new List<SaturatingInt64>();
            foreach (long x in new[] { long.MinValue, long.MinValue + 1, -3L, -1L, 0L, 1L, 3L, long.MaxValue })
            {
                foreach (long y in new[] { long.MinValue, -3L, -1L, 0L, 1L, 3L, long.MaxValue })
                { Check(x, y); }
            }
            for (int i = 0; i < left.Length; i++) { Check(left[i], right[i]); }
            Fingerprint("fixed/q" + shift, results);
            Measure("fixed/q" + shift + "/4096", () =>
            {
                ulong sum = 0;
                for (int i = 0; i < left.Length; i++)
                { sum = unchecked(sum + (ulong)Product(left[i], right[i]).Value); }
                return sum;
            });

            SaturatingInt64 Product(long x, long y) => shift == 32
                ? FixedPointMath.MultiplyQ32(x, y) : FixedPointMath.MultiplyCoefficientQ62(x, y);

            void Check(long x, long y)
            {
                BigInteger product = (BigInteger)x * y, divisor = BigInteger.One << shift;
                var quotient = BigInteger.DivRem(BigInteger.Abs(product), divisor, out var remainder);
                if (remainder * 2 > divisor || remainder * 2 == divisor && !quotient.IsEven) { quotient++; }
                if (product.Sign < 0) { quotient = -quotient; }
                var expected = new SaturatingInt64((long)BigInteger.Clamp(quotient, long.MinValue, long.MaxValue),
                    quotient < long.MinValue || quotient > long.MaxValue);
                var actual = Product(x, y);
                if (actual != expected) { throw new InvalidOperationException("Fixed-point oracle mismatch."); }
                results.Add(actual);
            }
        }
    }

    private static OpticalSample[] OpticalWindow(int offset = 0) => Enumerable.Range(0, 500).Select(i =>
        new OpticalSample(i * 8_000_000L, 400_000 + (int)(5000 * Math.Sin((i + offset) * Math.PI / 50)),
            500_000 + (int)(10000 * Math.Sin((i + offset) * Math.PI / 50)))).ToArray();

    private static void MeasureOptical()
    {
        var estimator = OpticalSaturationMeasurement.CreateIllustration();
        var corpus = new List<object>();
        for (int test = 0; test < 40; test++)
        {
            var samples = OpticalWindow(test);
            if (test == 20) { samples[1] = samples[1] with { Red = int.MaxValue }; }
            if (test == 21) { samples[1] = samples[1] with { Infrared = int.MinValue }; }
            if (test == 22) { samples[30] = samples[30] with { QualityFlags = 1 }; }
            if (test == 23) { samples[15] = samples[15] with { SampleTimeNs = 0 }; }
            if (test == 24) { samples[40] = null!; }
            if (test == 25) { samples = samples[..300]; }
            if (test == 26) { samples = []; }
            long endNs = samples.Length == 0 ? 0 : samples[^1].SampleTimeNs;
            foreach (long atNs in new[] { -1L, endNs, endNs + 500_000_000, endNs + 500_000_001 })
            {
                try { corpus.Add(estimator.Estimate(samples, atNs)); }
                catch (ArgumentException exception) { corpus.Add(new { error = exception.GetType().Name, exception.ParamName }); }
            }
        }
        Fingerprint("optical/valid-invalid-stale", corpus);
        var normal = OpticalWindow();
        Measure("optical/estimate-500", () => (ulong)(estimator.Estimate(normal, normal[^1].SampleTimeNs).SaturationMilliPercent ?? 0));
        var acquisition = new OpticalSaturationAcquisition(EcgChannel, PlethChannel, estimator);
        var cacheChecks = new List<object>();
        for (int block = 0; block < 10; block++)
        {
            short[] red = normal.Skip(block * 50).Take(50).Select(s => (short)(s.Red / 20)).ToArray();
            short[] infrared = normal.Skip(block * 50).Take(50).Select(s => (short)(s.Infrared / 20)).ToArray();
            var envelope = new WaveformEnvelope(EcgChannel, PlethChannel, 1, 1, (ulong)block, 1, block * 400_000_000L, 400_000_000,
            [
                new(EcgChannel, 125, 1, (ulong)(block * 50), 20, 1, 0, 1, WaveformQualityEncoding.None, red, []),
                new(PlethChannel, 125, 1, (ulong)(block * 50), 20, 1, 0, 1, WaveformQualityEncoding.None, infrared, [])
            ]);
            cacheChecks.Add(acquisition.Consume(WaveformEnvelopeCodec.EncodeRaw(envelope)));
            acquisition = OpticalSaturationAcquisition.Restore(acquisition.Capture());
            long lastNs = (block * 50 + 49) * 8_000_000L;
            foreach (long atNs in new[] { lastNs, lastNs + 500_000_000, lastNs + 500_000_001, lastNs - 1, -1 })
            {
                try { cacheChecks.Add(acquisition.Read(atNs)); }
                catch (ArgumentException exception) { cacheChecks.Add(new { error = exception.GetType().Name, exception.ParamName }); }
            }
        }
        Fingerprint("optical/cache-restore-stale", cacheChecks);
        Measure("optical/acquisition-read", () => (ulong)(acquisition.Read(normal[^1].SampleTimeNs).SaturationMilliPercent ?? 0));
    }

    private static PhysiologyWaveformGroup CreateGroup(string name)
    {
        var channels = new List<PhysiologyWaveformChannelPlan>();
        if (name is "ecg" or "all")
        {
            channels.Add(new(Plan, new(EcgChannel, "AcqECGMonitor250@1", 1, 1, 0, 1), TextbookEcgReference.CreateBands(), 0));
        }
        if (name is "pleth" or "all")
        {
            channels.Add(new(Plan, new(PlethChannel, "AcqPleth125@1", 1, 1, 0, 1), [], 0,
                PlethRunoff: new(80_000_000, 512_000_000, 1000)));
        }
        if (name is "pressure" or "all")
        {
            channels.Add(new VascularPressurePlan(80_000_001, 240_000_000, 1_500_000_000, 10000, 1200, 26000)
                .CreateChannel(Plan, PressureChannel, 0));
        }
        var group = PhysiologyWaveformGroup.Start(EcgChannel, PlethChannel, 1, 1, 1, 0, 128, channels);
        _ = group.AdvanceTo(20_000_000_000, 5000, 100, 1000);
        return group;
    }

    private static void MeasurePipelines()
    {
        foreach (string name in new[] { "ecg", "pleth", "pressure", "all" })
        {
            var group = CreateGroup(name);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var check = group.Fork();
            for (int block = 1; block <= 10; block++)
            {
                foreach (byte[] wire in check.AdvanceTo(20_000_000_000 + block * 200_000_000L, 50, 1, 100))
                { hash.AppendData(wire); }
            }
            Write(new { kind = "fingerprint", name = "pipeline/" + name, sha256 = Convert.ToHexString(hash.GetHashAndReset()) });
            Measure("pipeline/" + name + "/200ms", () =>
            {
                var trial = group.Fork();
                return trial.AdvanceTo(20_200_000_000, 50, 1, 100)[0][^1];
            });
        }
    }

    private static void MeasureStreamPipelines()
    {
        foreach (string name in new[] { "pleth", "pressure", "pressure-morphology" })
        {
            bool pressure = name != "pleth";
            var generator = PhysiologySignalGenerator.Start(Plan,
                pressure ? "AcqPressure125@1" : "AcqPleth125@1", 1, [],
                vascularPressure: pressure ? new VascularPressurePlan(80_000_001, 240_000_000,
                    1_500_000_000, 10000, 1200, 26000, Morphology: name == "pressure-morphology"
                        ? new(VascularPressureMorphologyKind.Arterial, 600_000_000, 4000) : null) : null,
                plethRunoff: pressure ? null : new PlethRunoffPlan(80_000_000, 512_000_000, 1000));
            long nextTimeNs = 20_000_000_000;
            _ = generator.GenerateBefore(nextTimeNs, 5000, 1000);
            Measure("stream/" + name + "/1s", () =>
            {
                nextTimeNs += 1_000_000_000;
                var samples = generator.GenerateBefore(nextTimeNs, 125, 100);
                return unchecked((ulong)samples[^1].ValueQ32);
            });
        }
    }

    private static void MeasureOxygen()
    {
        var source = new RealtimeOxygenationSource(PhysiologyTransportSource.Create(
            Plan, new(450000, 150000, 210000), new(66667, 240_000_000)), new());
        for (int i = 1; i <= 20; i++) { source.AdvanceTo(i * 1_000_000_000L); }
        var check = source.Fork();
        check.AdvanceTo(20_200_000_000);
        Fingerprint("oxygen/snapshot", check.Snapshot);
        Measure("oxygen/200ms", () =>
        {
            var trial = source.Fork();
            trial.AdvanceTo(20_200_000_000);
            return (ulong)trial.Snapshot.SaturationMilliPercent;
        });
    }

    private static LocalMonitorPreviewSession CreateSession(bool twelveLead)
    {
        var session = new LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration.Default,
            MonitorDisplayConfiguration.Default(), enableMeasurements: true, opticalSaturationMilliPercent: 98000,
            enableTwelveLead: twelveLead);
        while (session.SimulationTimeNs < 45_000_000_000L) { session.Advance(50_000_000L); }
        return session;
    }

    private static ulong ScanEcgHistory(LocalMonitorPreviewSession session)
    {
        ulong sum = 0;
        for (int strip = 0; strip < 13; strip++)
        {
            long durationNs = strip == 12 ? 10_000_000_000L : 2_500_000_000L;
            long cycle = session.SimulationTimeNs / durationNs;
            for (int age = 0; age < 2; age++)
            {
                long fromNs = (cycle - age) * durationNs;
                foreach (var sample in session.EcgSamples(strip == 12 ? EcgLead.II : (EcgLead)strip, fromNs, fromNs + durationNs))
                { sum = unchecked(sum + (ulong)BitConverter.DoubleToInt64Bits(sample.Value) + (ulong)sample.TimeNs); }
            }
        }
        return sum;
    }

    private static void MeasureSession()
    {
        var session = CreateSession(true);
        var samples = new List<(long TimeNs, long ValueBits)>();
        for (int lead = 0; lead < 12; lead++)
        {
            foreach (var sample in session.EcgSamples((EcgLead)lead, 40_000_000_000, 45_000_000_000))
            { samples.Add((sample.TimeNs, BitConverter.DoubleToInt64Bits(sample.Value))); }
        }
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
        {
            foreach (var sample in samples)
            {
                writer.Write(sample.TimeNs);
                writer.Write(sample.ValueBits);
            }
        }
        Fingerprint("session/twelve-lead", bytes.ToArray());
        Fingerprint("session/measurements", session.Measurements!);
        Measure("history/26-window-reads", () => ScanEcgHistory(session));
        Measure("session/measurements-getter", () => (ulong)(session.Measurements!.SpO2.SaturationMilliPercent ?? 0));
        if (_verifyOnly) { return; }
        MeasureLiveSession(session, true);
        MeasureLiveSession(CreateSession(false), false);
    }

    private static void MeasureLiveSession(LocalMonitorPreviewSession session, bool twelveLead)
    {
        // All variants measure exactly the same 45s..52s source-time sequence.
        // Preparation is outside the timer; fullopts is primary for these fixed-length trials.
        const int frames = 60;
        double[] timesNs = new double[7], allocatedBytes = new double[7];
        var snapshots = new List<LiveMeasurementSnapshot>();
        for (int batch = 0; batch < 7; batch++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int frame = 0; frame < frames; frame++)
            {
                session.Advance(16_666_667);
                _sink ^= (ulong)(session.Measurements!.SpO2.SaturationMilliPercent ?? 0);
                _sink ^= (ulong)(session.Measurements!.SpO2.SaturationMilliPercent ?? 0);
                if (twelveLead) { _sink ^= ScanEcgHistory(session); }
            }
            timesNs[batch] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / frames;
            allocatedBytes[batch] = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / (double)frames;
            snapshots.Add(session.Measurements!);
        }
        Write(new
        {
            kind = "measurement",
            name = "live/" + (twelveLead ? "twelve-lead" : "monitor") + "/frame",
            medianNs = timesNs.Order().ElementAt(3),
            medianAllocatedBytes = allocatedBytes.Order().ElementAt(3),
            times = timesNs,
            allocated = allocatedBytes,
            frames,
        });
        Fingerprint("live/" + (twelveLead ? "twelve-lead" : "monitor") + "/snapshots", snapshots);
    }

    private static void Measure(string name, Func<ulong> operation)
    {
        if (_verifyOnly) { return; }
        long start = Stopwatch.GetTimestamp();
        do
        {
            _sink ^= operation();
        }
        while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < (IsTieredCompilationEnabled ? 3000 : 200));
        if (IsTieredCompilationEnabled) { Thread.Sleep(1000); }
        start = Stopwatch.GetTimestamp();
        int count = 0;
        do
        {
            _sink ^= operation();
            count++;
        }
        while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < (IsTieredCompilationEnabled ? 500 : 150));
        double estimateNs = Stopwatch.GetElapsedTime(start).TotalNanoseconds / count;
        int iterations = (int)Math.Clamp(50_000_000 / Math.Max(estimateNs, 1), 1, 1_000_000);
        double[] timesNs = new double[7], allocatedBytes = new double[7];
        for (int batch = 0; batch < 7; batch++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            start = Stopwatch.GetTimestamp();
            ulong checksum = 0;
            for (int i = 0; i < iterations; i++) { checksum = unchecked(checksum + operation()); }
            timesNs[batch] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
            allocatedBytes[batch] = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / (double)iterations;
            _sink ^= checksum;
        }
        Write(new
        {
            kind = "measurement",
            name,
            medianNs = timesNs.Order().ElementAt(3),
            medianAllocatedBytes = allocatedBytes.Order().ElementAt(3),
            times = timesNs,
            allocated = allocatedBytes,
            iterations,
        });
    }
}
