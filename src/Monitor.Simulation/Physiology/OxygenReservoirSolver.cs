// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Immutable parameter preparation is shared by every step of a realtime source.
internal readonly struct OxygenReservoirSolver
{
    private readonly double _arterialVolumeMl;
    private readonly double _venousVolumeMl;
    private readonly double _hemoglobinGramsPerDl;
    private readonly double _shuntFraction;
    private readonly double _demandMlPerSecond;
    private readonly double _respiratoryQuotient;
    private readonly double _consumptionFloorMlPerMl;
    private readonly double _recoverySeconds;
    private readonly double _targetGasMl;
    private readonly double _maximumArterialOxygenMl;
    private readonly double _maximumVenousOxygenMl;
    private static readonly double BtpsToStpd = (double)OxygenReservoirModel.BtpsToStpd;

    internal OxygenReservoirSolver(OxygenReservoirParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        _arterialVolumeMl = (double)parameters.ArterialVolumeMl;
        _venousVolumeMl = (double)parameters.VenousVolumeMl;
        _hemoglobinGramsPerDl = (double)parameters.HemoglobinGramsPerDl;
        _shuntFraction = (double)parameters.ShuntFraction;
        _demandMlPerSecond = (double)(parameters.OxygenDemandMlStpdPerMinute / 60m);
        _respiratoryQuotient = (double)parameters.RespiratoryQuotient;
        _consumptionFloorMlPerMl = (double)(parameters.ConsumptionFloorMlPerDl / 100m);
        _recoverySeconds = (double)parameters.VolumeRecoverySeconds;
        _targetGasMl = (double)(parameters.FrcMlBtps * OxygenReservoirModel.BtpsToStpd);
        _maximumArterialOxygenMl = _arterialVolumeMl * ContentMlPerMl(1000);
        _maximumVenousOxygenMl = _venousVolumeMl * ContentMlPerMl(1000);
    }

    private double ContentMlPerMl(double pressureMmHg)
    {
        double numerator = pressureMmHg * pressureMmHg * pressureMmHg + 150 * pressureMmHg;
        return (1.34 * _hemoglobinGramsPerDl * (numerator / (numerator + 23400)) + .0031 * pressureMmHg) / 100;
    }

    internal int ArterialSaturationMilliPercent(OxygenReservoirState state)
    {
        ValidateState(state);
        double content = state.ArterialOxygen.Value / _arterialVolumeMl;
        double low = 0, high = 1000;
        for (int iteration = 0; iteration < 48; iteration++)
        {
            double middle = (low + high) / 2;
            if (ContentMlPerMl(middle) < content) { low = middle; } else { high = middle; }
        }
        double pressure = (low + high) / 2;
        double numerator = pressure * pressure * pressure + 150 * pressure;
        return (int)Math.Round(numerator / (numerator + 23400) * 100000, MidpointRounding.ToEven);
    }

    internal OxygenReservoirState Step(OxygenReservoirState state, PhysiologyTransportInterval input,
        decimal oxygenDemandMultiplier)
    {
        OxygenReservoirModel.ValidateOxygenDemandMultiplier(oxygenDemandMultiplier);
        if (input.FromSimTimeNs < 0 || input.ToExclusiveSimTimeNs < input.FromSimTimeNs ||
            input.ToExclusiveSimTimeNs - input.FromSimTimeNs != OxygenReservoirModel.StepNs ||
            input.DeliveredVolumeNanolitersBtps is < 0 or > 100_000_000 ||
            input.AlveolarVentilationNanolitersBtps < 0 || input.AlveolarVentilationNanolitersBtps > input.DeliveredVolumeNanolitersBtps ||
            input.EffectiveBloodVolumeNanoliters is < 0 or > 16_000_000 ||
            input.InspiredOxygenMillionths is < 100_000 or > 1_000_000 || !input.AirwayOpen && input.DeliveredVolumeNanolitersBtps != 0)
        { throw new ArgumentException("OxygenReservoir.InvalidTransport", nameof(input)); }
        const double intervalSeconds = .008;
        const double substepSeconds = .002;
        double ventilationMlPerSecond = input.AlveolarVentilationNanolitersBtps / 1_000_000.0 * BtpsToStpd / intervalSeconds;
        double bloodFlowMlPerSecond = input.EffectiveBloodVolumeNanoliters / 1_000_000.0 / intervalSeconds;
        double inspiredFraction = input.InspiredOxygenMillionths / 1_000_000.0;
        double demandMlPerSecond = _demandMlPerSecond * (double)oxygenDemandMultiplier;
        // Each substep sees the same mean transport rate over this 8ms interval.
        // Subdivision refines integration without inventing new transport events.
        for (int substep = 0; substep < OxygenReservoirModel.StepNs / OxygenReservoirModel.IntegrationSubstepNs; substep++)
        {
            var first = Derivative(state, ventilationMlPerSecond, bloodFlowMlPerSecond, inspiredFraction, demandMlPerSecond, input.AirwayOpen);
            var second = Derivative(Apply(state, first.Scale(substepSeconds / 2)), ventilationMlPerSecond,
                bloodFlowMlPerSecond, inspiredFraction, demandMlPerSecond, input.AirwayOpen);
            var third = Derivative(Apply(state, second.Scale(substepSeconds / 2)), ventilationMlPerSecond,
                bloodFlowMlPerSecond, inspiredFraction, demandMlPerSecond, input.AirwayOpen);
            var fourth = Derivative(Apply(state, third.Scale(substepSeconds)), ventilationMlPerSecond,
                bloodFlowMlPerSecond, inspiredFraction, demandMlPerSecond, input.AirwayOpen);
            state = Apply(state, first.AddScaled(second, 2).AddScaled(third, 2).AddScaled(fourth, 1).Scale(substepSeconds / 6));
            ValidateState(state);
        }
        return state;
    }

    private Flux Derivative(OxygenReservoirState state, double ventilationMlPerSecond, double bloodFlowMlPerSecond,
        double inspiredFraction, double demandMlPerSecond, bool airwayOpen)
    {
        ValidateState(state);
        double gasMl = state.GasValue;
        double oxygenFraction = state.AlveolarOxygen.Value / gasMl;
        double carbonFraction = state.AlveolarCarbonDioxide.Value / gasMl;
        double inertFraction = state.AlveolarInertGas.Value / gasMl;
        double arterial = state.ArterialOxygen.Value / _arterialVolumeMl;
        double venous = state.VenousOxygen.Value / _venousVolumeMl;
        double capillary = ContentMlPerMl(oxygenFraction * (double)OxygenReservoirModel.DryPressureMmHg);
        double lungTransfer = bloodFlowMlPerSecond * (1 - _shuntFraction) * (capillary - venous);
        double systemicTransfer = bloodFlowMlPerSecond * (arterial - venous);
        double consumed = demandMlPerSecond * Math.Min(1, venous / _consumptionFloorMlPerMl);
        double carbon = _respiratoryQuotient * consumed;
        double inspiredFlow = 0, expiredFlow = 0;
        if (airwayOpen)
        {
            double netUptake = lungTransfer - carbon;
            double volumeError = (_targetGasMl - gasMl) / _recoverySeconds;
            inspiredFlow = ventilationMlPerSecond + Math.Max(0, netUptake + volumeError);
            expiredFlow = ventilationMlPerSecond + Math.Max(0, -netUptake - volumeError);
        }
        return new(inspiredFlow * inspiredFraction, expiredFlow * oxygenFraction, lungTransfer, systemicTransfer,
            consumed, carbon, expiredFlow * carbonFraction, inspiredFlow * (1 - inspiredFraction),
            expiredFlow * inertFraction, demandMlPerSecond - consumed);
    }

    // Use the identical rounded transfer for both sides of each exchange.
    // Summing net derivatives first would discard the low bits needed by the ledger.
    private static OxygenReservoirState Apply(OxygenReservoirState state, Flux flux) => state with
    {
        AlveolarOxygen = state.AlveolarOxygen.Add(flux.InspiredOxygen).Add(-flux.ExpiredOxygen).Add(-flux.Lung),
        AlveolarCarbonDioxide = state.AlveolarCarbonDioxide.Add(flux.Carbon).Add(-flux.ExpiredCarbonDioxide),
        AlveolarInertGas = state.AlveolarInertGas.Add(flux.InspiredInert).Add(-flux.ExpiredInert),
        ArterialOxygen = state.ArterialOxygen.Add(flux.Lung).Add(-flux.Systemic),
        VenousOxygen = state.VenousOxygen.Add(flux.Systemic).Add(-flux.Consumed),
        InspiredOxygen = state.InspiredOxygen.Add(flux.InspiredOxygen),
        ExpiredOxygen = state.ExpiredOxygen.Add(flux.ExpiredOxygen),
        ConsumedOxygen = state.ConsumedOxygen.Add(flux.Consumed),
        UnmetOxygenDemand = state.UnmetOxygenDemand.Add(flux.Unmet),
    };

    internal void ValidateState(OxygenReservoirState state)
    {
        if (!state.AmountsAreValid || !double.IsFinite(state.GasValue) ||
            state.GasValue <= 1 || state.GasValue >= 2 * _targetGasMl ||
            state.ArterialOxygen.Value > _maximumArterialOxygenMl || state.VenousOxygen.Value > _maximumVenousOxygenMl)
        { throw new ArgumentException("OxygenReservoir.InvalidState"); }
    }

    private readonly record struct Flux(double InspiredOxygen, double ExpiredOxygen, double Lung, double Systemic,
        double Consumed, double Carbon, double ExpiredCarbonDioxide, double InspiredInert, double ExpiredInert, double Unmet)
    {
        internal Flux AddScaled(Flux other, double scale) => new(
            InspiredOxygen + other.InspiredOxygen * scale, ExpiredOxygen + other.ExpiredOxygen * scale,
            Lung + other.Lung * scale, Systemic + other.Systemic * scale, Consumed + other.Consumed * scale,
            Carbon + other.Carbon * scale, ExpiredCarbonDioxide + other.ExpiredCarbonDioxide * scale,
            InspiredInert + other.InspiredInert * scale, ExpiredInert + other.ExpiredInert * scale, Unmet + other.Unmet * scale);
        internal Flux Scale(double scale) => new(InspiredOxygen * scale, ExpiredOxygen * scale, Lung * scale,
            Systemic * scale, Consumed * scale, Carbon * scale, ExpiredCarbonDioxide * scale,
            InspiredInert * scale, ExpiredInert * scale, Unmet * scale);
    }
}
