// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Identity;

namespace Monitor.Application.Identity;

public sealed record InstitutionAuthenticationRequest(
    string Username,
    string SourceAddress,
    InstitutionAuthenticationContext Context,
    Guid CorrelationId);

public sealed record InstitutionAuthenticationResult(
    AuthenticationOutcomeKind Kind,
    string ReasonCode,
    LocalPrincipal? Principal = null,
    DateTimeOffset? LockedUntilUtc = null);

public sealed class InstitutionAuthenticationService
{
    private readonly IInstitutionAccountRepository _accounts;
    private readonly IAuthenticationFailureTracker _failures;
    private readonly IPasswordHasher _passwordHasher;
    private readonly PasswordVerifier _dummyVerifier;
    private readonly IAuthorityWallClock _clock;
    private readonly IIdentityIdSource _ids;

    public InstitutionAuthenticationService(
        IInstitutionAccountRepository accounts,
        IAuthenticationFailureTracker failures,
        IPasswordHasher passwordHasher,
        PasswordVerifier dummyVerifier,
        IAuthorityWallClock clock,
        IIdentityIdSource ids)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _failures = failures ?? throw new ArgumentNullException(nameof(failures));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _dummyVerifier = dummyVerifier ?? throw new ArgumentNullException(nameof(dummyVerifier));
        if (!_passwordHasher.IsSupported(_dummyVerifier))
        {
            throw new ArgumentException(
                "Dummy verifier must use the active password algorithm and cost.",
                nameof(dummyVerifier));
        }

        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public InstitutionAuthenticationResult Authenticate(
        InstitutionAuthenticationRequest request,
        ReadOnlySpan<char> password)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CorrelationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.SourceAddress) ||
            !Enum.IsDefined(request.Context) ||
            !IdentityPolicy.TryCanonicalizeUsername(request.Username, out string canonicalUsername))
        {
            return Reject("identity.authentication.request_invalid");
        }

        DateTimeOffset nowUtc = _clock.UtcNow;
        AuthenticationLockState lockState = _failures.GetLockState(
            canonicalUsername,
            request.SourceAddress,
            nowUtc);
        if (lockState.IsLocked)
        {
            _failures.RecordRejection(CreateFailureAudit(
                request,
                canonicalUsername,
                nowUtc,
                null,
                "identity.account.locked"));
            return new InstitutionAuthenticationResult(
                AuthenticationOutcomeKind.Rejected,
                "identity.account.locked",
                LockedUntilUtc: lockState.LockedUntilUtc);
        }

        InstitutionAccount? account = _accounts.FindByCanonicalUsername(canonicalUsername);
        if (account is not null &&
            !StringComparer.Ordinal.Equals(account.CanonicalUsername, canonicalUsername))
        {
            throw new InvalidOperationException("Account repository returned a mismatched identity.");
        }

        PasswordVerifier verifier = account?.PasswordVerifier ?? _dummyVerifier;
        bool passwordValid = _passwordHasher.Verify(password, verifier);
        if (account is null || !passwordValid)
        {
            AuthenticationLockState failure = _failures.RecordFailure(CreateFailureAudit(
                request,
                canonicalUsername,
                nowUtc,
                account?.PrincipalId,
                "identity.password.invalid"));
            return new InstitutionAuthenticationResult(
                AuthenticationOutcomeKind.Rejected,
                "identity.password.invalid",
                LockedUntilUtc: failure.LockedUntilUtc);
        }

        AuthenticationOutcome decision = IdentityPolicy.EvaluateAuthentication(
            request.Context,
            true,
            account.State,
            false);
        if (decision.Kind != AuthenticationOutcomeKind.Accepted)
        {
            _failures.RecordRejection(CreateFailureAudit(
                request,
                canonicalUsername,
                nowUtc,
                account.PrincipalId,
                decision.ReasonCode));
            return Reject(decision.ReasonCode);
        }

        _failures.ClearAccountFailures(canonicalUsername);
        Guid sessionId = _ids.NewSessionId();
        if (sessionId == Guid.Empty)
        {
            throw new InvalidOperationException("Identity ID source returned an empty session identifier.");
        }

        LocalPrincipal principal = new(
            sessionId,
            account.PrincipalId,
            "InstitutionPassword",
            account.Role,
            IdentityPolicy.GetScopes(account.Role),
            nowUtc,
            nowUtc,
            nowUtc,
            nowUtc + IdentityPolicy.SessionLifetime);
        return new InstitutionAuthenticationResult(
            AuthenticationOutcomeKind.Accepted,
            "identity.authentication.accepted",
            principal);
    }

    private static InstitutionAuthenticationResult Reject(string reasonCode) =>
        new(AuthenticationOutcomeKind.Rejected, reasonCode);

    private AuthenticationFailureAuditRecord CreateFailureAudit(
        InstitutionAuthenticationRequest request,
        string canonicalUsername,
        DateTimeOffset recordedAtUtc,
        Guid? principalId,
        string reasonCode)
    {
        Guid auditId = _ids.NewAuditId();
        if (auditId == Guid.Empty)
        {
            throw new InvalidOperationException("Identity ID source returned an empty audit identifier.");
        }

        return new AuthenticationFailureAuditRecord(
            auditId,
            request.CorrelationId,
            canonicalUsername,
            request.SourceAddress,
            request.Context,
            principalId,
            reasonCode,
            recordedAtUtc);
    }
}
