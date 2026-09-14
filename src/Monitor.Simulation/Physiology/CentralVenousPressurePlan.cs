// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

public sealed record CvpWaveComponent(long DelayNs, long DurationNs, int MagnitudeCentiMmHg);

// Independent event components, not a measured mean CVP or a rhythm classifier.
public sealed record CentralVenousPressurePlan(int BaselineCentiMmHg,
    CvpWaveComponent A, CvpWaveComponent C, CvpWaveComponent X,
    CvpWaveComponent V, CvpWaveComponent Y, int RespiratoryDeltaCentiMmHg)
{
    public const string EvidenceId = "InfirmaryCvpComponentsDraft@1";

    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology,
        Guid channelId, uint qualityFlags)
    {
        _ = RegularPhysiologyTimeline.Start(physiology);
        CvpWaveComponent[] components = [A, C, X, V, Y];
        long budget = Math.Abs((long)RespiratoryDeltaCentiMmHg);
        if (BaselineCentiMmHg is < short.MinValue or > short.MaxValue) { throw Invalid(); }
        foreach (var component in components)
        {
            if (component is null || component.DelayNs < 0 || component.DurationNs <= 0 ||
                component.DurationNs > physiology.HeartPeriodNs || component.DelayNs > long.MaxValue - component.DurationNs ||
                component.MagnitudeCentiMmHg is < 0 or > short.MaxValue) { throw Invalid(); }
            budget += component.MagnitudeCentiMmHg;
        }
        // Each component lasts at most one trigger period. This conservative
        // absolute budget also bounds overlapping positive/negative components.
        if (budget > short.MaxValue) { throw Invalid(); }
        var tables = new[] { CvpComponentTables.A, CvpComponentTables.C, CvpComponentTables.Descent,
            CvpComponentTables.V, CvpComponentTables.Descent };
        List<EventWaveformBand> bands = [];
        for (int index = 0; index < components.Length; index++)
        {
            var component = components[index];
            int signedAmplitude = component.MagnitudeCentiMmHg * (index is 2 or 4 ? -1 : 1);
            bands.Add(new(index == 0 ? PhysiologyCycleEventKind.AtrialMechanical : PhysiologyCycleEventKind.VentricularMechanical,
                component.DelayNs, component.DurationNs,
                Array.AsReadOnly(tables[index].Select(value => checked(value * signedAmplitude)).ToArray())));
        }
        bands.Add(new(PhysiologyCycleEventKind.InspirationStart, 0, physiology.BreathPeriodNs,
            Array.AsReadOnly(CvpComponentTables.Respiratory.Select(value => checked(value * RespiratoryDeltaCentiMmHg)).ToArray()),
            Array.AsReadOnly(new EventWaveformPhasePoint[] { new(0, 0), new(physiology.InspirationDurationNs, 64), new(physiology.BreathPeriodNs, 128) })));
        return new(physiology, new(channelId, "AcqPressure125@1", 1, 100, BaselineCentiMmHg, 100), bands.AsReadOnly(), 10, qualityFlags);
    }

    private static EventWaveformException Invalid() => new("Cvp.InvalidPlan", "plan");
}
