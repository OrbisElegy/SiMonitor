// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Monitor.Network;

public sealed record ClassroomAdmissionPolicy(int InvitationLength = 8, int InvitationLifetimeSeconds = 300,
    int TokenLifetimeSeconds = 3600, int RefreshWindowSeconds = 600, int SourceFailuresPerMinute = 8, int HostFailuresPerMinute = 120)
{
    internal void Validate()
    {
        if (InvitationLength is < 8 or > 16 || InvitationLifetimeSeconds is < 30 or > 900 ||
            TokenLifetimeSeconds is < 60 or > 86400 || RefreshWindowSeconds < 10 || RefreshWindowSeconds > TokenLifetimeSeconds / 2 ||
            SourceFailuresPerMinute is < 1 or > 16 || HostFailuresPerMinute < SourceFailuresPerMinute || HostFailuresPerMinute > 240)
        { throw new ArgumentOutOfRangeException(nameof(ClassroomAdmissionPolicy), "Invalid classroom admission limits."); }
    }
}

public sealed class ClassroomAdmission : IDisposable
{
    private const string InvitationAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int TokenBytes = 32;
    private const int CredentialCapacity = 256;
    private readonly TimeSpan _invitationLifetime;
    private readonly TimeSpan _tokenLifetime;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Grant> _invitations = [];
    private readonly Dictionary<string, Grant> _tokens = [];
    private readonly Queue<(string Source, long Timestamp)> _failures = new();
    private readonly Dictionary<string, int> _sourceFailures = [];
    private readonly byte[] _lookupKey;
    private bool _disposed;
    private bool _open = true;
    private DateTimeOffset? _admissionNotAfter;
    private long _admissionOpenedAt;
    private TimeSpan? _admissionWindow;
    public ClassroomAdmissionPolicy Policy { get; }
    private bool IsAdmissionOpen()
    {
        if (_admissionNotAfter is { } end && (Clock.GetUtcNow() >= end ||
            Clock.GetElapsedTime(_admissionOpenedAt, Clock.GetTimestamp()) >= _admissionWindow))
        { _invitations.Clear(); _open = false; }
        return _open && !_ended && !_disposed;
    }
    private bool _ended;
    private ulong _epoch = 1;
    public ulong SessionEpoch { get { lock (_gate) { return _epoch; } } }
    public bool IsOpen { get { lock (_gate) { return IsAdmissionOpen(); } } }
    internal bool Ended { get { lock (_gate) { return _ended; } } }

    public void CloseInvitations()
    {
        lock (_gate) { _open = false; _invitations.Clear(); }
    }

