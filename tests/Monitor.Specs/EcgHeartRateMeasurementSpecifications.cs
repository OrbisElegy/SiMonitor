// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class EcgHeartRateMeasurementSpecifications
{
    private static readonly Guid Channel = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static Specification[] All =>
    [
        new(nameof(TwistingMorphologyPreservesMeasuredFluctuationAndRecovery), TwistingMorphologyPreservesMeasuredFluctuationAndRecovery),
        new(nameof(EcgRateComesFromAcquiredQrsAndHandlesPolarityAndFastRates), EcgRateComesFromAcquiredQrsAndHandlesPolarityAndFastRates),
        new(nameof(EcgContinuousDisorganizationDoesNotBecomeANormalRate), EcgContinuousDisorganizationDoesNotBecomeANormalRate),
        new(nameof(EcgMeasurementFencesBadInputAndRestoresPartialCandidates), EcgMeasurementFencesBadInputAndRestoresPartialCandidates),
    ];

    private static void TwistingMorphologyPreservesMeasuredFluctuationAndRecovery()
    {
        foreach (var configuration in new[]
        {
            PhysiologyIllustrationConfiguration.AarPreset,
            PhysiologyIllustrationConfiguration.SvtPreset,
            PhysiologyIllustrationConfiguration.SvtPreset with { SvtRbbb = true },
            PhysiologyIllustrationConfiguration.SvtPreset with { SvtLbbb = true },
            PhysiologyIllustrationConfiguration.VtPreset,
            PhysiologyIllustrationConfiguration.VtPreset with { VtFusion = true },
            PhysiologyIllustrationConfiguration.VtPreset with { VtCapture = true },
            PhysiologyIllustrationConfiguration.VtPreset with { VtBidirectional = true },
            PhysiologyIllustrationConfiguration.VtPreset with { VtTwisting = true }
        })
        {
            var source = PhysiologyIllustrationSource.Create(configuration);
            var detector = new EcgHeartRateMeasurement(Channel);
            EcgHeartRateMeasurement? restored = null;
            var rates = new HashSet<int>();
            for (int step = 1; step <= 210; step++)
                foreach (var wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
                {
                    var events = detector.Consume(wire);
                    long last = WaveformEnvelopeCodec.Decode(wire).StartSimTimeNs + 196_000_000;
                    var reading = detector.Read(last);
                    if (restored is not null)
                    {
                        Check.That(events.SequenceEqual(restored.Consume(wire)) && reading == restored.Read(last),
                            "twisting measurement fluctuations and beat confirmations survive checkpoint recovery");
                    }
                    else if (last >= 12_000_000_000) { restored = EcgHeartRateMeasurement.Restore(detector.Capture()); }
                    Check.That(reading.Status != WaveformMeasurementStatus.Uncountable,
                        "twisting discrete complexes must not be classified as ventricular disorganization");
                    if (last < 10_000_000_000) { continue; }
                    if (reading.Status == WaveformMeasurementStatus.Valid)
                    { Check.That(reading.MilliBeatsPerMinute > 0, "valid measurement retains a numeric rate"); rates.Add(reading.MilliBeatsPerMinute!.Value); }
                }
            Check.That(rates.Count > 0, "organized rhythm retains measured numeric rates");
            if (configuration.VtTwisting)
            { Check.That(rates.Count > 1, "single-lead morphology variation retains measured fluctuations"); }
        }
    }

    private static void EcgRateComesFromAcquiredQrsAndHandlesPolarityAndFastRates()
    {
        var source = PhysiologyIllustrationSource.Create();
        var measured = new EcgHeartRateMeasurement(Channel); List<DetectedEcgBeat> beats = []; long last = 0;
        for (int step = 1; step <= 110; step++)
            foreach (var wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
            { beats.AddRange(measured.Consume(wire)); last = WaveformEnvelopeCodec.Decode(wire).StartSimTimeNs + 196_000_000; }
        Check.That(measured.Read(last) is { Status: WaveformMeasurementStatus.Valid, MilliBeatsPerMinute: 75000 }, "real acquired lead II gives75bpm without source timing input");
        Check.That(beats.Count >= 20 && beats.All(b => b.ConfirmedAtNs > b.PeakTimeNs), "detected sample peak and later causal confirmation are separate");
        foreach (var (configuration, expected) in new[]
        {
            (PhysiologyIllustrationConfiguration.Default with { VentricularConductionRatio = 2 }, 37500),
            (PhysiologyIllustrationConfiguration.SinusArrhythmiaPreset, 75000),
            (PhysiologyIllustrationConfiguration.SinusArrestPreset, 54545)
        })
        {
            var input = PhysiologyIllustrationSource.Create(configuration); var calculator = new EcgHeartRateMeasurement(Channel);
            for (int step = 1; step <= 110; step++)
                foreach (var wire in input.AdvanceTo(step * 200_000_000L, 50, 1, 100))
                { calculator.Consume(wire); last = WaveformEnvelopeCodec.Decode(wire).StartSimTimeNs + 196_000_000; }
            Check.That(calculator.Read(last).MilliBeatsPerMinute == expected, "finite interval average includes variable RR and missed conducted beats: " + calculator.Read(last));
        }
        foreach (int period in new[] { 200, 95, 75 })
            foreach (int polarity in new[] { -1, 1 })
            {
                var detector = new EcgHeartRateMeasurement(Channel); int count = 0;
                for (int block = 0; block < 70; block++) { count += detector.Consume(Wire(block, period, polarity)).Count; }
                var result = detector.Read(13_996_000_000);
                Check.That(result.Status == WaveformMeasurementStatus.Valid && Math.Abs(result.MilliBeatsPerMinute!.Value - 15_000_000 / period) <= 1,
                    $"sample-defined75/158/200bpm works with inverted QRS, DC offset and slower T wave: {period}/{polarity} -> {result}");
                Check.That(Math.Abs(count - 3500 / period) <= 1, "one event per QRS, no double count of slower T wave");
            }
    }

    private static void EcgContinuousDisorganizationDoesNotBecomeANormalRate()
    {
        foreach (var pattern in new[] { AvConductionPattern.VentricularFlutterIllustration, AvConductionPattern.VentricularFibrillationCoarseIllustration, AvConductionPattern.VentricularFibrillationFineIllustration })
        {
            var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Disorganized(pattern));
            var detector = new EcgHeartRateMeasurement(Channel); long last = 0;
            for (int step = 1; step <= 110; step++)
                foreach (var wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
                {
                    detector.Consume(wire); last = WaveformEnvelopeCodec.Decode(wire).StartSimTimeNs + 196_000_000;
                    if (last >= 5_000_000_000)
                    { Check.That(detector.Read(last).Status == WaveformMeasurementStatus.Uncountable, $"no transient valid HR for continuous example {pattern} at {last}: {detector.Read(last)}"); }
                }
            var result = detector.Read(last);
            Check.That(result.Status == WaveformMeasurementStatus.Uncountable && result.MilliBeatsPerMinute is null,
                "continuous ventricular waveform must not present a counted normal heart rate: " + pattern + " -> " + result);
            Check.That(MeasurementDisplay.Resolve(MeasurementSource.Ecg, result.Status, null).NumericText == "-?-", "uncountable sampled ECG maps to requested placeholder");
        }
    }

    private static void EcgMeasurementFencesBadInputAndRestoresPartialCandidates()
    {
        var detector = new EcgHeartRateMeasurement(Channel);
        Check.That(detector.Read(0).Status == WaveformMeasurementStatus.NoData, "empty input is not zero HR");
        detector.Consume(Wire(0));
        var checkpoint = detector.Capture(); var restored = EcgHeartRateMeasurement.Restore(checkpoint);
        for (int block = 1; block < 30; block++)
        { Check.That(detector.Consume(Wire(block)).SequenceEqual(restored.Consume(Wire(block))), "partial candidate restoration emits same beats"); }
        var before = detector.Read(5_996_000_000);
        Check.That(before == restored.Read(5_996_000_000), "restored finite interval history agrees");
        Check.That(EcgHeartRateMeasurement.Restore(checkpoint).Read(196_000_000).Status == WaveformMeasurementStatus.WarmingUp, "checkpoint sample buffer not mutated later");
        foreach (var wire in new[] { Wire(29), Wire(30, scale: int.MaxValue) })
        {
            bool rejected = false;
            try { detector.Consume(wire); } catch (Exception error) when (error is ArgumentException or OverflowException) { rejected = true; }
            Check.That(rejected && detector.Read(5_996_000_000) == before, "bad batch rejected atomically");
        }
        detector.Consume(Wire(30, quality: true));
        Check.That(detector.Read(6_196_000_000).Status == WaveformMeasurementStatus.PoorSignal, "quality faults suppress old HR");
        for (int block = 31; block < 50; block++) { detector.Consume(Wire(block)); }
        Check.That(detector.Read(9_996_000_000).Status == WaveformMeasurementStatus.Valid, "clean sample recovery relearns beats");
        Check.That(detector.Read(10_500_000_000).Status == WaveformMeasurementStatus.NoData, "missing sample expiry independent of rate history");
        detector.Consume(Wire(60));
        Check.That(detector.Read(12_196_000_000).MilliBeatsPerMinute is null, "gap clears rate and candidate history");
        detector.Consume(Wire(0, epoch: 2));
        bool rollback = false; try { detector.Consume(Wire(1)); } catch (ArgumentException) { rollback = true; }
        Check.That(rollback, "old epoch cannot replay beats");
        var flat = new EcgHeartRateMeasurement(Channel);
        for (int block = 0; block < 35; block++) { Check.That(flat.Consume(Wire(block, flat: true)).Count == 0, "flat signal has no beats"); }
        Check.That(flat.Read(6_996_000_000) is { Status: WaveformMeasurementStatus.Stale, MilliBeatsPerMinute: null }, "flat signal neither normal HR nor a VF diagnosis");
        var continuous = new EcgHeartRateMeasurement(Channel);
        for (int block = 0; block < 35; block++) { continuous.Consume(Wire(block, continuous: true)); }
        Check.That(continuous.Read(6_996_000_000).Status == WaveformMeasurementStatus.Uncountable, "sustained activity has no separable candidates");
        for (int block = 35; block < 65; block++) { continuous.Consume(Wire(block, flat: true)); }
        Check.That(continuous.Read(12_996_000_000).Status == WaveformMeasurementStatus.Stale, "past uncountable activity does not latch beyond expiry over a flat signal");
        for (int block = 65; block < 90; block++) { continuous.Consume(Wire(block, period: 95)); }
        Check.That(continuous.Read(17_996_000_000).MilliBeatsPerMinute == 157895, "organized fast beats recover with new bounded history");
        var impulse = new EcgHeartRateMeasurement(Channel);
        for (int block = 0; block < 35; block++) { Check.That(impulse.Consume(Wire(block, flat: true, spike: true)).Count == 0, "isolated sample spike produces no QRS"); }
    }

    private static byte[] Wire(int block, int period = 200, int polarity = 1, bool quality = false, int scale = 1, ulong epoch = 1, bool flat = false, bool continuous = false, bool spike = false)
    {
        short[] samples = Enumerable.Range(block * 50, 50).Select(i =>
        {
            int phase = i % period;
            int qrs = Math.Max(0, 1000 - Math.Abs(phase - 45) * 125);
            int t = Math.Max(0, 200 - Math.Abs(phase - 70) * 16);
            return checked((short)(600 + (continuous ? Math.Abs(i % 60 - 30) * 50 - 750 : spike && i == 123 ? 5000 : flat ? 0 : polarity * (qrs + t))));
        }).ToArray();
        var plane = new WaveformPlane(Channel, 250, 1, (ulong)block * 50, scale, 1, 0, 1,
            quality ? WaveformQualityEncoding.Ranges : WaveformQualityEncoding.None, samples, quality ? [new(0, 50, 1)] : []);
        return WaveformEnvelopeCodec.EncodeRaw(new(Channel, Channel, 1, epoch, (ulong)block, 1, block * 200_000_000L, 200_000_000, [plane]));
    }
}
