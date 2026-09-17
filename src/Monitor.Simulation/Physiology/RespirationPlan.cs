// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Relative thoracic impedance illustration, not calibrated tidal volume.
// Signed amplitude explicitly selects polarity; phase timing comes from the
// shared breath plan rather than assuming equal inspiration and expiration.
public sealed record RespirationPlan(int AmplitudeCounts, int CardiacArtifactCounts = 0)
{
    public const string EvidenceId = "RespirationIllustrationDraft@1";

    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology,
        Guid channelId, uint qualityFlags)
    {
        _ = RegularPhysiologyTimeline.Start(physiology);
        long artifactMagnitude = Math.Abs((long)CardiacArtifactCounts);
        if (Math.Max(AmplitudeCounts, 0) + artifactMagnitude > short.MaxValue ||
            Math.Min(AmplitudeCounts, 0) - artifactMagnitude < short.MinValue)
        { throw new EventWaveformException("Respiration.InvalidPlan", "plan"); }
        var table = Array.AsReadOnly(RespirationTables.Cycle.Select(value => checked(value * AmplitudeCounts)).ToArray());
        var phases = RespiratoryPhaseMap.Create(physiology, table.Count);
        List<EventWaveformBand> bands =
        [
            new(PhysiologyCycleEventKind.InspirationStart, 0, physiology.BreathPeriodNs - physiology.ExpiratoryPauseNs, table, phases, DepthPattern: physiology.RespiratoryPattern),
        ];
        if (CardiacArtifactCounts != 0)
        {
            // Project-authored bipolar illustration from opposite quarter-cycle
            // shifts of the smooth excursion. No new breath event or detector.
            int count = RespirationTables.Cycle.Count;
            long[] artifact = Enumerable.Range(0, count).Select(index => checked(
                (RespirationTables.Cycle[(index + count / 4) % count] -
                 RespirationTables.Cycle[(index + 3 * count / 4) % count]) * CardiacArtifactCounts)).ToArray();
            bands.Add(new(PhysiologyCycleEventKind.VentricularMechanical, 0, physiology.HeartPeriodNs,
                Array.AsReadOnly(artifact)));
        }
        return new(physiology, new(channelId, "AcqResp125@1", 1, 1, 0, 1), bands.AsReadOnly(), 10, qualityFlags);
    }
}
