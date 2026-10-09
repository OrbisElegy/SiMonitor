// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Application.Presentation;

public sealed record MonitorTherapyPreferences(int EnergyJoules, int PacingRatePerMinute,
    int PacingCurrentMilliamps, PacingIllustration PacingType)
{
    public static MonitorTherapyPreferences Default { get; } = new(150, 70, 0, PacingIllustration.RightVentricularVvi);
    public void Validate()
    {
        if (EnergyJoules is < 1 or > 1000 || !Enum.IsDefined(PacingType))
        { throw new ArgumentException("TherapyPreferences.InvalidConfiguration"); }
        _ = new PacingOutputSettings(PacingRatePerMinute, PacingCurrentMilliamps);
    }
}
