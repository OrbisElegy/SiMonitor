// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Identity;

public enum BootstrapLifecycleState
{
    BootstrapOnly,
    Completed,
}

public enum InstitutionAccountState
{
    Active,
    Disabled,
}

public enum InstitutionRole
{
    Admin,
    Teacher,
    Grader,
    Candidate,
}

public enum InstitutionAuthenticationContext
{
    Teaching,
    FormalExam,
}

public enum AuthenticationOutcomeKind
{
    Accepted,
    Rejected,
}

public enum MobileFactorOutcomeKind
{
    NoAuthorityChange,
    RejectConfiguration,
}

public sealed record AuthenticationOutcome(AuthenticationOutcomeKind Kind, string ReasonCode);

public sealed record MobileFactorOutcome(MobileFactorOutcomeKind Kind, string ReasonCode);

public sealed record PasswordVerifier(
    string Algorithm,
    int Iterations,
    string SaltBase64,
    string SubkeyBase64);

public sealed record InstitutionAccount(
    Guid PrincipalId,
    string Username,
    string CanonicalUsername,
    InstitutionRole Role,
    InstitutionAccountState State,
    PasswordVerifier PasswordVerifier,
    ulong Revision);

public sealed record BootstrapIdentitySnapshot(
    BootstrapLifecycleState State,
    PasswordVerifier? BootstrapVerifier,
    bool RootPermanentlyRetired,
    ulong Revision);

public sealed record BootstrapAuditRecord(
    Guid AuditId,
    Guid CorrelationId,
    string EventType,
    Guid ActivatedPrincipalId,
    string CanonicalUsername,
    DateTimeOffset RecordedAtUtc);

public sealed record BootstrapCompletion(
    ulong ExpectedRevision,
    InstitutionAccount Account,
    BootstrapAuditRecord Audit,
    bool PermanentlyRetireRoot);

public enum BootstrapCommitResult
{
    Committed,
    ConcurrencyConflict,
    Failed,
}

public enum BootstrapOutcomeKind
{
    RequireUsernameAndPasswordChange,
    Activated,
    Rejected,
    RetryableConflict,
}

public sealed record BootstrapOutcome(
    BootstrapOutcomeKind Kind,
    string ReasonCode,
    InstitutionAccount? Account = null);
