// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Relative thoracic impedance illustration, not calibrated tidal volume.
// Signed amplitude explicitly selects polarity; phase timing comes from the
// shared breath plan rather than assuming equal inspiration and expiration.
public sealed record RespirationPlan(int AmplitudeCounts, int CardiacArtifactCounts = 0)
{
    private static readonly IReadOnlyList<int> MandatoryBreathGains = Array.AsReadOnly(new[] { 1000, 550, 700, 500 });
    public RespirationIllustration Illustration { get; init; }
    // Optional shared source timing; CO2 sampling delay is deliberately absent.
    public long? InspiratoryEffortDelayNs { get; init; }
    public long? InspiratoryEffortDurationNs { get; init; }
    public const string EvidenceId = "RespirationIllustrationDraft@1";

    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology,
        Guid channelId, uint qualityFlags)
    {
        _ = RegularPhysiologyTimeline.Start(physiology);
        long artifactMagnitude = Math.Abs((long)CardiacArtifactCounts);
        if (!Enum.IsDefined(Illustration) || Math.Max(AmplitudeCounts, 0) + artifactMagnitude > short.MaxValue ||
            Math.Min(AmplitudeCounts, 0) - artifactMagnitude < short.MinValue)
        { throw new EventWaveformException("Respiration.InvalidPlan", "plan"); }
        var table = Array.AsReadOnly(RespirationTables.Cycle.Select(value => checked(value * AmplitudeCounts)).ToArray());
        var phases = RespiratoryPhaseMap.Create(physiology, table.Count);
        List<EventWaveformBand> bands =
        [
            new(PhysiologyCycleEventKind.InspirationStart, 0, physiology.BreathPeriodNs - physiology.ExpiratoryPauseNs, table, phases, DepthPattern: physiology.RespiratoryPattern),
        ];
        if (Illustration == RespirationIllustration.IntermittentMandatoryVentilation)
        {
            bands[0] = bands[0] with { InspirationCycleGainsPermille = MandatoryBreathGains };
        }
        if (Illustration == RespirationIllustration.InspiratoryEffort)
        {
            long expirationNs = physiology.BreathPeriodNs - physiology.InspirationDurationNs;
            long durationNs = InspiratoryEffortDurationNs ?? Math.Max(4, expirationNs / 5);
            long delayNs = InspiratoryEffortDelayNs ?? physiology.InspirationDurationNs + expirationNs / 2;
            if (durationNs <= 0 || delayNs < physiology.InspirationDurationNs ||
                durationNs > physiology.BreathPeriodNs - delayNs ||
                Math.Abs((long)AmplitudeCounts) * 13 + artifactMagnitude * 10 > short.MaxValue * 10)
            { throw new EventWaveformException("Respiration.InvalidEffort", "plan"); }
            long[] effort = RespirationTables.Cycle.Select(value => checked((long)FixedPointMath.RoundDivideTiesToEven(
                (Int128)value * AmplitudeCounts * 3, 10))).ToArray();
            bands.Add(new(PhysiologyCycleEventKind.InspirationStart, delayNs, durationNs,
                Array.AsReadOnly(effort), DepthPattern: physiology.RespiratoryPattern));
        }
        if (CardiacArtifactCounts != 0)
        {
            // Project-authored bipolar illustration from opposite quarter-cycle
            // shifts of the smooth excursion. No new breath event or detector.
            int count = RespirationTables.Cycle.Count;
            long[] artifact = Enumerable.Range(0, count).Select(index => checked(
                (RespirationTables.Cycle[(index + count / 4) % count] -
                 RespirationTables.Cycle[(index + 3 * count / 4) % count]) * CardiacArtifactCounts)).ToArray();
            // Independent faster ventricles must not stack multiple full atrial-cycle artifacts.
            long support = physiology.ConductionPattern == AvConductionPattern.VtCaptureIllustration ? 330_000_000 :
                physiology.ConductionPattern == AvConductionPattern.SinusArrhythmiaIllustration ? 600_000_000 :
                Math.Min(physiology.HeartPeriodNs, physiology.IndependentVentricularPeriodNs ?? physiology.HeartPeriodNs);
            bands.Add(new(PhysiologyCycleEventKind.VentricularMechanical, 0, support,
                Array.AsReadOnly(artifact)));
        }
        return new(physiology, new(channelId, "AcqResp125@1", 1, 1, 0, 1), bands.AsReadOnly(), qualityFlags);
    }
}