    public void ReopenInvitations(DateTimeOffset? notAfter = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ended) { throw new InvalidOperationException("Classroom session has ended."); }
            DateTimeOffset now = Clock.GetUtcNow();
            if (notAfter <= now) { throw new ArgumentOutOfRangeException(nameof(notAfter)); }
            _invitations.Clear();
            _admissionOpenedAt = Clock.GetTimestamp();
            _admissionNotAfter = notAfter;
            _admissionWindow = notAfter - now;
            _open = true;
        }
    }

    internal void EndSession()
    {
        lock (_gate)
        {
            if (_ended) { return; }
            _ended = true;
            _open = false;
            _epoch++;
            _invitations.Clear();
            _tokens.Clear();
        }
    }


    public ClassroomAdmission(Guid sessionId, Guid instanceId, TimeProvider? timeProvider = null, ClassroomAdmissionPolicy? policy = null, Guid? cohortId = null)
    {
        if (sessionId == Guid.Empty) { throw new ArgumentException("A session ID is required.", nameof(sessionId)); }
        if (instanceId == Guid.Empty) { throw new ArgumentException("An instance ID is required.", nameof(instanceId)); }
        if (cohortId == Guid.Empty) { throw new ArgumentException("A cohort ID must be nonempty.", nameof(cohortId)); }
        CohortId = cohortId ?? Guid.NewGuid();
        SessionId = sessionId;
        InstanceId = instanceId;
        Clock = timeProvider ?? TimeProvider.System;
        Policy = policy ?? new();
        Policy.Validate();
        _invitationLifetime = TimeSpan.FromSeconds(Policy.InvitationLifetimeSeconds);
        _tokenLifetime = TimeSpan.FromSeconds(Policy.TokenLifetimeSeconds);
        _lookupKey = RandomNumberGenerator.GetBytes(32);
    }

    public Guid SessionId { get; }
    public Guid CohortId { get; }
    public Guid InstanceId { get; }
    internal TimeProvider Clock { get; }

    public string IssueInvitation(Guid studentId)
    {
        if (studentId == Guid.Empty) { throw new ArgumentException("A student ID is required.", nameof(studentId)); }
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsAdmissionOpen()) { throw new InvalidOperationException("Classroom admission is closed."); }
            long timestamp = Clock.GetTimestamp();
            DateTimeOffset now = Clock.GetUtcNow();
            RemoveExpired(_invitations, now, timestamp, _invitationLifetime);
            RemoveExpired(_tokens, now, timestamp, _tokenLifetime);
            if (_invitations.Count + _tokens.Count >= CredentialCapacity)
            {
                throw new InvalidOperationException("The classroom admission capacity has been reached.");
            }

            Grant invitation = new(studentId, timestamp, now + _invitationLifetime);
            string code;
            do
            {
                code = RandomNumberGenerator.GetString(InvitationAlphabet, Policy.InvitationLength);
            }
            while (!_invitations.TryAdd(Lookup(code), invitation));
            return code;
        }
    }

    public bool TryRedeem(string code, string source, [NotNullWhen(true)] out string? token)
    {
        token = null;
        if (string.IsNullOrWhiteSpace(source) || source.Length > 128) { return false; }
        lock (_gate)
        {
            if (!IsAdmissionOpen()) { return false; }
            long timestamp = Clock.GetTimestamp();
            DateTimeOffset now = Clock.GetUtcNow();
            while (_failures.TryPeek(out var oldest) && Clock.GetElapsedTime(oldest.Timestamp, timestamp) >= FailureWindow)
            {
                _failures.Dequeue();
                if (--_sourceFailures[oldest.Source] == 0) { _sourceFailures.Remove(oldest.Source); }
            }

            int sourceFailures = _sourceFailures.GetValueOrDefault(source);
            if (_failures.Count >= Policy.HostFailuresPerMinute || sourceFailures >= Policy.SourceFailuresPerMinute) { return false; }
            if (code?.Length != Policy.InvitationLength || !_invitations.Remove(Lookup(code), out Grant? invitation) ||
                Remaining(invitation, now, timestamp, _invitationLifetime) <= TimeSpan.Zero)
            {
                _failures.Enqueue((source, timestamp));
                _sourceFailures[source] = sourceFailures + 1;
                return false;
            }

            Grant grant = new(invitation.StudentId, timestamp, now + _tokenLifetime);
            do
            {
                token = RandomNumberGenerator.GetHexString(TokenBytes * 2);
            }
            while (!_tokens.TryAdd(Lookup(token), grant));
            return true;
        }
    }

    public bool TryAuthorize(string token, out Guid studentId, out DateTimeOffset expiresAt)
    {
        studentId = Guid.Empty;
        expiresAt = default;
        if (token?.Length != TokenBytes * 2) { return false; }
        lock (_gate)
        {
            if (_disposed) { return false; }
            string lookup = Lookup(token);
            if (!_tokens.TryGetValue(lookup, out Grant? grant)) { return false; }
            DateTimeOffset now = Clock.GetUtcNow();
            TimeSpan remaining = Remaining(grant, now, Clock.GetTimestamp(), _tokenLifetime);
            if (remaining <= TimeSpan.Zero)
            {
                _tokens.Remove(lookup);
                return false;
            }

            studentId = grant.StudentId;
            expiresAt = now + remaining;
            return true;
        }
    }

    internal string ReplaceInvitation(Guid studentId)
    {
        lock (_gate)
        {
            foreach (var grant in _invitations.Where(item => item.Value.StudentId == studentId).ToArray())
            { _invitations.Remove(grant.Key); }
            return IssueInvitation(studentId);
        }
    }

    internal void RequireNewNegotiation(Guid studentId)
    {
        lock (_gate)
        {
            foreach (var grant in _tokens.Where(item => item.Value.StudentId == studentId).ToArray())
            { _tokens[grant.Key] = grant.Value with { Negotiated = false }; }
        }
    }

    internal bool IsNegotiated(string token)
    {
        lock (_gate) { return !_ended && _tokens.TryGetValue(Lookup(token), out Grant? grant) && grant.Negotiated; }
    }

    internal void RevokeStudent(Guid student)
    {
        lock (_gate)
        {
            foreach (var (key, grant) in _invitations) { if (grant.StudentId == student) { _invitations.Remove(key); } }
            foreach (var (key, grant) in _tokens) { if (grant.StudentId == student) { _tokens.Remove(key); } }
        }
    }

    internal bool AllowsLz4(string token)
    {
        lock (_gate) { return !_ended && _tokens.TryGetValue(Lookup(token), out Grant? grant) && grant.AllowLz4; }
    }

    internal bool CompleteNegotiation(string token, bool allowLz4)
    {
        lock (_gate)
        {
            if (!TryAuthorize(token, out _, out _)) { return false; }
            string lookup = Lookup(token);
            _tokens[lookup] = _tokens[lookup] with { Negotiated = true, AllowLz4 = allowLz4 };
            return true;
        }
    }

    internal bool TryRefresh(string token, Guid requestId, out string? replacement)
    {
        lock (_gate)
        {
            replacement = null;
            if (requestId == Guid.Empty || !TryAuthorize(token, out Guid student, out DateTimeOffset expires) ||
                expires - Clock.GetUtcNow() > TimeSpan.FromSeconds(Policy.RefreshWindowSeconds)) { return false; }
            string old = Lookup(token);
            Grant previous = _tokens[old];
            if (previous.RenewalId is Guid prior && prior != requestId) { return false; }
            // A retry of the same rotation can reconstruct the original reply without retaining a bearer in plaintext.
            string keyMaterial = "Classroom.Refresh.v1/" + requestId.ToString("N") + "/" + token;
            string next = Lookup(keyMaterial);
            string nextLookup = Lookup(next);
            if (previous.RenewalId is null)
            {
                var now = Clock.GetUtcNow();
                long timestamp = Clock.GetTimestamp();
                RemoveExpired(_tokens, now, timestamp, _tokenLifetime);
                RemoveExpired(_invitations, now, timestamp, _invitationLifetime);
                // Reserve a bounded overlap for rotations of active grants even when invitations fill the primary pool.
                if (_tokens.Count + _invitations.Count >= 2 * CredentialCapacity) { return false; }
                Grant grant = new(student, timestamp, now + _tokenLifetime, previous.Negotiated, AllowLz4: previous.AllowLz4);
                if (!_tokens.TryAdd(nextLookup, grant)) { return false; }
                _tokens[old] = previous with { RenewalId = requestId };
            }
            if (!_tokens.ContainsKey(nextLookup)) { return false; }
            replacement = next;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _invitations.Clear();
            _tokens.Clear();
            _sourceFailures.Clear();
            _failures.Clear();
            CryptographicOperations.ZeroMemory(_lookupKey);
        }
    }

    private string Lookup(string secret)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        HMACSHA256.HashData(_lookupKey, MemoryMarshal.AsBytes(secret.AsSpan()), digest);
        return Convert.ToHexString(digest);
    }

    private void RemoveExpired(Dictionary<string, Grant> grants, DateTimeOffset now, long timestamp, TimeSpan lifetime)
    {
        foreach (var (lookup, grant) in grants)
        {
            if (Remaining(grant, now, timestamp, lifetime) <= TimeSpan.Zero) { grants.Remove(lookup); }
        }
    }

    private TimeSpan Remaining(Grant grant, DateTimeOffset now, long timestamp, TimeSpan lifetime)
    {
        TimeSpan monotonic = lifetime - Clock.GetElapsedTime(grant.IssuedTimestamp, timestamp);
        TimeSpan wall = grant.ExpiresAt - now;
        return monotonic < wall ? monotonic : wall;
    }

    private sealed record Grant(Guid StudentId, long IssuedTimestamp, DateTimeOffset ExpiresAt, bool Negotiated = false, Guid? RenewalId = null, bool AllowLz4 = false);
}
