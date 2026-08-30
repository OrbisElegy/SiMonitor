// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Authority;

public enum SafetyInputKind
{
    AuthorityEpochChanged,
    PermissionRevoked,
    DeviceFaulted,
    LeaseExpired,
    ShockReleased,
    ShockCancelled,
    EnergyDisarmed,
    SessionPaused,
    SessionEnded,
    AuthorityRecovered,
    ValidQrs,
    HoldThresholdElapsed,
    LeaseRenewed,
}

public sealed record SafetyInput(
    long SafetyTimeNs,
    SafetyInputKind Kind,
    string StableId,
    ulong LocalSequence,
    Guid CorrelationId,
    Guid? InteractionId = null,
    ulong? AuthorityEpoch = null);

public sealed class SafetyInputComparer : IComparer<SafetyInput>
{
    public static SafetyInputComparer Instance { get; } = new();

    public int Compare(SafetyInput? x, SafetyInput? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int comparison = x.SafetyTimeNs.CompareTo(y.SafetyTimeNs);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Rank(x.Kind).CompareTo(Rank(y.Kind));
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.Ordinal.Compare(x.StableId, y.StableId);
        return comparison != 0 ? comparison : x.LocalSequence.CompareTo(y.LocalSequence);
    }

    private static int Rank(SafetyInputKind kind) => kind switch
    {
        SafetyInputKind.AuthorityEpochChanged or
        SafetyInputKind.PermissionRevoked or
        SafetyInputKind.DeviceFaulted or
        SafetyInputKind.LeaseExpired => 0,

        SafetyInputKind.ShockReleased or
        SafetyInputKind.ShockCancelled or
        SafetyInputKind.EnergyDisarmed or
        SafetyInputKind.SessionPaused or
        SafetyInputKind.SessionEnded or
        SafetyInputKind.AuthorityRecovered => 1,

        SafetyInputKind.ValidQrs => 2,
        SafetyInputKind.HoldThresholdElapsed => 3,
        SafetyInputKind.LeaseRenewed => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
