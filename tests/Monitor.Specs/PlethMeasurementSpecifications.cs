// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class PlethMeasurementSpecifications
{
    private static readonly Guid Channel = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static Specification[] All =>
    [
        new(nameof(LowPerfusionRetainsQualifiedOpticalEstimate), LowPerfusionRetainsQualifiedOpticalEstimate),
        new(nameof(PulseRateUsesAcquiredPleth), PulseRateUsesAcquiredPleth),
        new(nameof(PulseRateRejectsInvalidInputAndRestores), PulseRateRejectsInvalidInputAndRestores),
        new(nameof(OpticalRatioUsesBothWavelengthsAndCalibration), OpticalRatioUsesBothWavelengthsAndCalibration),
        new(nameof(OpticalMissingAndPoorSignalsDoNotProduceSaturation), OpticalMissingAndPoorSignalsDoNotProduceSaturation),
    ];

    private static void LowPerfusionRetainsQualifiedOpticalEstimate()
    {
        var calculator = Optical();
        foreach (var (red, ir, expectedPi, questionable) in new[] { (12, 47, 470, false), (10, 20, 200, true), (14, 29, 290, true), (15, 30, 300, false), (3, 5, 50, true) })
        {
            var reading = calculator.Estimate(Pairs(red, ir), 3_992_000_000);
            Check.That(reading.Status == WaveformMeasurementStatus.Valid && reading.SaturationMilliPercent is not null &&
                reading.PerfusionMilliPercent == expectedPi && reading.IsQuestionable == questionable,
                "low but coherent modulation reports calibrated saturation with separate PI qualification");
        }
        var rejected = calculator.Estimate(Pairs(2, 4), 3_992_000_000);
        Check.That(rejected.SaturationMilliPercent is null && !rejected.IsQuestionable, "below teaching resolution floor never invents a questioned number");
        foreach (var config in new[] { PhysiologyIllustrationConfiguration.SvtPreset,
            PhysiologyIllustrationConfiguration.SvtPreset with { SvtRbbb = true }, PhysiologyIllustrationConfiguration.SvtPreset with { SvtLbbb = true } })
        {
            var session = new Monitor.Application.Presentation.LocalMonitorPreviewSession(config,
                Monitor.Application.Presentation.MonitorDisplayConfiguration.Default(), true, 98000);
            for (int i = 0; i < 320; i++) { session.Advance(50_000_000); }
            var reading = session.Measurements!.SpO2;
            Check.That(reading.Status == WaveformMeasurementStatus.Valid && reading.SaturationMilliPercent is >= 97000 and <= 99000 &&
                reading.PerfusionMilliPercent is >= 300 and < 1000 && !reading.IsQuestionable,
                "default SVT perfusion reports measured saturation at acceptable PI: " + reading);
        }
    }

    private static void PulseRateUsesAcquiredPleth()
    {
        foreach (var (config, expected) in new[]
        {
            (PhysiologyIllustrationConfiguration.Default, 75000),
            (PhysiologyIllustrationConfiguration.Default with { VentricularConductionRatio = 2 }, 37500)
        })
        {
            var source = PhysiologyIllustrationSource.Create(config);
            var detector = new PlethPulseRateMeasurement(Channel); long last = 0; int count = 0;
            for (int step = 1; step <= 110; step++)
                foreach (byte[] wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
                {
                    count += detector.Consume(wire).Count;
                    last = WaveformEnvelopeCodec.Decode(wire).StartSimTimeNs + 192_000_000;
                }
            var result = detector.Read(last);
            Check.That(result.Status == WaveformMeasurementStatus.Valid && result.MilliBeatsPerMinute == expected,
                "acquired peripheral pulses determine PR: " + result);
            Check.That(count > 8, "real acquired runoff yields repeated independent pulses");
        }
        foreach (int period in new[] { 100, 50 })
        {
            var detector = new PlethPulseRateMeasurement(Channel); int count = 0;
            for (int block = 0; block < 60; block++) { count += detector.Consume(Wire(block, period)).Count; }
            var result = detector.Read(11_992_000_000);
            Check.That(result.MilliBeatsPerMinute == 7_500_000 / period, "raw pulse periods, not a configured HR, set rate");
            Check.That(Math.Abs(count - 1500 / period) <= 1, "one event per sampled pulse");
        }
        var deficit = new PlethPulseRateMeasurement(Channel);
        for (int block = 0; block < 100; block++) { deficit.Consume(Wire(block, dropAlternate: true)); }
        Check.That(deficit.Read(19_992_000_000).MilliBeatsPerMinute == 37500,
            "absent alternating peripheral pulses halve PR without any ECG rate input");
    }

    private static void PulseRateRejectsInvalidInputAndRestores()
    {
        var detector = new PlethPulseRateMeasurement(Channel);
        Check.That(detector.Read(0).Status == WaveformMeasurementStatus.NoData, "empty Pleth is not zero PR");
        detector.Consume(Wire(0)); var checkpoint = detector.Capture();
        var restored = PlethPulseRateMeasurement.Restore(checkpoint);
        for (int block = 1; block < 30; block++)
        { Check.That(detector.Consume(Wire(block)).SequenceEqual(restored.Consume(Wire(block))), "mid-rise restore preserves emitted pulses"); }
        var before = detector.Read(5_992_000_000);
        Check.That(before == restored.Read(5_992_000_000), "restored rate history agrees");
        Check.That(PlethPulseRateMeasurement.Restore(checkpoint).Read(192_000_000).Status == WaveformMeasurementStatus.WarmingUp, "checkpoint isolated from subsequent input");
        foreach (byte[]? wire in new[] { Wire(29), Wire(30, scale: int.MaxValue), Wire(30, epoch: 0) })
        {
            bool rejected = false;
            try { detector.Consume(wire); } catch (Exception ex) when (ex is ArgumentException or OverflowException) { rejected = true; }
            Check.That(rejected && before == detector.Read(5_992_000_000), "bad delivery rejects atomically");
        }
        detector.Consume(Wire(30, quality: true));
        Check.That(detector.Read(6_192_000_000).Status == WaveformMeasurementStatus.PoorSignal, "quality flags remove old rate");
        for (int block = 31; block < 55; block++) { detector.Consume(Wire(block)); }
        Check.That(detector.Read(10_992_000_000).MilliBeatsPerMinute == 75000, "clean pulses rebuild rate after quality recovery");
        for (int block = 55; block < 85; block++) { detector.Consume(Wire(block, flat: true)); }
        Check.That(detector.Read(16_992_000_000).Status == WaveformMeasurementStatus.Stale, "flat signal expires without inventing a pulse or saturation");
        Check.That(detector.Read(17_600_000_000).Status == WaveformMeasurementStatus.NoData, "missing samples are distinct from flat live signal");
        detector.Consume(Wire(90));
        Check.That(detector.Read(18_192_000_000).MilliBeatsPerMinute is null, "gap clears interval association");
        var impulse = new PlethPulseRateMeasurement(Channel);
        for (int block = 0; block < 30; block++)
        { Check.That(impulse.Consume(Wire(block, flat: true, spike: true)).Count == 0, "median rejects isolated sample impulse"); }
    }

    private static OpticalSaturationMeasurement Optical() => new("test-fixture-only@1",
        [new(500000, 98000), new(1000000, 90000), new(2000000, 70000)]);

    private static OpticalSample[] Pairs(int redAmplitude = 500, int infraredAmplitude = 1000) =>
        Enumerable.Range(0, 500).Select(i => new OpticalSample(i * 8_000_000L,
            10000 + (i % 100 < 50 ? redAmplitude : -redAmplitude),
            20000 + (i % 100 < 50 ? infraredAmplitude : -infraredAmplitude))).ToArray();

    private static void OpticalRatioUsesBothWavelengthsAndCalibration()
    {
        var calculator = Optical(); var pairs = Pairs();
        var value = calculator.Estimate(pairs, 3_992_000_000);
        Check.That(value is { Status: WaveformMeasurementStatus.Valid, RatioPpm: 1000000, SaturationMilliPercent: 90000 }, "AC rms/DC ratio uses both unequal DC levels and supplied test calibration");
        var second = calculator.Estimate(Pairs(250), 3_992_000_000);
        Check.That(second.SaturationMilliPercent == 98000, "identical IR Pleth can have different saturation when red signal differs");
        var scaled = pairs.Select(p => p with { Red = p.Red * 3, Infrared = p.Infrared * 2 }).ToArray();
        Check.That(calculator.Estimate(scaled, 3_992_000_000) == value, "separate wavelength gains cancel in AC/DC ratios");
        Check.That(calculator.Estimate(Pairs(375), 3_992_000_000).SaturationMilliPercent == 94000, "interpolates only inside explicit calibration range");
        SaturationCalibrationPoint[] table = [new(500000, 98000), new(1000000, 90000)];
        var owned = new OpticalSaturationMeasurement("owned-test", table); table[1] = new(1000000, 1000);
        Check.That(owned.Estimate(pairs, 3_992_000_000).SaturationMilliPercent == 90000, "calibration copied, not caller-mutable");
    }

    private static void OpticalMissingAndPoorSignalsDoNotProduceSaturation()
    {
        var calculator = Optical();
        Check.That(calculator.Estimate([], 0).Status == WaveformMeasurementStatus.NoData, "no optical channels produce no saturation");
        Check.That(calculator.Estimate(Pairs()[..250], 1_992_000_000).Status == WaveformMeasurementStatus.WarmingUp, "partial window not emitted");
        Check.That(calculator.Estimate(Pairs(), 5_000_000_000).Status == WaveformMeasurementStatus.NoData, "old window cannot look fresh");
        foreach (var pairs in new[]
        {
            Pairs(0, 0), Pairs(1, 1), Pairs(2000),
            Pairs().Select(p => p with { QualityFlags = 1 }).ToArray(),
            Pairs().Select(p => p with { Red = 0 }).ToArray(),
            Pairs().Select(p => p with { Infrared = 1_000_000 }).ToArray(),
            Pairs().Select(p => p with { Red = 20000 - p.Red }).ToArray()
        })
        {
            var result = calculator.Estimate(pairs, 3_992_000_000);
            Check.That(result.Status == WaveformMeasurementStatus.PoorSignal && result.SaturationMilliPercent is null,
                "flat, low modulation, out-of-calibration, flagged, clipped or discordant pairs never default to normal saturation");
        }
        var malformed = Pairs(); malformed[2] = malformed[2] with { SampleTimeNs = 17_000_000 };
        bool rejected = false;
        try { calculator.Estimate(malformed, 3_992_000_000); } catch (ArgumentException) { rejected = true; }
        Check.That(rejected, "misaligned/gapped wavelength sampling is rejected");
        rejected = false;
        try { _ = new OpticalSaturationMeasurement("invalid", [new(1000000, 90000), new(500000, 98000)]); }
        catch (ArgumentException) { rejected = true; }
        Check.That(rejected, "reversed calibration rejected");
    }

    private static byte[] Wire(int block, int period = 100, bool flat = false, bool quality = false,
        bool spike = false, int scale = 1, ulong epoch = 1, bool dropAlternate = false)
    {
        short[] samples = Enumerable.Range(block * 25, 25).Select(i =>
        {
            int phase = i % period;
            int pulse = phase < 20 ? phase * 50 : Math.Max(0, 1000 - (phase - 20) * 40);
            if (dropAlternate && i / period % 2 == 1) { pulse = 0; }
            return (short)(600 + (spike && i == 123 ? 5000 : flat ? 0 : pulse));
        }).ToArray();
        var plane = new WaveformPlane(Channel, 125, 1, (ulong)block * 25, scale, 1, 0, 1,
            quality ? WaveformQualityEncoding.Ranges : WaveformQualityEncoding.None, samples,
            quality ? [new(0, 25, 1)] : []);
        return WaveformEnvelopeCodec.EncodeRaw(new(Channel, Channel, 1, epoch, (ulong)block, 1,
            block * 200_000_000L, 200_000_000, [plane]));
    }
}
