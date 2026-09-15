// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal sealed record PhysiologyDemoConfiguration(int BreathPeriodMilliseconds,
    int InspirationMilliseconds, int RespAmplitudeCounts, int? Co2PlateauStartCentiMmHg = null,
    int Co2BaselineMmHg = 0, int Co2EndExpiratoryMmHg = 40, int Co2DeadSpaceMilliseconds = 125,
    int Co2RiseMilliseconds = 250, int Co2FallMilliseconds = 200, int Co2TransportDelayMilliseconds = 0, int Co2DispersionStepMilliseconds = 0,
    int InspiratoryPauseMilliseconds = 0, int ExpiratoryPauseMilliseconds = 0, int RespCardiacArtifactCounts = 0,
    RespiratoryActivity RespiratoryActivity = RespiratoryActivity.Breathing, int? ActivityAfterBreaths = null, int? ActivityDurationBreaths = null, int VentricularConductionRatio = 1, CardiacActivity CardiacActivity = CardiacActivity.AtrialAndVentricular, bool VentricularMechanicalEnabled = true, int? MechanicalAfterCycles = null, int? MechanicalDurationCycles = null, int MechanicalEveryCycles = 1, bool UseVascularReservoir = false, int? IndependentVentricularPeriodMilliseconds = null, int? IndependentVentricularOffsetMilliseconds = null)
{
    internal static PhysiologyDemoConfiguration Default { get; } = new(3750, 1875, 1000, UseVascularReservoir: true);

    internal CapnogramPlan ResolveCapnogram()
    {
        // Match the demo's fixed0..80mmHg display range; source limits are separate.
        if (Co2BaselineMmHg is < 0 or > 80 || Co2EndExpiratoryMmHg is < 0 or > 80)
        { throw new ArgumentException("PhysiologyDemo.InvalidCo2Pressure"); }
        if (Co2TransportDelayMilliseconds is < 0 or > 5000)
        { throw new ArgumentException("PhysiologyDemo.InvalidCo2TransportDelay"); }
        if (Co2DispersionStepMilliseconds is < 0 or > 500)
        { throw new ArgumentException("PhysiologyDemo.InvalidCo2Dispersion"); }
        return new(Co2DeadSpaceMilliseconds * 1_000_000L, Co2RiseMilliseconds * 1_000_000L,
            Co2FallMilliseconds * 1_000_000L, Co2BaselineMmHg, Co2EndExpiratoryMmHg, Co2PlateauStartCentiMmHg, Co2TransportDelayMilliseconds * 1_000_000L, Co2DispersionStepMilliseconds * 1_000_000L);
    }

    internal RegularPhysiologyPlan ResolvePlan()
    {
        // Demo input/display bounds, not physiological normal ranges.
        if ((IndependentVentricularPeriodMilliseconds is { } independent && (independent is < 800 or > 3200 || VentricularConductionRatio != 1)) ||
            MechanicalEveryCycles is < 1 or > 4 || (MechanicalAfterCycles is { } cycles && (cycles is < 1 or > 100 || VentricularMechanicalEnabled || CardiacActivity is not (CardiacActivity.AtrialAndVentricular or CardiacActivity.VentricularOnly))) ||
            (MechanicalDurationCycles is { } mechanicalDuration && (mechanicalDuration is < 1 or > 100 || MechanicalAfterCycles is null)) ||
            !Enum.IsDefined(CardiacActivity) || VentricularConductionRatio is < 1 or > 4 || BreathPeriodMilliseconds is < 1000 or > 10000 || InspirationMilliseconds <= 0 ||
            InspirationMilliseconds >= BreathPeriodMilliseconds || RespAmplitudeCounts is < -1000 or > 1000 ||
            InspiratoryPauseMilliseconds < 0 || InspiratoryPauseMilliseconds >= InspirationMilliseconds ||
            ExpiratoryPauseMilliseconds < 0 || ExpiratoryPauseMilliseconds >= BreathPeriodMilliseconds - InspirationMilliseconds ||
            RespCardiacArtifactCounts is < -200 or > 200 || !Enum.IsDefined(RespiratoryActivity) ||
            (ActivityAfterBreaths is { } breaths && (breaths is < 1 or > 100 || RespiratoryActivity == RespiratoryActivity.Breathing)) ||
            (ActivityDurationBreaths is { } duration && (duration is < 1 or > 100 || ActivityAfterBreaths is null)))
        { throw new ArgumentException("PhysiologyDemo.InvalidConfiguration"); }
        var timing = TextbookEcgReference.Timing;
        long offset = DemoVentricularTiming.ResolveOffset(IndependentVentricularPeriodMilliseconds, IndependentVentricularOffsetMilliseconds, timing.PrIntervalNs);
        return new(0, timing.RrIntervalNs, offset, 80_000_000,
            offset + 80_000_000, BreathPeriodMilliseconds * 1_000_000L, InspirationMilliseconds * 1_000_000L,
            InspiratoryPauseMilliseconds * 1_000_000L, ExpiratoryPauseMilliseconds * 1_000_000L, RespiratoryActivity, ActivityAfterBreaths is { } count ? (ulong)count : null,
            ActivityDurationBreaths is { } durationCount ? (ulong)durationCount : null, VentricularConductionRatio, CardiacActivity, VentricularMechanicalEnabled, MechanicalAfterCycles is { } mechanicalCycles ? (ulong)mechanicalCycles : null, MechanicalDurationCycles is { } durationCycles ? (ulong)durationCycles : null, MechanicalEveryCycles, IndependentVentricularPeriodMilliseconds is { } period ? period * 1_000_000L : null);
    }
}
