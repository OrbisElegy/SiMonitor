// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers;
using System.Text;

namespace Monitor.Domain.Identity;

public static class IdentityPolicy
{
    public const int PasswordIterations = 600_000;
    public const int PasswordSaltBytes = 16;
    public const int PasswordSubkeyBytes = 32;
    public const int MinimumPasswordLength = 12;
    public const int MaximumPasswordLength = 128;
    public const int MinimumUsernameLength = 3;
    public const int MaximumUsernameLength = 64;
    public const int FailureLimit = 5;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    public static readonly TimeSpan InactivityLimit = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan SensitiveReauthenticationLimit = TimeSpan.FromMinutes(15);

    private static readonly Dictionary<InstitutionRole, HashSet<string>> RoleScopes = new()
    {
        [InstitutionRole.Admin] =
        [
            "audit.read",
            "deployment.manage",
            "identity.manage",
            "privacy.manage",
        ],
        [InstitutionRole.Teacher] =
        [
            "assessment.author",
            "assessment.release",
            "case.edit",
            "result.export",
            "result.read",
            "session.manage",
        ],
        [InstitutionRole.Grader] = ["assessment.review", "result.read"],
        [InstitutionRole.Candidate] =
        [
            "answer.submit.own",
            "question.read.own",
            "result.read_released.own",
        ],
    };

    public static string NormalizeUsername(string username)
    {
        ArgumentNullException.ThrowIfNull(username);
        return username.Normalize(NormalizationForm.FormKC).Trim();
    }

    public static string CanonicalizeUsername(string username) =>
        NormalizeUsername(username).ToUpperInvariant();

    public static bool TryCanonicalizeUsername(string username, out string canonicalUsername)
    {
        try
        {
            canonicalUsername = CanonicalizeUsername(username);
            int length = canonicalUsername.EnumerateRunes().Count();
            return length is >= MinimumUsernameLength and <= MaximumUsernameLength;
        }
        catch (ArgumentException)
        {
            canonicalUsername = string.Empty;
            return false;
        }
    }

    public static bool IsUsernameAllowed(string username, out string reasonCode)
    {
        string normalized;
        try
        {
            normalized = NormalizeUsername(username);
        }
        catch (ArgumentException)
        {
            reasonCode = "identity.username.unicode_invalid";
            return false;
        }

        int length = normalized.EnumerateRunes().Count();
        if (length is < MinimumUsernameLength or > MaximumUsernameLength)
        {
            reasonCode = "identity.username.length";
            return false;
        }

        if (StringComparer.Ordinal.Equals(CanonicalizeUsername(normalized), "ROOT"))
        {
            reasonCode = "identity.username.reserved";
            return false;
        }

        reasonCode = string.Empty;
        return true;
    }

    public static bool IsPasswordLengthAllowed(ReadOnlySpan<char> password)
    {
        int runeCount = 0;
        while (!password.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf16(password, out _, out int consumed);
            if (status != OperationStatus.Done)
            {
                return false;
            }

            runeCount++;
            if (runeCount > MaximumPasswordLength)
            {
                return false;
            }

            password = password[consumed..];
        }

        return runeCount is >= MinimumPasswordLength and <= MaximumPasswordLength;
    }

    public static bool IsAuthorized(InstitutionRole role, string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return RoleScopes.TryGetValue(role, out HashSet<string>? scopes) && scopes.Contains(scope);
    }

    public static IReadOnlyList<string> GetScopes(InstitutionRole role) =>
        RoleScopes.TryGetValue(role, out HashSet<string>? scopes)
            ? scopes.Order(StringComparer.Ordinal).ToArray()
            : [];

