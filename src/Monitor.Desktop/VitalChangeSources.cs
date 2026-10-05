// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;
using Monitor.Application.Scenarios;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// Everything an applied source is built from, so vital changes can rebuild it with
// other values without reading unapplied drafts from the settings page.
internal sealed record PreviewSourceInputs(PhysiologyDemoConfiguration Physiology, ProjectedEcgDemoConfiguration Ecg,
    MonitorDisplayConfiguration Display, int? OpticalTargetMilliPercent, int OpticalModulationPermille,
    int OpticalVariationMilliPercent, RealtimeOxygenationConfiguration? RealtimeOxygenation, string SeedHex,
    bool HeartRateAdjustable)
{
    internal LocalMonitorPreviewSession CreateSession() => new(Physiology, Display, enableMeasurements: true,
        opticalSaturationMilliPercent: OpticalTargetMilliPercent, opticalModulationPermille: OpticalModulationPermille,
        opticalVariation: OpticalTargetMilliPercent is { } target && OpticalVariationMilliPercent > 0
            ? new SeededOpticalSaturation(target, OpticalVariationMilliPercent, SeedHex) : null,
        realtimeOxygenation: RealtimeOxygenation);
}

internal static class VitalChangeSources
{
    private const int ReferenceSinusRateBpm = 75;

    // The signs the applied inputs can vary, at their applied values. Pressures without
    // targets start from the current reading, or from reference values before one exists.
    internal static Dictionary<VitalSign, int> Baseline(PreviewSourceInputs inputs, LiveMeasurementSnapshot? measured)
    {
        var physiology = inputs.Physiology;
        var values = new Dictionary<VitalSign, int>
        {
            [VitalSign.EtCo2MmHg] = Clamp(VitalSign.EtCo2MmHg, physiology.Co2EndExpiratoryMmHg),
            [VitalSign.CvpCentiMmHg] = Clamp(VitalSign.CvpCentiMmHg, physiology.CvpBaselineCentiMmHg),
        };
        if (inputs.HeartRateAdjustable)
        { values[VitalSign.HeartRateBpm] = physiology.SeededRate?.HeartRateBpm ?? ReferenceSinusRateBpm; }
        if (physiology.RespiratoryActivity != RespiratoryActivity.Absent)
        {
            int rate = (int)Math.Round(60_000m / physiology.BreathPeriodMilliseconds, MidpointRounding.ToEven);
            values[VitalSign.RespiratoryRatePerMinute] = Clamp(VitalSign.RespiratoryRatePerMinute, rate);
        }
        if (inputs.OpticalTargetMilliPercent is { } saturation) { values[VitalSign.SpO2MilliPercent] = saturation; }
        AddPressure(values, physiology.AbpTarget, measured?.AbpMean.Pulse, VitalSign.AbpSystolicCentiMmHg, VitalSign.AbpDiastolicCentiMmHg, 12000, 8000);
        AddPressure(values, physiology.PaTarget, measured?.PaMean.Pulse, VitalSign.PaSystolicCentiMmHg, VitalSign.PaDiastolicCentiMmHg, 2500, 1000);
        return values;
    }

