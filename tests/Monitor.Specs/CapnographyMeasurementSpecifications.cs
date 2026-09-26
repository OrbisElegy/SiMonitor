// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class CapnographyMeasurementSpecifications
{
    private static readonly Guid Channel = Guid.Parse("55555555-5555-4555-8555-555555555555");
    public static Specification[] All =>
    [
        new(nameof(CapnographyMeasuresAcquiredSamplesRatherThanSettings), CapnographyMeasuresAcquiredSamplesRatherThanSettings),
        new(nameof(CapnographyHandlesAbsenceQualityAndAtomicRejection), CapnographyHandlesAbsenceQualityAndAtomicRejection),
        new(nameof(CapnographyRestoresMidBreathAndResetsDiscontinuities), CapnographyRestoresMidBreathAndResetsDiscontinuities),
        new(nameof(CapnographyUsesBoundedIntervalsAndRetainsRespiratoryPauses), CapnographyUsesBoundedIntervalsAndRetainsRespiratoryPauses),
    ];

    private static void CapnographyMeasuresAcquiredSamplesRatherThanSettings()
    {
        foreach (int concentration in new[] { 25, 55 })
        {
            var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with { Co2EndExpiratoryMmHg = concentration, Co2PlateauStartCentiMmHg = concentration * 70 });
            var measurement = new CapnographyMeasurement(Channel);
            var measured = new List<MeasuredExpiration>(); long end = 0;
            for (int step = 1; step <= 110; step++)
                foreach (var wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
                { measured.AddRange(measurement.Consume(wire)); end = WaveformEnvelopeCodec.Decode(wire).StartSimTimeNs + 190_000_000; }
            var result = measurement.Read(end);
            Check.That(result.EndTidalCentiMmHg.Status == WaveformMeasurementStatus.Valid && Math.Abs(result.EndTidalCentiMmHg.Value!.Value - concentration * 100) < 100,
                "end-tidal estimate follows acquired scaled peak, not a nominal 40mmHg");
            Check.That(result.RespirationsMilliPerMinute.Value == 16000 && measured.Count >= 4,
                "16 breaths/min derived from independently detected cycles");
            Check.That(measured.All(e => e.RiseTimeNs < e.PeakTimeNs && e.PeakTimeNs < e.ConfirmedAtNs), "sample landmarks and later confirmation are distinct");
        }
        // Completely independent artificial raw input, with nonzero offset and
        // no physiology generator:600ms high pulse every2s after baseline.
        var independent = new CapnographyMeasurement(Channel);
        for (int block = 0; block < 30; block++) { independent.Consume(Wire(block)); }
        var numeric = independent.Read(5_990_000_000);
        Check.That(numeric.EndTidalCentiMmHg.Value == 4000 && numeric.RespirationsMilliPerMinute.Value == 30000,
            "scale+offset decoded and rate measured from raw samples alone");
        var cyclic = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with { RespiratoryPattern = RespiratoryPattern.CheyneStokesIllustration });
        var detector = new CapnographyMeasurement(Channel); List<MeasuredExpiration> breaths = [];
        for (int step = 1; step <= 260; step++)
            foreach (var wire in cyclic.AdvanceTo(step * 200_000_000L, 50, 1, 100)) { breaths.AddRange(detector.Consume(wire)); }
        Check.That(breaths.Count >= 9 && breaths.Max(b => b.EndTidalCentiMmHg) - breaths.Min(b => b.EndTidalCentiMmHg) > 200,
            "measured CO2 follows the existing variable-depth capnogram instead of staying at its configured nominal value");
        Check.That(breaths.Zip(breaths.Skip(1), (a, b) => b.RiseTimeNs - a.RiseTimeNs).Any(interval => interval > 7_000_000_000),
            "absent expirations do not produce artificial breath events during the pause");
    }

    private static void CapnographyHandlesAbsenceQualityAndAtomicRejection()
    {
        var m = new CapnographyMeasurement(Channel);
        Check.That(m.Read(0).EndTidalCentiMmHg.Status == WaveformMeasurementStatus.NoData, "empty input is not zero CO2");
        for (int i = 0; i < 10; i++) { m.Consume(Wire(i)); }
        Check.That(m.Read(1_990_000_000).RespirationsMilliPerMinute is { Status: WaveformMeasurementStatus.WarmingUp, Value: null },
            "one expiration cannot establish a respiratory rate");
        for (int i = 10; i < 30; i++) { m.Consume(Wire(i)); }
        var before = m.Read(5_990_000_000);
        foreach (var bad in new[] { Wire(29), Wire(29, revision: 2), Wire(30, scale: int.MaxValue) })
        {
            bool rejected = false;
            try { m.Consume(bad); } catch (Exception error) when (error is ArgumentException or OverflowException) { rejected = true; }
            Check.That(rejected && m.Read(5_990_000_000) == before, "duplicate/overflow rejects atomically");
        }
        Check.That(m.Read(6_500_000_000).EndTidalCentiMmHg is { Status: WaveformMeasurementStatus.NoData, Value: null }, "missing sample age hides previous number");
        m.Consume(Wire(30, badQuality: true));
        Check.That(m.Read(6_190_000_000).EndTidalCentiMmHg is { Status: WaveformMeasurementStatus.PoorSignal, Value: null }, "quality-marked samples cannot support measurement");
        for (int i = 31; i < 95; i++) { m.Consume(Wire(i, flat: true)); }
        Check.That(m.Read(18_990_000_000).EndTidalCentiMmHg is { Status: WaveformMeasurementStatus.Stale, Value: null }, "continued flat samples expire without fabricating zero or apnea diagnosis");
    }

    private static void CapnographyRestoresMidBreathAndResetsDiscontinuities()
    {
        var original = new CapnographyMeasurement(Channel);
        for (int i = 0; i < 13; i++) { original.Consume(Wire(i)); }
        var checkpoint = original.Capture(); var restored = CapnographyMeasurement.Restore(checkpoint);
        for (int i = 13; i < 30; i++)
        { Check.That(original.Consume(Wire(i)).SequenceEqual(restored.Consume(Wire(i))), "mid-expiration continuation has exact same events"); }
        Check.That(original.Read(5_990_000_000) == restored.Read(5_990_000_000), "checkpoint produces same rate and CO2");
        var again = CapnographyMeasurement.Restore(checkpoint);
        Check.That(again.Read(2_590_000_000).EndTidalCentiMmHg.MeasuredAtNs < 2_000_000_000, "checkpoint is independent of subsequent mutations");
        original.Consume(Wire(40));
        Check.That(original.Read(8_190_000_000).EndTidalCentiMmHg.Value is null, "missing blocks reset partial cycles and numeric state");
        original.Consume(Wire(0, epoch: 2));
        Check.That(original.Read(190_000_000).RespirationsMilliPerMinute is { Status: WaveformMeasurementStatus.WarmingUp, Value: null }, "new epoch permits time reset without old rates");
        bool rejected = false;
        try { original.Consume(Wire(1)); } catch (ArgumentException) { rejected = true; }
        Check.That(rejected, "late old epoch cannot reset newer stream");
        // One isolated large sample does not invent a breath.
        var spike = new CapnographyMeasurement(Channel);
        for (int i = 0; i < 10; i++) { Check.That(spike.Consume(Wire(i, flat: true, spike: true)).Count == 0, "median and persistence reject isolated spike"); }
    }

    private static void CapnographyUsesBoundedIntervalsAndRetainsRespiratoryPauses()
    {
        int[] onsets = [40, 240, 440, 640, 1040, 1440, 1840, 2240];
        var m = new CapnographyMeasurement(Channel);
        var expected = new Dictionary<int, int> { [55] = 24000, [75] = 20000, [95] = 17143, [115] = 15000 };
        for (int block = 0; block <= 115; block++)
        {
            m.Consume(Wire(block, onsetSamples: onsets));
            if (expected.TryGetValue(block, out int rate))
            { Check.That(m.Read(block * 200_000_000L + 190_000_000).RespirationsMilliPerMinute.Value == rate, "finite pooled intervals respond gradually, oldest intervals disappear"); }
        }
        int[] paused = [40, 240, 440, 1640];
        var p = new CapnographyMeasurement(Channel);
        for (int block = 0; block <= 85; block++) { p.Consume(Wire(block, onsetSamples: paused)); }
        Check.That(p.Read(17_190_000_000).RespirationsMilliPerMinute.Value == 11250,
            "12-second gap remains a measured interval, not discarded as an outlier");
        var checkpoint = p.Capture();
        var copy = CapnographyMeasurement.Restore(checkpoint);
        for (int block = 86; block <= 105; block++)
        { p.Consume(Wire(block, onsetSamples: paused)); copy.Consume(Wire(block, onsetSamples: paused)); }
        Check.That(p.Read(21_190_000_000).RespirationsMilliPerMinute.Value == 8571 && p.Read(21_190_000_000) == copy.Read(21_190_000_000),
            "20-second sample-time window expires old endpoints without requiring a new breath; checkpoint agrees");
        for (int block = 106; block <= 140; block++) { p.Consume(Wire(block, onsetSamples: paused)); }
        Check.That(p.Read(28_190_000_000).RespirationsMilliPerMinute is { Status: WaveformMeasurementStatus.Stale, Value: null },
            "old average cannot mask prolonged absence of detected respiration");
        Check.That(CapnographyMeasurement.Restore(checkpoint).Read(17_190_000_000).RespirationsMilliPerMinute.Value == 11250,
            "subsequent history replacement does not mutate the saved interval window");
    }

    private static byte[] Wire(int block, bool flat = false, bool badQuality = false, int scale = 1, ulong epoch = 1, bool spike = false, ulong revision = 1, int[]? onsetSamples = null)
    {
        short[] samples = Enumerable.Range(block * 20, 20).Select(i => (short)(onsetSamples is not null
            ? onsetSamples.Any(start => i >= start && i < start + 60) ? 3950 : -50
            : spike && i % 200 == 40 ? 8000 : !flat && i % 200 is >= 40 and < 100 ? 3950 : -50)).ToArray();
        var plane = new WaveformPlane(Channel, 100, 1, (ulong)block * 20, scale, 100, 1, 2,
            badQuality ? WaveformQualityEncoding.Ranges : WaveformQualityEncoding.None, samples,
            badQuality ? [new(0, 20, 1)] : []);
        return WaveformEnvelopeCodec.EncodeRaw(new(Channel, Channel, 1, epoch, (ulong)block, revision, block * 200_000_000L, 200_000_000, [plane]));
    }
}
