// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;

namespace Monitor.Simulation.Physiology;

// Adapted ABP illustration, not a qualified arterial site or measured SYS/DIA/MAP.
public sealed record ArterialPulsePlan(long TransitDelayNs, long DurationNs,
    int BaselineMmHg, int PulseHeightMmHg)
{
    public const string EvidenceId = "InfirmaryArterialPulseDraft@1";

    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology,
        Guid channelId, uint qualityFlags)
    {
        _ = RegularPhysiologyTimeline.Start(physiology);
        if (TransitDelayNs < 0 || DurationNs <= 0 || DurationNs > physiology.VentricularPeriodNs ||
            TransitDelayNs > long.MaxValue - DurationNs || BaselineMmHg is < 0 or > short.MaxValue ||
            PulseHeightMmHg is < 0 or > 327)
        { throw new EventWaveformException("ArterialPulse.InvalidPlan", "plan"); }
        // Wire counts are 0.01mmHg increments above the explicit affine baseline.
        // This preserves pressure resolution without changing acquisition profiles.
        IReadOnlyList<EventWaveformBand> bands = Array.AsReadOnly(new EventWaveformBand[]
        {
            new(PhysiologyCycleEventKind.VentricularMechanical, TransitDelayNs, DurationNs,
                Array.AsReadOnly(ArterialPulseTables.Pulse.Select(value => checked(value * PulseHeightMmHg * 100)).ToArray())),
        });
        return new(physiology, new WaveformBlockPlaneConfiguration(channelId, "AcqPressure125@1",
            1, 100, BaselineMmHg, 1), bands, 10, qualityFlags);
    }
}
