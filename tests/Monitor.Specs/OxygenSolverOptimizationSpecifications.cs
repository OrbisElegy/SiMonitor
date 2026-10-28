// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Numerics;
using System.Text.Json;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class OxygenSolverOptimizationSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(OxygenCheckpointRetainsLowPartsAndReplay), OxygenCheckpointRetainsLowPartsAndReplay),
        new(nameof(OxygenCheckpointAndStepRejectInvalidValues), OxygenCheckpointAndStepRejectInvalidValues),
        new(nameof(OxygenSharedTransfersConserveExactInventory), OxygenSharedTransfersConserveExactInventory),
        new(nameof(OxygenSubstepsMatchIndependentDecimalIntegration), OxygenSubstepsMatchIndependentDecimalIntegration),
        new(nameof(OxygenReportingPreservesDecimalPrecisionAndSaturation), OxygenReportingPreservesDecimalPrecisionAndSaturation),
    ];

    private static void OxygenCheckpointRetainsLowPartsAndReplay()
    {
        var parameters = new OxygenReservoirParameters();
        var solver = new OxygenReservoirSolver(parameters);
        var state = OxygenReservoirModel.ReferenceState(parameters);
        for (int step = 0; step < 500; step++) { state = solver.Step(state, Input(step), 1); }
        var checkpoint = state.Checkpoint;
        Check.That(Amounts(checkpoint).Count(amount => amount.LowMl != 0) >= 8,
            "all advancing inventory/ledger values retain their independent rounding corrections");
        var restored = JsonSerializer.Deserialize<OxygenReservoirState>(JsonSerializer.Serialize(state));
        Check.That(restored == state && restored.Checkpoint == checkpoint,
            "versioned JSON preserves both components of every inventory bit for bit");
        var edited = state with { ArterialOxygenMl = 150m };
        Check.That(edited.ArterialOxygenMl == 150m && edited.Checkpoint.VenousOxygen == checkpoint.VenousOxygen,
            "decimal API edits replace only the selected inventory");
        for (int step = 500; step < 1000; step++)
        {
            state = solver.Step(state, Input(step), 1);
            restored = solver.Step(restored, Input(step), 1);
        }
        Check.That(restored == state, "checkpoint replay retains hidden low bits through subsequent integration");
    }

    private static void OxygenCheckpointAndStepRejectInvalidValues()
    {
        var parameters = new OxygenReservoirParameters();
        var state = OxygenReservoirModel.ReferenceState(parameters);
        var checkpoint = state.Checkpoint;
        Reject(() => _ = new OxygenReservoirState(checkpoint with { ModelId = "OxygenTransportDecimal@2" }));
        foreach (var bad in new[] { new OxygenReservoirAmount(double.NaN), new(double.PositiveInfinity),
            new OxygenReservoirAmount(1, double.NegativeInfinity), new(double.MaxValue), new(1, .01), new(0, -double.Epsilon), new(-1) })
        {
            Reject(() => _ = new OxygenReservoirState(checkpoint with { AlveolarOxygen = bad }));
            Reject(() => _ = new OxygenReservoirState(checkpoint with { AlveolarCarbonDioxide = bad }));
            Reject(() => _ = new OxygenReservoirState(checkpoint with { AlveolarInertGas = bad }));
            Reject(() => _ = new OxygenReservoirState(checkpoint with { ArterialOxygen = bad }));
            Reject(() => _ = new OxygenReservoirState(checkpoint with { VenousOxygen = bad }));
            Reject(() => _ = new OxygenReservoirState(checkpoint with { InspiredOxygen = bad }));
            Reject(() => _ = new OxygenReservoirState(checkpoint with { ExpiredOxygen = bad }));
            Reject(() => _ = new OxygenReservoirState(checkpoint with { ConsumedOxygen = bad }));
            Reject(() => _ = new OxygenReservoirState(checkpoint with { UnmetOxygenDemand = bad }));
        }
        Reject(() => JsonSerializer.Deserialize<OxygenReservoirState>("{}"));
        Reject(() => OxygenReservoirModel.Step(default, parameters, Input(0)));
        Reject(() => OxygenReservoirModel.Step(state with { InspiredOxygenMl = decimal.MaxValue }, parameters, Input(0)));
        Reject(() => OxygenReservoirModel.Step(state, parameters, Input(0), 0));
        Reject(() => OxygenReservoirModel.Step(state, parameters, Input(0), 4.0001m));
        Reject(() => OxygenReservoirModel.Step(state, parameters, Input(0) with { FromSimTimeNs = -1 }));
        Reject(() => OxygenReservoirModel.Step(state, parameters, Input(0) with { ToExclusiveSimTimeNs = long.MaxValue }));
        Reject(() => OxygenReservoirModel.Step(state, parameters, Input(0) with { DeliveredVolumeNanolitersBtps = 100_000_001 }));
        Reject(() => OxygenReservoirModel.Step(state, parameters, Input(0) with { EffectiveBloodVolumeNanoliters = 16_000_001 }));
        Reject(() => OxygenReservoirModel.Step(state, parameters, Input(0) with { InspiredOxygenMillionths = 99_999 }));
        Reject(() => OxygenReservoirModel.Step(state, parameters, Input(0) with { AirwayOpen = false }));
        var malformed = state with { ArterialOxygen = new(double.NaN) };
        Reject(() => OxygenReservoirModel.Step(malformed, parameters, Input(0)));
        Reject(() => OxygenReservoirModel.ArterialSaturationMilliPercent(malformed, parameters));
        Check.That(state.Checkpoint == checkpoint, "validation failures cannot mutate the caller's complete inventory");
    }

    private static void OxygenSharedTransfersConserveExactInventory()
    {
        var parameters = new OxygenReservoirParameters();
        var solver = new OxygenReservoirSolver(parameters);
        var state = OxygenReservoirModel.ReferenceState(parameters);
        BigInteger initial = Ledger(state.Checkpoint);
        decimal reportedInitial = state.ConservedOxygenMl;
        for (int step = 0; step < 75_000; step++)
        {
            state = solver.Step(state, Input(step), step is >= 10_000 and < 30_000 ? 4 : 1);
            if (step % 125 != 0) { continue; }
            var difference = BigInteger.Abs(Ledger(state.Checkpoint) - initial);
            Check.That(difference * BigInteger.Pow(10, 18) < (BigInteger.One << 1074),
                "600s of ventilation, apnea, hyperoxia, low flow and high demand conserve the exact binary inventory below 1e-18 mL");
            Check.That(Math.Abs(state.ConservedOxygenMl - reportedInitial) < .000000000000000001m,
                "decimal balance diagnostics retain the precision established by the independent binary ledger");
        }
        Check.That(state.UnmetOxygenDemandMl > 0, "ledger coverage includes oxygen-limited consumption");
    }

    private static void OxygenSubstepsMatchIndependentDecimalIntegration()
    {
        var parameters = new OxygenReservoirParameters();
        var solver = new OxygenReservoirSolver(parameters);
        decimal maximumFineError = 0, maximumCoarseError = 0;
        for (int scenario = 0; scenario < 3; scenario++)
        {
            var state = OxygenReservoirModel.ReferenceState(parameters);
            decimal[] reference = Values(state), coarse = Values(state);
            for (int step = 0; step < 1500; step++)
            {
                var input = Input(step) with
                {
                    DeliveredVolumeNanolitersBtps = scenario == 0 ? 100_000_000 : scenario == 1 ? 2_000_000 : 0,
                    AlveolarVentilationNanolitersBtps = scenario == 0 ? 100_000_000 : scenario == 1 ? 1_200_000 : 0,
                    EffectiveBloodVolumeNanoliters = scenario == 0 ? 16_000_000 : scenario == 1 ? 2_000_000 : 200_000,
                    InspiredOxygenMillionths = scenario == 0 ? 1_000_000 : 100_000,
                    AirwayOpen = scenario != 2,
                };
                state = solver.Step(state, input, 4);
                reference = DecimalStep(reference, parameters, input, 4, 16);
                coarse = DecimalStep(coarse, parameters, input, 4, 1);
                if (step % 25 != 0) { continue; }
                decimal[] actual = Values(state);
                for (int index = 0; index < actual.Length; index++)
                {
                    maximumFineError = Math.Max(maximumFineError, Math.Abs(actual[index] - reference[index]));
                    maximumCoarseError = Math.Max(maximumCoarseError, Math.Abs(coarse[index] - reference[index]));
                }
                Check.That(Math.Abs(solver.ArterialSaturationMilliPercent(state) - DecimalSaturation(reference[3], parameters)) <= 1,
                    "reported milli-percent saturation agrees with the independent 0.5ms decimal oracle");
            }
        }
        Check.That(maximumFineError < .000001m && maximumFineError < maximumCoarseError / 10,
            $"2ms integration refines the old 8ms solver against a 0.5ms decimal oracle: fine={maximumFineError}, coarse={maximumCoarseError}");
    }

    private static void OxygenReportingPreservesDecimalPrecisionAndSaturation()
    {
        foreach (decimal value in new[] { 0m, .0000000000000000000000000001m, 1m, .1m,
            1234.5678901234567890123456789m, decimal.MaxValue, decimal.MinValue })
        {
            Check.That(OxygenReservoirAmount.FromDecimal(value).ToDecimal() == value,
                "decimal reporting rounds exact hi/lo values without the binary64 cast's 15-digit truncation");
        }
        var parameters = new OxygenReservoirParameters();
        var state = OxygenReservoirModel.ReferenceState(parameters);
        for (int pressure = 0; pressure <= 1000; pressure++)
        {
            decimal content = DecimalContent(pressure, parameters);
            var sample = state with { ArterialOxygenMl = content * parameters.ArterialVolumeMl };
            int expected = decimal.ToInt32(decimal.Round(OxygenReservoirModel.SaturationFraction(pressure) * 100000,
                0, MidpointRounding.ToEven));
            Check.That(OxygenReservoirModel.ArterialSaturationMilliPercent(sample, parameters) == expected,
                "bounded binary64 saturation inversion preserves milli-percent output over the full pressure range");
        }
    }

    private static PhysiologyTransportInterval Input(int step)
    {
        bool ventilated = step < 5000 || step >= 40_000;
        return new(step * OxygenReservoirModel.StepNs, (step + 1L) * OxygenReservoirModel.StepNs,
            ventilated ? 2_000_000 : 0, ventilated ? 1_200_000 : 0,
            step % 100 < 30 ? 2_000_000 : 0, step >= 40_000 ? 1_000_000 : 210_000, step < 10_000 || step >= 40_000);
    }

    private static OxygenReservoirAmount[] Amounts(OxygenReservoirCheckpoint state) =>
        [state.AlveolarOxygen, state.AlveolarCarbonDioxide, state.AlveolarInertGas, state.ArterialOxygen, state.VenousOxygen,
            state.InspiredOxygen, state.ExpiredOxygen, state.ConsumedOxygen, state.UnmetOxygenDemand];
    private static decimal[] Values(OxygenReservoirState state) =>
        [state.AlveolarOxygenMl, state.AlveolarCarbonDioxideMl, state.AlveolarInertGasMl, state.ArterialOxygenMl, state.VenousOxygenMl,
            state.InspiredOxygenMl, state.ExpiredOxygenMl, state.ConsumedOxygenMl, state.UnmetOxygenDemandMl];
    private static BigInteger Ledger(OxygenReservoirCheckpoint state) => Units(state.AlveolarOxygen) + Units(state.ArterialOxygen) +
        Units(state.VenousOxygen) - Units(state.InspiredOxygen) + Units(state.ExpiredOxygen) + Units(state.ConsumedOxygen);
    private static BigInteger Units(OxygenReservoirAmount amount) => Units(amount.HighMl) + Units(amount.LowMl);
    private static BigInteger Units(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        ulong magnitude = (ulong)bits & 0x7fff_ffff_ffff_ffffUL;
        int exponent = (int)(magnitude >> 52);
        BigInteger fraction = magnitude & 0x000f_ffff_ffff_ffffUL;
        BigInteger units = exponent == 0 ? fraction : (fraction + (BigInteger.One << 52)) << (exponent - 1);
        return bits < 0 ? -units : units;
    }

    // Retained independent decimal equations from the former production model.
    // All nine net derivatives are integrated without the new shared-flux path.
    private static decimal[] DecimalStep(decimal[] state, OxygenReservoirParameters parameters,
        PhysiologyTransportInterval input, decimal multiplier, int subdivisions)
    {
        decimal h = .008m / subdivisions;
        decimal ventilation = input.AlveolarVentilationNanolitersBtps / 1_000_000m * OxygenReservoirModel.BtpsToStpd / .008m;
        decimal flow = input.EffectiveBloodVolumeNanoliters / 1_000_000m / .008m;
        decimal inspiredFraction = input.InspiredOxygenMillionths / 1_000_000m;
        for (int substep = 0; substep < subdivisions; substep++)
        {
            decimal[] first = Derivative(state);
            decimal[] second = Derivative(Add(state, first, h / 2));
            decimal[] third = Derivative(Add(state, second, h / 2));
            decimal[] fourth = Derivative(Add(state, third, h));
            state = Add(state, Add(Add(Add(first, second, 2), third, 2), fourth, 1), h / 6);
        }
        return state;

        decimal[] Derivative(decimal[] value)
        {
            decimal gas = value[0] + value[1] + value[2];
            decimal oxygenFraction = value[0] / gas;
            decimal arterial = value[3] / parameters.ArterialVolumeMl;
            decimal venous = value[4] / parameters.VenousVolumeMl;
            decimal lung = flow * (1 - parameters.ShuntFraction) * (DecimalContent(oxygenFraction * 713, parameters) - venous);
            decimal systemic = flow * (arterial - venous);
            decimal demand = parameters.OxygenDemandMlStpdPerMinute * multiplier / 60;
            decimal consumed = demand * Math.Min(1, venous / (parameters.ConsumptionFloorMlPerDl / 100));
            decimal carbon = parameters.RespiratoryQuotient * consumed;
            decimal inspired = 0, expired = 0;
            if (input.AirwayOpen)
            {
                decimal net = lung - carbon;
                decimal volume = (parameters.FrcMlBtps * OxygenReservoirModel.BtpsToStpd - gas) / parameters.VolumeRecoverySeconds;
                inspired = ventilation + Math.Max(0, net + volume);
                expired = ventilation + Math.Max(0, -net - volume);
            }
            return [inspired * inspiredFraction - expired * oxygenFraction - lung,
                carbon - expired * (value[1] / gas), inspired * (1 - inspiredFraction) - expired * (value[2] / gas),
                lung - systemic, systemic - consumed, inspired * inspiredFraction, expired * oxygenFraction, consumed, demand - consumed];
        }
    }

    private static decimal[] Add(decimal[] state, decimal[] slope, decimal scale)
    {
        decimal[] result = new decimal[state.Length];
        for (int index = 0; index < state.Length; index++) { result[index] = state[index] + slope[index] * scale; }
        return result;
    }

    private static decimal DecimalContent(decimal pressureMmHg, OxygenReservoirParameters parameters)
    {
        decimal numerator = pressureMmHg * pressureMmHg * pressureMmHg + 150m * pressureMmHg;
        return (1.34m * parameters.HemoglobinGramsPerDl * numerator / (numerator + 23400m) + .0031m * pressureMmHg) / 100m;
    }

    private static int DecimalSaturation(decimal arterialOxygenMl, OxygenReservoirParameters parameters)
    {
        decimal target = arterialOxygenMl / parameters.ArterialVolumeMl;
        decimal low = 0, high = 1000;
        for (int iteration = 0; iteration < 48; iteration++)
        {
            decimal middle = (low + high) / 2;
            if (DecimalContent(middle, parameters) < target) { low = middle; } else { high = middle; }
        }
        decimal pressure = (low + high) / 2;
        decimal numerator = pressure * pressure * pressure + 150 * pressure;
        return decimal.ToInt32(decimal.Round(numerator / (numerator + 23400) * 100000, 0, MidpointRounding.ToEven));
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected oxygen checkpoint or solver input rejection.");
    }
}
