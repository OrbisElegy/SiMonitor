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

public readonly record struct OxygenReservoirState(decimal AlveolarOxygenMl, decimal AlveolarCarbonDioxideMl,
    decimal AlveolarInertGasMl, decimal ArterialOxygenMl, decimal VenousOxygenMl,
    decimal InspiredOxygenMl = 0, decimal ExpiredOxygenMl = 0, decimal ConsumedOxygenMl = 0,
    decimal UnmetOxygenDemandMl = 0)
{
    public decimal GasMlStpd => AlveolarOxygenMl + AlveolarCarbonDioxideMl + AlveolarInertGasMl;
    public decimal ConservedOxygenMl => AlveolarOxygenMl + ArterialOxygenMl + VenousOxygenMl -
        InspiredOxygenMl + ExpiredOxygenMl + ConsumedOxygenMl;
    internal OxygenReservoirState AddScaled(OxygenReservoirState slope, decimal scale) => new(
        AlveolarOxygenMl + slope.AlveolarOxygenMl * scale,
        AlveolarCarbonDioxideMl + slope.AlveolarCarbonDioxideMl * scale,
        AlveolarInertGasMl + slope.AlveolarInertGasMl * scale,
        ArterialOxygenMl + slope.ArterialOxygenMl * scale,
        VenousOxygenMl + slope.VenousOxygenMl * scale,
        InspiredOxygenMl + slope.InspiredOxygenMl * scale,
        ExpiredOxygenMl + slope.ExpiredOxygenMl * scale,
        ConsumedOxygenMl + slope.ConsumedOxygenMl * scale,
        UnmetOxygenDemandMl + slope.UnmetOxygenDemandMl * scale);
}

// Decimal port of tools/oxygenation_model.py. Fixed 8ms RK4 steps and bounded
// inversion; no floating point, wall clock, UI, IO or implicit random state.
public static class OxygenReservoirModel
{
    public const string ModelId = "OxygenTransportDecimal@2";
    public const long StepNs = 8_000_000;
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

