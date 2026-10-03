// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using Monitor.Application.Identity;
using Monitor.Domain.Identity;
using Monitor.Infrastructure.Identity;

namespace Monitor.Specs;

internal static class IdentitySpecifications
{
    public static Specification[] All =>
    [
        new(nameof(LocalRootRequiresBothChanges), LocalRootRequiresBothChanges),
        new(nameof(RemoteBootstrapIsRejected), RemoteBootstrapIsRejected),
        new(nameof(AtomicBootstrapActivatesAdmin), AtomicBootstrapActivatesAdmin),
        new(nameof(NfkcRootReuseIsRejected), NfkcRootReuseIsRejected),
        new(nameof(CompromisedPasswordIsRejected), CompromisedPasswordIsRejected),
        new(nameof(FailedCommitRemainsBootstrapOnly), FailedCommitRemainsBootstrapOnly),
        new(nameof(ConcurrentCommitRemainsBootstrapOnly), ConcurrentCommitRemainsBootstrapOnly),
        new(nameof(RoleScopesRemainSeparated), RoleScopesRemainSeparated),
        new(nameof(AuthenticationVectorsMatch), AuthenticationVectorsMatch),
        new(nameof(MobileFactorRemainsDeferred), MobileFactorRemainsDeferred),
        new(nameof(Pbkdf2VerifierEnforcesFrozenParameters), Pbkdf2VerifierEnforcesFrozenParameters),
    ];

    private static void LocalRootRequiresBothChanges()
    {
        IdentityFixture fixture = new();
        BootstrapOutcome outcome = fixture.Service.Complete(
            Request(true, null), fixture.BootstrapPassword, []);
        Check.That(outcome.Kind == BootstrapOutcomeKind.RequireUsernameAndPasswordChange,
            "valid local root must be forced to change both fields");
        Check.That(!fixture.Service.MayStartNetworkListener(),
            "bootstrap-only state must keep listeners closed");
        BootstrapOutcome passwordOnly = fixture.Service.Complete(
            Request(true, null), fixture.BootstrapPassword, fixture.NewPassword);
        Check.That(passwordOnly.Kind == BootstrapOutcomeKind.Rejected && fixture.Repository.Account is null &&
            !fixture.Service.MayStartNetworkListener(), "changing only the password cannot activate root or open listeners");
    }

    private static void RemoteBootstrapIsRejected()
    {
        IdentityFixture fixture = new();
        BootstrapOutcome outcome = fixture.Service.Complete(
            Request(false, "examadmin"), fixture.BootstrapPassword, fixture.NewPassword);
        Check.That(outcome.ReasonCode == "identity.bootstrap.local_only", "remote root must reject");
        Check.That(fixture.Repository.Account is null, "remote attempt must not create an account");
    }

    private static void AtomicBootstrapActivatesAdmin()
    {
        IdentityFixture fixture = new();
        BootstrapOutcome outcome = fixture.Service.Complete(
            Request(true, "  ExamAdmin  "), fixture.BootstrapPassword, fixture.NewPassword);

        Check.That(outcome.Kind == BootstrapOutcomeKind.Activated, "bootstrap must activate");
        Check.That(outcome.Account?.Role == InstitutionRole.Admin, "first principal must be Admin");
        Check.That(outcome.Account?.Username == "ExamAdmin", "display username must be normalized");
        Check.That(outcome.Account?.CanonicalUsername == "EXAMADMIN", "identity must canonicalize");
        Check.That(fixture.Repository.Snapshot.State == BootstrapLifecycleState.Completed,
            "bootstrap must complete in the same transaction");
        Check.That(fixture.Repository.Snapshot.RootPermanentlyRetired,
            "root must be permanently retired");
        Check.That(fixture.Repository.Snapshot.BootstrapVerifier is null,
            "retired bootstrap verifier must be removed");
        Check.That(fixture.Repository.Audit?.EventType == "BootstrapAdminActivated",
            "activation must be audited atomically");
        Check.That(fixture.Service.MayStartNetworkListener(),
            "listener may start only after completion");
        BootstrapOutcome reused = fixture.Service.Complete(
            Request(true, "anotheradmin"), fixture.BootstrapPassword, fixture.NewPassword);
        Check.That(reused.ReasonCode == "identity.bootstrap.completed",
            "retired root credential must remain permanently rejected");
    }

    private static void NfkcRootReuseIsRejected()
    {
        IdentityFixture fixture = new();
        BootstrapOutcome outcome = fixture.Service.Complete(
            Request(true, "ｒｏｏｔ"), fixture.BootstrapPassword, fixture.NewPassword);
        Check.That(outcome.ReasonCode == "identity.username.reserved",
            "NFKC-equivalent root must remain reserved");
    }

