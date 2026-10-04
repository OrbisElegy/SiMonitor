// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class PulseOximeterChainSpecifications
{
    private static readonly Guid Pleth = PhysiologyIllustrationSource.ChannelId(2);
    private static readonly Guid Sensor = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    public static Specification[] All =>
    [
        new(nameof(SeededOpticalVariationReproducesMeasuredSignals), SeededOpticalVariationReproducesMeasuredSignals),
        new(nameof(AcquiredPeripheralSourceProducesPulseAndSaturation), AcquiredPeripheralSourceProducesPulseAndSaturation),
        new(nameof(MechanicalPerfusionLossClearsSaturationAndRecovers), MechanicalPerfusionLossClearsSaturationAndRecovers),
        new(nameof(OpticalStreamFencesIdentityQualityAndContinuity), OpticalStreamFencesIdentityQualityAndContinuity),
        new(nameof(OximeterRestoreAndFailureAreAtomic), OximeterRestoreAndFailureAreAtomic),
    ];
    private static PulseOximeterIllustrationSource Source(int saturation = 98000, Guid? sensor = null) => new(Pleth, Pleth, sensor ?? Sensor, saturation);
    private static PulseOximeterMeasurement Measurement() => PulseOximeterMeasurement.CreateIllustration(Pleth);

    private static void MechanicalPerfusionLossClearsSaturationAndRecovers()
    {
        // Exercise the actual mechanical schedule and runoff through the preview's
        // acquired red/IR samples. Do not replace the waveform or inject a reading.
        foreach (int target in new[] { 98000, 80000 })
            foreach (var (afterCycles, durationCycles) in new (int?, int?)[] { (null, null), (10, null), (10, 15) })
            {
                var configuration = PhysiologyIllustrationConfiguration.Default with
                {
                    VentricularMechanicalEnabled = false,
                    MechanicalAfterCycles = afterCycles,
                    MechanicalDurationCycles = durationCycles
                };
                var session = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), true,
                    target, opticalVariation: new(target, 2500, new string('1', 64)));
                bool measuredBeforeStop = false, lost = false, flat = false, recovered = false;
                for (int step = 1; step <= 160; step++)
                {
                    session.Advance(200_000_000);
                    var snapshot = session.Measurements!;
                    var reading = snapshot.SpO2;
                    var notice = SpO2LimitNotice.Evaluate(true, 92000, 85000, reading);
                    long timeNs = session.SimulationTimeNs;
                    bool beforeStop = afterCycles is not null && timeNs is >= 6_000_000_000 and < 10_000_000_000;
                    bool afterRecovery = durationCycles is not null && timeNs >= 26_000_000_000;
                    if (beforeStop || afterRecovery)
                    {
                        Check.That(reading.Status == WaveformMeasurementStatus.Valid &&
                            reading.SaturationMilliPercent is { } value && Math.Abs(value - target) <= 3000 &&
                            reading.PerfusionMilliPercent > 0,
                            "actual peripheral pulses support measured saturation before loss and after recovery");
                        Check.That(target == 80000
                            ? notice is { Id: "spo2-low", Level: MonitorNoticeLevel.Critical }
                            : notice is null,
                            "only valid measured low saturation activates or reactivates its physiological notice");
                        measuredBeforeStop |= beforeStop;
                        recovered |= afterRecovery;
                    }
                    // Ten 800ms cycles stop at source time8s, resuming at20s when
                    // requested. Allow the2s acquisition delay and4s optical window.
                    bool withoutPulses = afterCycles is null ? timeNs >= 6_000_000_000
                        : timeNs >= 14_000_000_000 && (durationCycles is null || timeNs < 22_000_000_000);
                    if (withoutPulses)
                    {
                        Check.That(reading.Status == WaveformMeasurementStatus.PoorSignal &&
                            reading.SaturationMilliPercent is null && reading.RatioPpm is null && !reading.IsQuestionable,
                            "continued sample delivery without effective ejections cannot retain an old saturation");
                        var display = MeasurementDisplay.Resolve(MeasurementSource.SpO2, reading.Status, null);
                        Check.That(display.NumericText == "---" && display.TopNotice == "SpO₂信号质量不足" && notice is null,
                            "no pulsation clears the low-saturation notice and displays unavailable without inventing sensor disconnection");
                        Check.That(snapshot.HeartRate.Status == WaveformMeasurementStatus.Valid,
                            "ongoing electrical activity cannot stand in for peripheral perfusion");
                        if (timeNs >= 18_000_000_000)
                        {
                            Check.That(reading.PerfusionMilliPercent == 0 && snapshot.PulseRate.MilliBeatsPerMinute is null,
                                "fully settled optical signal has zero PI and no retained pulse rate");
                            flat = true;
                        }
                        lost = true;
                    }
                    if (reading.Status != WaveformMeasurementStatus.Valid || target == 98000)
                    { Check.That(notice is null, "signal transitions never manufacture a low-saturation alarm"); }
                }
                Check.That(lost && flat && measuredBeforeStop == (afterCycles is not null) && recovered == (durationCycles is not null),
                    "startup without ejection, permanent arrest and resumption all exercise their complete measurement lifecycle");
                if (afterCycles is not null)
                {
                    double[] tail = session.Samples(2, 8_000_000_000, 14_000_000_000).Select(sample => sample.Value).ToArray();
                    Check.That(tail.Length == 750 && tail[0] > 0 && tail[^1] == 0 &&
                        tail.Zip(tail.Skip(1)).All(pair => pair.First >= pair.Second),
                        "the acquired final pulse naturally decays to baseline while electrical activity continues");
                }
            }
    }

    private static void SeededOpticalVariationReproducesMeasuredSignals()
    {
        string seed = new('1', 64);
        var plan = new SeededOpticalSaturation(95000, 2000, seed);
        var same = new SeededOpticalSaturation(95000, 2000, seed);
        var other = new SeededOpticalSaturation(95000, 2000, new string('2', 64));
        long loop = SeededOpticalSaturation.KnotPeriodNs * SeededOpticalSaturation.KnotCount;
        Check.That(plan.PreparedState == same.PreparedState && plan.At(0) == 95000, "same seed retains full preparation state and starts at nominal");
        bool differs = false;
        for (long t = 0; t <= loop; t += 1_000_000_000)
        {
            int value = plan.At(t);
            Check.That(value is >= 93000 and <= 97000 && value == same.At(t) && value == plan.At(t + loop), "bounded periodic deterministic target");
            Check.That(Math.Abs(value - plan.At(t + 8_000_000)) <= 2, "125Hz target steps remain smooth");
            differs |= value != other.At(t);
        }
        Check.That(differs && plan.At(long.MaxValue) is >= 93000 and <= 97000, "different seeds differ; late lookup is bounded and overflow-safe");
        foreach (int target in new[] { 0, 1000, 40000, 70000, 75000, 97500, 98000, 100000 })
        {
            var bounded = new SeededOpticalSaturation(target, 2500, seed);
            for (long t = 0; t < loop; t += 8_000_000)
            {
                int value = bounded.At(t);
                Check.That(value >= Math.Max(0, target - 2500) && value <= Math.Min(100000, target + 2500), "bounded2.5pp excursions");
                Check.That(Math.Abs(value - bounded.At(t + 8_000_000)) <= 2, "bounded knots retain smooth transitions");
            }
        }
        foreach (int target in new[] { 97500, 98000, 100000 })
        {
            var source = new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, target,
                variation: new SeededOpticalSaturation(target, 2500, seed));
            var estimator = Measurement(); List<int> readings = [];
            for (int i = 0; i < 450; i++)
            {
                var result = estimator.Consume(source.ConvertAcquiredPulse(Input(i)));
                if (i < 25) { continue; }
                Check.That(result.SpO2.Status == WaveformMeasurementStatus.Valid, "upper-bound variation retains measured saturation");
                readings.Add(result.SpO2.SaturationMilliPercent!.Value);
            }
            Check.That(readings.Max() <= 100000 && readings.Max() >= 99500 && readings.Max() - readings.Min() >= 1800,
                "bounded2.5pp variation reaches displayed100 and varies through actual optical samples");
        }
        Reject(() => _ = new SeededOpticalSaturation(95000, 2501, seed), "excess amplitude rejects");
        Reject(() => _ = new SeededOpticalSaturation(95000, 1000, "invalid"), "invalid seed rejects");
        Reject(() => plan.At(-1), "negative time rejects");
        Reject(() => _ = new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, 98000, variation: plan), "target mismatch rejects");
        var varied = new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, 95000, variation: plan);
        var zero = new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, 95000, variation: new(95000, 0, seed));
        var measured = Measurement(); var restored = Measurement(); List<int> values = [];
        for (int i = 0; i < 600; i++)
        {
            byte[] input = Input(i);
            Check.That(zero.ConvertAcquiredPulse(input).SequenceEqual(Source(95000).ConvertAcquiredPulse(input)), "zero amplitude preserves old bytes");
            byte[] wire = varied.ConvertAcquiredPulse(input);
            var replay = new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, 95000, variation: same);
            Check.That(wire.SequenceEqual(replay.ConvertAcquiredPulse(input)), "arbitrary-time conversion needs no prior random draws");
            var result = measured.Consume(wire);
            Check.That(result == restored.Consume(wire), "measurement checkpoint continuation is unchanged under drift");
            if (i == 301) { restored = PulseOximeterMeasurement.Restore(restored.Capture()); }
            if (i > 25)
            {
                Check.That(result.SpO2.Status == WaveformMeasurementStatus.Valid, "slow drift retains valid optical signal");
                int value = result.SpO2.SaturationMilliPercent!.Value;
                values.Add(value);
                Check.That(Math.Abs(value - plan.At(i * 200_000_000L - 2_000_000_000)) < 700, "measured four-second optical window follows authored target within teaching tolerance");
            }
        }
        Check.That(values.Max() - values.Min() > 1000, "independent measured saturation changes by more than one percentage point");
        var flatMeasurement = Measurement();
        for (int i = 0; i < 40; i++) { flatMeasurement.Consume(varied.ConvertAcquiredPulse(Input(i, flat: true))); }
        Check.That(flatMeasurement.Read(7_992_000_000).SpO2.Status == WaveformMeasurementStatus.PoorSignal,
            "target drift cannot manufacture valid saturation without peripheral pulses");
        byte[] bad = Rewrite(Input(600), b => b with { InstanceId = Sensor });
        Reject(() => varied.ConvertAcquiredPulse(bad), "bad source rejected during variation");
        Check.That(varied.ConvertAcquiredPulse(Input(600)).SequenceEqual(new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, 95000, variation: same).ConvertAcquiredPulse(Input(600))), "failure consumes no random state");
    }

    private static void AcquiredPeripheralSourceProducesPulseAndSaturation()
    {
        foreach (int conduction in new[] { 1, 2 })
        {
            // The physical source depends on conduction, not the optical target or modulation.
            var physical = PhysiologyIllustrationSource.Create(PhysiologyIllustrationConfiguration.Default with { VentricularConductionRatio = conduction });
            List<byte[]> acquired = [];
            for (int step = 1; step <= 65; step++)
                acquired.AddRange(physical.AdvanceTo(step * 200_000_000L, 50, 1, 100));
            foreach (int target in new[] { 75000, 90000, 98000, 99000, 100000 })
                foreach (int modulation in target == 100000 ? new[] { 1000, 2000 } : new[] { 1000 })
                {
                    var optical = new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, target, modulation); var measurement = Measurement(); PulseOximeterReading? reading = null;
                    foreach (byte[] original in acquired)
                    {
                        byte[] wire = optical.ConvertAcquiredPulse(original);
                        var packet = WaveformEnvelopeCodec.Decode(wire);
                        var old = WaveformEnvelopeCodec.Decode(original);
                        Check.That(packet.InstanceId == Sensor && packet.StartSimTimeNs == old.StartSimTimeNs &&
                            packet.Planes.Single(p => p.ChannelId == Pleth).Samples.SequenceEqual(old.Planes.Single(p => p.ChannelId == Pleth).Samples),
                            "same sensor bundle preserves acquired Pleth shape/time, not screen pixels");
                        reading = measurement.Consume(wire);
                        if (packet.StartSimTimeNs < 3_800_000_000)
                        { Check.That(reading.SpO2.SaturationMilliPercent is null, "no saturation before full window"); }
                    }
                    Check.That(reading!.PulseRate.Status == WaveformMeasurementStatus.Valid && Math.Abs(reading.PulseRate.MilliBeatsPerMinute!.Value - 75000 / conduction) <= 100, "sensor PR follows actual peripheral ejection: " + conduction + "/" + reading);
                    Check.That(reading.SpO2.Status == WaveformMeasurementStatus.Valid && Math.Abs(reading.SpO2.SaturationMilliPercent!.Value - target) <= 500,
                        $"authored target recovered from two quantized wavelengths within0.5 percentage point: {target}/{conduction}: {reading.SpO2}");
                }
        }
        var sensor = Source(); var m = Measurement();
        for (int block = 0; block < 40; block++) { m.Consume(sensor.ConvertAcquiredPulse(Input(block, flat: true))); }
        Check.That(m.Read(7_992_000_000).SpO2.Status == WaveformMeasurementStatus.PoorSignal,
            "constant light without pulsatility cannot display the configured98%");
        byte[] originalBytes = Input(0);
        Check.That(Source(90000).ConvertAcquiredPulse(originalBytes).SequenceEqual(Source(90000).ConvertAcquiredPulse(originalBytes)), "conversion is deterministic");
        Reject(() => Source().ConvertAcquiredPulse(Rewrite(Input(0), b => b with { InstanceId = Sensor })), "cannot conceal an upstream source switch");
        var offsetMeasurement = Measurement(); var plainMeasurement = Measurement();
        for (int block = 0; block < 25; block++)
        {
            byte[] plain = sensor.ConvertAcquiredPulse(Input(block));
            byte[] offset = Rewrite(plain, b => b with
            {
                Planes = b.Planes.Select(p => p.ChannelId == Pleth ? p : p with
                {
                    Samples = p.Samples.Select(v => (short)(v - 1000)).ToArray(),
                    OffsetNumerator = 1000
                }).ToArray()
            });
            Check.That(offsetMeasurement.Consume(offset) == plainMeasurement.Consume(plain), "wire scale/offset contract preserves physical optical samples");
        }
    }

    private static void OpticalStreamFencesIdentityQualityAndContinuity()
    {
        var source = Source(); var measurement = Measurement();
        for (int block = 0; block < 30; block++) { measurement.Consume(source.ConvertAcquiredPulse(Input(block))); }
        var good = measurement.Read(5_992_000_000);
        Check.That(good.SpO2.Status == WaveformMeasurementStatus.Valid, "window fills");
        Check.That(measurement.Read(6_600_000_000).SpO2.Status == WaveformMeasurementStatus.NoData, "no new samples expire reading");
        measurement.Consume(source.ConvertAcquiredPulse(Input(30, quality: true)));
        Check.That(measurement.Read(6_192_000_000).SpO2.Status == WaveformMeasurementStatus.PoorSignal, "quality propagates to both optical planes");
        for (int block = 31; block < 50; block++) { measurement.Consume(source.ConvertAcquiredPulse(Input(block))); }
        Check.That(measurement.Read(9_992_000_000).SpO2.Status == WaveformMeasurementStatus.PoorSignal, "bad data retained invalid for full averaging window");
        measurement.Consume(source.ConvertAcquiredPulse(Input(50)));
        Check.That(measurement.Read(10_192_000_000).SpO2.Status == WaveformMeasurementStatus.Valid, "recovers after complete clean window");
        measurement.Consume(source.ConvertAcquiredPulse(Input(52)));
        Check.That(measurement.Read(10_592_000_000).SpO2.Status == WaveformMeasurementStatus.WarmingUp, "gap clears previous history");
        measurement.Consume(source.ConvertAcquiredPulse(Input(53, revision: 2)));
        Reject(() => measurement.Consume(source.ConvertAcquiredPulse(Input(54))), "reject revision rollback");
        var replacement = Source(90000, Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"));
        Check.That(measurement.Consume(replacement.ConvertAcquiredPulse(Input(54))).SpO2.Status == WaveformMeasurementStatus.WarmingUp,
            "different sensor/target starts new window");
        for (int block = 55; block < 75; block++) { measurement.Consume(replacement.ConvertAcquiredPulse(Input(block))); }
        Check.That(Math.Abs(measurement.Read(14_992_000_000).SpO2.SaturationMilliPercent!.Value - 90000) < 500,
            "old target is not blended into replacement sensor readings");
    }

    private static void OximeterRestoreAndFailureAreAtomic()
    {
        var source = Source(); var measurement = Measurement();
        for (int block = 0; block < 7; block++) { measurement.Consume(source.ConvertAcquiredPulse(Input(block))); }
        var checkpoint = measurement.Capture(); var restored = PulseOximeterMeasurement.Restore(checkpoint);
        for (int block = 7; block < 30; block++)
        {
            byte[] wire = source.ConvertAcquiredPulse(Input(block));
            Check.That(measurement.Consume(wire) == restored.Consume(wire), "mid-window restore preserves PR and SpO2");
        }
        Check.That(PulseOximeterMeasurement.Restore(checkpoint).Read(1_392_000_000).SpO2.Status == WaveformMeasurementStatus.WarmingUp,
            "captured window not changed by later consumes");
        var before = measurement.Read(5_992_000_000);
        byte[] next = source.ConvertAcquiredPulse(Input(30));
        foreach (byte[]? bad in new[]
        {
            source.ConvertAcquiredPulse(Input(29)),
            Rewrite(next, b => b with { Planes = b.Planes.Where(p => p.ChannelId != PulseOximeterIllustrationSource.InfraredChannelId).ToArray() }),
            Rewrite(next, b => b with { Planes = b.Planes.Select(p => p.ChannelId == PulseOximeterIllustrationSource.InfraredChannelId ? p with { FirstSampleIndex = p.FirstSampleIndex + 1 } : p).ToArray() }),
            Rewrite(next, b => b with { Planes = b.Planes.Select(p => p.ChannelId == PulseOximeterIllustrationSource.RedChannelId ? p with { ScaleNumerator = int.MaxValue } : p).ToArray() })
        })
        {
            Reject(() => measurement.Consume(bad), "reject malformed optical packet");
            Check.That(before == measurement.Read(5_992_000_000), "late optical failure does not advance PR or window");
        }
        Check.That(measurement.Consume(next) == restored.Consume(next), "valid retry agrees with untouched branch");
        byte[] clipped = source.ConvertAcquiredPulse(Input(31, clip: true));
        Check.That(measurement.Consume(clipped).SpO2.Status == WaveformMeasurementStatus.PoorSignal, "source clipping never becomes plausible optical amplitude");
    }

    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (Exception ex) when (ex is ArgumentException or OverflowException) { rejected = true; }
        Check.That(rejected, message);
    }
    private static byte[] Rewrite(byte[] bytes, Func<WaveformEnvelope, WaveformEnvelope> change) => WaveformEnvelopeCodec.EncodeRaw(change(WaveformEnvelopeCodec.Decode(bytes)));
    private static byte[] Input(int block, bool flat = false, bool quality = false, ulong revision = 1, bool clip = false)
    {
        short[] samples = Enumerable.Range(block * 25, 25).Select(i =>
        {
            int phase = i % 100;
            return clip ? short.MaxValue : (short)(flat ? 500 : phase < 20 ? phase * 50 : Math.Max(0, 1000 - (phase - 20) * 40));
        }).ToArray();
        var plane = new WaveformPlane(Pleth, 125, 1, (ulong)block * 25, 1, 1, 0, 1,
            quality ? WaveformQualityEncoding.Ranges : WaveformQualityEncoding.None, samples, quality ? [new(0, 25, 1)] : []);
        return WaveformEnvelopeCodec.EncodeRaw(new(Pleth, Pleth, 1, 1, (ulong)block, revision,
            block * 200_000_000L, 200_000_000, [plane]));
    }
}
