// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Teaching output controls. Current scales the displayed stimulus, not a
// patient-specific capture threshold; capture/failure follows the chosen example.
public sealed record PacingOutputSettings
{
    public int RatePerMinute { get; }
    public int CurrentMilliamps { get; }
    public int PulseDivision { get; }
    public long PeriodNs => 60_000_000_000L / RatePerMinute * PulseDivision;
    public PacingOutputSettings(int ratePerMinute, int currentMilliamps, int pulseDivision = 1)
    {
        if (ratePerMinute is < 30 or > 210) { throw new ArgumentOutOfRangeException(nameof(ratePerMinute)); }
        if (currentMilliamps is < 0 or > 200) { throw new ArgumentOutOfRangeException(nameof(currentMilliamps)); }
        if (pulseDivision is not (1 or 4)) { throw new ArgumentOutOfRangeException(nameof(pulseDivision)); }
        RatePerMinute = ratePerMinute;
        CurrentMilliamps = currentMilliamps;
        PulseDivision = pulseDivision;
    }
}
