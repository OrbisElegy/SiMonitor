// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Authority;

public sealed class AuthorityCandidateException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed class AuthorityCandidateKey
{
    private readonly AuthorityEventDescriptor _eventDescriptor;

    public AuthorityCandidateKey(
        long simTimeNs,
        string eventType,
        int explicitPriority,
        string stableId,
        ulong localSequence)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(stableId);
        _eventDescriptor = AuthorityEventRegistry.Get(eventType);
        ValidateStableId(stableId);
        if (explicitPriority != 0 && !_eventDescriptor.AllowsExplicitPriority)
        {
            throw new AuthorityCandidateException(
                "AuthorityCandidate.ExplicitPriorityForbidden",
                nameof(explicitPriority));
        }

        SimTimeNs = simTimeNs;
        EventType = eventType;
        ExplicitPriority = explicitPriority;
        StableId = stableId;
        LocalSequence = localSequence;
    }

    public long SimTimeNs { get; }

    public string EventType { get; }

    public int PhaseRank => _eventDescriptor.PhaseRank;

    public int EventTypeRank => _eventDescriptor.EventTypeRank;

    public int ExplicitPriority { get; }

    public string StableId { get; }

    public ulong LocalSequence { get; }

    private static void ValidateStableId(string stableId)
    {
        if (stableId.Length is < 1 or > 128 || stableId[0] is not (>= 'a' and <= 'z'))
        {
            throw InvalidStableId();
        }

        bool previousWasSeparator = false;
        foreach (char character in stableId)
        {
            bool alphaNumeric = character is >= 'a' and <= 'z' or >= '0' and <= '9';
            bool separator = character is '.' or '_' or '-';
            if ((!alphaNumeric && !separator) || (separator && previousWasSeparator))
            {
                throw InvalidStableId();
            }

            previousWasSeparator = separator;
        }

        if (previousWasSeparator)
        {
            throw InvalidStableId();
        }

        static AuthorityCandidateException InvalidStableId() => new(
            "AuthorityCandidate.InvalidStableId",
            nameof(stableId));
    }
}

public sealed class AuthorityCandidateKeyComparer : IComparer<AuthorityCandidateKey>
{
    public static AuthorityCandidateKeyComparer Instance { get; } = new();

    public int Compare(AuthorityCandidateKey? x, AuthorityCandidateKey? y)
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

        int comparison = x.SimTimeNs.CompareTo(y.SimTimeNs);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = x.PhaseRank.CompareTo(y.PhaseRank);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = x.EventTypeRank.CompareTo(y.EventTypeRank);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = y.ExplicitPriority.CompareTo(x.ExplicitPriority);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.Ordinal.Compare(x.StableId, y.StableId);
        return comparison != 0 ? comparison : x.LocalSequence.CompareTo(y.LocalSequence);
    }
}

public static class AuthorityCandidateOrdering
{
    public static IReadOnlyList<T> Order<T>(
        IEnumerable<T> candidates,
        Func<T, AuthorityCandidateKey> keySelector)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(keySelector);
        List<(T Candidate, AuthorityCandidateKey Key)> materialized = [];
        foreach (T candidate in candidates)
        {
            AuthorityCandidateKey key = keySelector(candidate) ??
                throw new AuthorityCandidateException(
                    "AuthorityCandidate.MissingOrderKey",
                    nameof(keySelector));
            materialized.Add((candidate, key));
        }

        materialized.Sort((left, right) =>
            AuthorityCandidateKeyComparer.Instance.Compare(left.Key, right.Key));
        for (int index = 1; index < materialized.Count; index++)
        {
            if (AuthorityCandidateKeyComparer.Instance.Compare(
                materialized[index - 1].Key,
                materialized[index].Key) == 0)
            {
                throw new AuthorityCandidateException(
                    "AuthorityCandidate.DuplicateOrderKey",
                    nameof(candidates));
            }
        }

        T[] ordered = materialized.Select(item => item.Candidate).ToArray();
        return Array.AsReadOnly(ordered);
    }
}

internal readonly record struct AuthorityEventDescriptor(
    int PhaseRank,
    int EventTypeRank,
    bool AllowsExplicitPriority = false);

internal static class AuthorityEventRegistry
{
    public static AuthorityEventDescriptor Get(string eventType) => eventType switch
    {
        "authority-epoch-changed" => new(0, 0),
        "permission-revoked" => new(0, 10),
        "safety-fault-raised" => new(0, 20),
        "momentary-lease-expired" => new(0, 30),
        "interaction-released" => new(10, 0),
        "operation-cancelled" => new(10, 10),
        "therapy-disarmed" => new(10, 20),
        "session-ended" => new(20, 0),
        "session-paused" => new(20, 10),
        "configuration-committed" => new(30, 0),
        "intrinsic-qrs" => new(100, 0),
        "acquisition-quality-changed" => new(110, 0),
        "estimate-committed" => new(120, 0),
        "rate-source-selected" => new(130, 0),
        "therapy-sense-accepted" => new(140, 0),
        "pacing-stimulus-candidate" => new(150, 0),
        "synchronized-shock-delivery" => new(160, 0),
        "therapy-response-committed" => new(170, 0),
        "scenario-trigger-matched" => new(180, 0, true),
        "scenario-effect-committed" => new(190, 0, true),
        "alarm-evaluated" => new(200, 0),
        "numeric-published" => new(210, 0),
        "projection-emitted" => new(220, 0),
        "prompt-intent-emitted" => new(230, 0),
        "assessment-evidence-recorded" => new(240, 0),
        "checkpoint-written" => new(250, 0),
        _ => throw new AuthorityCandidateException(
            "AuthorityCandidate.UnregisteredEventType",
            nameof(eventType)),
    };
}
