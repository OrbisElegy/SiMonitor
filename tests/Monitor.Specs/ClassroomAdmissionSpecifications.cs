// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Monitor.Application.Identity;
using Monitor.Domain.Identity;
using Monitor.Network;

namespace Monitor.Specs;

internal static class ClassroomAdmissionSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ClassroomInvitationHasOneWinnerAndOneHost), ClassroomInvitationHasOneWinnerAndOneHost),
        new(nameof(ClassroomGrantsExpireAndReleaseCapacity), ClassroomGrantsExpireAndReleaseCapacity),
        new(nameof(ClassroomJoinFailuresHaveSourceAndGlobalLimits), ClassroomJoinFailuresHaveSourceAndGlobalLimits),
        new(nameof(ClassroomHostRequiresDurableLocalAuthentication), ClassroomHostRequiresDurableLocalAuthentication),
    ];

    private static void ClassroomInvitationHasOneWinnerAndOneHost()
    {
        var sessionId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        using ClassroomAdmission admission = new(sessionId, Guid.NewGuid());
        using ClassroomAdmission otherInstance = new(sessionId, Guid.NewGuid());
        string invitation = admission.IssueInvitation(studentId);
        ConcurrentBag<string> issued = [];
        Parallel.For(0, 16, index =>
        {
            if (admission.TryRedeem(invitation, $"source-{index}", out string? accepted)) { issued.Add(accepted); }
        });

        string token = issued.Single();
        Check.That(admission.TryAuthorize(token, out Guid actual, out _) && actual == studentId,
            "an invitation binds the issued token to its provisioned observer");
        Check.That(!otherInstance.TryAuthorize(token, out _, out _), "another host must reject the same bearer");
        string pending = admission.IssueInvitation(studentId);
        admission.Dispose();
        Check.That(!admission.TryAuthorize(token, out _, out _) && !admission.TryRedeem(pending, "source", out _),
            "stopping a host invalidates all grants and invitations");
    }

    private static void ClassroomGrantsExpireAndReleaseCapacity()
    {
        ClassroomClock clock = new();
        using ClassroomAdmission admission = new(Guid.NewGuid(), Guid.NewGuid(), clock);
        var studentId = Guid.NewGuid();
        string expiring = admission.IssueInvitation(studentId);
        for (int seat = 1; seat < 256; seat++) { admission.IssueInvitation(Guid.NewGuid()); }
        Reject(() => admission.IssueInvitation(Guid.NewGuid()), "The classroom admission capacity has been reached.");

        clock.Advance(TimeSpan.FromMinutes(5));
        Check.That(!admission.TryRedeem(expiring, "source", out _), "an unredeemed invitation expires at five minutes");
        string fresh = admission.IssueInvitation(studentId);
        Check.That(admission.TryRedeem(fresh, "source", out string? token), "expired invitations must release capacity");
        Check.That(admission.TryAuthorize(token!, out _, out DateTimeOffset expiry) &&
            expiry == clock.GetUtcNow() + TimeSpan.FromHours(1), "the host receives a bounded bearer expiry");
        clock.Advance(TimeSpan.FromHours(1));
        clock.ShiftWall(TimeSpan.FromHours(-1));
        Check.That(!admission.TryAuthorize(token!, out _, out _), "a wall-clock rollback cannot extend a bearer");
    }

    private static void ClassroomJoinFailuresHaveSourceAndGlobalLimits()
    {
        ClassroomClock clock = new();
        using ClassroomAdmission admission = new(Guid.NewGuid(), Guid.NewGuid(), clock);
        string invitation = admission.IssueInvitation(Guid.NewGuid());
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Check.That(!admission.TryRedeem("", "first", out _), "malformed invitations must count as failures");
        }

        Check.That(!admission.TryRedeem(invitation, "first", out _), "a throttled source cannot redeem a valid code");
        Check.That(admission.TryRedeem(invitation, "second", out _), "other sources retain access below the host limit");
        for (int attempt = 8; attempt < 120; attempt++)
        {
            Check.That(!admission.TryRedeem("", $"source-{attempt}", out _), "each source contributes to the host limit");
        }

        string pending = admission.IssueInvitation(Guid.NewGuid());
        Check.That(!admission.TryRedeem(pending, "new", out _), "source changes cannot bypass the host limit");
        clock.Advance(TimeSpan.FromMinutes(1));
        Check.That(admission.TryRedeem(pending, "first", out _), "unused valid codes survive the rate-limit window");
    }

    private static void ClassroomHostRequiresDurableLocalAuthentication()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("monitor-classroom-auth-");
        string database = Path.Combine(directory.FullName, "identity.db");
        string blocklist = Path.Combine(directory.FullName, "blocked.sha256");
        byte[] key = RandomNumberGenerator.GetBytes(32);
        const string adminName = "classroom-admin";
        const string bootstrapPassword = "initial classroom credential";
        const string adminPassword = "test-only admin passphrase";
        const string compromised = "test-only compromised passphrase";
        try
        {
            File.WriteAllText(blocklist, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(compromised))) + "\n");
            Reject(() => ClassroomBootstrap.AuthenticateHost(database, key, adminName, adminPassword), "identity.bootstrap.required");
            Check.That(!File.Exists(database), "a rejected host startup must not provision an identity database");
            Reject(() => ClassroomBootstrap.Initialize(database, key, adminName, bootstrapPassword, compromised, blocklist),
                "identity.password.compromised");
            Reject(() => ClassroomBootstrap.AuthenticateHost(database, key, adminName, adminPassword), "identity.bootstrap.required");
            Reject(() => ClassroomBootstrap.Initialize(database, key, adminName, "different initial credential", adminPassword, blocklist),
                "identity.bootstrap.credential_invalid");
            ClassroomBootstrap.Initialize(database, key, adminName, bootstrapPassword, adminPassword, blocklist);
            Reject(() => ClassroomBootstrap.AuthenticateHost(database, key, adminName, bootstrapPassword), "identity.password.invalid");
            Reject(() => ClassroomBootstrap.AuthenticateHost(database, key, "root", bootstrapPassword), "identity.password.invalid");
            using var host = ClassroomBootstrap.AuthenticateHost(database, key, adminName, adminPassword);
            Check.That(host.Principal.Role == InstitutionRole.Admin && BootstrapIdentityService.MayStartNetworkListener(host.Repository),
                "only committed enrollment and valid institution credentials may authorize a host");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            directory.Delete(recursive: true);
        }
    }

    private static void Reject(Action operation, string reason)
    {
        try { operation(); }
        catch (InvalidOperationException rejected) when (rejected.Message == reason) { return; }
        throw new InvalidOperationException($"Expected rejection: {reason}.");
    }

    private sealed class ClassroomClock : TimeProvider
    {
        private long _elapsed;
        private TimeSpan _wallOffset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_elapsed) + _wallOffset;
        public override long GetTimestamp() => _elapsed;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan duration) => _elapsed += duration.Ticks;
        public void ShiftWall(TimeSpan offset) => _wallOffset += offset;
    }
}
