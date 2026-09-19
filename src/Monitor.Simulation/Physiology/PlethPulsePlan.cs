// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// A relative pulse illustration, not a calibrated optical PPG/SpO2 model.
// Trigger is mechanical; the caller supplies additional propagation delay.
public sealed record PlethPulsePlan(long TransitDelayNs, long DurationNs,
    int AmplitudeCounts, bool IncludeNotch = false)
{
    public const string EvidenceId = "PlethPulseIllustrationDraft@2";

    public IReadOnlyList<EventWaveformBand> CreateBands()
    {
        if (TransitDelayNs < 0 || DurationNs <= 0 || TransitDelayNs > long.MaxValue - DurationNs ||
            AmplitudeCounts is < 0 or > short.MaxValue)
        { throw new EventWaveformException("PlethPulse.InvalidPlan", "plan"); }
        var basis = IncludeNotch ? PlethPulseTables.Notched : PlethPulseTables.Plain;
        return Array.AsReadOnly(new EventWaveformBand[]
        {
            new(PhysiologyCycleEventKind.VentricularMechanical, TransitDelayNs, DurationNs,
                Array.AsReadOnly(basis.Select(value => checked(value * AmplitudeCounts)).ToArray())),
        });
    }
}