    public static AuthenticationOutcome EvaluateAuthentication(
        InstitutionAuthenticationContext context,
        bool passwordValid,
        InstitutionAccountState accountState,
        bool locked)
    {
        if (!Enum.IsDefined(context))
        {
            return new AuthenticationOutcome(AuthenticationOutcomeKind.Rejected, "identity.context.unknown");
        }

        if (accountState != InstitutionAccountState.Active)
        {
            return new AuthenticationOutcome(AuthenticationOutcomeKind.Rejected, "identity.account.disabled");
        }

        if (locked)
        {
            return new AuthenticationOutcome(AuthenticationOutcomeKind.Rejected, "identity.account.locked");
        }

        return passwordValid
            ? new AuthenticationOutcome(AuthenticationOutcomeKind.Accepted, "identity.authentication.accepted")
            : new AuthenticationOutcome(AuthenticationOutcomeKind.Rejected, "identity.password.invalid");
    }

    public static MobileFactorOutcome EvaluateMobileFactor(
        bool productionEnabled,
        string? compatibilityClaim)
    {
        if (compatibilityClaim is not null &&
            !StringComparer.Ordinal.Equals(compatibilityClaim, "None"))
        {
            return new MobileFactorOutcome(
                MobileFactorOutcomeKind.RejectConfiguration,
                "identity.mobile_factor.compatibility_claim_forbidden");
        }

        return productionEnabled
            ? new MobileFactorOutcome(
                MobileFactorOutcomeKind.RejectConfiguration,
                "identity.mobile_factor.production_disabled")
            : new MobileFactorOutcome(
                MobileFactorOutcomeKind.NoAuthorityChange,
                "identity.mobile_factor.deferred");
    }

    public static SessionAccessOutcome EvaluateSession(
        LocalPrincipal principal,
        DateTimeOffset nowUtc,
        bool sensitiveOperation)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.AuthenticatedAtUtc > nowUtc ||
            principal.LastActivityAtUtc < principal.AuthenticatedAtUtc ||
            principal.LastActivityAtUtc > nowUtc ||
            principal.SensitiveAuthenticatedAtUtc < principal.AuthenticatedAtUtc ||
            principal.SensitiveAuthenticatedAtUtc > nowUtc ||
            principal.ExpiresAtUtc != principal.AuthenticatedAtUtc + SessionLifetime)
        {
            return new SessionAccessOutcome(
                SessionAccessOutcomeKind.InvalidClockState,
                "identity.session.clock_state_invalid");
        }

        if (nowUtc >= principal.ExpiresAtUtc)
        {
            return new SessionAccessOutcome(
                SessionAccessOutcomeKind.SessionExpired,
                "identity.session.overall_expired");
        }

        if (nowUtc - principal.LastActivityAtUtc >= InactivityLimit)
        {
            return new SessionAccessOutcome(
                SessionAccessOutcomeKind.InactivityExpired,
                "identity.session.inactivity_expired");
        }

        if (sensitiveOperation &&
            nowUtc - principal.SensitiveAuthenticatedAtUtc >= SensitiveReauthenticationLimit)
        {
            return new SessionAccessOutcome(
                SessionAccessOutcomeKind.SensitiveReauthenticationRequired,
                "identity.session.sensitive_reauthentication_required");
        }

        return new SessionAccessOutcome(SessionAccessOutcomeKind.Allowed, "identity.session.allowed");
    }

    public static LocalPrincipal RecordActivity(LocalPrincipal principal, DateTimeOffset nowUtc)
    {
        SessionAccessOutcome access = EvaluateSession(principal, nowUtc, false);
        if (access.Kind != SessionAccessOutcomeKind.Allowed)
        {
            throw new InvalidOperationException(access.ReasonCode);
        }

        return principal with { LastActivityAtUtc = nowUtc };
    }

    public static LocalPrincipal RecordSensitiveReauthentication(
        LocalPrincipal principal,
        DateTimeOffset nowUtc)
    {
        LocalPrincipal active = RecordActivity(principal, nowUtc);
        return active with { SensitiveAuthenticatedAtUtc = nowUtc };
    }
}
