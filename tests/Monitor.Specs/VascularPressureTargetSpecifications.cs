// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VascularPressureTargetSpecifications
{
    private static readonly string Seed = new('0', 64);
    private static readonly VascularPressureTarget Arterial = new(12000, 8000);
    private static readonly VascularPressureTarget Pulmonary = new(2500, 1000);

    public static Specification[] All =>
    [
        new(nameof(PressureTargetsAreMeasuredAtRegularRates), PressureTargetsAreMeasuredAtRegularRates),
        new(nameof(PressureTargetsCalibrateIrregularRhythmsOnCompleteBeats), PressureTargetsCalibrateIrregularRhythmsOnCompleteBeats),
        new(nameof(PressureTargetsRejectInvalidOrUnreachableInputs), PressureTargetsRejectInvalidOrUnreachableInputs),
        new(nameof(MorphologyReservoirsAcceptEjectionStrengthAboveTheSampleRange), MorphologyReservoirsAcceptEjectionStrengthAboveTheSampleRange),
    ];

    private static (PulsePressureReading Abp, PulsePressureReading Pa) Measure(PhysiologyIllustrationConfiguration configuration)
    {
        var session = new LocalMonitorPreviewSession(configuration, MonitorDisplayConfiguration.Default(), enableMeasurements: true);
        session.DiscardStartup();
        for (int step = 0; step < 600; step++) { session.Advance(50_000_000); }
        var measurements = session.Measurements!;
        return (measurements.AbpMean.Pulse!, measurements.PaMean.Pulse!);
    }

    private static bool Near(PulsePressureReading reading, VascularPressureTarget target, int toleranceCentiMmHg) =>
        reading.Status == WaveformMeasurementStatus.Valid &&
        Math.Abs(reading.SystolicCentiMmHg!.Value - target.SystolicCentiMmHg) <= toleranceCentiMmHg &&
        Math.Abs(reading.DiastolicCentiMmHg!.Value - target.DiastolicCentiMmHg) <= toleranceCentiMmHg;

    private static void PressureTargetsAreMeasuredAtRegularRates()
    {
        var defaults = PhysiologyIllustrationConfiguration.Default;
        foreach (int? rate in new int?[] { null, 40, 150 })
        {
            var configuration = defaults with { AbpTarget = Arterial, PaTarget = Pulmonary };
            if (rate is { } bpm) { configuration = configuration with { SeededRate = new SeededCardiacRate(bpm, Seed, 0) }; }
            var (abp, pa) = Measure(configuration);
            Check.That(Near(abp, Arterial, 100) && Near(pa, Pulmonary, 100),
                $"regular rhythm at {rate?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "the default"} bpm measures the ABP and PA targets within 1 mmHg");
        }
        var high = new VascularPressureTarget(20000, 13000);
        Check.That(Near(Measure(defaults with { AbpTarget = high }).Abp, high, 100), "hypertensive targets are reachable at the default rate");
    }

    private static void PressureTargetsCalibrateIrregularRhythmsOnCompleteBeats()
    {
        foreach (var rhythm in new[] { PhysiologyIllustrationConfiguration.Fibrillation(), PhysiologyIllustrationConfiguration.SinusArrestPreset,
            PhysiologyIllustrationConfiguration.VentricularEscape })
        {
            var (abp, pa) = Measure(rhythm with { AbpTarget = Arterial, PaTarget = Pulmonary });
            Check.That(Near(abp, Arterial, 300) && Near(pa, Pulmonary, 200),
                $"irregular or slow rhythm {rhythm.ConductionPattern} stays within a few mmHg of the targets");
        }
    }

    private static void PressureTargetsRejectInvalidOrUnreachableInputs()
    {
        static string Reason(PhysiologyIllustrationConfiguration configuration)
        {
            try { _ = PhysiologyIllustrationSource.Create(configuration); }
            catch (ArgumentException exception) { return exception.Message; }
            return "accepted";
        }
        var defaults = PhysiologyIllustrationConfiguration.Default;
        Check.That(Reason(defaults with { AbpTarget = new(26000, 8000) }) == "Physiology.PressureTargetOutOfRange" &&
            Reason(defaults with { PaTarget = new(2500, 500) }) == "Physiology.PressureTargetOutOfRange",
            "targets outside the channel ranges are rejected");
        Check.That(Reason(defaults with { AbpTarget = new(8400, 8000) }) == "Physiology.PressureTargetPulseTooSmall",
            "a pulse pressure below 5 mmHg is rejected");
        Check.That(Reason(defaults with { AbpTarget = Arterial, AbpPulsePermille = 1200 }) == "Physiology.PressureTargetConflictsWithPulse",
            "a target and a pulse factor on the same channel are exclusive");
        Check.That(Reason(defaults with { UseVascularReservoir = false, AbpTarget = Arterial }) == "Physiology.PressureTargetRequiresReservoirMorphology",
            "targets require the reservoir pressure contour");
        Check.That(Reason(PhysiologyIllustrationConfiguration.SvtPreset with { AbpTarget = Arterial }) == "Physiology.PressureTargetUnreachable",
            "filling-limited 200 bpm SVT cannot reach normal pressure and is rejected instead of clipped");
        var first = PhysiologyIllustrationSource.Create(defaults with { AbpTarget = Arterial }).CaptureState();
        var second = PhysiologyIllustrationSource.Create(defaults with { AbpTarget = Arterial }).CaptureState();
        Check.That(System.Text.Json.JsonSerializer.Serialize(first) == System.Text.Json.JsonSerializer.Serialize(second),
            "solving the same targets twice yields identical source state");
    }

    private static void MorphologyReservoirsAcceptEjectionStrengthAboveTheSampleRange()
    {
        var physiology = new RegularPhysiologyPlan(0, 1_500_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
        var plan = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 50000,
            Morphology: new(VascularPressureMorphologyKind.Arterial, 600_000_000, 4000));
        long pressure = VascularPressureSource.Create(physiology, plan).EvaluateAt(60_000_000_000);
        Check.That(pressure > 0 && pressure < short.MaxValue * Monitor.Simulation.Determinism.FixedPointMath.Q32One,
            "R*Q above the sample range is valid when the contour bounds the output");
        bool rejected = false;
        try { _ = VascularPressureSource.Create(physiology, plan with { EjectionEquilibriumCentiMmHg = 1_000_001 }); }
        catch (EventWaveformException exception) { rejected = exception.ReasonCode == "VascularPressure.InvalidPlan"; }
        Check.That(rejected, "R*Q above the arithmetic cap is rejected");
        rejected = false;
        try { _ = VascularPressureSource.Create(physiology, plan with { Morphology = null }); }
        catch (EventWaveformException exception) { rejected = exception.ReasonCode == "VascularPressure.InvalidPlan"; }
        Check.That(rejected, "without a contour R*Q stays within the sample range");
    }
}
