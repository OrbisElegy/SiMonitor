// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Identity;

namespace Monitor.Application.Identity;

public sealed record CompleteBootstrapRequest(
    bool LocalInteractive,
    string InitialUsername,
    string? NewUsername,
    Guid CorrelationId);

public sealed class BootstrapIdentityService
{
    private readonly IBootstrapIdentityRepository _repository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICompromisedPasswordChecker _compromisedPasswords;
    private readonly IAuthorityWallClock _clock;
    private readonly IIdentityIdSource _ids;

    public BootstrapIdentityService(
        IBootstrapIdentityRepository repository,
        IPasswordHasher passwordHasher,
        ICompromisedPasswordChecker compromisedPasswords,
        IAuthorityWallClock clock,
        IIdentityIdSource ids)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _compromisedPasswords = compromisedPasswords ??
            throw new ArgumentNullException(nameof(compromisedPasswords));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public bool MayStartNetworkListener() =>
        _repository.LoadBootstrap() is
        {
            State: BootstrapLifecycleState.Completed,
            RootPermanentlyRetired: true,
            BootstrapVerifier: null,
        };

    public BootstrapOutcome Complete(
        CompleteBootstrapRequest request,
        ReadOnlySpan<char> bootstrapPassword,
        ReadOnlySpan<char> newPassword)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CorrelationId == Guid.Empty)
        {
            return Reject("identity.correlation_id.invalid");
        }

        BootstrapIdentitySnapshot bootstrap = _repository.LoadBootstrap();

        if (bootstrap.State != BootstrapLifecycleState.BootstrapOnly ||
            bootstrap.RootPermanentlyRetired ||
            bootstrap.BootstrapVerifier is null)
        {
            return Reject("identity.bootstrap.completed");
        }

        if (!request.LocalInteractive)
        {
            return Reject("identity.bootstrap.local_only");
        }

        if (!StringComparer.Ordinal.Equals(request.InitialUsername, "root") ||
            !_passwordHasher.Verify(bootstrapPassword, bootstrap.BootstrapVerifier))
        {
            return Reject("identity.bootstrap.credential_invalid");
        }

        if (request.NewUsername is null && newPassword.IsEmpty)
        {
            return new BootstrapOutcome(
                BootstrapOutcomeKind.RequireUsernameAndPasswordChange,
                "identity.bootstrap.change_both_required");
        }

        if (request.NewUsername is null || newPassword.IsEmpty)
        {
            return Reject("identity.bootstrap.change_both_required");
        }

        if (!IdentityPolicy.IsUsernameAllowed(request.NewUsername, out string usernameReason))
        {
            return Reject(usernameReason);
        }

        string username = IdentityPolicy.NormalizeUsername(request.NewUsername);
        string canonicalUsername = IdentityPolicy.CanonicalizeUsername(username);
        if (_repository.UsernameExists(canonicalUsername))
        {
            return Reject("identity.username.not_unique");
        }

        if (!IdentityPolicy.IsPasswordLengthAllowed(newPassword))
        {
            return Reject("identity.password.length");
        }

        if (_compromisedPasswords.IsCompromised(newPassword))
        {
            return Reject("identity.password.compromised");
        }

        Guid principalId = _ids.NewPrincipalId();
        Guid auditId = _ids.NewAuditId();
        if (principalId == Guid.Empty || auditId == Guid.Empty)
        {
            throw new InvalidOperationException("Identity ID source returned an empty identifier.");
        }

        InstitutionAccount account = new(
            principalId,
            username,
            canonicalUsername,
            InstitutionRole.Admin,
            InstitutionAccountState.Active,
            _passwordHasher.Hash(newPassword),
            1);
        BootstrapAuditRecord audit = new(
            auditId,
            request.CorrelationId,
            "BootstrapAdminActivated",
            principalId,
            canonicalUsername,
            _clock.UtcNow);
        BootstrapCompletion completion = new(bootstrap.Revision, account, audit, true);

        return _repository.TryCompleteBootstrap(completion) switch
        {
            BootstrapCommitResult.Committed => new BootstrapOutcome(
                BootstrapOutcomeKind.Activated,
                "identity.bootstrap.activated",
                account),
            BootstrapCommitResult.ConcurrencyConflict => new BootstrapOutcome(
                BootstrapOutcomeKind.RetryableConflict,
                "identity.bootstrap.revision_conflict"),
            BootstrapCommitResult.Failed => Reject("identity.bootstrap.atomic_commit_failed"),
            _ => throw new InvalidOperationException("Unknown bootstrap commit result."),
        };
    }

    private static BootstrapOutcome Reject(string reasonCode) =>
        new(BootstrapOutcomeKind.Rejected, reasonCode);
}
