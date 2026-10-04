// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Audio;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class DeepOxygenationSpecifications
{
    private static readonly Guid Pleth = PhysiologyIllustrationSource.ChannelId(2);
    private static readonly Guid Sensor = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
    public static Specification[] All =>
    [
        new(nameof(DeepOpticsReportNumbersAcrossPhysicalRange), DeepOpticsReportNumbersAcrossPhysicalRange),
        new(nameof(OpticalAttenuationMatchesIndependentExponential), OpticalAttenuationMatchesIndependentExponential),
        new(nameof(DeepOpticalCalibrationRetainsQualityAndRangeSemantics), DeepOpticalCalibrationRetainsQualityAndRangeSemantics),
        new(nameof(DeepMeasurementRestoresAndRecoversAfterSignalLoss), DeepMeasurementRestoresAndRecoversAfterSignalLoss),
        new(nameof(DeepPreviewPreservesTimingAndAlarmRecovery), DeepPreviewPreservesTimingAndAlarmRecovery),
    ];

    private static void OpticalAttenuationMatchesIndependentExponential()
    {
        foreach (int target in new[] { 0, 70000, 98000, 100000 })
        {
            short[] samples = Enumerable.Range(0, 25).Select(i => (short)(new[] { -1000, 0, 1000, 10000 }[i % 4])).ToArray();
            var plane = new WaveformPlane(Pleth, 125, 1, 0, 1, 1, 0, 1, WaveformQualityEncoding.None, samples, []);
            byte[] wire = WaveformEnvelopeCodec.EncodeRaw(new(Pleth, Pleth, 1, 1, 0, 1, 0, 200_000_000, [plane]));
            var source = new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, target, 2000);
            var light = WaveformEnvelopeCodec.Decode(source.ConvertAcquiredPulse(wire));
            var red = light.Planes.Single(p => p.ChannelId == PulseOximeterIllustrationSource.RedChannelId);
            var infrared = light.Planes.Single(p => p.ChannelId == PulseOximeterIllustrationSource.InfraredChannelId);
            for (int i = 0; i < samples.Length; i++)
            {
                double depth = samples[i] * .00004;
                double ratio = Math.Max((110000 - target) / 25000.0, .408);
                Check.That(Math.Abs(red.Samples[i] - 16000 * Math.Exp(-depth * ratio)) <= .501 &&
                    Math.Abs(infrared.Samples[i] - 20000 * Math.Exp(-depth)) <= .501,
                    "bounded integer attenuation matches an independent exponential within half an ADC count");
            }
        }
    }

    private static void DeepOpticsReportNumbersAcrossPhysicalRange()
    {
        foreach (int target in new[] { 0, 1000, 5000, 20000, 40000, 69000, 70000, 74000, 75000, 98000, 100000 })
            foreach (int gain in new[] { 100, 1000, 2000 })
            {
                var physical = PhysiologyIllustrationSource.Create();
                var optical = new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, target, gain);
                var measurement = PulseOximeterMeasurement.CreateIllustration(Pleth);
                PulseOximeterReading? result = null;
                for (int step = 1; step <= 45; step++)
                    foreach (byte[] wire in physical.AdvanceTo(step * 200_000_000L, 50, 1, 100))
                    { result = measurement.Consume(optical.ConvertAcquiredPulse(wire)); }
                var reading = result!.SpO2;
                Check.That(reading.Status == WaveformMeasurementStatus.Valid && reading.SaturationMilliPercent is >= 0 and <= 100000 &&
                    Math.Abs(reading.SaturationMilliPercent.Value - target) <= 4000,
                    $"paired acquired optics retain a numerical deep reading within the teaching error budget: {target}/{gain}: {reading}");
                Check.That(result.PulseRate.Status == WaveformMeasurementStatus.Valid && Math.Abs(result.PulseRate.MilliBeatsPerMinute.GetValueOrDefault() - 75000) <= 100,
                    "deep saturation does not replace the independent pulse measurement");
                Check.That(target >= 75000 || SpO2LimitNotice.Evaluate(true, 92000, 85000, reading)?.Level == MonitorNoticeLevel.Critical,
                    "valid deep values, including zero, keep the low-saturation alarm active");
            }
    }

    private static void DeepOpticalCalibrationRetainsQualityAndRangeSemantics()
    {
        OpticalSample[] Pairs(int redAmplitude) => Enumerable.Range(0, 500).Select(i => new OpticalSample(i * 8_000_000L,
            10000 + (i % 100 < 50 ? redAmplitude : -redAmplitude), 20000 + (i % 100 < 50 ? 1000 : -1000))).ToArray();
        var illustration = OpticalSaturationMeasurement.CreateIllustration();
        foreach (var (amplitude, expected) in new[] { (200, 100000), (800, 70000), (1400, 40000), (2200, 0), (2300, 0) })
        {
            var reading = illustration.Estimate(Pairs(amplitude), 3_992_000_000);
            Check.That(reading.Status == WaveformMeasurementStatus.Valid && reading.SaturationMilliPercent == expected,
                "teaching transfer spans0-100 with physical endpoint clipping only");
        }
        var custom = new OpticalSaturationMeasurement("caller-calibration", [new(400000, 100000), new(1600000, 70000)]);
        var outside = custom.Estimate(Pairs(1400), 3_992_000_000);
        Check.That(outside.Status == WaveformMeasurementStatus.OutOfRange && outside.SaturationMilliPercent is null && outside.RatioPpm == 2800000,
            "only explicit illustration calibration extends the numeric range");
        Check.That(MeasurementDisplay.Resolve(MeasurementSource.SpO2, outside.Status, null).TopNotice == "SpO₂超出测量范围",
            "uncovered calibration is distinct from poor signal");
        foreach (var invalid in new[]
        {
            Pairs(1400).Select(p => p with { QualityFlags = 1 }).ToArray(),
            Pairs(1400).Select(p => p with { Red = 0 }).ToArray(),
            Pairs(1400).Select(p => p with { Red = 20000 - p.Red }).ToArray(),
            Pairs(1400).Select(p => p with { Red = 16000, Infrared = 20000 }).ToArray(),
        })
        {
            var reading = illustration.Estimate(invalid, 3_992_000_000);
            Check.That(reading.Status == WaveformMeasurementStatus.PoorSignal && reading.SaturationMilliPercent is null &&
                SpO2LimitNotice.Evaluate(true, 92000, 85000, reading) is null,
                "deep range never relaxes flags, clipping, coherence or pulsatility gates");
        }
    }

    private static void DeepMeasurementRestoresAndRecoversAfterSignalLoss()
    {
        var physical = PhysiologyIllustrationSource.Create();
        var trace = new SampledArterialOxygenation(new(0, 1_000_000_000,
            Enumerable.Range(0, 41).Select(t => t < 24 ? 40000 : 98000).ToArray()));
        var optical = new PulseOximeterIllustrationSource(Pleth, Pleth, Sensor, trace);
        var measurement = PulseOximeterMeasurement.CreateIllustration(Pleth);
        PulseOximeterMeasurement? restored = null;
        bool deep = false, poor = false, recoveredLow = false, recoveredNormal = false, expired = false;
        for (int step = 1; step <= 180; step++)
            foreach (byte[] wire in physical.AdvanceTo(step * 200_000_000L, 50, 1, 100))
            {
                var light = WaveformEnvelopeCodec.Decode(optical.ConvertAcquiredPulse(wire));
                long time = light.StartSimTimeNs;
                if (time is >= 8_000_000_000 and < 12_000_000_000)
                {
                    light = light with
                    {
                        Planes = light.Planes.Select(p => p with
                        { QualityEncoding = WaveformQualityEncoding.Ranges, QualityRanges = [new(0, (uint)p.Samples.Count, 1)] }).ToArray()
                    };
                }
                byte[] packet = WaveformEnvelopeCodec.EncodeRaw(light);
                var result = measurement.Consume(packet);
                if (restored is not null) { Check.That(restored.Consume(packet) == result, "mid-window checkpoint retains deep readings, quality and recovery"); }
                if (time == 9_000_000_000) { restored = PulseOximeterMeasurement.Restore(measurement.Capture()); }
                var notice = SpO2LimitNotice.Evaluate(true, 92000, 85000, result.SpO2);
                if (time is >= 5_000_000_000 and < 8_000_000_000 || time is >= 16_000_000_000 and < 22_000_000_000)
                {
                    Check.That(result.SpO2.Status == WaveformMeasurementStatus.Valid && result.SpO2.SaturationMilliPercent < 45000 &&
                        notice?.Level == MonitorNoticeLevel.Critical, "deep measurement and critical notice return after a full clean window");
                    deep = true;
                    recoveredLow |= time >= 16_000_000_000;
                }
                if (time is >= 8_000_000_000 and < 15_800_000_000)
                {
                    Check.That(result.SpO2.Status == WaveformMeasurementStatus.PoorSignal && result.SpO2.SaturationMilliPercent is null && notice is null,
                        "bad optical samples clear low readings until the complete window is clean");
                    poor = true;
                }
                if (time >= 30_000_000_000)
                {
                    Check.That(result.SpO2.Status == WaveformMeasurementStatus.Valid && result.SpO2.SaturationMilliPercent > 96000 && notice is null,
                        "normal recovery clears the measured low condition");
                    recoveredNormal = true;
                }
                if (time == 20_000_000_000)
                {
                    var stale = measurement.Read(time + 800_000_000).SpO2;
                    Check.That(stale.Status == WaveformMeasurementStatus.NoData && stale.SaturationMilliPercent is null &&
                        SpO2LimitNotice.Evaluate(true, 92000, 85000, stale) is null, "missing data does not retain a deep value");
                    expired = true;
                }
            }
        Check.That(deep && poor && recoveredLow && recoveredNormal && expired, "all deep recovery stages observed");
    }

    private static void DeepPreviewPreservesTimingAndAlarmRecovery()
    {
        int Target(int second) => second switch
        {
            < 8 => 98000,
            < 16 => 98000 - (second - 8) * 1000,
            < 24 => 90000,
            < 32 => 90000 - (second - 24) * 1250,
            < 40 => 80000,
            < 64 => 80000 - (second - 40) * 2500,
            < 96 => 20000,
            < 128 => 20000 + (second - 96) * 2400,
            _ => 98000
        };
        var trace = new SampledArterialOxygenation(new(0, 1_000_000_000, Enumerable.Range(0, 145).Select(Target).ToArray()));
        var configuration = PhysiologyIllustrationConfiguration.Default with
        { VentricularMechanicalEnabled = false, MechanicalAfterCycles = 90, MechanicalDurationCycles = 20 };
        var large = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), true, oxygenation: trace);
        var small = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), true, oxygenation: trace);
        var rotation = new MonitorNoticeRotation();
        var pitch = new MonitorBeatPitch();
        var audio = new AudioRenderSession();
        var sequencer = new MonitorAlarmSequencer(audio);
        bool warning = false, critical = false, lost = false, deepRestored = false, normal = false;
        for (int tick = 0; tick < 560; tick++)
        {
            large.Advance(250_000_000);
            for (int part = 0; part < 10; part++) { small.Advance(25_000_000); }
            Check.That(large.Measurements == small.Measurements, "source-time deep measurements do not depend on preview pacing");
            var snapshot = large.Measurements!;
            var reading = snapshot.SpO2;
            var notice = SpO2LimitNotice.Evaluate(true, 92000, 85000, reading);
            rotation.Update(notice is null ? [] : [notice], large.SimulationTimeNs);
            pitch.Update(reading, snapshot.SampleTimeNs);
            sequencer.Update(notice is null ? null : new(notice.Level, 50, new()));
            long time = large.SimulationTimeNs;
            if (time is >= 22_000_000_000 and < 26_000_000_000)
            { Check.That(notice?.Level == MonitorNoticeLevel.Warning, "warning is driven by acquired desaturation"); warning = true; }
            if (time is >= 68_000_000_000 and < 74_000_000_000 || time is >= 94_000_000_000 and < 98_000_000_000)
            {
                Check.That(reading.Status == WaveformMeasurementStatus.Valid && reading.SaturationMilliPercent is >= 0 and < 30000 &&
                    notice?.Level == MonitorNoticeLevel.Critical && rotation.CriticalElapsedNs("spo2-low") is not null &&
                    !pitch.Unavailable && pitch.SaturationPercent == 70, "deep number retains critical alarm and the lowest supported pitch");
                critical = true;
                deepRestored |= time >= 94_000_000_000;
            }
            if (time is >= 80_000_000_000 and < 90_000_000_000)
            {
                Check.That(reading.Status == WaveformMeasurementStatus.PoorSignal && reading.SaturationMilliPercent is null &&
                    notice is null && rotation.CriticalElapsedNs("spo2-low") is null && pitch.Unavailable,
                    "actual mechanical stop removes measurable saturation despite latent deep SaO2");
                lost = true;
            }
            if (time >= 136_000_000_000)
            {
                Check.That(reading.Status == WaveformMeasurementStatus.Valid && reading.SaturationMilliPercent > 96000 &&
                    notice is null && rotation.Highest is null && !pitch.Unavailable, "normal recovery clears the full physiological notice chain");
                normal = true;
            }
            if (time is 70_000_000_000 or 82_000_000_000 or 96_000_000_000 or 138_000_000_000)
            {
                float[] pcm = new float[480];
                audio.TryProduce(480); audio.Read(pcm);
                audio.TryProduce(480); audio.Read(pcm);
                Check.That(pcm.Any(v => Math.Abs(v) > .0001f) == (notice is not null), "critical audio renders and clears through the actual sequencer");
                var paused = large.Measurements;
                Check.That(large.Measurements == paused && large.SimulationTimeNs == time, "reading a paused session never advances oxygenation");
            }
        }
        Check.That(warning && critical && lost && deepRestored && normal, "warning, deep alarm, pulse loss, deep recovery and normal recovery all observed");
    }
}
