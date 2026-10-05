// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Authoring;

// Seeded drift of the arterial and pulmonary pressures around their level, in
// ±centi-mmHg; zero leaves a channel steady. Amplitudes are converted to a beat
// strength factor using the channel's target, or its reference level without one.
public sealed record VascularPressureVariation(int AbpAmplitudeCentiMmHg, int PaAmplitudeCentiMmHg, string SeedHex)
{
    public static (int MinimumCentiMmHg, int MaximumCentiMmHg) AbpAmplitudeRange => (0, 2000);
    public static (int MinimumCentiMmHg, int MaximumCentiMmHg) PaAmplitudeRange => (0, 500);

    internal void Validate()
    {
        if (AbpAmplitudeCentiMmHg < AbpAmplitudeRange.MinimumCentiMmHg || AbpAmplitudeCentiMmHg > AbpAmplitudeRange.MaximumCentiMmHg ||
            PaAmplitudeCentiMmHg < PaAmplitudeRange.MinimumCentiMmHg || PaAmplitudeCentiMmHg > PaAmplitudeRange.MaximumCentiMmHg ||
            SeedHex is null)
        { throw new ArgumentException("Physiology.PressureVariationOutOfRange"); }
    }
}
