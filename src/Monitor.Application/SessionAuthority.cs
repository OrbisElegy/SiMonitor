// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Authority;
using Monitor.Domain.Common;
using Monitor.Domain.Therapy;

namespace Monitor.Application;

public sealed record AuthorityCommit(
    ulong CommitSequence,
    TherapyState State,
    IReadOnlyList<TherapyFact> Facts);

public sealed class SessionAuthority
{
    private readonly object _commitLock = new();
    private readonly TherapyController _therapy;
    private ulong _commitSequence;

    public SessionAuthority(TherapyController therapy)
    {
        _therapy = therapy ?? throw new ArgumentNullException(nameof(therapy));
    }

    public DomainResult<AuthorityCommit> CommitSafetyInputs(IEnumerable<SafetyInput> inputs)
    {
        lock (_commitLock)
        {
            DomainResult<TherapyTransition> transition = _therapy.ApplySafetyInputs(inputs);
            if (!transition.IsAccepted)
            {
                return DomainResult.Reject<AuthorityCommit>(transition.Rejection!);
            }

            if (transition.Value!.Facts.Count > 0)
            {
                _commitSequence++;
            }

            return DomainResult.Accept(
                new AuthorityCommit(_commitSequence, transition.Value.State, transition.Value.Facts));
        }
    }
}
