// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Relative thoracic impedance illustration, not calibrated tidal volume.
// Signed amplitude explicitly selects polarity; phase timing comes from the
// shared breath plan rather than assuming equal inspiration and expiration.
public sealed record RespirationPlan(int AmplitudeCounts)
{
    public const string EvidenceId = "RespirationIllustrationDraft@1";

    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology,
        Guid channelId, uint qualityFlags)
    {
        _ = RegularPhysiologyTimeline.Start(physiology);
        if (AmplitudeCounts is < short.MinValue or > short.MaxValue)
        { throw new EventWaveformException("Respiration.InvalidPlan", "plan"); }
        var table = Array.AsReadOnly(RespirationTables.Cycle.Select(value => checked(value * AmplitudeCounts)).ToArray());
        var phases = RespiratoryPhaseMap.Create(physiology, table.Count);
        var bands = Array.AsReadOnly(new EventWaveformBand[]
        {
            new(PhysiologyCycleEventKind.InspirationStart, 0, physiology.BreathPeriodNs - physiology.ExpiratoryPauseNs, table, phases),
        });
        return new(physiology, new(channelId, "AcqResp125@1", 1, 1, 0, 1), bands, 10, qualityFlags);
    }
}
