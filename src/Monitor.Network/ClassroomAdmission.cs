// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Monitor.Network;

public sealed class ClassroomAdmission : IDisposable
{
    private const string InvitationAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int InvitationLength = 8;
    private const int TokenBytes = 32;
    private const int CredentialCapacity = 256;
    private const int SourceFailureLimit = 8;
    private const int GlobalFailureLimit = 120;
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Grant> _invitations = [];
    private readonly Dictionary<string, Grant> _tokens = [];
    private readonly Queue<(string Source, long Timestamp)> _failures = new();
    private readonly Dictionary<string, int> _sourceFailures = [];
    private readonly byte[] _lookupKey;
    private bool _disposed;

    public ClassroomAdmission(Guid sessionId, Guid instanceId, TimeProvider? timeProvider = null)
    {
        if (sessionId == Guid.Empty) { throw new ArgumentException("A session ID is required.", nameof(sessionId)); }
        if (instanceId == Guid.Empty) { throw new ArgumentException("An instance ID is required.", nameof(instanceId)); }
        SessionId = sessionId;
        InstanceId = instanceId;
        Clock = timeProvider ?? TimeProvider.System;
        _lookupKey = RandomNumberGenerator.GetBytes(32);
    }

    public Guid SessionId { get; }
    public Guid InstanceId { get; }
    internal TimeProvider Clock { get; }

    public string IssueInvitation(Guid studentId)
    {
        if (studentId == Guid.Empty) { throw new ArgumentException("A student ID is required.", nameof(studentId)); }
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            long timestamp = Clock.GetTimestamp();
            DateTimeOffset now = Clock.GetUtcNow();
            RemoveExpired(_invitations, now, timestamp, InvitationLifetime);
            RemoveExpired(_tokens, now, timestamp, TokenLifetime);
            if (_invitations.Count + _tokens.Count >= CredentialCapacity)
            {
                throw new InvalidOperationException("The classroom admission capacity has been reached.");
            }

            Grant invitation = new(studentId, timestamp, now + InvitationLifetime);
            string code;
            do
            {
                code = RandomNumberGenerator.GetString(InvitationAlphabet, InvitationLength);
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
            if (_disposed) { return false; }
            long timestamp = Clock.GetTimestamp();
            DateTimeOffset now = Clock.GetUtcNow();
            while (_failures.TryPeek(out var oldest) && Clock.GetElapsedTime(oldest.Timestamp, timestamp) >= FailureWindow)
            {
                _failures.Dequeue();
                if (--_sourceFailures[oldest.Source] == 0) { _sourceFailures.Remove(oldest.Source); }
            }

            int sourceFailures = _sourceFailures.GetValueOrDefault(source);
            if (_failures.Count >= GlobalFailureLimit || sourceFailures >= SourceFailureLimit) { return false; }
            if (code?.Length != InvitationLength || !_invitations.Remove(Lookup(code), out Grant? invitation) ||
                Remaining(invitation, now, timestamp, InvitationLifetime) <= TimeSpan.Zero)
            {
                _failures.Enqueue((source, timestamp));
                _sourceFailures[source] = sourceFailures + 1;
                return false;
            }

            Grant grant = new(invitation.StudentId, timestamp, now + TokenLifetime);
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
            TimeSpan remaining = Remaining(grant, now, Clock.GetTimestamp(), TokenLifetime);
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

    private sealed record Grant(Guid StudentId, long IssuedTimestamp, DateTimeOffset ExpiresAt);
}
