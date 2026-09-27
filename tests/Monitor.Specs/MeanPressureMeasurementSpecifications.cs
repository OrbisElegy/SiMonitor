// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class MeanPressureMeasurementSpecifications
{
    private static readonly Guid Channel = PhysiologyIllustrationSource.ChannelId(3);
    public static Specification[] All =>
    [
        new(nameof(CvpBaselineChangesSamplesAndMeasuredMean), CvpBaselineChangesSamplesAndMeasuredMean),
        new(nameof(PressureMeanUsesTimeSamplesAndFiniteHistory), PressureMeanUsesTimeSamplesAndFiniteHistory),
        new(nameof(PressureMeanRejectsBadInputAndRestores), PressureMeanRejectsBadInputAndRestores),
        new(nameof(RealPressureChannelsJoinAtomicLiveReadings), RealPressureChannelsJoinAtomicLiveReadings),
    ];
    private static void CvpBaselineChangesSamplesAndMeasuredMean()
    {
        foreach (var config in new[] { PhysiologyIllustrationConfiguration.Default, PhysiologyIllustrationConfiguration.SinusArrestPreset,
            PhysiologyIllustrationConfiguration.Default with { VentricularMechanicalEnabled = false }, PhysiologyIllustrationConfiguration.SvtPreset })
        {
            var original = PhysiologyIllustrationSource.Create(config);
            var shifted = PhysiologyIllustrationSource.Create(config with { CvpBaselineCentiMmHg = -500 });
            var restored = PhysiologyIllustrationSource.Create(config with { CvpBaselineCentiMmHg = -500 });
            var left = LiveWaveformMeasurements.CreateIllustration(); var right = LiveWaveformMeasurements.CreateIllustration();
            for (int step = 1; step <= 45; step++)
            {
                long time = step * 200_000_000L;
                var a = original.AdvanceTo(time, 50, 1, 100); var b = shifted.AdvanceTo(time, 50, 1, 100);
                var c = restored.AdvanceTo(time, 50, 1, 100);
                Check.That(a.Count == b.Count && b.Count == c.Count, "CVP baseline preserves acquisition timing");
                for (int i = 0; i < a.Count; i++)
                {
                    Check.That(b[i].SequenceEqual(c[i]), "restoration preserves changed CVP baseline");
                    var old = WaveformEnvelopeCodec.Decode(a[i]); var changed = WaveformEnvelopeCodec.Decode(b[i]);
                    foreach (var plane in old.Planes)
                    {
                        var next = changed.Planes.Single(p => p.ChannelId == plane.ChannelId);
                        Check.That(plane.Samples.SequenceEqual(next.Samples), "baseline adjustment preserves pulsatile components");
                        if (plane.ChannelId != PhysiologyIllustrationSource.ChannelId(6))
                        { Check.That(plane.OffsetNumerator == next.OffsetNumerator && plane.OffsetDenominator == next.OffsetDenominator, "other channels retain physical pressure/voltage"); }
                    }
                    var x = left.Consume(a[i]); var y = right.Consume(b[i]);
                    if (x.CvpMean.Status == WaveformMeasurementStatus.Valid)
                    { Check.That(y.CvpMean.Status == WaveformMeasurementStatus.Valid && y.CvpMean.MeanCentiMmHg == x.CvpMean.MeanCentiMmHg - 1100, "actual sampled mean shifts by baseline delta in normal/fixed/no-ejection rhythms"); }
                }
                if (step == 21) { restored = Monitor.Simulation.Physiology.PhysiologyWaveformGroup.Restore(restored.CaptureState()); }
            }
        }
        foreach (int value in new[] { -501, 3001, int.MaxValue })
        {
            bool rejected = false;
            try { _ = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with { CvpBaselineCentiMmHg = value }); }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "baseline outside authored range rejects");
        }
        _ = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with { CvpBaselineCentiMmHg = 3000 });
    }

    private static void PressureMeanUsesTimeSamplesAndFiniteHistory()
    {
        var measurement = new MeanPressureMeasurement(Channel);
        for (int b = 0; b < 19; b++) { Check.That(measurement.Consume(Wire(b)).MeanCentiMmHg is null, "partial window not presented as complete average"); }
        var result = measurement.Consume(Wire(19));
        Check.That(result is { Status: WaveformMeasurementStatus.Valid, MeanCentiMmHg: 8800, WindowStartTimeNs: 0, MeasuredAtNs: 3_992_000_000 },
            "120/80 pressure lasting20/80% of time averages88, not the one-third pulse-pressure approximation");
        for (int b = 20; b < 30; b++) { measurement.Consume(Wire(b, constant: 4000)); }
        Check.That(measurement.Read(5_992_000_000).MeanCentiMmHg == 6400, "two seconds old88 and two seconds new40 average64");
        for (int b = 30; b < 40; b++) { measurement.Consume(Wire(b, constant: 4000)); }
        Check.That(measurement.Read(7_992_000_000).MeanCentiMmHg == 4000, "old pressure leaves after exactly500 samples");
        foreach (short pressure in new short[] { 0, -300 })
        {
            var flat = new MeanPressureMeasurement(Channel);
            for (int b = 0; b < 40; b++) { flat.Consume(Wire(b, constant: pressure)); }
            Check.That(flat.Read(7_992_000_000).MeanCentiMmHg == pressure, "valid nonpulsatile zero or negative pressure is not interpreted as missing hardware");
        }
        var offset = new MeanPressureMeasurement(Channel);
        for (int b = 0; b < 20; b++) { offset.Consume(Wire(b, offset: 3)); }
        Check.That(offset.Read(3_992_000_000).MeanCentiMmHg == 9100, "mmHg affine offset remains in measured pressure");
    }
    private static void PressureMeanRejectsBadInputAndRestores()
    {
        var m = new MeanPressureMeasurement(Channel);
        Check.That(m.Read(0).Status == WaveformMeasurementStatus.NoData, "no samples is not zero pressure");
        for (int b = 0; b < 7; b++) { m.Consume(Wire(b)); }
        var checkpoint = m.Capture(); var restored = MeanPressureMeasurement.Restore(checkpoint);
        for (int b = 7; b < 40; b++) { Check.That(m.Consume(Wire(b)) == restored.Consume(Wire(b)), "partial ring restoration preserves sum and cursor"); }
        Check.That(MeanPressureMeasurement.Restore(checkpoint).Read(1_392_000_000).Status == WaveformMeasurementStatus.WarmingUp, "captured array not overwritten by future ring wraps");
        var before = m.Read(7_992_000_000);
        foreach (var bad in new[] { Wire(39), Wire(40, offset: int.MaxValue), Wire(40, epoch: 0) })
        {
            Reject(() => m.Consume(bad));
            Check.That(before == m.Read(7_992_000_000), "bad delivery leaves complete mean unchanged");
        }
        m.Consume(Wire(40, quality: true));
        Check.That(m.Read(8_192_000_000).Status == WaveformMeasurementStatus.PoorSignal, "flagged samples remove old number");
        for (int b = 41; b < 60; b++) { m.Consume(Wire(b)); }
        Check.That(m.Read(11_992_000_000).Status == WaveformMeasurementStatus.PoorSignal, "cannot resume with an incomplete clean window");
        Check.That(m.Consume(Wire(60)).MeanCentiMmHg == 8800, "full clean window restores mean");
        Check.That(m.Read(12_800_000_000).Status == WaveformMeasurementStatus.NoData, "missing input expires old mean");
        Check.That(m.Consume(Wire(65)).Status == WaveformMeasurementStatus.WarmingUp, "gap discards old ring");
        Check.That(m.Consume(Wire(66, constant: short.MaxValue)).Status == WaveformMeasurementStatus.PoorSignal, "raw clipping rejected even if physical range would otherwise fit");
    }
    private static void RealPressureChannelsJoinAtomicLiveReadings()
    {
        foreach (var (ratio, mechanical) in new[] { (1, true), (2, true), (1, false) })
        {
            var source = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with { VentricularConductionRatio = ratio, VentricularMechanicalEnabled = mechanical });
            var owner = LiveWaveformMeasurements.CreateIllustration();
            Dictionary<int, List<decimal>> samples = new() { [3] = [], [5] = [], [6] = [] };
            LiveMeasurementSnapshot? reading = null;
            for (int step = 1; step <= 60; step++)
                foreach (var wire in source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
                {
                    reading = owner.Consume(wire);
                    var packet = WaveformEnvelopeCodec.Decode(wire);
                    foreach (int row in samples.Keys)
                    {
                        var plane = packet.Planes.Single(p => p.ChannelId == PhysiologyIllustrationSource.ChannelId(row));
                        samples[row].AddRange(plane.Samples.Select(v => 100m * (v * (decimal)plane.ScaleNumerator / plane.ScaleDenominator + (decimal)plane.OffsetNumerator / plane.OffsetDenominator)));
                    }
                }
            foreach (var (row, value) in new[] { (3, reading!.AbpMean), (5, reading.PaMean), (6, reading.CvpMean) })
            {
                int expected = (int)decimal.Round(samples[row].TakeLast(500).Average(), 0, MidpointRounding.ToEven);
                Check.That(value.Status == WaveformMeasurementStatus.Valid && value.MeanCentiMmHg == expected,
                    "ABP/PA/CVP measured from real physical samples, including2:1 timing: " + row);
            }
            if (!mechanical) { Check.That(reading!.AbpMean.MeanCentiMmHg is >= 1000 and < 4000, "no ejection still reports sampled reservoir runoff rather than an old normal pressure"); }
            var previous = reading;
            var next = source.AdvanceTo(12_200_000_000, 50, 1, 100).Single();
            var decoded = WaveformEnvelopeCodec.Decode(next);
            var bad = WaveformEnvelopeCodec.EncodeRaw(decoded with
            { Planes = decoded.Planes.Select(p => p.ChannelId == PhysiologyIllustrationSource.ChannelId(6) ? p with { OffsetNumerator = int.MaxValue, OffsetDenominator = 1 } : p).ToArray() });
            Reject(() => owner.Consume(bad));
            Check.That(owner.Read(previous!.SampleTimeNs) == previous, "late CVP overflow rolls back every detector including ABP/PA");
            var flagged = WaveformEnvelopeCodec.EncodeRaw(decoded with
            {
                Planes = decoded.Planes.Select(p => p.ChannelId != Channel ? p : p with
                { QualityEncoding = WaveformQualityEncoding.Ranges, QualityRanges = [new(0, (uint)p.Samples.Count, 1)] }).ToArray()
            });
            var isolated = owner.Consume(flagged);
            Check.That(isolated.AbpMean.Status == WaveformMeasurementStatus.PoorSignal && isolated.PaMean.Status == WaveformMeasurementStatus.Valid &&
                isolated.CvpMean.Status == WaveformMeasurementStatus.Valid && isolated.HeartRate.Status == WaveformMeasurementStatus.Valid, "ABP quality loss does not erase other valid sensors");
        }
    }
    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); } catch (Exception ex) when (ex is ArgumentException or OverflowException) { rejected = true; }
        Check.That(rejected, "invalid pressure input rejected");
    }
    private static byte[] Wire(int block, short? constant = null, int offset = 0, bool quality = false, ulong epoch = 1)
    {
        short[] values = Enumerable.Range(block * 25, 25).Select(i => constant ?? (short)(i % 125 < 25 ? 12000 : 8000)).ToArray();
        var plane = new WaveformPlane(Channel, 125, 1, (ulong)block * 25, 1, 100, offset, 1,
            quality ? WaveformQualityEncoding.Ranges : WaveformQualityEncoding.None, values, quality ? [new(0, 25, 1)] : []);
        return WaveformEnvelopeCodec.EncodeRaw(new(Channel, Channel, 1, epoch, (ulong)block, 1, block * 200_000_000L, 200_000_000, [plane]));
    }
}
