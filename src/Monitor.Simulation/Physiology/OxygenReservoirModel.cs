// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Reference adult parameters, not population 0-SD defaults. All oxygen/gas
// inventories use mL(STPD); blood volumes are liquid mL and FRC is mL(BTPS).
public sealed record OxygenReservoirParameters(
    decimal FrcMlBtps = 2200m,
    decimal BloodVolumeMl = 4823.7546875m,
    decimal ArterialVolumeFraction = .2m,
    decimal HemoglobinGramsPerDl = 15m,
    decimal ShuntFraction = .02m,
    decimal OxygenDemandMlStpdPerMinute = 250m,
    decimal RespiratoryQuotient = .8m,
    decimal ConsumptionFloorMlPerDl = 2m,
    decimal VolumeRecoverySeconds = 2m)
{
    public decimal ArterialVolumeMl => BloodVolumeMl * ArterialVolumeFraction;
    public decimal VenousVolumeMl => BloodVolumeMl - ArterialVolumeMl;
    public void Validate()
    {
        if (FrcMlBtps is < 500 or > 5000 || BloodVolumeMl is < 2500 or > 7500 ||
            ArterialVolumeFraction is < .1m or > .3m || ArterialVolumeMl is < 250 or > 2250 ||
            VenousVolumeMl is < 1750 or > 6750 || HemoglobinGramsPerDl is < 6 or > 20 ||
            ShuntFraction is < 0 or > .3m || OxygenDemandMlStpdPerMinute is < 0 or > 500 ||
            RespiratoryQuotient is < .7m or > 1 || ConsumptionFloorMlPerDl is < 1 or > 4 ||
            VolumeRecoverySeconds is < .5m or > 10)
        { throw new ArgumentException("OxygenReservoir.InvalidParameters"); }
    }
}

// Oxygen transport with binary64 fluxes and double-double inventories.
// Each 8ms transport interval has four deterministic 2ms RK4 substeps.
public static class OxygenReservoirModel
{
    public const string ModelId = "OxygenTransportDoubleDouble@3";
    public const long StepNs = 8_000_000;
    public const long IntegrationSubstepNs = 2_000_000;
    public const decimal DryPressureMmHg = 713m;
    public static decimal BtpsToStpd => (273.15m / 310.15m) * (713m / 760m);

    public static decimal SaturationFraction(decimal pressureMmHg)
    {
        if (pressureMmHg is < 0 or > 1000) { throw new ArgumentOutOfRangeException(nameof(pressureMmHg)); }
        decimal numerator = pressureMmHg * pressureMmHg * pressureMmHg + 150m * pressureMmHg;
        return numerator / (numerator + 23400m);
    }

    private static decimal ContentMlPerMl(decimal pressureMmHg, OxygenReservoirParameters parameters) =>
        (1.34m * parameters.HemoglobinGramsPerDl * SaturationFraction(pressureMmHg) + .0031m * pressureMmHg) / 100m;

    public static int ArterialSaturationMilliPercent(OxygenReservoirState state, OxygenReservoirParameters parameters) =>
        new OxygenReservoirSolver(parameters).ArterialSaturationMilliPercent(state);

    // Start each new scenario at the same ventilated reference state. Current
    // controls then act on these stores; zero VT/closed airway can start a run.
    public static OxygenReservoirState ReferenceState(OxygenReservoirParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        decimal ventilation = 300m * 16m / 60m * BtpsToStpd;
        decimal demand = parameters.OxygenDemandMlStpdPerMinute / 60m;
        decimal carbon = parameters.RespiratoryQuotient * demand;
        decimal inspired = ventilation + demand - carbon;
        decimal oxygenFraction = (inspired * .21m - demand) / ventilation;
        decimal carbonFraction = carbon / ventilation;
        decimal inertFraction = inspired * .79m / ventilation;
        if (oxygenFraction < 0 || carbonFraction < 0 || inertFraction < 0)
        { throw new ArgumentException("OxygenReservoir.NoReferenceEquilibrium"); }
        decimal capillary = ContentMlPerMl(oxygenFraction * DryPressureMmHg, parameters);
        decimal pulmonaryFlow = 5000m / 60m * (1 - parameters.ShuntFraction);
        decimal venous = capillary - demand / pulmonaryFlow;
        decimal arterial = capillary - parameters.ShuntFraction * demand / pulmonaryFlow;
        if (venous < parameters.ConsumptionFloorMlPerDl / 100m)
        { throw new ArgumentException("OxygenReservoir.NoReferenceEquilibrium"); }
        decimal gas = parameters.FrcMlBtps * BtpsToStpd;
        var state = new OxygenReservoirState(gas * oxygenFraction, gas * carbonFraction, gas * inertFraction,
            arterial * parameters.ArterialVolumeMl, venous * parameters.VenousVolumeMl);
        ValidateState(state, parameters);
        return state;
    }

    public static OxygenReservoirState Step(OxygenReservoirState state, OxygenReservoirParameters parameters,
        PhysiologyTransportInterval input, decimal oxygenDemandMultiplier = 1) =>
        new OxygenReservoirSolver(parameters).Step(state, input, oxygenDemandMultiplier);

    public static void ValidateOxygenDemandMultiplier(decimal oxygenDemandMultiplier)
    {
        if (oxygenDemandMultiplier is < 1 or > 4)
        { throw new ArgumentOutOfRangeException(nameof(oxygenDemandMultiplier), "Oxygenation.InvalidDemandMultiplier"); }
    }

    public static void ValidateState(OxygenReservoirState state, OxygenReservoirParameters parameters) =>
        new OxygenReservoirSolver(parameters).ValidateState(state);
}
