// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Project-authored landmarks describe independent pressure components, not
// patient measurements. Monotone Hermite joins keep the rise, rounded peak,
// notch and runoff bounded without inheriting digitized upstream vertices.
internal static class AuthoredHemodynamicContours
{
    private const long Q = FixedPointMath.Q32One;

    internal static IReadOnlyList<long> Arterial { get; } = Array.AsReadOnly(SmoothWaveformTable.Create(256,
        [(0, 0), (24, Q * 78 / 100), (44, Q), (80, Q * 84 / 100),
         (112, Q * 56 / 100), (124, Q * 40 / 100), (132, Q * 50 / 100),
         (176, Q * 16 / 100), (216, Q * 5 / 100), (256, 0)]));

    internal static IReadOnlyList<long> PulmonaryArtery { get; } = Array.AsReadOnly(SmoothWaveformTable.Create(256,
        [(0, 0), (24, Q * 78 / 100), (48, Q), (72, Q * 73 / 100),
         (96, Q * 34 / 100), (108, Q * 27 / 100), (120, Q * 35 / 100),
         (156, Q * 12 / 100), (200, Q * 4 / 100), (256, 0)]));

    internal static IReadOnlyList<long> Atrial { get; } = Array.AsReadOnly(SmoothWaveformTable.Create(128,
        [(0, 0), (32, Q * 54 / 100), (64, Q), (96, Q * 48 / 100), (128, 0)]));

    internal static IReadOnlyList<long> Ventricular { get; } = Array.AsReadOnly(SmoothWaveformTable.Create(128,
        [(0, 0), (28, Q * 45 / 100), (60, Q), (88, Q * 59 / 100), (128, 0)]));
}
