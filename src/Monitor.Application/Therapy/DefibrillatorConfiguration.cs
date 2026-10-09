// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Therapy;

namespace Monitor.Application.Therapy;

// Device behavior, independent of the response thresholds on individual ECG templates.
public sealed record DefibrillatorConfiguration(
    IReadOnlyList<int> EnergyStepsJoules,
    DefibrillationWaveformKind Waveform,
    int ChargeDurationMilliseconds,
    int AutoDisarmSeconds)
{
    public int EcgRecoveryMilliseconds { get; init; } = 1000;
    public static DefibrillatorConfiguration Default { get; } = new(
        Array.AsReadOnly(new[] { 1, 2, 5, 10, 20, 30, 50, 70, 100, 120, 150, 200 }),
        DefibrillationWaveformKind.BiphasicTruncatedExponential, 3000, 30);

    public DefibrillatorConfiguration Snapshot()
    {
        if (EnergyStepsJoules is null || EnergyStepsJoules.Count is < 1 or > 64 ||
            EnergyStepsJoules.Any(value => value is < 1 or > 1000) ||
            !EnergyStepsJoules.SequenceEqual(EnergyStepsJoules.Distinct().Order()) ||
            !Enum.IsDefined(Waveform) || ChargeDurationMilliseconds is < 100 or > 60000 ||
            AutoDisarmSeconds is < 1 or > 300 || EcgRecoveryMilliseconds is < 100 or > 10000)
        { throw new ArgumentException("Defibrillator.InvalidConfiguration"); }
        return this with { EnergyStepsJoules = Array.AsReadOnly(EnergyStepsJoules.ToArray()) };
    }

    public int NearestEnergy(int energyJoules) => EnergyStepsJoules.MinBy(value => Math.Abs((long)value - energyJoules));

    // A device skin's fixed profile takes precedence over editable local preferences.
    public static DefibrillatorConfiguration Resolve(DefibrillatorConfiguration editable,
        DefibrillatorConfiguration? deviceOverride) => (deviceOverride ?? editable).Snapshot();
}
