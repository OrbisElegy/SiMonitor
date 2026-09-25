// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

// UI input adapter only; source construction is independent of Avalonia.
internal static class PhysiologyDemoSource
{
    internal static Guid ChannelId(int row) => PhysiologyIllustrationSource.ChannelId(row);
    internal static PhysiologyWaveformGroup Create(PhysiologyDemoConfiguration? configuration = null, PressureZeroOffsets? pressureOffsets = null)
    {
        pressureOffsets ??= new();
        return PhysiologyIllustrationSource.Create(configuration, pressureOffsets.Abp, pressureOffsets.Pa, pressureOffsets.Cvp);
    }
}