    // Applies values to the inputs. Signs the inputs cannot vary are rejected; the
    // generator validates the combined result when the session is created.
    internal static PreviewSourceInputs Apply(PreviewSourceInputs inputs, IReadOnlyDictionary<VitalSign, int> values)
    {
        var physiology = inputs.Physiology;
        var ecg = inputs.Ecg;
        int? saturation = inputs.OpticalTargetMilliPercent;
        foreach (var (sign, value) in values)
        {
            switch (sign)
            {
                case VitalSign.HeartRateBpm:
                    if (!inputs.HeartRateAdjustable) { throw new ArgumentException("VitalChange.HeartRateRequiresSinus"); }
                    var rate = new SeededCardiacRate(value, inputs.SeedHex, physiology.SeededRate?.VariationPermille ?? 0);
                    physiology = physiology with { SeededRate = rate };
                    ecg = ecg with { SeededRate = rate };
                    break;
                case VitalSign.RespiratoryRatePerMinute:
                    physiology = WithBreathingRate(physiology, value);
                    break;
                case VitalSign.SpO2MilliPercent:
                    saturation = inputs.OpticalTargetMilliPercent is null
                        ? throw new ArgumentException("VitalChange.SpO2RequiresTeachingSource") : value;
                    break;
                case VitalSign.EtCo2MmHg:
                    physiology = physiology with
                    {
                        Co2EndExpiratoryMmHg = value,
                        SeededCo2 = physiology.SeededCo2 is { } co2 ? new(value, co2.AmplitudeCentiMmHg, inputs.SeedHex) : null
                    };
                    break;
                case VitalSign.CvpCentiMmHg:
                    physiology = physiology with { CvpBaselineCentiMmHg = value };
                    break;
                case VitalSign.AbpSystolicCentiMmHg or VitalSign.AbpDiastolicCentiMmHg or
                    VitalSign.PaSystolicCentiMmHg or VitalSign.PaDiastolicCentiMmHg:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(values));
            }
        }
        if (values.ContainsKey(VitalSign.AbpSystolicCentiMmHg) || values.ContainsKey(VitalSign.AbpDiastolicCentiMmHg))
        {
            physiology = physiology with
            {
                AbpPulsePermille = 1000,
                AbpTarget = new(values[VitalSign.AbpSystolicCentiMmHg], values[VitalSign.AbpDiastolicCentiMmHg])
            };
        }
        if (values.ContainsKey(VitalSign.PaSystolicCentiMmHg) || values.ContainsKey(VitalSign.PaDiastolicCentiMmHg))
        {
            physiology = physiology with
            {
                PaPulsePermille = 1000,
                PaTarget = new(values[VitalSign.PaSystolicCentiMmHg], values[VitalSign.PaDiastolicCentiMmHg])
            };
        }
        return inputs with { Physiology = physiology, Ecg = ecg, OpticalTargetMilliPercent = saturation };
    }

    // Keeps the applied inspiration share and the CO₂ timing limits of the vital signs page.
    private static PhysiologyDemoConfiguration WithBreathingRate(PhysiologyDemoConfiguration physiology, int ratePerMinute)
    {
        int period = (int)Math.Round(60_000m / ratePerMinute, MidpointRounding.ToEven);
        int inspiration = (int)Math.Round((decimal)period * physiology.InspirationMilliseconds / physiology.BreathPeriodMilliseconds,
            MidpointRounding.ToEven);
        if (inspiration < physiology.Co2FallMilliseconds ||
            period - inspiration <= physiology.Co2DeadSpaceMilliseconds + physiology.Co2RiseMilliseconds)
        { throw new ArgumentException("VitalChange.BreathingTimingUnsupported"); }
        return physiology with { BreathPeriodMilliseconds = period, InspirationMilliseconds = inspiration };
    }

    private static void AddPressure(Dictionary<VitalSign, int> values, VascularPressureTarget? target, PulsePressureReading? reading,
        VitalSign systolicSign, VitalSign diastolicSign, int referenceSystolicCentiMmHg, int referenceDiastolicCentiMmHg)
    {
        var (systolic, diastolic) = target is not null ? (target.SystolicCentiMmHg, target.DiastolicCentiMmHg) :
            reading is { Status: WaveformMeasurementStatus.Valid, SystolicCentiMmHg: { } high, DiastolicCentiMmHg: { } low }
                ? (Clamp(systolicSign, high), Clamp(diastolicSign, low)) : (referenceSystolicCentiMmHg, referenceDiastolicCentiMmHg);
        if (systolic - diastolic < VascularPressureTarget.MinimumPulseCentiMmHg)
        { (systolic, diastolic) = (referenceSystolicCentiMmHg, referenceDiastolicCentiMmHg); }
        values[systolicSign] = systolic;
        values[diastolicSign] = diastolic;
    }

    private static int Clamp(VitalSign sign, int value)
    {
        var (minimum, maximum) = VitalChangeScheduler.Range(sign);
        return Math.Clamp(value, minimum, maximum);
    }
}
