// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

public sealed record ManualNibp
{
    public int SystolicMmHg { get; }
    public int DiastolicMmHg { get; }
    public int MeanMmHg { get; }

    public ManualNibp(int systolicMmHg, int diastolicMmHg, int meanMmHg)
    {
        if (diastolicMmHg < 0 || systolicMmHg > 300 ||
            diastolicMmHg > meanMmHg || meanMmHg > systolicMmHg)
        { throw new ArgumentException("ManualVitals.InvalidNibp"); }
        SystolicMmHg = systolicMmHg;
        DiastolicMmHg = diastolicMmHg;
        MeanMmHg = meanMmHg;
    }
}

public sealed record ManualCustomVital
{
    public string Name { get; }
    public decimal Value { get; }
    public string Unit { get; }

    public ManualCustomVital(string name, decimal value, string unit)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 24 || name.Any(char.IsControl) ||
            unit is null || unit.Trim().Length > 12 || unit.Any(char.IsControl))
        { throw new ArgumentException("ManualVitals.InvalidLabel"); }
        if (value is < -99999 or > 99999 || decimal.Round(value, 2) != value)
        { throw new ArgumentException("ManualVitals.InvalidValue"); }
        Name = name.Trim();
        Value = value;
        Unit = unit.Trim();
    }
}

// Null means disabled/unavailable. Slot identity does not depend on the editable label.
// These are manually supplied display values, not waveform measurements or alarm inputs.
public sealed record ManualVitalSigns
{
    public static ManualVitalSigns Empty { get; } = new();
    public ManualNibp? Nibp { get; }
    public int? TemperatureDeciCelsius { get; }
    public ManualCustomVital? Custom1 { get; }
    public ManualCustomVital? Custom2 { get; }

    public ManualVitalSigns(ManualNibp? nibp = null, int? temperatureDeciCelsius = null,
        ManualCustomVital? custom1 = null, ManualCustomVital? custom2 = null)
    {
        if (temperatureDeciCelsius is < 0 or > 500)
        { throw new ArgumentException("ManualVitals.InvalidTemperature"); }
        Nibp = nibp;
        TemperatureDeciCelsius = temperatureDeciCelsius;
        Custom1 = custom1;
        Custom2 = custom2;
    }
}
