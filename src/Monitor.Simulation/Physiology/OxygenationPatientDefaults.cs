// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Selects the published physiological formula coefficients, not gender identity.
public enum OxygenationReferenceSex { Male, Female }

public sealed record OxygenationPatientProfile(decimal AgeYears, OxygenationReferenceSex Sex,
    decimal HeightCm, decimal WeightKg)
{
    public static OxygenationPatientProfile Default => new(35, OxygenationReferenceSex.Male, 175, 70);
    public decimal BodyMassIndex => WeightKg / (HeightCm / 100m * HeightCm / 100m);
    public void Validate()
    {
        if (AgeYears is < 18 or > 90 || !Enum.IsDefined(Sex) || HeightCm is < 140 or > 210 || WeightKg is < 40 or > 150)
        { throw new ArgumentException("OxygenationDefaults.InvalidAdultProfile"); }
    }
}

public sealed record OxygenationBaselineOverrides(decimal? BloodVolumeMl = null, decimal? FrcMlBtps = null,
    decimal? HemoglobinGramsPerDl = null, decimal? BasalOxygenDemandMlStpdPerMinute = null);

public enum OxygenationBaselineOrigin { ReferenceCenter, ReferencePrediction, Explicit }

public sealed record OxygenationBaselineValue(decimal Value, string Unit, OxygenationBaselineOrigin Origin,
    string ReferenceId, string CenterSelection);

public sealed record OxygenationBaselineResolution(string ReferenceSetId, OxygenReservoirParameters Parameters,
    OxygenationBaselineValue BloodVolume, OxygenationBaselineValue Frc,
    OxygenationBaselineValue Hemoglobin, OxygenationBaselineValue BasalOxygenDemand);

// Versioned, explicitly European/US-white adult teaching reference set. Each
// parameter keeps its own population and central-value definition in metadata.
public static class OxygenationPatientDefaults
{
    public const string ReferenceSetId = OxygenationReferenceData.SetId;
    private const decimal LogTwo = .6931471805599453094172321215m;

    public static OxygenationBaselineResolution Resolve(OxygenationPatientProfile profile,
        OxygenationBaselineOverrides? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        overrides ??= new();
        var blood = overrides.BloodVolumeMl is { } volume ? Explicit(volume, "mL") :
            new OxygenationBaselineValue(PredictBloodVolumeMl(profile), "mL", OxygenationBaselineOrigin.ReferencePrediction,
                OxygenationReferenceData.BloodVolumeId, "regression prediction");
        var frc = overrides.FrcMlBtps is { } lung ? Explicit(lung, "mL(BTPS)") :
            new OxygenationBaselineValue(PredictFrcMlBtps(profile), "mL(BTPS)", OxygenationBaselineOrigin.ReferenceCenter,
                OxygenationReferenceData.FrcId, "LMS z=0 median");
        var hemoglobin = overrides.HemoglobinGramsPerDl is { } hb ? Explicit(hb, "g/dL") :
            new OxygenationBaselineValue(ReferenceHemoglobinGramsPerDl(profile), "g/dL", OxygenationBaselineOrigin.ReferenceCenter,
                OxygenationReferenceData.HemoglobinId, "age-group mean, 0 SD");
        var oxygen = overrides.BasalOxygenDemandMlStpdPerMinute is { } demand ? Explicit(demand, "mL(STPD)/min") :
            new OxygenationBaselineValue(PredictBasalOxygenDemandMlStpdPerMinute(profile), "mL(STPD)/min",
                OxygenationBaselineOrigin.ReferencePrediction, OxygenationReferenceData.BasalMetabolismId, "basal prediction, zero residual");
        var parameters = new OxygenReservoirParameters(FrcMlBtps: frc.Value, BloodVolumeMl: blood.Value,
            HemoglobinGramsPerDl: hemoglobin.Value, OxygenDemandMlStpdPerMinute: oxygen.Value);
        parameters.Validate();
        _ = OxygenReservoirModel.ReferenceState(parameters); // Validate the complete combination before publication.
        return new(ReferenceSetId, parameters, blood, frc, hemoglobin, oxygen);
    }

