// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Teaching output controls. Current scales the displayed stimulus, not a
// patient-specific capture threshold; capture/failure follows the chosen example.
public sealed record PacingOutputSettings
{
    public int RatePerMinute { get; }
    public int CurrentMilliamps { get; }
    public long PeriodNs => 60_000_000_000L / RatePerMinute;
    public PacingOutputSettings(int ratePerMinute, int currentMilliamps)
    {
        if (ratePerMinute is < 30 or > 180) { throw new ArgumentOutOfRangeException(nameof(ratePerMinute)); }
        if (currentMilliamps is < 0 or > 200) { throw new ArgumentOutOfRangeException(nameof(currentMilliamps)); }
        RatePerMinute = ratePerMinute;
        CurrentMilliamps = currentMilliamps;
    }
}
