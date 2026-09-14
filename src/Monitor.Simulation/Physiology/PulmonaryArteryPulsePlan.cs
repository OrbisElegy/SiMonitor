// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Independent PA seed, not an ABP scale or a qualified RV/PA hemodynamic model.
public sealed record PulmonaryArteryPulsePlan(long TransitDelayNs, long DurationNs,
    int BaselineMmHg, int PulseHeightMmHg)
{
    public const string EvidenceId = "InfirmaryPulmonaryArteryDraft@1";

    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology,
        Guid channelId, uint qualityFlags)
    {
        _ = RegularPhysiologyTimeline.Start(physiology);
        if (TransitDelayNs < 0 || DurationNs <= 0 || DurationNs > physiology.HeartPeriodNs ||
            TransitDelayNs > long.MaxValue - DurationNs || BaselineMmHg is < 0 or > short.MaxValue ||
            PulseHeightMmHg is < 0 or > 327)
        { throw new EventWaveformException("PulmonaryArtery.InvalidPlan", "plan"); }
        var table = Array.AsReadOnly(PulmonaryArteryTables.Pulse.Select(value =>
            checked(value * PulseHeightMmHg * 100)).ToArray());
        var bands = Array.AsReadOnly(new EventWaveformBand[]
        {
            new(PhysiologyCycleEventKind.VentricularMechanical, TransitDelayNs, DurationNs, table),
        });
        return new(physiology, new(channelId, "AcqPressure125@1", 1, 100, BaselineMmHg, 1), bands, 10, qualityFlags);
    }
}
