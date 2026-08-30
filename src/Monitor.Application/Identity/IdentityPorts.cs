// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Identity;

namespace Monitor.Application.Identity;

public interface IPasswordHasher
{
    public PasswordVerifier Hash(ReadOnlySpan<char> password);

    public bool Verify(ReadOnlySpan<char> password, PasswordVerifier verifier);
}

public interface ICompromisedPasswordChecker
{
    public bool IsCompromised(ReadOnlySpan<char> password);
}

public interface IBootstrapIdentityRepository
{
    public BootstrapIdentitySnapshot LoadBootstrap();

    public bool UsernameExists(string canonicalUsername);

    public BootstrapCommitResult TryCompleteBootstrap(BootstrapCompletion completion);
}

public interface IAuthorityWallClock
{
    public DateTimeOffset UtcNow { get; }
}

public interface IIdentityIdSource
{
    public Guid NewPrincipalId();

    public Guid NewAuditId();
}
