// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Identity;
using Monitor.Domain.Identity;

namespace Monitor.Infrastructure.Identity;

public sealed class InMemoryAuthenticationFailureTracker : IAuthenticationFailureTracker
{
    private readonly object _sync = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _accountFailures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<DateTimeOffset>> _sourceFailures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _accountLocks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _sourceLocks = new(StringComparer.Ordinal);
    private readonly List<AuthenticationFailureAuditRecord> _audit = [];

    public IReadOnlyList<AuthenticationFailureAuditRecord> AuditRecords
    {
        get
        {
            lock (_sync)
            {
                return [.. _audit];
            }
        }
    }

    public AuthenticationLockState GetLockState(
        string canonicalUsername,
        string sourceAddress,
        DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            DateTimeOffset? accountUntil = ActiveLock(_accountLocks, canonicalUsername, nowUtc);
            DateTimeOffset? sourceUntil = ActiveLock(_sourceLocks, sourceAddress, nowUtc);
            DateTimeOffset? lockedUntil = Latest(accountUntil, sourceUntil);
            return new AuthenticationLockState(lockedUntil is not null, lockedUntil);
        }
    }

    public AuthenticationLockState RecordFailure(AuthenticationFailureAuditRecord audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        if (audit.CanonicalUsername is null || audit.SourceAddress is null)
        {
            throw new ArgumentException("The authentication failure subjects must not be null.", nameof(audit));
        }
        DateTimeOffset windowStart = audit.RecordedAtUtc - IdentityPolicy.FailureWindow;
        DateTimeOffset candidateLockedUntil = audit.RecordedAtUtc + IdentityPolicy.LockDuration;
        lock (_sync)
        {
            _audit.Add(audit);
            int accountCount = AddFailure(
                _accountFailures,
                audit.CanonicalUsername,
                windowStart,
                audit.RecordedAtUtc);
            int sourceCount = AddFailure(
                _sourceFailures,
                audit.SourceAddress,
                windowStart,
                audit.RecordedAtUtc);
            DateTimeOffset? accountUntil = ActiveLock(_accountLocks, audit.CanonicalUsername, audit.RecordedAtUtc);
            DateTimeOffset? sourceUntil = ActiveLock(_sourceLocks, audit.SourceAddress, audit.RecordedAtUtc);
            if (accountCount >= IdentityPolicy.FailureLimit)
            {
                accountUntil = accountUntil is { } currentAccountUntil && currentAccountUntil > candidateLockedUntil
                    ? currentAccountUntil : candidateLockedUntil;
                _accountLocks[audit.CanonicalUsername] = accountUntil.Value;
            }

            if (sourceCount >= IdentityPolicy.FailureLimit)
            {
                sourceUntil = sourceUntil is { } currentSourceUntil && currentSourceUntil > candidateLockedUntil
                    ? currentSourceUntil : candidateLockedUntil;
                _sourceLocks[audit.SourceAddress] = sourceUntil.Value;
            }

            DateTimeOffset? lockedUntil = Latest(accountUntil, sourceUntil);
            return new AuthenticationLockState(lockedUntil is not null, lockedUntil);
        }
    }

    public void RecordRejection(AuthenticationFailureAuditRecord audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        lock (_sync)
        {
            _audit.Add(audit);
        }
    }

    public AuthenticationFailureAuditRecord? FindAudit(Guid auditId)
    {
        lock (_sync)
        {
            return _audit.Find(record => record.AuditId == auditId);
        }
    }

    public void ClearAccountFailures(string canonicalUsername)
    {
        lock (_sync)
        {
            _accountFailures.Remove(canonicalUsername);
            _accountLocks.Remove(canonicalUsername);
        }
    }

    private static int AddFailure(
        Dictionary<string, List<DateTimeOffset>> failures,
        string key,
        DateTimeOffset windowStart,
        DateTimeOffset nowUtc)
    {
        if (!failures.TryGetValue(key, out List<DateTimeOffset>? values))
        {
            values = [];
            failures.Add(key, values);
        }

        // Older requests may finish after newer attempts; keep those newer failures
        // for their own windows instead of erasing them during delayed completion.
        values.RemoveAll(value => value <= windowStart);
        values.Add(nowUtc);
        return values.Count(value => value <= nowUtc);
    }

    private static DateTimeOffset? ActiveLock(
        Dictionary<string, DateTimeOffset> locks,
        string key,
        DateTimeOffset nowUtc)
    {
        if (!locks.TryGetValue(key, out DateTimeOffset lockedUntil))
        {
            return null;
        }

        if (nowUtc < lockedUntil)
        {
            return lockedUntil;
        }

        locks.Remove(key);
        return null;
    }

    private static DateTimeOffset? Latest(DateTimeOffset? first, DateTimeOffset? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        return first > second ? first : second;
    }
}
