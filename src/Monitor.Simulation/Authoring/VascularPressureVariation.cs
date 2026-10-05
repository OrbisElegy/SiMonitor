// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Authoring;

// Seeded drift of the arterial and pulmonary pressures around their level, in
// approximate ±centi-mmHg; zero leaves a channel steady. The actual waveform
// response calibrates the beat strength factor, including targets and pulse factors.
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
