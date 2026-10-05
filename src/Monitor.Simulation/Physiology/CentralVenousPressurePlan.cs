// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

public sealed record CvpWaveComponent(long DelayNs, long DurationNs, int MagnitudeCentiMmHg);

// Independent event components, not a measured mean CVP or a rhythm classifier.
public sealed record CentralVenousPressurePlan(int BaselineCentiMmHg,
    CvpWaveComponent A, CvpWaveComponent C, CvpWaveComponent X,
    CvpWaveComponent V, CvpWaveComponent Y, int RespiratoryDeltaCentiMmHg, int MaximumComponentOverlap = 1)
{
    public const string EvidenceId = "InfirmaryCvpComponentsDraft@2";

    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology,
        Guid channelId, uint qualityFlags)
    {
        _ = RegularPhysiologyTimeline.Start(physiology);
        CvpWaveComponent[] components = [A, C, X, V, Y];
        long budget = Math.Abs((long)BaselineCentiMmHg) + Math.Abs((long)RespiratoryDeltaCentiMmHg);
        if (MaximumComponentOverlap is < 1 or > 8 || BaselineCentiMmHg is < short.MinValue or > short.MaxValue) { throw Invalid(); }
        for (int index = 0; index < components.Length; index++)
        {
            var component = components[index];
            Int128 triggerPeriod = index == 0 ? physiology.AtrialPeriodNs : physiology.VentricularPeriodNs;
            if (component is null || component.DelayNs < 0 || component.DurationNs <= 0 ||
                component.DelayNs > long.MaxValue - component.DurationNs ||
                component.MagnitudeCentiMmHg is < 0 or > short.MaxValue) { throw Invalid(); }
            Int128 overlaps = ((Int128)component.DurationNs + triggerPeriod - 1) / triggerPeriod;
            if (overlaps > MaximumComponentOverlap) { throw Invalid(); }
            budget += (long)overlaps * component.MagnitudeCentiMmHg;
        }
        // Half-open support permits at most ceil(duration/minimum interval)
        // simultaneous copies. Reserve their absolute sum before publication.
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
        if (AtrialFlutterReference.IsPattern(physiology.ConductionPattern))
        {
            // The normal a-wave trigger remains absent. A separate small, short
            // flutter component follows each atrial activation, including during
            // ventricular systole; valve-closed pressure amplification is omitted.
            int amplitude = (int)Determinism.FixedPointMath.RoundDivideTiesToEven(
                (Int128)A.MagnitudeCentiMmHg * AtrialFlutterMechanics.TransportPermille, 1000);
            bands.Add(new(PhysiologyCycleEventKind.AtrialElectrical, physiology.AtrialMechanicalOffsetNs,
                AtrialFlutterMechanics.ContractionDurationNs,
                Array.AsReadOnly(CvpComponentTables.A.Select(value => checked(value * amplitude)).ToArray())));
        }
        bands.Add(new(PhysiologyCycleEventKind.InspirationStart, 0, physiology.BreathPeriodNs - physiology.ExpiratoryPauseNs,
            Array.AsReadOnly(CvpComponentTables.Respiratory.Select(value => checked(value * RespiratoryDeltaCentiMmHg)).ToArray()),
            RespiratoryPhaseMap.Create(physiology, CvpComponentTables.Respiratory.Count), DepthPattern: physiology.RespiratoryPattern));
        // The patient baseline belongs to the source, not the wire calibration.
        // Continuation can then change it without reinterpreting acquired samples.
        return new(physiology, new(channelId, "AcqPressure125@1", 1, 100, 0, 1), bands.AsReadOnly(), 10, qualityFlags,
            PressureBaselineCentiMmHg: BaselineCentiMmHg);
    }

    private static EventWaveformException Invalid() => new("Cvp.InvalidPlan", "plan");
}
