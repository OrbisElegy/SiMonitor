// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;

namespace Monitor.Desktop;

// A bounded demo control, not a transducer calibration or height model.
internal sealed record PressureZeroOffsets(int Abp = 0, int Pa = 0, int Cvp = 0)
{
    internal static int Parse(string? text)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal value) || value is < -10 or > 10 ||
            decimal.Truncate(value * 100) != value * 100)
        { throw new ArgumentException("Pressure demo offset requires signed mmHg with at most two decimals."); }
        return (int)(value * 100);
    }
}
