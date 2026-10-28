// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json.Serialization;

namespace Monitor.Simulation.Physiology;

// Versioned checkpoints carry both binary64 components of every inventory.
public readonly record struct OxygenReservoirCheckpoint(string ModelId,
    OxygenReservoirAmount AlveolarOxygen,
    OxygenReservoirAmount AlveolarCarbonDioxide,
    OxygenReservoirAmount AlveolarInertGas,
    OxygenReservoirAmount ArterialOxygen,
    OxygenReservoirAmount VenousOxygen,
    OxygenReservoirAmount InspiredOxygen,
    OxygenReservoirAmount ExpiredOxygen,
    OxygenReservoirAmount ConsumedOxygen,
    OxygenReservoirAmount UnmetOxygenDemand);

// Decimal properties preserve the reporting API. JSON uses the versioned
// checkpoint instead, because decimal-only reconstruction loses low bits.
public readonly record struct OxygenReservoirState(decimal AlveolarOxygenMl, decimal AlveolarCarbonDioxideMl,
    decimal AlveolarInertGasMl, decimal ArterialOxygenMl, decimal VenousOxygenMl,
    decimal InspiredOxygenMl = 0, decimal ExpiredOxygenMl = 0, decimal ConsumedOxygenMl = 0,
    decimal UnmetOxygenDemandMl = 0)
{
    internal OxygenReservoirAmount AlveolarOxygen { get; init; } = OxygenReservoirAmount.FromDecimal(AlveolarOxygenMl);
    [JsonIgnore]
    public decimal AlveolarOxygenMl { get => AlveolarOxygen.ToDecimal(); init => AlveolarOxygen = OxygenReservoirAmount.FromDecimal(value); }
    internal OxygenReservoirAmount AlveolarCarbonDioxide { get; init; } = OxygenReservoirAmount.FromDecimal(AlveolarCarbonDioxideMl);
    [JsonIgnore]
    public decimal AlveolarCarbonDioxideMl { get => AlveolarCarbonDioxide.ToDecimal(); init => AlveolarCarbonDioxide = OxygenReservoirAmount.FromDecimal(value); }
    internal OxygenReservoirAmount AlveolarInertGas { get; init; } = OxygenReservoirAmount.FromDecimal(AlveolarInertGasMl);
    [JsonIgnore]
    public decimal AlveolarInertGasMl { get => AlveolarInertGas.ToDecimal(); init => AlveolarInertGas = OxygenReservoirAmount.FromDecimal(value); }
    internal OxygenReservoirAmount ArterialOxygen { get; init; } = OxygenReservoirAmount.FromDecimal(ArterialOxygenMl);
    [JsonIgnore]
    public decimal ArterialOxygenMl { get => ArterialOxygen.ToDecimal(); init => ArterialOxygen = OxygenReservoirAmount.FromDecimal(value); }
    internal OxygenReservoirAmount VenousOxygen { get; init; } = OxygenReservoirAmount.FromDecimal(VenousOxygenMl);
    [JsonIgnore]
    public decimal VenousOxygenMl { get => VenousOxygen.ToDecimal(); init => VenousOxygen = OxygenReservoirAmount.FromDecimal(value); }
    internal OxygenReservoirAmount InspiredOxygen { get; init; } = OxygenReservoirAmount.FromDecimal(InspiredOxygenMl);
    [JsonIgnore]
    public decimal InspiredOxygenMl { get => InspiredOxygen.ToDecimal(); init => InspiredOxygen = OxygenReservoirAmount.FromDecimal(value); }
    internal OxygenReservoirAmount ExpiredOxygen { get; init; } = OxygenReservoirAmount.FromDecimal(ExpiredOxygenMl);
    [JsonIgnore]
    public decimal ExpiredOxygenMl { get => ExpiredOxygen.ToDecimal(); init => ExpiredOxygen = OxygenReservoirAmount.FromDecimal(value); }
    internal OxygenReservoirAmount ConsumedOxygen { get; init; } = OxygenReservoirAmount.FromDecimal(ConsumedOxygenMl);
    [JsonIgnore]
    public decimal ConsumedOxygenMl { get => ConsumedOxygen.ToDecimal(); init => ConsumedOxygen = OxygenReservoirAmount.FromDecimal(value); }
    internal OxygenReservoirAmount UnmetOxygenDemand { get; init; } = OxygenReservoirAmount.FromDecimal(UnmetOxygenDemandMl);
    [JsonIgnore]
    public decimal UnmetOxygenDemandMl { get => UnmetOxygenDemand.ToDecimal(); init => UnmetOxygenDemand = OxygenReservoirAmount.FromDecimal(value); }

    public OxygenReservoirCheckpoint Checkpoint => new(OxygenReservoirModel.ModelId,
        AlveolarOxygen, AlveolarCarbonDioxide, AlveolarInertGas, ArterialOxygen, VenousOxygen,
        InspiredOxygen, ExpiredOxygen, ConsumedOxygen, UnmetOxygenDemand);

    [JsonConstructor]
    public OxygenReservoirState(OxygenReservoirCheckpoint checkpoint) : this(0, 0, 0, 0, 0)
    {
        if (checkpoint.ModelId != OxygenReservoirModel.ModelId)
        { throw new ArgumentException("OxygenReservoir.UnsupportedCheckpoint", nameof(checkpoint)); }
        AlveolarOxygen = checkpoint.AlveolarOxygen;
        AlveolarCarbonDioxide = checkpoint.AlveolarCarbonDioxide;
        AlveolarInertGas = checkpoint.AlveolarInertGas;
        ArterialOxygen = checkpoint.ArterialOxygen;
        VenousOxygen = checkpoint.VenousOxygen;
        InspiredOxygen = checkpoint.InspiredOxygen;
        ExpiredOxygen = checkpoint.ExpiredOxygen;
        ConsumedOxygen = checkpoint.ConsumedOxygen;
        UnmetOxygenDemand = checkpoint.UnmetOxygenDemand;
        if (!AmountsAreValid)
        { throw new ArgumentException("OxygenReservoir.InvalidCheckpoint", nameof(checkpoint)); }
    }

    [JsonIgnore]
    public decimal GasMlStpd => AlveolarOxygen.Add(AlveolarCarbonDioxide).Add(AlveolarInertGas).ToDecimal();
    [JsonIgnore]
    public decimal ConservedOxygenMl => AlveolarOxygen.Add(ArterialOxygen).Add(VenousOxygen)
        .Add(InspiredOxygen.Negate()).Add(ExpiredOxygen).Add(ConsumedOxygen).ToDecimal();

    internal double GasValue => AlveolarOxygen.Value + AlveolarCarbonDioxide.Value + AlveolarInertGas.Value;
    internal bool AmountsAreValid =>
        AlveolarOxygen.IsValid && !AlveolarOxygen.IsNegative &&
        AlveolarCarbonDioxide.IsValid && !AlveolarCarbonDioxide.IsNegative &&
        AlveolarInertGas.IsValid && !AlveolarInertGas.IsNegative &&
        ArterialOxygen.IsValid && !ArterialOxygen.IsNegative &&
        VenousOxygen.IsValid && !VenousOxygen.IsNegative &&
        InspiredOxygen.IsValid && !InspiredOxygen.IsNegative &&
        ExpiredOxygen.IsValid && !ExpiredOxygen.IsNegative &&
        ConsumedOxygen.IsValid && !ConsumedOxygen.IsNegative &&
        UnmetOxygenDemand.IsValid && !UnmetOxygenDemand.IsNegative;
}
