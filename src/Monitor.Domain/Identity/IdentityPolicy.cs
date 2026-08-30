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
}
