// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class EcgDetectionRegressionSpecifications
{
    private const long StepNs = 4_000_000;
    private static readonly Guid Channel = PhysiologyIllustrationSource.ChannelId(0);

    public static Specification[] All =>
    [
        new(nameof(RotatingQrsRetainsEveryAcquiredBeat), RotatingQrsRetainsEveryAcquiredBeat),
        new(nameof(EcgDetectionDoesNotDependOnPacketBoundaries), EcgDetectionDoesNotDependOnPacketBoundaries),
        new(nameof(FastDetectionRelearnsAfterDisorganizationAndSignalLoss), FastDetectionRelearnsAfterDisorganizationAndSignalLoss),
        new(nameof(EcgRateRetainsRealPausesAndRateChanges), EcgRateRetainsRealPausesAndRateChanges),
    ];

    private static void RotatingQrsRetainsEveryAcquiredBeat()
    {
        foreach (var configuration in new[]
        {
            PhysiologyIllustrationConfiguration.VtPreset,
            PhysiologyIllustrationConfiguration.VtPreset with { VtBidirectional = true },
            PhysiologyIllustrationConfiguration.VtPreset with { VtTwisting = true }
        })
        {
            var source = PhysiologyIllustrationSource.Create(configuration);
            var owner = LiveWaveformMeasurements.CreateIllustration();
            var beats = new List<DetectedEcgBeat>();
            var rates = new HashSet<int>();
            for (int step = 1; step <= 700; step++)
            {
                foreach (byte[] wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
                {
                    var snapshot = owner.Consume(wire, out var detected);
                    beats.AddRange(detected);
                    if (snapshot.SampleTimeNs < 20_000_000_000) { continue; }
                    Check.That(snapshot.HeartRate is { Status: WaveformMeasurementStatus.Valid, MilliBeatsPerMinute: >= 150000 and <= 170000 },
                        "acquired changing QRS stays above 140 bpm without replacing the measurement: " + snapshot.HeartRate);
                    rates.Add(snapshot.HeartRate.MilliBeatsPerMinute!.Value);
                }
            }
            var truth = RegularPhysiologyTimeline.Start(configuration.ResolvePlan()).AdvanceBefore(81_000_000_000, 2000)
                .Where(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).ToArray();
            var expected = truth.Where(e => e.SimTimeNs >= 20_000_000_000 && e.SimTimeNs < 80_000_000_000).ToArray();
            Check.That(expected.Length == 160, "test oracle contains 160 ventricular activations in sixty seconds");
            foreach (var activation in expected)
            {
                var matches = beats.Where(b => b.PeakTimeNs >= activation.SimTimeNs &&
                    b.PeakTimeNs < activation.SimTimeNs + 160_000_000 + StepNs).ToArray();
                Check.That(matches.Length == 1, "exactly one sampled QRS peak per activation, including low rotating projections");
                Check.That(matches[0].ConfirmedAtNs > matches[0].PeakTimeNs &&
                    matches[0].ConfirmedAtNs <= activation.SimTimeNs + 300_000_000,
                    "confirmation remains causal and bounded, with no retrospective beat insertion");
            }
            var observed = beats.Where(b => b.PeakTimeNs >= 20_000_000_000 && b.PeakTimeNs < 80_000_000_000).ToArray();
            Check.That(observed.Length == 160 && observed.All(b => truth.Any(e =>
                b.PeakTimeNs >= e.SimTimeNs && b.PeakTimeNs < e.SimTimeNs + 160_000_000 + StepNs)),
                "no missed QRS offset by a false T-wave detection; boundary-straddling QRS uses the complete oracle");
            Check.That(rates.Count > 1, "sample peak movement remains visible rather than forcing a configured constant rate");
        }
    }

    private static void EcgDetectionDoesNotDependOnPacketBoundaries()
    {
        short[] samples = Acquire(PhysiologyIllustrationConfiguration.VtPreset with { VtTwisting = true }, 15);
        foreach (int polarity in new[] { 1, -1 })
        {
            short[] transformed = samples.Select(s => checked((short)(polarity * s + 600))).ToArray();
            var reference = new EcgHeartRateMeasurement(Channel);
            var expected = new List<DetectedEcgBeat>();
            for (int start = 0; start < transformed.Length; start += 750)
            {
                expected.AddRange(reference.Consume(Wire(transformed.Skip(start).Take(750).ToArray(), start, (ulong)(start / 750))));
            }
            foreach (int size in new[] { 1, 7, 50, 125 })
            {
                var detector = new EcgHeartRateMeasurement(Channel);
                var observed = new List<DetectedEcgBeat>();
                ulong sequence = 0;
                for (int start = 0; start < transformed.Length; start += size)
                {
                    byte[] wire = Wire(transformed.Skip(start).Take(size).ToArray(), start, sequence++);
                    var checkpoint = detector.Capture();
                    var restored = EcgHeartRateMeasurement.Restore(checkpoint);
                    var events = detector.Consume(wire);
                    observed.AddRange(events);
                    Check.That(events.SequenceEqual(restored.Consume(wire)), "restoration at any sample retains candidate and fast-rhythm evidence");
                    long lastSampleTimeNs = (Math.Min(start + size, transformed.Length) - 1) * StepNs;
                    Check.That(detector.Read(lastSampleTimeNs) == restored.Read(lastSampleTimeNs), "restored rates and validity agree exactly");
                }
                Check.That(observed.SequenceEqual(expected) && detector.Read((transformed.Length - 1) * StepNs) ==
                    reference.Read((transformed.Length - 1) * StepNs), "packet partition cannot change QRS decisions or confirmation times");
            }
        }
    }

    private static void FastDetectionRelearnsAfterDisorganizationAndSignalLoss()
    {
        short[] organized = Acquire(PhysiologyIllustrationConfiguration.VtPreset with { VtTwisting = true }, 12);
        foreach (var pattern in new[]
        {
            AvConductionPattern.VentricularFlutterIllustration,
            AvConductionPattern.VentricularFibrillationCoarseIllustration,
            AvConductionPattern.VentricularFibrillationFineIllustration
        })
        {
            var detector = new EcgHeartRateMeasurement(Channel);
            int offset = 0;
            ulong sequence = 0;
            Feed(organized, WaveformMeasurementStatus.Valid);
            Feed(Acquire(PhysiologyIllustrationConfiguration.Disorganized(pattern), 12), WaveformMeasurementStatus.Uncountable);
            Feed(new short[2000], WaveformMeasurementStatus.Stale);
            Feed(organized, WaveformMeasurementStatus.Valid);
            detector.Consume(Wire(new short[250], offset, sequence++, poor: true));
            offset += 250;
            Check.That(detector.Read((offset - 1) * StepNs) is { Status: WaveformMeasurementStatus.PoorSignal, MilliBeatsPerMinute: null },
                "quality failure discards prior rate and fast evidence");
            Feed(organized, WaveformMeasurementStatus.Valid);
            Check.That(detector.Read(offset * StepNs + 600_000_000).Status == WaveformMeasurementStatus.NoData,
                "transport silence remains distinct from live flatline or disorganization");

            void Feed(short[] samples, WaveformMeasurementStatus expected)
            {
                for (int start = 0; start < samples.Length; start += 50)
                {
                    detector.Consume(Wire(samples.Skip(start).Take(50).ToArray(), offset, sequence++));
                    offset += 50;
                    if (start < 1500) { continue; }
                    var reading = detector.Read((offset - 1) * StepNs);
                    Check.That(reading.Status == expected && (expected == WaveformMeasurementStatus.Valid || reading.MilliBeatsPerMinute is null),
                        "transition must forget previous fast evidence and recover from actual samples: " + pattern + "/" + expected + " -> " + reading);
                }
            }
        }
    }

    private static void EcgRateRetainsRealPausesAndRateChanges()
    {
        var detector = new EcgHeartRateMeasurement(Channel);
        ulong sequence = 0;
        for (int block = 0; block < 150; block++)
        {
            short[] samples = Enumerable.Range(block * 50, 50).Select(index =>
            {
                int period = index < 2500 ? 100 : 200;
                int phase = index % period;
                bool pause = index >= 5000 && index < 5500;
                return (short)(pause ? 0 : Math.Max(0, 1000 - Math.Abs(phase - 45) * 125));
            }).ToArray();
            var events = detector.Consume(Wire(samples, block * 50, sequence++));
            if (block is >= 101 and < 110) { Check.That(events.Count == 0, "a genuine pause is never filled with inferred beats"); }
            var reading = detector.Read((block * 50 + 49) * StepNs);
            if (block == 45) { Check.That(reading.MilliBeatsPerMinute == 150000, "initial fast rate is measured"); }
            if (block == 95) { Check.That(reading.MilliBeatsPerMinute == 75000, "real slowing survives morphology adaptation"); }
            if (block == 115) { Check.That(reading.MilliBeatsPerMinute < 75000, "the actual pause remains in bounded RR history"); }
            if (block == 149) { Check.That(reading.MilliBeatsPerMinute == 75000, "normal intervals eventually replace the pause"); }
        }
    }

    private static short[] Acquire(PhysiologyIllustrationConfiguration configuration, int seconds)
    {
        var source = PhysiologyIllustrationSource.Create(configuration);
        var samples = new List<short>();
        for (int step = 1; step <= (seconds + 3) * 5; step++)
        {
            foreach (byte[] wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
            {
                var plane = WaveformEnvelopeCodec.Decode(wire).Planes.Single(p => p.ChannelId == Channel);
                Check.That(plane.ScaleNumerator == 1 && plane.ScaleDenominator == 1 && plane.OffsetNumerator == 0,
                    "test repacketization preserves the source microvolt calibration");
                samples.AddRange(plane.Samples);
            }
        }
        Check.That(samples.Count >= seconds * 250, "acquisition delay is flushed before slicing the fixture");
        return samples.Take(seconds * 250).ToArray();
    }

    private static byte[] Wire(short[] samples, int firstSampleIndex, ulong sequence, bool poor = false)
    {
        var plane = new WaveformPlane(Channel, 250, 1, (ulong)firstSampleIndex, 1, 1, 0, 1,
            poor ? WaveformQualityEncoding.Ranges : WaveformQualityEncoding.None, samples,
            poor ? [new(0, (uint)samples.Length, 1)] : []);
        return WaveformEnvelopeCodec.EncodeRaw(new(Channel, Channel, 1, 1, sequence, 1,
            firstSampleIndex * StepNs, checked((uint)(samples.Length * StepNs)), [plane]));
    }
}
