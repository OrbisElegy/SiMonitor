// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class BundleBlockPreset
{
    internal static ProjectedEcgDemoConfiguration Ecg(EcgBundleBlockIllustration mode) => mode == EcgBundleBlockIllustration.Reference
        ? ProjectedEcgDemoConfiguration.Default : ProjectedEcgDemoConfiguration.Default with
        { BundleBlock = mode, QrsDurationMilliseconds = checked((int)(BundleBlockReference.Timing(mode).QrsDurationNs / 1_000_000)) };

    internal static PhysiologyDemoConfiguration Physiology(EcgBundleBlockIllustration mode)
    {
        _ = Ecg(mode);
        return PhysiologyDemoConfiguration.Default with { BundleBlock = mode };
    }
}
