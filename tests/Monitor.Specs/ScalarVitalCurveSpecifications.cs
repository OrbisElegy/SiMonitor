// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ScalarVitalCurveSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(PressureRampsBlendActualReservoirSamples), PressureRampsBlendActualReservoirSamples),
        new(nameof(ScalarRampsPreserveAbsentMechanicalAndRespiratoryActivity), ScalarRampsPreserveAbsentMechanicalAndRespiratoryActivity),
        new(nameof(ScalarRampsSurvivePendingCheckpointAndPartitionedAcquisition), ScalarRampsSurvivePendingCheckpointAndPartitionedAcquisition),
        new(nameof(CvpAndCo2RampsChangeAcquiredSamples), CvpAndCo2RampsChangeAcquiredSamples),
        new(nameof(SessionScalarTimelineChangesMeasuredWaveforms), SessionScalarTimelineChangesMeasuredWaveforms),
        new(nameof(FutureHeartRateDoesNotRecalibrateCurrentPressure), FutureHeartRateDoesNotRecalibrateCurrentPressure),
        new(nameof(TemplatePressureCurvesUseOnlyTheirActiveInterval), TemplatePressureCurvesUseOnlyTheirActiveInterval),
    ];

    private static VitalValueCurve Ramp(int from, int to, long startNs = 10_000_000_000, long durationNs = 10_000_000_000) =>
        new(from, [new(startNs, durationNs, to)]);

    private static void PressureRampsBlendActualReservoirSamples()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default;
        var state = PhysiologyIllustrationSource.Create(configuration).CaptureState().Channels
            .Single(channel => channel.ChannelId == PhysiologyIllustrationSource.ChannelId(3)).Generator;
        var physiology = state.Timeline.Plan;
        var pressure = state.VascularPressure!;
        VascularPressureSource Fixed(int systolic, int diastolic)
        {
            var solved = VascularPressureSource.SolveTarget(physiology, pressure, systolic, diastolic)!.Value;
            return VascularPressureSource.Create(physiology, pressure with
            {
                InitialPressureCentiMmHg = diastolic,
                EjectionEquilibriumCentiMmHg = solved.EjectionEquilibriumCentiMmHg,
                Morphology = pressure.Morphology! with { PulseHeightCentiMmHg = solved.PulseHeightCentiMmHg }
            });
        }
        var from = Fixed(12000, 8000);
        var to = Fixed(9000, 5000);
        var ramp = VascularPressureSource.Create(physiology, pressure with
        { TargetCurves = new(Ramp(12000, 9000), Ramp(8000, 5000)) });
        foreach (long time in new[] { 9_000_000_000L, 10_000_000_000, 15_000_000_000, 20_000_000_000, 21_000_000_000 })
        {
            long expected = time <= 10_000_000_000 ? from.EvaluateAt(time) : time >= 20_000_000_000 ? to.EvaluateAt(time) :
                (from.EvaluateAt(time) + to.EvaluateAt(time)) / 2;
            Check.That(Math.Abs(ramp.EvaluateAt(time) - expected) <= 1,
                "pressure ramp interpolates the actual reservoir and contour with unchanged event timing");
        }
        var stepped = VascularPressureSource.Create(physiology, pressure with
        { TargetCurves = new(Ramp(12000, 9000, 10_000_000_000, 0), Ramp(8000, 5000, 10_000_000_000, 0)) });
        Check.That(stepped.EvaluateAt(9_999_999_999) == from.EvaluateAt(9_999_999_999) &&
            stepped.EvaluateAt(10_000_000_000) == to.EvaluateAt(10_000_000_000), "zero-duration events never ramp before their declared boundary");
    }

    private static void ScalarRampsPreserveAbsentMechanicalAndRespiratoryActivity()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default with
        { VentricularMechanicalEnabled = false, RespiratoryActivity = RespiratoryActivity.Absent };
        var expected = PhysiologyIllustrationSource.Create(configuration);
        var actual = PhysiologyIllustrationSource.Create(configuration with
        {
            AbpSystolicCentiMmHgCurve = Ramp(12000, 20000, 0),
            AbpDiastolicCentiMmHgCurve = Ramp(8000, 13000, 0),
            PaSystolicCentiMmHgCurve = Ramp(2500, 5000, 0),
            PaDiastolicCentiMmHgCurve = Ramp(1000, 3000, 0),
            Co2EndExpiratoryCentiMmHgCurve = Ramp(4000, 8000, 0)
        });
        for (long time = 50_000_000; time <= 2_000_000_000; time += 50_000_000)
        {
            var left = expected.AdvanceTo(time, 20, 1, 100);
            var right = actual.AdvanceTo(time, 20, 1, 100);
            Check.That(left.Count == right.Count && left.Zip(right).All(pair => pair.First.SequenceEqual(pair.Second)),
                "scalar targets do not synthesize pressure ejections or expired gas");
        }
    }

    private static void ScalarRampsSurvivePendingCheckpointAndPartitionedAcquisition()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default with
        {
            CvpBaselineCentiMmHgCurve = Ramp(600, 1200, 0, 2_000_000_000),
            Co2EndExpiratoryCentiMmHgCurve = Ramp(4000, 2000, 0, 2_000_000_000)
        };
        var original = PhysiologyIllustrationSource.Create(configuration);
        _ = original.AdvanceTo(50_000_000, 20, 1, 100);
        var restored = PhysiologyWaveformGroup.Restore(original.CaptureState());
        List<byte[]> expected = [], actual = [];
        for (long time = 100_000_000; time <= 3_000_000_000; time += 50_000_000)
        { expected.AddRange(original.AdvanceTo(time, 20, 1, 100)); }
        for (long time = 200_000_000; time <= 3_000_000_000; time += 200_000_000)
        { actual.AddRange(restored.AdvanceTo(time, 50, 1, 100)); }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.SequenceEqual(pair.Second)),
            "pending samples validate and future bytes remain independent of caller partitions");
    }




    private static void TemplatePressureCurvesUseOnlyTheirActiveInterval()
    {
        var sinus = PhysiologyIllustrationConfiguration.Default with { AbpTarget = new(12000, 8000) };
        var svt = PhysiologyIllustrationConfiguration.SvtPreset with { AbpTarget = new(7700, 7000) };
        _ = PhysiologyIllustrationSource.Create(svt);
        var session = new LocalMonitorPreviewSession(sinus, MonitorDisplayConfiguration.Default(), enableMeasurements: true);
        session.ConfigurePatientTimeline([
            new("continuous venous ramp", 0, 4_000_000_000, new() { CvpCentiMmHg = 1600 }),
            new("svt", 1_000_000_000, 0, new() { Abp = svt.AbpTarget }) { EcgTemplateId = "test.svt" },
            new("sinus", 3_000_000_000, 0, new() { Abp = sinus.AbpTarget }) { EcgTemplateId = "test.sinus" }
        ], [null, new(svt, null), new(sinus, null)]);
        for (int step = 0; step < 10; step++) { session.Advance(200_000_000); }
        Check.That(session.Configuration.Svt && session.Configuration.CvpBaselineCentiMmHgCurve!.At(2_000_000_000) == 1100,
            "the narrow-pressure SVT segment excludes past and future sinus targets while retaining a crossing scalar ramp");
        for (int step = 0; step < 10; step++) { session.Advance(200_000_000); }
        Check.That(!session.Configuration.Svt && session.PatientTimelineCompleted &&
            session.Configuration.CvpBaselineCentiMmHgCurve!.At(4_000_000_000) == 1600,
            "the next template accepts its own pressure target and the original crossing ramp endpoint");
    }

    private static void FutureHeartRateDoesNotRecalibrateCurrentPressure()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default with
        {
            AbpTarget = new(12000, 8000),
            PaTarget = new(2500, 1000),
            PressureVariation = new(300, 100, new string('0', 64))
        };
        var expected = PhysiologyIllustrationSource.Create(configuration);
        var scheduled = PhysiologyIllustrationSource.Create(configuration with
        { CardiacRateClock = new(800_000_000, Ramp(75, 100)) });
        for (long time = 200_000_000; time <= 2_000_000_000; time += 200_000_000)
        {
            var left = expected.AdvanceTo(time, 50, 1, 100);
            var right = scheduled.AdvanceTo(time, 50, 1, 100);
            Check.That(left.Count == right.Count && left.Zip(right).All(pair => pair.First.SequenceEqual(pair.Second)),
                "future rate changes cannot recalibrate the current fixed pressure targets or seeded drift");
        }
    }

    private static void SessionScalarTimelineChangesMeasuredWaveforms()
    {
        var configuration = PhysiologyIllustrationConfiguration.Default with
        { AbpTarget = new(12000, 8000), PaTarget = new(2500, 1000) };
        var reference = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), enableMeasurements: true);
        var changed = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), enableMeasurements: true);
        reference.DiscardStartup();
        changed.DiscardStartup();
        reference.ConfigurePatientTimeline([new("rate", 1_000_000_000, 4_000_000_000, new() { HeartRateBpm = 100 })]);
        changed.ConfigurePatientTimeline([new("combined", 1_000_000_000, 4_000_000_000, new()
        {
            HeartRateBpm = 100,
            Abp = new(9000, 5000),
            Pa = new(2000, 800),
            CvpCentiMmHg = 1200,
            EtCo2MmHg = 20
        })]);
        for (int step = 0; step < 120; step++)
        {
            reference.Advance(200_000_000);
            changed.Advance(200_000_000);
        }
        var before = reference.Measurements!;
        var after = changed.Measurements!;
        Check.That(after.AbpMean.Status == WaveformMeasurementStatus.Valid && before.AbpMean.Status == WaveformMeasurementStatus.Valid &&
            after.AbpMean.MeanCentiMmHg < before.AbpMean.MeanCentiMmHg - 1500,
            "a simultaneous HR and pressure ramp changes the pressure detector's actual input");
        Check.That(after.CvpMean.MeanCentiMmHg - before.CvpMean.MeanCentiMmHg == 600,
            "the session CVP measurement follows the acquired baseline shift");
        Check.That(changed.Samples(4, 19_000_000_000, 23_000_000_000).Max(sample => sample.Value) <= 20.01 &&
            reference.Samples(4, 19_000_000_000, 23_000_000_000).Max(sample => sample.Value) >= 39.99,
            "the session capnogram itself reaches the EtCO2 target");
    }

    private static void CvpAndCo2RampsChangeAcquiredSamples()
    {
        var basis = PhysiologyIllustrationConfiguration.Default;
        var reference = PhysiologyIllustrationSource.Create(basis);
        var ramp = PhysiologyIllustrationSource.Create(basis with
        {
            CvpBaselineCentiMmHgCurve = Ramp(600, 1600, 0, 10_000_000_000),
            Co2EndExpiratoryCentiMmHgCurve = Ramp(4000, 2000, 0, 10_000_000_000)
        });
        bool gasObserved = false;
        for (long time = 50_000_000; time <= 5_050_000_000; time += 50_000_000)
        {
            _ = reference.AdvanceTo(time, 20, 1, 100, out var expected);
            _ = ramp.AdvanceTo(time, 20, 1, 100, out var actual);
            foreach (var pair in actual.Zip(expected))
            {
                long sampleTime = pair.First.SourceSimTimeNs;
                if (pair.First.ChannelId == PhysiologyIllustrationSource.ChannelId(6))
                {
                    int delta = (int)Math.Round(1000d * sampleTime / 10_000_000_000, MidpointRounding.ToEven);
                    Check.That(Math.Abs(pair.First.NormalizedValue - pair.Second.NormalizedValue - delta) <= 1,
                        "CVP baseline changes the acquired pressure sample");
                }
                if (pair.First.ChannelId == PhysiologyIllustrationSource.ChannelId(4) && pair.Second.NormalizedValue > 1000)
                {
                    gasObserved = true;
                    double fraction = 1 - 0.5 * sampleTime / 10_000_000_000;
                    Check.That(Math.Abs(pair.First.NormalizedValue - pair.Second.NormalizedValue * fraction) <= 2,
                        "EtCO2 target scales the acquired gas excursion continuously");
                }
                Check.That(pair.First.QualityFlags == pair.Second.QualityFlags, "scalar curves preserve quality flags");
            }
        }
        Check.That(gasObserved, "the gas scaling assertion includes an actual expiration");
    }
}
