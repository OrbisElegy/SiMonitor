// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Desktop;

internal static class ConductionSelection
{
    internal static (int Atrial, int Conducted) Resolve(int index) => index switch
    {
        >= 0 and <= 3 => (index + 1, 1),
        4 => (3, 2),
        5 => (4, 3),
        _ => throw new ArgumentException("Invalid conduction selection."),
    };
    internal static int Index(int atrial, int conducted) => (atrial, conducted) switch
    {
        ( >= 1 and <= 4, 1) => atrial - 1,
        (3, 2) => 4,
        (4, 3) => 5,
        _ => throw new ArgumentException("Invalid conduction selection."),
    };
}
