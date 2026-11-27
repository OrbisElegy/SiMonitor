// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using Monitor.Application.Identity;
using Monitor.Domain.Identity;
using Monitor.Infrastructure.Identity;

namespace Monitor.Network;

public sealed partial class ClassroomBootstrap : IDisposable
{
    private static readonly LocalAuthority Authority = new();
    private static readonly Pbkdf2PasswordHasher PasswordHasher = new();
    private static readonly Lazy<PasswordVerifier> DummyVerifier = new(() => PasswordHasher.Hash("dummy account verifier"));
    private readonly AesGcmIdentityPayloadProtector _protector;

    private ClassroomBootstrap(
        string databasePath, AesGcmIdentityPayloadProtector protector,
        IBootstrapIdentityRepository repository,
        LocalPrincipal principal)
    {
        DatabasePath = databasePath;
        _protector = protector;
        Repository = repository;
        Principal = principal;
    }

    internal string DatabasePath { get; }
    internal IIdentityPayloadProtector PayloadProtector => _protector;

    public IBootstrapIdentityRepository Repository { get; }
    public LocalPrincipal Principal { get; }

    public static void Initialize(
        string databasePath,
        ReadOnlySpan<byte> masterKey,
        string adminName,
        ReadOnlySpan<char> bootstrapPassword,
        ReadOnlySpan<char> adminPassword,
        string blockedPasswordHashesPath)
    {
        if (bootstrapPassword.IsWhiteSpace())
        {
            throw new ArgumentException("A bootstrap credential is required.", nameof(bootstrapPassword));
        }

        if (bootstrapPassword.SequenceEqual(adminPassword))
        {
            throw new InvalidOperationException("identity.bootstrap.change_both_required");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(blockedPasswordHashesPath);
        using AesGcmIdentityPayloadProtector protector = new(masterKey);
        SqliteIdentityRepository repository = new(databasePath, protector);
        repository.TryInitializeBootstrap(PasswordHasher.Hash(bootstrapPassword));
        BootstrapIdentityService service = new(
            repository, PasswordHasher, new BlockedPasswords(blockedPasswordHashesPath), Authority, Authority);
        BootstrapOutcome result = service.Complete(
            new CompleteBootstrapRequest(true, "root", adminName, Guid.NewGuid()), bootstrapPassword, adminPassword);
        if (result.Kind != BootstrapOutcomeKind.Activated) { throw new InvalidOperationException(result.ReasonCode); }
    }

    public static ClassroomBootstrap AuthenticateHost(
        string databasePath,
        ReadOnlySpan<byte> masterKey,
        string username,
        ReadOnlySpan<char> password) =>
        AuthenticateHost(databasePath, masterKey, username, password, InstitutionAuthenticationContext.Teaching);

    internal static ClassroomBootstrap AuthenticateHost(string databasePath, ReadOnlySpan<byte> masterKey,
        string username, ReadOnlySpan<char> password, InstitutionAuthenticationContext context)
    {
        if (!File.Exists(databasePath)) { throw new InvalidOperationException("identity.bootstrap.required"); }
        AesGcmIdentityPayloadProtector protector = new(masterKey);
        try
        {
            SqliteIdentityRepository repository = new(databasePath, protector);
            if (!BootstrapIdentityService.MayStartNetworkListener(repository))
            {
                throw new InvalidOperationException("identity.bootstrap.required");
            }

            InstitutionAuthenticationService authentication = new(
                repository, new SqliteAuthenticationFailureTracker(databasePath, protector),
                PasswordHasher, DummyVerifier.Value, Authority, Authority);
            InstitutionAuthenticationResult result = authentication.Authenticate(
                new InstitutionAuthenticationRequest(username, "local", context, Guid.NewGuid()),
                password);
            if (result.Kind != AuthenticationOutcomeKind.Accepted || result.Principal is not { } principal)
            {
                throw new InvalidOperationException(result.ReasonCode);
            }

            if (context == InstitutionAuthenticationContext.FormalExam && !IdentityPolicy.IsAuthorized(principal.Role, "deployment.manage"))
            { throw new UnauthorizedAccessException("Exam cluster administration requires deployment.manage."); }
            if (!IdentityPolicy.IsAuthorized(principal.Role, "deployment.manage") &&
                !IdentityPolicy.IsAuthorized(principal.Role, "session.manage"))
            {
                throw new InvalidOperationException("identity.classroom.host_forbidden");
            }

            return new ClassroomBootstrap(databasePath, protector, repository, principal);
        }
        catch
        {
            protector.Dispose();
            throw;
        }
    }

    public void Dispose() => _protector.Dispose();

    private sealed class BlockedPasswords(string path) : ICompromisedPasswordChecker
    {
        public bool IsCompromised(ReadOnlySpan<char> password)
        {
            // The identity service validates the scalar count and encoding before calling the blocklist.
            Span<byte> utf8 = stackalloc byte[4 * IdentityPolicy.MaximumPasswordLength];
            Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
            try
            {
                int length = Encoding.UTF8.GetBytes(password, utf8);
                SHA256.HashData(utf8[..length], digest);
                Span<char> hash = stackalloc char[SHA256.HashSizeInBytes * 2];
                Convert.TryToHexString(digest, hash, out _);
                bool hasEntries = false;
                foreach (string line in File.ReadLines(path, Encoding.UTF8))
                {
                    if (line.Length != hash.Length || !line.All(char.IsAsciiHexDigit))
                    {
                        throw new InvalidDataException("The password blocklist must contain one SHA-256 hash per line.");
                    }

                    hasEntries = true;
                    if (hash.Equals(line.AsSpan(), StringComparison.OrdinalIgnoreCase)) { return true; }
                }

                if (!hasEntries) { throw new InvalidDataException("The password blocklist is empty."); }
                return false;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(utf8);
                CryptographicOperations.ZeroMemory(digest);
            }
        }
    }

    private sealed class LocalAuthority : IAuthorityWallClock, IIdentityIdSource
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public Guid NewPrincipalId() => Guid.NewGuid();
        public Guid NewAuditId() => Guid.NewGuid();
        public Guid NewSessionId() => Guid.NewGuid();
    }
}
