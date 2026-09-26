// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ImpedanceRespirationMeasurementSpecifications
{
    private static readonly Guid Channel = PhysiologyIllustrationSource.ChannelId(1);
    public static Specification[] All =>
    [
        new(nameof(ImpedanceRateUsesSamplesAndHandlesPolarity), ImpedanceRateUsesSamplesAndHandlesPolarity),
        new(nameof(PeriodicBreathingChangesFiniteMeasuredRate), PeriodicBreathingChangesFiniteMeasuredRate),
        new(nameof(RespirationValidityRestorationAndFastActivity), RespirationValidityRestorationAndFastActivity),
    ];

    private static void ImpedanceRateUsesSamplesAndHandlesPolarity()
    {
        foreach (int period in new[] { 2000, 3750, 10000 })
            foreach (int polarity in new[] { -1, 1 })
            {
                var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with
                { BreathPeriodMilliseconds = period, InspirationMilliseconds = period / 2, RespAmplitudeCounts = polarity * 1000 });
                var measurement = new ImpedanceRespirationMeasurement(Channel); long last = 0;
                for (int step = 1; step <= 300; step++)
                    foreach (byte[] wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
                    { measurement.Consume(wire); last = WaveformEnvelopeCodec.Decode(wire).StartSimTimeNs + 192_000_000; }
                var reading = measurement.Read(last);
                Check.That(reading.Status == WaveformMeasurementStatus.Valid && Math.Abs(reading.MilliBreathsPerMinute!.Value - 60_000_000 / period) < 100,
                    $"sample-defined impedance RR with either polarity {period}/{polarity}: {reading}");
            }
        var offset = new ImpedanceRespirationMeasurement(Channel);
        for (int b = 0; b < 150; b++) { offset.Consume(Wire(b)); }
        Check.That(offset.Read(29_992_000_000).MilliBreathsPerMinute == 15000, "DC offset does not set respiratory rate");
    }

    private static void PeriodicBreathingChangesFiniteMeasuredRate()
    {
        foreach (var pattern in new[] { RespiratoryPattern.CheyneStokesIllustration, RespiratoryPattern.IntermittentIllustration })
        {
            var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with { RespiratoryPattern = pattern });
            var measurement = new ImpedanceRespirationMeasurement(Channel); List<int> rates = [];
            for (int step = 1; step <= 500; step++)
                foreach (byte[] wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
                {
                    measurement.Consume(wire); long last = WaveformEnvelopeCodec.Decode(wire).StartSimTimeNs + 192_000_000;
                    if (measurement.Read(last).MilliBreathsPerMinute is { } rate && last > 20_000_000_000) { rates.Add(rate); }
                }
            Check.That(rates.Count > 0 && rates.Min() < 14000 && rates.Max() - rates.Min() > 1000,
                "measured finite intervals reflect missing/shallow breath slots, not configured16: " + pattern);
        }
        var detector = new ImpedanceRespirationMeasurement(Channel);
        for (int b = 0; b < 150; b++) { detector.Consume(Wire(b)); }
        for (int b = 150; b < 300; b++) { detector.Consume(Wire(b, period: 250)); }
        Check.That(detector.Read(59_992_000_000).MilliBreathsPerMinute == 30000, "old slow intervals leave bounded history after rate change");
        for (int b = 300; b < 420; b++) { detector.Consume(Wire(b, flat: true)); }
        Check.That(detector.Read(83_992_000_000).Status == WaveformMeasurementStatus.Stale, "live flat signal expires without reporting zero breaths or disconnected hardware");
        for (int b = 420; b < 570; b++) { detector.Consume(Wire(b)); }
        Check.That(detector.Read(113_992_000_000).MilliBreathsPerMinute == 15000, "clean cycles rebuild rate after long pause");
    }

    private static void RespirationValidityRestorationAndFastActivity()
    {
        var detector = new ImpedanceRespirationMeasurement(Channel);
        Check.That(detector.Read(0).Status == WaveformMeasurementStatus.NoData, "empty is not zero RR");
        for (int b = 0; b < 7; b++) { detector.Consume(Wire(b)); }
        var checkpoint = detector.Capture(); var restored = ImpedanceRespirationMeasurement.Restore(checkpoint);
        for (int b = 7; b < 150; b++)
        { Check.That(detector.Consume(Wire(b)).SequenceEqual(restored.Consume(Wire(b))), "restore partial excursion and finite interval history"); }
        var before = detector.Read(29_992_000_000);
        Check.That(before == restored.Read(29_992_000_000), "restored rate matches");
        Check.That(ImpedanceRespirationMeasurement.Restore(checkpoint).Read(1_392_000_000).MilliBreathsPerMinute is null, "snapshot independent of future updates");
        foreach (byte[]? wire in new[] { Wire(149), Wire(150, scale: int.MaxValue), Wire(150, epoch: 0) })
        {
            bool rejected = false;
            try { detector.Consume(wire); } catch (Exception ex) when (ex is ArgumentException or OverflowException) { rejected = true; }
            Check.That(rejected && detector.Read(29_992_000_000) == before, "malformed input rejects atomically");
        }
        detector.Consume(Wire(150, quality: true));
        Check.That(detector.Read(30_192_000_000).Status == WaveformMeasurementStatus.PoorSignal, "quality removes old RR");
        for (int b = 151; b < 230; b++) { detector.Consume(Wire(b)); }
        Check.That(detector.Read(45_992_000_000).Status == WaveformMeasurementStatus.Valid, "quality recovery relearns excursions");
        Check.That(detector.Read(46_600_000_000).Status == WaveformMeasurementStatus.NoData, "missing samples differ from live flatline");
        detector.Consume(Wire(240));
        Check.That(detector.Read(48_192_000_000).MilliBreathsPerMinute is null, "gap clears rate");
        var fast = new ImpedanceRespirationMeasurement(Channel);
        for (int b = 0; b < 120; b++)
        {
            fast.Consume(Wire(b, period: 100));
            Check.That(fast.Read(b * 200_000_000L + 192_000_000).MilliBreathsPerMinute is null,
                "75/min excursions never alias to37.5/min by dropping alternate peaks");
        }
        for (int b = 120; b < 240; b++) { fast.Consume(Wire(b, flat: true)); }
        Check.That(fast.Read(47_992_000_000).Status == WaveformMeasurementStatus.Stale, "fast-activity rejection does not latch over an expired flatline");
        for (int b = 240; b < 390; b++) { fast.Consume(Wire(b)); }
        Check.That(fast.Read(77_992_000_000).MilliBreathsPerMinute == 15000, "regular cycles recover after rejected activity");
        var spike = new ImpedanceRespirationMeasurement(Channel);
        for (int b = 0; b < 130; b++) { Check.That(spike.Consume(Wire(b, flat: true, spike: true)).Count == 0, "single sample impulses are not respiratory cycles"); }
    }

    private static byte[] Wire(int block, int period = 500, bool flat = false, bool quality = false, bool spike = false, int scale = 1, ulong epoch = 1)
    {
        short[] samples = Enumerable.Range(block * 25, 25).Select(i =>
        {
            int phase = i % period;
            int excursion = phase < period / 2 ? 2000 * phase / period : 2000 * (period - phase) / period;
            return (short)(600 + (spike && i % 125 == 10 ? 5000 : flat ? 0 : excursion));
        }).ToArray();
        var plane = new WaveformPlane(Channel, 125, 1, (ulong)block * 25, scale, 1, 0, 1,
            quality ? WaveformQualityEncoding.Ranges : WaveformQualityEncoding.None, samples, quality ? [new(0, 25, 1)] : []);
        return WaveformEnvelopeCodec.EncodeRaw(new(Channel, Channel, 1, epoch, (ulong)block, 1, block * 200_000_000L, 200_000_000, [plane]));
    }
}