    private static void CompromisedPasswordIsRejected()
    {
        IdentityFixture fixture = new(compromiseNewPassword: true);
        BootstrapOutcome outcome = fixture.Service.Complete(
            Request(true, "examadmin"), fixture.BootstrapPassword, fixture.NewPassword);
        Check.That(outcome.ReasonCode == "identity.password.compromised",
            "blocklisted password must reject");
    }

    private static void FailedCommitRemainsBootstrapOnly()
    {
        IdentityFixture fixture = new();
        fixture.Repository.FailNextCommit = true;
        BootstrapOutcome outcome = fixture.Service.Complete(
            Request(true, "examadmin"), fixture.BootstrapPassword, fixture.NewPassword);

        Check.That(outcome.ReasonCode == "identity.bootstrap.atomic_commit_failed",
            "storage failure must be visible");
        Check.That(fixture.Repository.Snapshot.State == BootstrapLifecycleState.BootstrapOnly,
            "failed transaction must remain bootstrap-only");
        Check.That(!fixture.Repository.Snapshot.RootPermanentlyRetired,
            "failed transaction must not partially retire root");
        Check.That(fixture.Repository.Account is null && fixture.Repository.Audit is null,
            "failed transaction must not leave account or audit fragments");
    }

    private static void RoleScopesRemainSeparated()
    {
        Check.That(IdentityPolicy.IsAuthorized(InstitutionRole.Grader, "assessment.review"),
            "grader must review");
        Check.That(IdentityPolicy.IsAuthorized(InstitutionRole.Teacher, "assessment.release"),
            "teacher must release");
        Check.That(!IdentityPolicy.IsAuthorized(InstitutionRole.Admin, "assessment.review"),
            "admin must not implicitly become grader");
        Check.That(!IdentityPolicy.IsAuthorized(InstitutionRole.Candidate, "assessment.answer_key.read"),
            "candidate must not read answer keys");
    }

    private static void ConcurrentCommitRemainsBootstrapOnly()
    {
        IdentityFixture fixture = new();
        fixture.Repository.ConflictNextCommit = true;
        BootstrapOutcome outcome = fixture.Service.Complete(
            Request(true, "examadmin"), fixture.BootstrapPassword, fixture.NewPassword);
        Check.That(outcome.Kind == BootstrapOutcomeKind.RetryableConflict,
            "revision conflict must be retryable");
        Check.That(fixture.Repository.Snapshot.State == BootstrapLifecycleState.BootstrapOnly,
            "conflict must not partially complete bootstrap");
    }

    private static void AuthenticationVectorsMatch()
    {
        Check.That(IdentityPolicy.EvaluateAuthentication(
            InstitutionAuthenticationContext.Teaching,
            true,
            InstitutionAccountState.Active,
            false).Kind == AuthenticationOutcomeKind.Accepted,
            "teaching password must be accepted");
        Check.That(IdentityPolicy.EvaluateAuthentication(
            InstitutionAuthenticationContext.FormalExam,
            true,
            InstitutionAccountState.Active,
            false).Kind == AuthenticationOutcomeKind.Accepted,
            "formal exam uses the same institution password factor");
        Check.That(IdentityPolicy.EvaluateAuthentication(
            InstitutionAuthenticationContext.Teaching,
            false,
            InstitutionAccountState.Active,
            false).Kind == AuthenticationOutcomeKind.Rejected,
            "invalid password must reject");
        Check.That(IdentityPolicy.EvaluateAuthentication(
            InstitutionAuthenticationContext.Teaching,
            true,
            InstitutionAccountState.Active,
            true).Kind == AuthenticationOutcomeKind.Rejected,
            "locked account must reject");
        Check.That(IdentityPolicy.EvaluateAuthentication(
            InstitutionAuthenticationContext.Teaching,
            true,
            InstitutionAccountState.Disabled,
            false).Kind == AuthenticationOutcomeKind.Rejected,
            "disabled account must reject");
    }

    private static void MobileFactorRemainsDeferred()
    {
        Check.That(IdentityPolicy.EvaluateMobileFactor(false, null).Kind ==
            MobileFactorOutcomeKind.NoAuthorityChange,
            "deferred factor must not elevate");
        Check.That(IdentityPolicy.EvaluateMobileFactor(false, "AllTeacherPhones").Kind ==
            MobileFactorOutcomeKind.RejectConfiguration,
            "unverified compatibility claim must reject");
        Check.That(IdentityPolicy.EvaluateMobileFactor(true, "None").Kind ==
            MobileFactorOutcomeKind.RejectConfiguration,
            "V1 must reject production mobile-factor enablement");
    }

