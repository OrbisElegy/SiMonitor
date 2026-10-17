// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AbnormalCapnographyMeasurementSpecifications
{
    private static readonly Guid Channel = PhysiologyIllustrationSource.ChannelId(4);
    public static Specification[] All =>
    [
        new(nameof(AbnormalCapnogramsYieldTerminalValuesAndSingleBreaths), AbnormalCapnogramsYieldTerminalValuesAndSingleBreaths),
        new(nameof(ElevatedFlatCo2DoesNotInventExpirations), ElevatedFlatCo2DoesNotInventExpirations),
        new(nameof(AcquiredBaselineChangesAndBriefCleftsRetainRespiratoryRate), AcquiredBaselineChangesAndBriefCleftsRetainRespiratoryRate),
        new(nameof(ReversePlateauDetectionIsPartitionAndCheckpointIndependent), ReversePlateauDetectionIsPartitionAndCheckpointIndependent),
        new(nameof(TerminalDetectionHandlesRateAmplitudeAndSensorResponseChanges), TerminalDetectionHandlesRateAmplitudeAndSensorResponseChanges),
        new(nameof(CardiogenicAndCleftContoursDoNotMultiplyBreathsAcrossRates), CardiogenicAndCleftContoursDoNotMultiplyBreathsAcrossRates),
        new(nameof(EsophagealWashoutStopsRefreshingExpirationActivity), EsophagealWashoutStopsRefreshingExpirationActivity),
    ];

    private static void AbnormalCapnogramsYieldTerminalValuesAndSingleBreaths()
    {
        foreach (var (illustration, baseline) in new[]
        {
            (CapnogramIllustration.Reference, 0),
            (CapnogramIllustration.Rebreathing, 5),
            (CapnogramIllustration.ExhaustedAbsorber, 8),
            (CapnogramIllustration.Emphysema, 0),
            (CapnogramIllustration.CurareCleft, 0),
            (CapnogramIllustration.CardiogenicOscillations, 0),
            (CapnogramIllustration.RightMainstemIntubation, 0),
            (CapnogramIllustration.SpontaneousVentilation, 0),
            (CapnogramIllustration.IntermittentMandatoryVentilation, 0),
            (CapnogramIllustration.Obstructive, 0),
            (CapnogramIllustration.MechanicalObstruction, 0),
            (CapnogramIllustration.Pigtail, 0)
        })
        {
            var configuration = PhysiologyIllustrationConfiguration.Default with
            { CapnogramIllustration = illustration, Co2BaselineMmHg = baseline };
            var source = PhysiologyIllustrationSource.Create(configuration);
            var detector = new CapnographyMeasurement(Channel);
            List<MeasuredExpiration> breaths = [];
            for (int step = 1; step <= 120; step++)
            {
                foreach (byte[] wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
                { breaths.AddRange(detector.Consume(wire)); }
            }
            var result = detector.Read(23_990_000_000);
            Check.That(breaths.Count == 6 && result.RespirationsMilliPerMinute.Value is >= 15900 and <= 16100,
                $"{illustration}: internal notches and elevated inspired CO2 do not split or suppress complete breaths");
            int tolerance = illustration == CapnogramIllustration.CardiogenicOscillations ? 350 : 100;
            if (illustration == CapnogramIllustration.IntermittentMandatoryVentilation)
            {
                Check.That(breaths.All(breath => breath.EndTidalCentiMmHg is >= 3500 and <= 4100),
                    "mandatory and spontaneous breaths retain their measured terminal concentrations");
            }
            else
            {
                Check.That(breaths.All(breath => Math.Abs(breath.EndTidalCentiMmHg - 4000) < tolerance),
                    $"{illustration}: reported end-tidal CO2 follows the terminal segment, including reverse and terminal-rising plateaus");
            }
            Check.That(result.Activity is { Status: WaveformMeasurementStatus.Valid, LastExpirationNs: not null },
                "the ordinary no-expiration path receives measured complete expirations");
        }
    }

    private static void ElevatedFlatCo2DoesNotInventExpirations()
    {
        foreach (int baseline in new[] { 500, 800, 4000 })
        {
            var detector = new CapnographyMeasurement(Channel);
            for (int block = 0; block < 110; block++)
            {
                Check.That(detector.Consume(Wire(block * 20, 20, (ulong)block, _ => baseline)).Count == 0,
                    "a flat elevated concentration contains no completed respiratory excursion");
            }
            var result = detector.Read(21_990_000_000);
            Check.That(result.EndTidalCentiMmHg is { Status: WaveformMeasurementStatus.Stale, Value: null } &&
                result.Activity is { Status: WaveformMeasurementStatus.Valid, LastExpirationNs: null },
                "valid flat acquisition remains absence evidence without inventing an end-tidal numeric");
            Check.That(NoExpirationNotice.Evaluate(true, 20, 21_990_000_000, result.Activity) is not null,
                "the existing absence timer also works on clean elevated flat samples");
        }
    }

    private static void AcquiredBaselineChangesAndBriefCleftsRetainRespiratoryRate()
    {
        var detector = new CapnographyMeasurement(Channel);
        List<MeasuredExpiration> breaths = [];
        for (int block = 0; block < 60; block++)
        { breaths.AddRange(detector.Consume(Wire(block * 20, 20, (ulong)block, CleftWaveform))); }
        Check.That(breaths.Count == 6 && breaths.All(breath => breath.EndTidalCentiMmHg == 4000) &&
            detector.Read(11_990_000_000).RespirationsMilliPerMinute.Value == 30000,
            "raw 5-to-8mmHg baseline changes and brief intrabreath dips preserve six terminal values and the actual interval");
        foreach (int raisedBaseline in new[] { 1500, 2000, 3000 })
        {
            var raised = new CapnographyMeasurement(Channel);
            List<MeasuredExpiration> detected = [];
            int Waveform(int sample) => sample % 200 is >= 40 and < 100 ? 4000 : sample < 400 ? 0 : raisedBaseline;
            for (int block = 0; block < 100; block++)
            { detected.AddRange(raised.Consume(Wire(block * 20, 20, (ulong)block, Waveform))); }
            Check.That(detected.Count == 10 && raised.Read(19_990_000_000).Activity?.ContinuousUsableSinceNs == 0,
                "a large acquired baseline increase does not stall detection or reset continuous usable acquisition");
        }
    }

    private static void ReversePlateauDetectionIsPartitionAndCheckpointIndependent()
    {
        var whole = new CapnographyMeasurement(Channel);
        var split = new CapnographyMeasurement(Channel);
        List<MeasuredExpiration> expected = [], actual = [];
        for (int block = 0; block < 60; block++)
        {
            expected.AddRange(whole.Consume(Wire(block * 20, 20, (ulong)block, ReversePlateau)));
            actual.AddRange(split.Consume(Wire(block * 20, 7, (ulong)(block * 2), ReversePlateau)));
            var checkpoint = split.Capture();
            split = CapnographyMeasurement.Restore(checkpoint);
            byte[] tail = Wire(block * 20 + 7, 13, (ulong)(block * 2 + 1), ReversePlateau);
            var tailEvents = split.Consume(tail);
            actual.AddRange(tailEvents);
            Check.That(CapnographyMeasurement.Restore(checkpoint).Consume(tail).SequenceEqual(tailEvents),
                "continuing one restored detector cannot mutate the saved ring buffer");
            var before = split.Read((block * 20L + 19) * 10_000_000);
            bool rejected = false;
            try { split.Consume(tail); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected && split.Read((block * 20L + 19) * 10_000_000) == before,
                "duplicate rejection leaves baseline and terminal-descent state unchanged");
        }
        Check.That(expected.Count == 6 && expected.SequenceEqual(actual) &&
            whole.Read(11_990_000_000) == split.Read(11_990_000_000),
            "baseline and terminal landmarks are independent of block boundaries and midbreath checkpoint restores");
        Check.That(expected.All(breath => Math.Abs(breath.EndTidalCentiMmHg - 4000) < 50),
            "a 50mmHg early peak followed by a 40mmHg terminal segment reports the terminal segment");
        Check.That(expected.All(breath => breath.EndTidalTimeNs > breath.PeakTimeNs && breath.EndTidalTimeNs < breath.ConfirmedAtNs),
            "terminal and whole-cycle peak timestamps remain distinct acquired landmarks");
        var beforeFailure = split.Read(11_990_000_000);
        var malformed = WaveformEnvelopeCodec.Decode(Wire(1200, 20, 120, index => index < 1210 ? 0 : 4000));
        byte[] overflow = WaveformEnvelopeCodec.EncodeRaw(malformed with
        { Planes = [malformed.Planes[0] with { ScaleNumerator = int.MaxValue }] });
        bool overflowRejected = false;
        try { split.Consume(overflow); } catch (OverflowException) { overflowRejected = true; }
        Check.That(overflowRejected && split.Read(11_990_000_000) == beforeFailure,
            "overflow after valid prefix samples leaves the committed sample history and measurement unchanged");
    }

    private static void EsophagealWashoutStopsRefreshingExpirationActivity()
    {
        var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with
        { CapnogramIllustration = CapnogramIllustration.EsophagealIntubation });
        var detector = new CapnographyMeasurement(Channel);
        List<MeasuredExpiration> breaths = [];
        for (int step = 1; step <= 150; step++)
        {
            foreach (byte[] wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
            { breaths.AddRange(detector.Consume(wire)); }
        }
        var result = detector.Read(29_990_000_000);
        Check.That(breaths.Count is >= 1 and <= 4 && breaths.All(breath => breath.ConfirmedAtNs < 16_000_000_000) &&
            result.EndTidalCentiMmHg.Status == WaveformMeasurementStatus.Stale,
            "finite esophageal CO2 washout cannot refresh respiratory measurements after gas excursions disappear");
    }

    private static void TerminalDetectionHandlesRateAmplitudeAndSensorResponseChanges()
    {
        foreach (int breathsPerMinute in new[] { 8, 16, 30, 60 })
            foreach (int concentration in new[] { 25, 40, 55 })
                foreach (int fallMilliseconds in new[] { 200, 600 })
                    foreach (int dispersionMilliseconds in new[] { 0, 30 })
                    {
                        int period = 60000 / breathsPerMinute;
                        if (fallMilliseconds > period / 2) { continue; }
                        var configuration = PhysiologyIllustrationConfiguration.Default with
                        {
                            BreathPeriodMilliseconds = period,
                            InspirationMilliseconds = period / 2,
                            Co2EndExpiratoryMmHg = concentration,
                            Co2FallMilliseconds = fallMilliseconds,
                            Co2TransportDelayMilliseconds = 125,
                            Co2DispersionStepMilliseconds = dispersionMilliseconds,
                            CapnogramIllustration = CapnogramIllustration.Emphysema
                        };
                        var source = PhysiologyIllustrationSource.Create(configuration);
                        var detector = new CapnographyMeasurement(Channel);
                        bool verifyPartition = concentration == 40 && dispersionMilliseconds == 30 &&
                            (breathsPerMinute == 8 && fallMilliseconds == 600 || breathsPerMinute == 60 && fallMilliseconds == 200);
                        var partitioned = verifyPartition ? new CapnographyMeasurement(Channel) : null;
                        List<MeasuredExpiration> detected = [];
                        List<MeasuredExpiration> restored = [];
                        long durationNs = period * 4_000_000L + 1_000_000_000;
                        for (long time = 200_000_000; time <= durationNs; time += 200_000_000)
                        {
                            foreach (byte[] wire in source.AdvanceTo(time, 50, 1, 100))
                            {
                                detected.AddRange(detector.Consume(wire));
                                if (partitioned is null) { continue; }
                                var block = WaveformEnvelopeCodec.Decode(wire);
                                var plane = block.Planes.Single(plane => plane.ChannelId == Channel);
                                byte[] Part(int offset, int count, ulong sequence) => WaveformEnvelopeCodec.EncodeRaw(block with
                                {
                                    StartSimTimeNs = block.StartSimTimeNs + offset * 10_000_000L,
                                    DurationNs = checked((uint)count * 10_000_000),
                                    BlockSequence = sequence,
                                    Planes = [plane with
                                    {
                                        FirstSampleIndex = plane.FirstSampleIndex + (ulong)offset,
                                        Samples = plane.Samples.Skip(offset).Take(count).ToArray()
                                    }]
                                });
                                restored.AddRange(partitioned.Consume(Part(0, 7, block.BlockSequence * 2)));
                                var checkpoint = partitioned.Capture();
                                partitioned = CapnographyMeasurement.Restore(checkpoint);
                                byte[] tail = Part(7, 13, block.BlockSequence * 2 + 1);
                                var tailEvents = partitioned.Consume(tail);
                                restored.AddRange(tailEvents);
                                Check.That(CapnographyMeasurement.Restore(checkpoint).Consume(tail).SequenceEqual(tailEvents),
                                    "rounded descent continuation leaves the captured sample history immutable");
                                Check.That(partitioned.Read(time - 10_000_000) == detector.Read(time - 10_000_000),
                                    "rounded descent and dispersion produce identical readings after midblock restores");
                            }
                        }
                        Check.That(detected.Count >= 3 && detected.All(breath => Math.Abs(breath.EndTidalCentiMmHg - concentration * 100) < concentration * 7),
                            $"reverse plateau at {breathsPerMinute}/min, {concentration}mmHg, fall {fallMilliseconds}ms, dispersion {dispersionMilliseconds}ms follows terminal values rather than the 25% higher early peak");
                        Check.That(!verifyPartition || detected.SequenceEqual(restored),
                            "rounded terminal values and timestamps are independent of sample block partition and checkpoint restore");
                    }
    }

    private static void CardiogenicAndCleftContoursDoNotMultiplyBreathsAcrossRates()
    {
        foreach (var illustration in new[] { CapnogramIllustration.CurareCleft, CapnogramIllustration.CardiogenicOscillations })
            foreach (int breathsPerMinute in new[] { 8, 30, 60 })
                foreach (int heartRate in new[] { 40, 120 })
                {
                    int period = 60000 / breathsPerMinute;
                    var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with
                    {
                        BreathPeriodMilliseconds = period,
                        InspirationMilliseconds = period / 2,
                        CapnogramIllustration = illustration,
                        SeededRate = new(heartRate, new string('3', 64), 0)
                    });
                    var detector = new CapnographyMeasurement(Channel);
                    List<MeasuredExpiration> detected = [];
                    long durationNs = period * 4_000_000L + 1_000_000_000;
                    for (long time = 200_000_000; time <= durationNs; time += 200_000_000)
                    {
                        foreach (byte[] wire in source.AdvanceTo(time, 50, 1, 100))
                        { detected.AddRange(detector.Consume(wire)); }
                    }
                    Check.That(detected.Count == 4,
                        $"{illustration} at RR {breathsPerMinute} and HR {heartRate} counts expirations rather than intrabreath features");
                }
    }

    private static int CleftWaveform(int sample)
    {
        int phase = sample % 200;
        int baseline = sample < 400 ? 500 : 800;
        return phase switch
        {
            < 40 => baseline,
            >= 70 and < 85 => baseline,
            < 120 => 4000,
            < 140 => baseline + (4000 - baseline) * (140 - phase) / 20,
            _ => baseline
        };
    }

    private static int ReversePlateau(int sample)
    {
        int phase = sample % 200;
        return phase switch
        {
            < 40 => 800,
            < 120 => 5000 - (phase - 40) * 1000 / 80,
            < 140 => 800 + 3200 * (140 - phase) / 20,
            _ => 800
        };
    }

    private static byte[] Wire(int firstSample, int sampleCount, ulong sequence, Func<int, int> waveform)
    {
        short[] samples = Enumerable.Range(firstSample, sampleCount).Select(index => checked((short)waveform(index))).ToArray();
        var plane = new WaveformPlane(Channel, 100, 1, (ulong)firstSample, 1, 100, 0, 1,
            WaveformQualityEncoding.None, samples, []);
        return WaveformEnvelopeCodec.EncodeRaw(new(Channel, Channel, 1, 1, sequence, 1,
            firstSample * 10_000_000L, checked((uint)sampleCount * 10_000_000), [plane]));
    }
}
