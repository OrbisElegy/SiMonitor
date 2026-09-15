// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

public enum EcgLimbPlacement { Standard, SwapRaLa, SwapRaLl, SwapLaLl }

// Acquisition wiring permutation, applied before lead projection. The latent
// electrode sources and reference/drive electrode RL remain unchanged.
public static class EcgLimbWiring
{
    public static EcgElectrodePotentials Apply(EcgElectrodePotentials source, EcgLimbPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(source);
        return placement switch
        {
            EcgLimbPlacement.Standard => source,
            EcgLimbPlacement.SwapRaLa => source with { RA = source.LA, LA = source.RA },
            EcgLimbPlacement.SwapRaLl => source with { RA = source.LL, LL = source.RA },
            EcgLimbPlacement.SwapLaLl => source with { LA = source.LL, LL = source.LA },
            _ => throw new EventWaveformException("EcgPlacement.InvalidWiring", nameof(placement)),
        };
    }
}
