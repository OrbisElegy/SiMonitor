// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Therapy;
using Monitor.Simulation.Therapy;

namespace Monitor.Desktop;

internal sealed record MonitorDeviceProfile(
    DefibrillatorConfiguration ManualConfiguration,
    DefibrillatorConfiguration AedConfiguration,
    IReadOnlyList<int> AedEnergySequenceJoules,
    int DefaultManualEnergyJoules,
    bool ExitSynchronizationAfterShock);

internal static class MonitorDeviceProfiles
{
    private static readonly IReadOnlyList<int> Biphasic360EnergyStepsJoules =
        Array.AsReadOnly(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20, 30, 50, 70, 100, 150, 170, 200, 300, 360 });
    private static readonly IReadOnlyList<int> Biphasic360AedEnergySequenceJoules = Array.AsReadOnly(new[] { 200, 300, 360 });
    private static MonitorDeviceProfile Biphasic360 { get; } = CreateBiphasic360();

    // Archives select a trusted host profile; they never supply executable device logic.
    internal static MonitorDeviceProfile? Resolve(string? id) => id switch
    {
        null => null,
        "biphasic-360" => Biphasic360,
        _ => throw new ArgumentException("Skin.UnknownDeviceProfile")
    };

    private static MonitorDeviceProfile CreateBiphasic360()
    {
        // Teaching preset shared by compatible skins, independent of their layout.
        // Charge timing is fixed; see docs/research/devices/beneheart-d3.md for sources.
        var manual = new DefibrillatorConfiguration(
            Biphasic360EnergyStepsJoules,
            DefibrillationWaveformKind.BiphasicTruncatedExponential, 7000, 60)
        {
            EcgRecoveryMilliseconds = 2500
        };
        return new MonitorDeviceProfile(manual, manual with { AutoDisarmSeconds = 30 },
            Biphasic360AedEnergySequenceJoules, 200, ExitSynchronizationAfterShock: true);
    }
}
