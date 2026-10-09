// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Authoring;

public static class SinusIllustrationConfiguration
{
    // Restore sinus activity while retaining respiratory and pressure inputs.
    public static PhysiologyIllustrationConfiguration Apply(PhysiologyIllustrationConfiguration current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var next = PhysiologyIllustrationConfiguration.Default with
        {
            BreathPeriodMilliseconds = current.BreathPeriodMilliseconds,
            InspirationMilliseconds = current.InspirationMilliseconds,
            RespAmplitudeCounts = current.RespAmplitudeCounts,
            RespCardiacArtifactCounts = current.RespCardiacArtifactCounts,
            RespiratoryActivity = current.RespiratoryActivity,
            RespiratoryPattern = current.RespiratoryPattern,
            ActivityAfterBreaths = current.ActivityAfterBreaths,
            ActivityDurationBreaths = current.ActivityDurationBreaths,
            InspiratoryPauseMilliseconds = current.InspiratoryPauseMilliseconds,
            ExpiratoryPauseMilliseconds = current.ExpiratoryPauseMilliseconds,
            Co2PlateauStartCentiMmHg = current.Co2PlateauStartCentiMmHg,
            Co2BaselineMmHg = current.Co2BaselineMmHg,
            Co2EndExpiratoryMmHg = current.Co2EndExpiratoryMmHg,
            Co2DeadSpaceMilliseconds = current.Co2DeadSpaceMilliseconds,
            Co2RiseMilliseconds = current.Co2RiseMilliseconds,
            Co2FallMilliseconds = current.Co2FallMilliseconds,
            Co2TransportDelayMilliseconds = current.Co2TransportDelayMilliseconds,
            Co2DispersionStepMilliseconds = current.Co2DispersionStepMilliseconds,
            SeededCo2 = current.SeededCo2,
            UseVascularReservoir = current.UseVascularReservoir,
            AbpPulsePermille = current.AbpPulsePermille,
            PaPulsePermille = current.PaPulsePermille,
            CvpBaselineCentiMmHg = current.CvpBaselineCentiMmHg,
            AbpTarget = current.AbpTarget,
            PaTarget = current.PaTarget,
            PressureVariation = current.PressureVariation
        };
        _ = next.ResolvePlan();
        return next;
    }
}