    private static OxygenationBaselineValue Explicit(decimal value, string unit) =>
        new(value, unit, OxygenationBaselineOrigin.Explicit, "Explicit@1", "user input");

    public static decimal PredictBloodVolumeMl(OxygenationPatientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        if (profile.BodyMassIndex is < 18.5m or >= 30m)
        { throw new ArgumentException("OxygenationDefaults.BloodVolumeNeedsOverride"); }
        var coefficients = profile.Sex == OxygenationReferenceSex.Male
            ? OxygenationReferenceData.MaleBloodCoefficients : OxygenationReferenceData.FemaleBloodCoefficients;
        decimal heightMeters = profile.HeightCm / 100m;
        return 1000 * (coefficients[0] * heightMeters * heightMeters * heightMeters + coefficients[1] * profile.WeightKg + coefficients[2]);
    }

    public static decimal PredictFrcMlBtps(OxygenationPatientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        if (profile.AgeYears > 80) { throw new ArgumentException("OxygenationDefaults.FrcNeedsOverride"); }
        var coefficients = profile.Sex == OxygenationReferenceSex.Male
            ? OxygenationReferenceData.MaleFrcCoefficients : OxygenationReferenceData.FemaleFrcCoefficients;
        var spline = profile.Sex == OxygenationReferenceSex.Male
            ? OxygenationReferenceData.MaleFrcSpline : OxygenationReferenceData.FemaleFrcSpline;
        decimal position = (profile.AgeYears - 18) * 4;
        int index = decimal.ToInt32(decimal.Floor(position));
        decimal fraction = position - index;
        decimal ageSpline = spline[index];
        if (fraction != 0) { ageSpline += fraction * (spline[index + 1] - ageSpline); }
        return Exp(coefficients[0] + coefficients[1] * Log(profile.AgeYears) +
            coefficients[2] * Log(profile.HeightCm) + ageSpline) * 1000;
    }

    public static decimal ReferenceHemoglobinGramsPerDl(OxygenationPatientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        if (profile.AgeYears < 20) { throw new ArgumentException("OxygenationDefaults.HemoglobinNeedsOverride"); }
        var means = profile.Sex == OxygenationReferenceSex.Male
            ? OxygenationReferenceData.MaleHemoglobinMeans : OxygenationReferenceData.FemaleHemoglobinMeans;
        return means[Math.Min(6, decimal.ToInt32(decimal.Floor(profile.AgeYears / 10)) - 2)];
    }

    public static decimal PredictBasalOxygenDemandMlStpdPerMinute(OxygenationPatientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        var coefficients = profile.Sex == OxygenationReferenceSex.Male
            ? OxygenationReferenceData.MaleBasalCoefficients : OxygenationReferenceData.FemaleBasalCoefficients;
        int index = profile.AgeYears < 30 ? 0 : profile.AgeYears < 60 ? 2 : 4;
        decimal kcalPerDay = coefficients[index] * profile.WeightKg + coefficients[index + 1];
        const decimal respiratoryQuotient = .8m; // Same authored RQ as the reservoir model; no activity factor.
        return kcalPerDay * 1000 / (1440 * (3.941m + 1.106m * respiratoryQuotient));
    }

    // Bounded decimal log/exp; no platform floating-point math in simulation.
    // Inputs are validated adult ages/heights; reduction keeps |z| <= 1/3.
    private static decimal Log(decimal value)
    {
        int power = 0;
        while (value > 2) { value /= 2; power++; }
        decimal z = (value - 1) / (value + 1), square = z * z, term = z, sum = 0;
        for (int index = 0; index < 64; index++)
        {
            sum += term / (2 * index + 1);
            term *= square;
        }
        return 2 * sum + power * LogTwo;
    }

    private static decimal Exp(decimal value)
    {
        int power = decimal.ToInt32(decimal.Floor(value / LogTwo));
        decimal remainder = value - power * LogTwo, term = 1, sum = 1;
        for (int index = 1; index <= 64; index++) { term *= remainder / index; sum += term; }
        for (int index = 0; index < Math.Abs(power); index++) { sum *= power >= 0 ? 2 : .5m; }
        return sum;
    }
}
