// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Full-cycle source illustration, not a measured EtCO2/RR or gas transport model.
public sealed record CapnogramPlan(long DeadSpaceNs, long RiseNs, long InspiratoryFallNs,
    int BaselineMmHg, int EndExpiratoryMmHg)
{
    public const string EvidenceId = "InfirmaryCapnogramDraft@1";

    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology,
        Guid channelId, uint qualityFlags)
    {
        _ = RegularPhysiologyTimeline.Start(physiology);
        long expiration = physiology.BreathPeriodNs - physiology.InspirationDurationNs;
        if (DeadSpaceNs <= 0 || RiseNs <= 0 || DeadSpaceNs >= expiration || RiseNs >= expiration - DeadSpaceNs ||
            InspiratoryFallNs <= 0 || InspiratoryFallNs > physiology.InspirationDurationNs ||
            BaselineMmHg < 0 || EndExpiratoryMmHg < BaselineMmHg || EndExpiratoryMmHg > 327)
        { throw new EventWaveformException("Capnogram.InvalidPlan", "plan"); }
        long duration = expiration + InspiratoryFallNs;
        EventWaveformPhasePoint[] phases = [new(0, 0), new(DeadSpaceNs, 32), new(DeadSpaceNs + RiseNs, 96),
            new(expiration, 480), new(duration, 512)];
        var table = Array.AsReadOnly(CapnogramTables.Cycle.Select(value =>
            checked(value * (EndExpiratoryMmHg - BaselineMmHg) * 100)).ToArray());
        var bands = Array.AsReadOnly(new EventWaveformBand[]
        {
            new(PhysiologyCycleEventKind.ExpirationStart, 0, duration, table, Array.AsReadOnly(phases)),
        });
        return new(physiology, new(channelId, "AcqCO2_100@1", 1, 100, BaselineMmHg, 1), bands, 200, qualityFlags);
    }
}