    public static int ArterialSaturationMilliPercent(OxygenReservoirState state, OxygenReservoirParameters parameters)
    {
        ValidateState(state, parameters);
        decimal content = state.ArterialOxygenMl / parameters.ArterialVolumeMl;
        decimal low = 0, high = 1000;
        for (int iteration = 0; iteration < 48; iteration++)
        {
            decimal middle = (low + high) / 2;
            if (ContentMlPerMl(middle, parameters) < content) { low = middle; } else { high = middle; }
        }
        return decimal.ToInt32(decimal.Round(SaturationFraction((low + high) / 2) * 100000, 0, MidpointRounding.ToEven));
    }

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
        PhysiologyTransportInterval input, decimal oxygenDemandMultiplier = 1)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        ValidateOxygenDemandMultiplier(oxygenDemandMultiplier);
        if (input.FromSimTimeNs < 0 || input.ToExclusiveSimTimeNs < input.FromSimTimeNs ||
            input.ToExclusiveSimTimeNs - input.FromSimTimeNs != StepNs ||
            input.DeliveredVolumeNanolitersBtps is < 0 or > 100_000_000 ||
            input.AlveolarVentilationNanolitersBtps < 0 || input.AlveolarVentilationNanolitersBtps > input.DeliveredVolumeNanolitersBtps ||
            input.EffectiveBloodVolumeNanoliters is < 0 or > 16_000_000 ||
            input.InspiredOxygenMillionths is < 100_000 or > 1_000_000 || !input.AirwayOpen && input.DeliveredVolumeNanolitersBtps != 0)
        { throw new ArgumentException("OxygenReservoir.InvalidTransport", nameof(input)); }
        const decimal seconds = .008m;
        decimal ventilation = input.AlveolarVentilationNanolitersBtps / 1_000_000m * BtpsToStpd / seconds;
        decimal flow = input.EffectiveBloodVolumeNanoliters / 1_000_000m / seconds;
        decimal inspiredFraction = input.InspiredOxygenMillionths / 1_000_000m;
        var first = Derivative(state);
        var second = Derivative(state.AddScaled(first, seconds / 2));
        var third = Derivative(state.AddScaled(second, seconds / 2));
        var fourth = Derivative(state.AddScaled(third, seconds));
        var result = state.AddScaled(first.AddScaled(second, 2).AddScaled(third, 2).AddScaled(fourth, 1), seconds / 6);
        ValidateState(result, parameters);
        return result;

        OxygenReservoirState Derivative(OxygenReservoirState value)
        {
            ValidateState(value, parameters);
            decimal oxygenFraction = value.AlveolarOxygenMl / value.GasMlStpd;
            decimal carbonFraction = value.AlveolarCarbonDioxideMl / value.GasMlStpd;
            decimal inertFraction = value.AlveolarInertGasMl / value.GasMlStpd;
            decimal arterial = value.ArterialOxygenMl / parameters.ArterialVolumeMl;
            decimal venous = value.VenousOxygenMl / parameters.VenousVolumeMl;
            decimal capillary = ContentMlPerMl(oxygenFraction * DryPressureMmHg, parameters);
            decimal lungTransfer = flow * (1 - parameters.ShuntFraction) * (capillary - venous);
            decimal systemicTransfer = flow * (arterial - venous);
            decimal demand = parameters.OxygenDemandMlStpdPerMinute * oxygenDemandMultiplier / 60m;
            decimal consumed = demand * Math.Min(1, venous / (parameters.ConsumptionFloorMlPerDl / 100m));
            decimal carbon = parameters.RespiratoryQuotient * consumed;
            decimal inspiredFlow = 0, expiredFlow = 0;
            if (input.AirwayOpen)
            {
                decimal netUptake = lungTransfer - carbon;
                decimal volumeError = (parameters.FrcMlBtps * BtpsToStpd - value.GasMlStpd) / parameters.VolumeRecoverySeconds;
                inspiredFlow = ventilation + Math.Max(0, netUptake + volumeError);
                expiredFlow = ventilation + Math.Max(0, -netUptake - volumeError);
            }
            decimal inspiredOxygen = inspiredFlow * inspiredFraction;
            decimal expiredOxygen = expiredFlow * oxygenFraction;
            return new(inspiredOxygen - expiredOxygen - lungTransfer, carbon - expiredFlow * carbonFraction,
                inspiredFlow * (1 - inspiredFraction) - expiredFlow * inertFraction,
                lungTransfer - systemicTransfer, systemicTransfer - consumed,
                inspiredOxygen, expiredOxygen, consumed, demand - consumed);
        }
    }

    public static void ValidateOxygenDemandMultiplier(decimal oxygenDemandMultiplier)
    {
        if (oxygenDemandMultiplier is < 1 or > 4)
        { throw new ArgumentOutOfRangeException(nameof(oxygenDemandMultiplier), "Oxygenation.InvalidDemandMultiplier"); }
    }

    public static void ValidateState(OxygenReservoirState state, OxygenReservoirParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        if (state.AlveolarOxygenMl < 0 || state.AlveolarCarbonDioxideMl < 0 || state.AlveolarInertGasMl < 0 ||
            state.ArterialOxygenMl < 0 || state.VenousOxygenMl < 0 || state.InspiredOxygenMl < 0 ||
            state.ExpiredOxygenMl < 0 || state.ConsumedOxygenMl < 0 || state.UnmetOxygenDemandMl < 0 ||
            state.GasMlStpd <= 1 || state.GasMlStpd >= 2 * parameters.FrcMlBtps * BtpsToStpd ||
            state.ArterialOxygenMl > parameters.ArterialVolumeMl * ContentMlPerMl(1000, parameters) ||
            state.VenousOxygenMl > parameters.VenousVolumeMl * ContentMlPerMl(1000, parameters))
        { throw new ArgumentException("OxygenReservoir.InvalidState"); }
    }
}
