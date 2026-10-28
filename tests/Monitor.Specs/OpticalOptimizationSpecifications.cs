// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class OpticalOptimizationSpecifications
{
    private static readonly Guid RedChannel = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid InfraredChannel = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private const long StepNs = OpticalSaturationMeasurement.SampleStepNs;
    private const long LastWindowSampleNs = 499 * StepNs;

    public static Specification[] All =>
    [
        new(nameof(OpticalStatisticsPreserveBoundsAndValidation), OpticalStatisticsPreserveBoundsAndValidation),
        new(nameof(OpticalReadsPreserveFreshnessAndCheckpointIsolation), OpticalReadsPreserveFreshnessAndCheckpointIsolation),
        new(nameof(OpticalReadsInvalidateOnQualityGapsAndIdentityChanges), OpticalReadsInvalidateOnQualityGapsAndIdentityChanges),
    ];

    private static OpticalSaturationMeasurement Measurement() => new("optimization-regression@1",
        [new(500000, 98000), new(1000000, 90000), new(2000000, 70000)]);

    private static OpticalSaturationAcquisition Acquisition() => new(RedChannel, InfraredChannel, Measurement());

    private static OpticalSample[] Samples(int firstIndex, int count, bool poor = false) =>
        Enumerable.Range(firstIndex, count).Select(index => new OpticalSample(index * StepNs,
            10000 + (index % 100 < 50 ? 500 : -500),
            20000 + (index % 100 < 50 ? 1000 : -1000), poor ? 1u : 0u)).ToArray();

    private static void OpticalStatisticsPreserveBoundsAndValidation()
    {
        var measurement = Measurement();
        var extremes = Enumerable.Range(0, 500).Select(index => new OpticalSample(index * StepNs,
            index % 2 == 0 ? 1 : 999999, index % 2 == 0 ? 1 : 999999)).ToArray();
        Check.That(measurement.Estimate(extremes, LastWindowSampleNs) is
        { Status: WaveformMeasurementStatus.Valid, RatioPpm: 1000000, SaturationMilliPercent: 90000, PerfusionMilliPercent: 200000 },
            "full-range optical values retain exact variance, covariance and perfusion without accumulator overflow");

        foreach (int invalid in new[] { int.MinValue, 0, 1000000, int.MaxValue })
        {
            var samples = Samples(0, 500);
            samples[0] = samples[0] with { Red = invalid, Infrared = invalid };
            Check.That(measurement.Estimate(samples, LastWindowSampleNs).Status == WaveformMeasurementStatus.PoorSignal,
                "out-of-range optical integers report poor signal without overflowing unused statistics");
            samples[^1] = samples[^1] with { SampleTimeNs = LastWindowSampleNs - 1 };
            Reject(() => measurement.Estimate(samples, LastWindowSampleNs),
                "invalid optical values cannot bypass validation of later timestamps");
        }
    }

    private static void OpticalReadsPreserveFreshnessAndCheckpointIsolation()
    {
        var acquisition = Acquisition();
        Check.That(acquisition.Read(0) == Measurement().Estimate([], 0), "an empty owner preserves NoData");
        Reject(() => acquisition.Read(-1), "an empty owner rejects negative read time");
        for (int block = 0; block < 20; block++)
        {
            var reading = acquisition.Consume(Wire(block));
            var expected = Samples(0, (block + 1) * 25);
            Check.That(reading == Measurement().Estimate(expected, expected[^1].SampleTimeNs),
                "consumption preserves partial-window and full-window estimates");
            AssertReads(acquisition, expected);
        }
        var checkpoint = acquisition.Capture();
        var restored = OpticalSaturationAcquisition.Restore(checkpoint);
        var before = acquisition.Read(LastWindowSampleNs);
        Reject(() => acquisition.Consume(Wire(19)), "a duplicate packet rejects without invalidating a reading");
        Reject(() => acquisition.Consume(Wire(20, scale: int.MaxValue)), "conversion overflow rejects without publishing a new cache");
        Check.That(acquisition.Read(LastWindowSampleNs) == before, "failed consumes preserve the complete prior reading");
        acquisition.Consume(Wire(20, poor: true));
        AssertReads(restored, Samples(0, 500));
        AssertReads(OpticalSaturationAcquisition.Restore(checkpoint), Samples(0, 500));
        restored.Consume(Wire(20));
        AssertReads(restored, Samples(25, 500));
        Check.That(acquisition.Read(524 * StepNs).Status == WaveformMeasurementStatus.PoorSignal &&
            restored.Read(524 * StepNs).Status == WaveformMeasurementStatus.Valid,
            "restored checkpoints own independent windows and cached readings");
    }

    private static void OpticalReadsInvalidateOnQualityGapsAndIdentityChanges()
    {
        var acquisition = Acquisition();
        for (int block = 0; block < 20; block++) { acquisition.Consume(Wire(block)); }
        var checkpoint = acquisition.Capture();
        acquisition.Consume(Wire(20, poor: true));
        AssertReads(acquisition, Samples(25, 475).Concat(Samples(500, 25, poor: true)).ToArray());
        for (int block = 21; block <= 40; block++) { acquisition.Consume(Wire(block)); }
        AssertReads(acquisition, Samples(525, 500));

        foreach (var (block, epoch, revision) in new (int, ulong, ulong)[] { (22, 1, 1), (20, 1, 2), (0, 2, 1) })
        {
            var reset = OpticalSaturationAcquisition.Restore(checkpoint);
            reset.Consume(Wire(block, epoch: epoch, revision: revision));
            AssertReads(reset, Samples(block * 25, 25));
            Check.That(reset.Read((block * 25 + 24) * StepNs).Status == WaveformMeasurementStatus.WarmingUp,
                "gaps, calibration revisions and stream epochs invalidate a previously valid full window");
        }
    }

    private static void AssertReads(OpticalSaturationAcquisition acquisition, OpticalSample[] samples)
    {
        long last = samples[^1].SampleTimeNs;
        foreach (long asOfNs in new[] { last, last + 1, last + 500_000_000, last + 500_000_001, long.MaxValue, last })
        {
            Check.That(acquisition.Read(asOfNs) == Measurement().Estimate(samples, asOfNs),
                "reads preserve the exact freshness boundary and do not mutate cached state on expiration");
        }
        Reject(() => acquisition.Read(last - 1), "reads before the last sample preserve timestamp rejection");
        Reject(() => acquisition.Read(-1), "cached reads reject negative time");
    }

    private static byte[] Wire(int block, bool poor = false, ulong epoch = 1, ulong revision = 1, int scale = 1)
    {
        var samples = Samples(block * 25, 25);
        WaveformQualityRange[] quality = poor ? [new(0, 25, 1)] : [];
        var red = new WaveformPlane(RedChannel, 125, 1, (ulong)block * 25, scale, 1, 0, 1,
            poor ? WaveformQualityEncoding.Ranges : WaveformQualityEncoding.None,
            samples.Select(sample => (short)sample.Red).ToArray(), quality);
        var infrared = red with { ChannelId = InfraredChannel, Samples = samples.Select(sample => (short)sample.Infrared).ToArray() };
        return WaveformEnvelopeCodec.EncodeRaw(new(RedChannel, RedChannel, 1, epoch, (ulong)block, revision,
            block * 200_000_000L, 200_000_000, [red, infrared]));
    }

    private static void Reject(Action action, string reason)
    {
        try { action(); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException) { return; }
        throw new InvalidOperationException(reason);
    }
}
