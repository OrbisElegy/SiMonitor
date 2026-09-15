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
    VascularPressureMorphologyPlan? Morphology = null)
{
    public const string EvidenceId = "VascularPressureRcIllustration@1";

    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology,
        Guid channelId, uint qualityFlags)
    {
        _ = VascularPressureSource.Create(physiology, this);
        // Full physical pressure uses centi-mmHg counts. A fixed affine offset
        // must not stand in for the changing diastolic reservoir pressure.
        return new(physiology, new WaveformBlockPlaneConfiguration(channelId, "AcqPressure125@1",
            1, 100, 0, 1), Array.Empty<EventWaveformBand>(), 10, qualityFlags, this);
    }
}
