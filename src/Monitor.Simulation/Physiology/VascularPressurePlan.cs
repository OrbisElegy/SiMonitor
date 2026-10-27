// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;

namespace Monitor.Simulation.Physiology;

// Project-chosen RC reservoir illustration. EjectionEquilibriumCentiMmHg is
// R*Q, the pressure increment sustained by one constant ejection input; it does
// not include AsymptoticPressureCentiMmHg. Optional morphology modulates this
// reservoir with the existing pressure seed; null retains the original RC-only
// source exactly. No arterial-site qualification, independent right-heart source,
// venous reservoir or whole circulation is implied.
public sealed record VascularPressurePlan(long TransitDelayNs, long EjectionDurationNs,
    long TimeConstantNs, int InitialPressureCentiMmHg, int AsymptoticPressureCentiMmHg,
    int EjectionEquilibriumCentiMmHg, string ModelId = "VascularPressureRcIllustration@1",
    VascularPressureMorphologyPlan? Morphology = null, bool UsePrematureBeatPerfusion = false,
    bool UseAtrialFibrillationPerfusion = false, bool IllustrateAfSystemicPulseDeficit = false,
    bool UseConductedFlutterPerfusion = false, bool UseCardiacFillingPerfusion = false,
    SeededVascularVariation? Variation = null)
{
    public const string EvidenceId = "VascularPressureRcIllustration@1";
    public VascularPressureTargetCurves? TargetCurves { get; init; }

    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology,
        Guid channelId, uint qualityFlags)
    {
        var prepared = this;
        if (physiology.CardiacRateClock is { } clock && Morphology is { } morphology)
        {
            Int128 period = UsePrematureBeatPerfusion
                ? clock.MinimumDuration(physiology.RateAdjustment?.MinimumEjectingIntervalNs(physiology) ??
                    PrematureBeatPerfusion.MinimumEjectingIntervalNs(physiology.ConductionPattern))
                : physiology.MinimumVentricularPeriodNs;
            int overlaps = checked((int)(((Int128)morphology.DurationNs + period - 1) / period));
            prepared = this with { Morphology = morphology with { MaximumPulseOverlap = Math.Max(morphology.MaximumPulseOverlap, overlaps) } };
        }
        _ = VascularPressureSource.Create(physiology, prepared);
        // Full physical pressure uses centi-mmHg counts. A fixed affine offset
        // must not stand in for the changing diastolic reservoir pressure.
        return new(physiology, new WaveformBlockPlaneConfiguration(channelId, "AcqPressure125@1",
            1, 100, 0, 1), Array.Empty<EventWaveformBand>(), qualityFlags, prepared);
    }
}