    private static void Pbkdf2VerifierEnforcesFrozenParameters()
    {
        Pbkdf2PasswordHasher hasher = new();
        string password = RandomSecret(24);
        PasswordVerifier verifier = hasher.Hash(password);

        Check.That(verifier.Iterations == 600_000, "iteration count must be frozen");
        Check.That(Convert.FromBase64String(verifier.SaltBase64).Length == 16,
            "salt must be 128 bits");
        Check.That(Convert.FromBase64String(verifier.SubkeyBase64).Length == 32,
            "subkey must be 256 bits");
        Check.That(hasher.Verify(password, verifier), "correct password must verify");
        Check.That(!hasher.Verify(RandomSecret(24), verifier), "incorrect password must reject");
        Check.That(!hasher.Verify(password, verifier with { Iterations = 599_999 }),
            "weakened verifier metadata must fail closed");
        Check.That(!hasher.Verify(password, verifier with { SaltBase64 = "not-base64" }),
            "malformed verifier encoding must fail closed");
    }

    private static CompleteBootstrapRequest Request(bool local, string? newUsername) =>
        new(local, "root", newUsername, Guid.Parse("30000000-0000-4000-8000-000000000001"));

    private static string RandomSecret(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    private sealed class IdentityFixture
    {
        public IdentityFixture(bool compromiseNewPassword = false)
        {
            BootstrapPassword = RandomSecret(18);
            NewPassword = RandomSecret(24);
            TestPasswordHasher hasher = new();
            Repository = new TestBootstrapRepository(hasher.Hash(BootstrapPassword));
            TestCompromisedChecker compromised = new(compromiseNewPassword ? NewPassword : null);
            Service = new BootstrapIdentityService(
                Repository,
                hasher,
                compromised,
                new FixedClock(),
                new FixedIds());
        }

        public string BootstrapPassword { get; }

        public string NewPassword { get; }

        public TestBootstrapRepository Repository { get; }

        public BootstrapIdentityService Service { get; }
    }

    private sealed class TestPasswordHasher : IPasswordHasher
    {
        public PasswordVerifier Hash(ReadOnlySpan<char> password)
        {
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(password.ToString()));
            return new PasswordVerifier("TEST-ONLY", 1, Convert.ToBase64String(new byte[16]),
                Convert.ToBase64String(digest));
        }

        public bool Verify(ReadOnlySpan<char> password, PasswordVerifier verifier) =>
            verifier == Hash(password);

        public bool IsSupported(PasswordVerifier verifier) => verifier.Algorithm == "TEST-ONLY";
    }

    private sealed class TestCompromisedChecker(string? compromised) : ICompromisedPasswordChecker
    {
        public bool IsCompromised(ReadOnlySpan<char> password) =>
            compromised is not null && password.Equals(compromised, StringComparison.Ordinal);
    }

    private sealed class FixedClock : IAuthorityWallClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 30, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class FixedIds : IIdentityIdSource
    {
        public Guid NewPrincipalId() => Guid.Parse("31111111-1111-4111-8111-111111111111");

        public Guid NewAuditId() => Guid.Parse("32222222-2222-4222-8222-222222222222");

        public Guid NewSessionId() => Guid.Parse("33333333-3333-4333-8333-333333333333");
    }

    internal sealed class TestBootstrapRepository : IBootstrapIdentityRepository
    {
        private readonly object _sync = new();

        public TestBootstrapRepository(PasswordVerifier bootstrapVerifier)
        {
            Snapshot = new BootstrapIdentitySnapshot(
                BootstrapLifecycleState.BootstrapOnly,
                bootstrapVerifier,
                false,
                1);
        }

        public BootstrapIdentitySnapshot Snapshot { get; private set; }

        public InstitutionAccount? Account { get; private set; }

        public BootstrapAuditRecord? Audit { get; private set; }

        public bool FailNextCommit { get; set; }

        public bool ConflictNextCommit { get; set; }

        public BootstrapIdentitySnapshot LoadBootstrap() => Snapshot;

        public bool UsernameExists(string canonicalUsername) =>
            Account is not null && StringComparer.Ordinal.Equals(Account.CanonicalUsername, canonicalUsername);

        public BootstrapCommitResult TryCompleteBootstrap(BootstrapCompletion completion)
        {
            lock (_sync)
            {
                if (FailNextCommit)
                {
                    FailNextCommit = false;
                    return BootstrapCommitResult.Failed;
                }

                if (ConflictNextCommit)
                {
                    ConflictNextCommit = false;
                    return BootstrapCommitResult.ConcurrencyConflict;
                }

                if (Snapshot.State != BootstrapLifecycleState.BootstrapOnly ||
                    Snapshot.Revision != completion.ExpectedRevision)
                {
                    return BootstrapCommitResult.ConcurrencyConflict;
                }

                Account = completion.Account;
                Audit = completion.Audit;
                Snapshot = Snapshot with
                {
                    State = BootstrapLifecycleState.Completed,
                    BootstrapVerifier = null,
                    RootPermanentlyRetired = completion.PermanentlyRetireRoot,
                    Revision = Snapshot.Revision + 1,
                };
                return BootstrapCommitResult.Committed;
            }
        }
    }
}
