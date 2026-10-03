// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using Monitor.Application.Identity;
using Monitor.Domain.Identity;
using Monitor.Infrastructure.Identity;

namespace Monitor.Specs;

internal static class AuthenticationSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(ValidLoginIssuesBoundPrincipal), ValidLoginIssuesBoundPrincipal),
        new(nameof(FifthFailureLocksAccountAndSource), FifthFailureLocksAccountAndSource),
        new(nameof(SourceFailuresThrottleDifferentAccounts), SourceFailuresThrottleDifferentAccounts),
        new(nameof(UnknownAccountUsesDummyVerifier), UnknownAccountUsesDummyVerifier),
        new(nameof(IncompatibleDummyVerifierIsRejected), IncompatibleDummyVerifierIsRejected),
        new(nameof(DisabledAccountIsRejected), DisabledAccountIsRejected),
        new(nameof(SessionTimeBoundariesFailClosed), SessionTimeBoundariesFailClosed),
    ];

    private static void ValidLoginIssuesBoundPrincipal()
    {
        AuthenticationFixture fixture = new(InstitutionRole.Teacher);
        InstitutionAuthenticationResult result = fixture.Authenticate(fixture.Password);

        Check.That(result.Kind == AuthenticationOutcomeKind.Accepted, "valid password must authenticate");
        Check.That(result.Principal?.PrincipalId == fixture.Account.PrincipalId,
            "principal must bind the account identity");
        Check.That(result.Principal?.AuthenticationMethod == "InstitutionPassword",
            "principal must record its authentication method");
        Check.That(result.Principal?.ExpiresAtUtc == fixture.Clock.UtcNow + TimeSpan.FromHours(8),
            "session must have the frozen overall lifetime");
        Check.That(result.Principal?.Scopes.SequenceEqual(
            ["assessment.author", "assessment.release", "case.edit", "result.export", "result.read", "session.manage"])
            == true,
            "principal scopes must be a stable role snapshot");
    }

    private static void FifthFailureLocksAccountAndSource()
    {
        AuthenticationFixture fixture = new();
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            InstitutionAuthenticationResult result = fixture.Authenticate(fixture.WrongPassword);
            Check.That(result.Kind == AuthenticationOutcomeKind.Rejected, "bad password must reject");
            Check.That((result.LockedUntilUtc is not null) == (attempt == 5),
                "only the fifth in-window failure must establish the lock");
        }

        InstitutionAuthenticationResult locked = fixture.Authenticate(fixture.Password);
        Check.That(locked.ReasonCode == "identity.account.locked", "correct password cannot bypass lock");
        Check.That(fixture.Tracker.AuditRecords[^1].ReasonCode == "identity.account.locked",
            "an attempt rejected by an active lock must be audited");
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        Check.That(fixture.Authenticate(fixture.Password).Kind == AuthenticationOutcomeKind.Accepted,
            "lock expires at the frozen 15-minute boundary");
    }

    private static void SourceFailuresThrottleDifferentAccounts()
    {
        AuthenticationFixture fixture = new();
        for (int attempt = 0; attempt < 5; attempt++)
        {
            fixture.Authenticate(
                fixture.WrongPassword,
                username: $"missing-{attempt}",
                sourceAddress: "192.0.2.55");
        }

        InstitutionAuthenticationResult result = fixture.Authenticate(
            fixture.Password,
            sourceAddress: "192.0.2.55");
        Check.That(result.ReasonCode == "identity.account.locked",
            "source dimension must combine failures across account names");
    }

    private static void UnknownAccountUsesDummyVerifier()
    {
        AuthenticationFixture fixture = new();
        int before = fixture.Hasher.VerifyCount;
        InstitutionAuthenticationResult result = fixture.Authenticate(
            fixture.WrongPassword,
            username: "missing-user",
            sourceAddress: "192.0.2.77");
        Check.That(result.ReasonCode == "identity.password.invalid", "unknown account must look invalid");
        Check.That(fixture.Hasher.VerifyCount == before + 1, "unknown account must execute dummy verify");
        Check.That(fixture.Hasher.LastVerifier == fixture.DummyVerifier,
            "unknown account must use the configured dummy verifier");
        AuthenticationFailureAuditRecord audit = fixture.Tracker.AuditRecords.Single();
        Check.That(result.ReasonCode == audit.ReasonCode && audit.ReasonCode == "identity.password.invalid",
            "audit preserves the public non-enumerating rejection code");
        Check.That(audit.CanonicalUsername == "MISSING-USER" && audit.SourceAddress == "192.0.2.77" &&
            audit.Context == InstitutionAuthenticationContext.Teaching && audit.PrincipalId is null,
            "audit binds the normalized account, source and context without inventing a principal");
    }

    private static void DisabledAccountIsRejected()
    {
        AuthenticationFixture fixture = new(accountState: InstitutionAccountState.Disabled);
        InstitutionAuthenticationResult result = fixture.Authenticate(fixture.Password);
        Check.That(result.ReasonCode == "identity.account.disabled", "disabled account must reject");
        Check.That(fixture.Tracker.AuditRecords.Single().PrincipalId == fixture.Account.PrincipalId,
            "disabled-account audit must bind the rejected principal");
    }

    private static void IncompatibleDummyVerifierIsRejected()
    {
        AuthenticationFixture fixture = new();
        bool rejected = false;
        try
        {
            _ = new InstitutionAuthenticationService(
                new TestAccountRepository(fixture.Account),
                new InMemoryAuthenticationFailureTracker(),
                fixture.Hasher,
                fixture.DummyVerifier with { Algorithm = "CHEAP-OR-UNKNOWN" },
                fixture.Clock,
                new FixedIds());
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        Check.That(rejected, "dummy verifier with a different cost path must reject at startup");
    }

    private static void SessionTimeBoundariesFailClosed()
    {
        AuthenticationFixture fixture = new();
        LocalPrincipal principal = fixture.Authenticate(fixture.Password).Principal!;

        Check.That(IdentityPolicy.EvaluateSession(
            principal,
            principal.AuthenticatedAtUtc + TimeSpan.FromMinutes(14),
            true).Kind == SessionAccessOutcomeKind.Allowed,
            "sensitive operation must be allowed before 15 minutes");
        Check.That(IdentityPolicy.EvaluateSession(
            principal,
            principal.AuthenticatedAtUtc + TimeSpan.FromMinutes(15),
            true).Kind == SessionAccessOutcomeKind.SensitiveReauthenticationRequired,
            "sensitive operation must require reauthentication at 15 minutes");
        Check.That(IdentityPolicy.EvaluateSession(
            principal,
            principal.AuthenticatedAtUtc + TimeSpan.FromMinutes(30),
            false).Kind == SessionAccessOutcomeKind.InactivityExpired,
            "session must expire at 30 minutes without activity");

        LocalPrincipal activeNearExpiry = principal with
        {
            LastActivityAtUtc = principal.AuthenticatedAtUtc + TimeSpan.FromHours(7),
        };
        Check.That(IdentityPolicy.EvaluateSession(
            activeNearExpiry,
            principal.AuthenticatedAtUtc + TimeSpan.FromHours(8),
            false).Kind == SessionAccessOutcomeKind.SessionExpired,
            "overall lifetime must win at eight hours");

        LocalPrincipal reauthenticated = IdentityPolicy.RecordSensitiveReauthentication(
            principal,
            principal.AuthenticatedAtUtc + TimeSpan.FromMinutes(10));
        Check.That(IdentityPolicy.EvaluateSession(
            reauthenticated,
            principal.AuthenticatedAtUtc + TimeSpan.FromMinutes(20),
            true).Kind == SessionAccessOutcomeKind.Allowed,
            "explicit reauthentication must refresh only the sensitive boundary");
    }

    private sealed class AuthenticationFixture
    {
        public AuthenticationFixture(
            InstitutionRole role = InstitutionRole.Admin,
            InstitutionAccountState accountState = InstitutionAccountState.Active)
        {
            Password = RandomSecret();
            WrongPassword = RandomSecret();
            Hasher = new CountingPasswordHasher();
            PasswordVerifier verifier = Hasher.Hash(Password);
            DummyVerifier = Hasher.Hash(RandomSecret());
            Account = new InstitutionAccount(
                Guid.Parse("41111111-1111-4111-8111-111111111111"),
                "examadmin",
                "EXAMADMIN",
                role,
                accountState,
                verifier,
                1);
            Clock = new MutableClock(new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.Zero));
            Tracker = new InMemoryAuthenticationFailureTracker();
            Service = new InstitutionAuthenticationService(
                new TestAccountRepository(Account),
                Tracker,
                Hasher,
                DummyVerifier,
                Clock,
                new FixedIds());
        }

        public string Password { get; }

        public string WrongPassword { get; }

        public CountingPasswordHasher Hasher { get; }

        public PasswordVerifier DummyVerifier { get; }

        public InstitutionAccount Account { get; }

        public MutableClock Clock { get; }

        public InMemoryAuthenticationFailureTracker Tracker { get; }

        public InstitutionAuthenticationService Service { get; }

        public InstitutionAuthenticationResult Authenticate(
            string password,
            string username = "examadmin",
            string sourceAddress = "192.0.2.10") => Service.Authenticate(
                new InstitutionAuthenticationRequest(
                    username,
                    sourceAddress,
                    InstitutionAuthenticationContext.Teaching,
                    Guid.Parse("40000000-0000-4000-8000-000000000001")),
                password);
    }

    private sealed class TestAccountRepository(InstitutionAccount account) : IInstitutionAccountRepository
    {
        public InstitutionAccount? FindByCanonicalUsername(string canonicalUsername) =>
            StringComparer.Ordinal.Equals(canonicalUsername, account.CanonicalUsername) ? account : null;
    }

    internal sealed class CountingPasswordHasher : IPasswordHasher
    {
        public int VerifyCount { get; private set; }

        public PasswordVerifier? LastVerifier { get; private set; }

        public PasswordVerifier Hash(ReadOnlySpan<char> password)
        {
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(password.ToString()));
            return new PasswordVerifier("TEST-ONLY", 1, Convert.ToBase64String(new byte[16]),
                Convert.ToBase64String(digest));
        }

        public bool Verify(ReadOnlySpan<char> password, PasswordVerifier verifier)
        {
            VerifyCount++;
            LastVerifier = verifier;
            return verifier == Hash(password);
        }

        public bool IsSupported(PasswordVerifier verifier) => verifier.Algorithm == "TEST-ONLY";
    }

    internal sealed class MutableClock(DateTimeOffset utcNow) : IAuthorityWallClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan duration) => UtcNow += duration;
    }

    private sealed class FixedIds : IIdentityIdSource
    {
        public Guid NewPrincipalId() => Guid.Parse("42111111-1111-4111-8111-111111111111");

        public Guid NewAuditId() => Guid.NewGuid();

        public Guid NewSessionId() => Guid.Parse("43333333-3333-4333-8333-333333333333");
    }

    private static string RandomSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
}
