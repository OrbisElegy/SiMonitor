// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Network;

internal static class Program
{
    private const string Usage = """
        SiMonitor classroom transport
          init    DATABASE ADMIN_NAME BLOCKED_PASSWORD_HASH_FILE
          teacher DATABASE USERNAME LISTEN_IP PORT CERTIFICATE_DNS_NAME CERTIFICATE INVITATIONS_FILE [SEATS]
          student HTTPS_ORIGIN [BLOCK_COUNT]
        Secrets and trust configuration are described in docs/interfaces/classroom-network.md.
        """;

    internal static async Task<int> Main(string[] args)
    {
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        try
        {
            switch (args)
            {
                case ["init", var db, var admin, var blocklist]:
                    byte[] key = IdentityKey();
                    try
                    {
                        ClassroomBootstrap.Initialize(Path.GetFullPath(db), key, admin, Secret("SIMONITOR_BOOTSTRAP_PASSWORD"),
                            Secret("SIMONITOR_HOST_PASSWORD"), blocklist);
                    }
                    finally { CryptographicOperations.ZeroMemory(key); }
                    Console.WriteLine("Classroom identity initialized.");
                    break;
                case ["teacher", ..] when args.Length is 8 or 9:
                    await RunTeacherAsync(args, stop.Token).ConfigureAwait(false);
                    break;
                case ["student", ..] when args.Length is 2 or 3:
                    await RunStudentAsync(args, stop.Token).ConfigureAwait(false);
                    break;
                case ["--help"]:
                    Console.WriteLine(Usage);
                    break;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or
            System.Text.Json.JsonException or HttpRequestException or
            CryptographicException or InvalidOperationException or FormatException or OverflowException or
            System.Net.WebSockets.WebSocketException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or
            System.Net.Sockets.SocketException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static async Task RunTeacherAsync(string[] args, CancellationToken cancellationToken)
    {
        int seats = args.Length == 9 ? Integer(args[8], 1, 36) : 1;
        var address = IPAddress.Parse(args[3]);
        int port = Integer(args[4], 0, 65535);
        string hostName = args[5];
        using X509Certificate2? trustedRoot = LoadTrustedRoot();
        using var certificate = ClassroomCertificate.Load(args[6],
            Environment.GetEnvironmentVariable("SIMONITOR_TLS_PEM_KEY"), Environment.GetEnvironmentVariable("SIMONITOR_TLS_PASSWORD"));
        using var identity = Authenticate(args[1], args[2]);
        var sessionId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var source = PhysiologyIllustrationSource.CreateForSession(sessionId, instanceId);
        var ring = WaveformBlockRing.Start(sessionId, instanceId, 1, 1, 0, 0, WaveformBlockRing.HostRetentionBlockCount);
        using var admission = new ClassroomAdmission(sessionId, instanceId);
        await using var host = await ClassroomHost.StartAsync(address, port, hostName, certificate, ring, admission, identity, trustedRoot,
            cancellationToken).ConfigureAwait(false);
        var origin = new UriBuilder(Uri.UriSchemeHttps, hostName, host.BaseAddress.Port).Uri;
        WriteInvitations(args[7], origin, admission, seats);
        Console.WriteLine($"Classroom {sessionId:D} listening at {origin}. Invitations are in the specified file.");
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        long simTimeNs = 0;
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            simTimeNs = checked(simTimeNs + WaveformBlockAssembler.BlockDurationNs);
            foreach (byte[] wire in source.AdvanceTo(simTimeNs, 250, 1, 256, cancellationToken))
            {
                host.Publish(wire);
            }
        }
    }

    private static void WriteInvitations(string path, Uri origin, ClassroomAdmission admission, int seats)
    {
        using var output = new StreamWriter(CreateInvitationFile(path));
        for (int index = 0; index < seats; index++)
        {
            var studentId = Guid.NewGuid();
            output.WriteLine($"{origin} {studentId:D} {admission.IssueInvitation(studentId)}");
        }
    }

    private static FileStream CreateInvitationFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            SecurityIdentifier owner = identity.User ?? throw new UnauthorizedAccessException("Cannot identify the teacher account.");
            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security);
        }
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        });
    }

    private static async Task RunStudentAsync(string[] args, CancellationToken cancellationToken)
    {
        int count = args.Length == 3 ? Integer(args[2], 1, int.MaxValue) : int.MaxValue;
        using X509Certificate2? root = LoadTrustedRoot();
        using var client = await ClassroomStudentClient.JoinAsync(new Uri(args[1]), Secret("SIMONITOR_JOIN_CODE"), root,
            cancellationToken).ConfigureAwait(false);
        ClassroomManifest manifest = await client.GetSessionAsync(cancellationToken).ConfigureAwait(false);
        ClassroomClockSample clock = await client.SynchronizeClockAsync(cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Session {manifest.SessionId:D}, student {manifest.StudentId:D}, clock RTT {clock.RoundTripNs} ns.");
        int received = 0;
        int retries = 0;
        const int maximumRetries = 3;
        ulong cursor = manifest.NextBlockSequence;
        while (received < count)
        {
            try
            {
                await using ClassroomWaveformSubscription subscription = retries == 0
                    ? await client.SubscribeAsync(manifest, cursor, cancellationToken).ConfigureAwait(false)
                    : await client.ResumeAsync(manifest, cursor, cancellationToken).ConfigureAwait(false);
                while (received < count)
                {
                    WaveformEnvelope block = await subscription.ReceiveAsync(cancellationToken).ConfigureAwait(false)
                        ?? throw new EndOfStreamException("The host closed the waveform stream before the requested block count.");
                    cursor = subscription.NextBlockSequence;
                    received++;
                    Console.WriteLine(FormattableString.Invariant($"Block {block.BlockSequence} at {block.StartSimTimeNs} ns, {block.Planes.Count} planes."));
                }
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested && IsTransientStreamError(error))
            {
                if (retries == maximumRetries)
                {
                    throw new IOException($"Waveform delivery stopped at block {cursor} after {retries} reconnect attempts.", error);
                }
                int delaySeconds = 1 << retries;
                retries++;
                Console.Error.WriteLine($"Reconnecting for block {cursor} in {delaySeconds} s (attempt {retries}/{maximumRetries}).");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
            }
        }

        static bool IsTransientStreamError(Exception error)
        {
            HttpRequestException? request = null;
            for (Exception? cause = error; cause is not null; cause = cause.InnerException)
            {
                if (cause is System.Security.Authentication.AuthenticationException or InvalidDataException) { return false; }
                request ??= cause as HttpRequestException;
            }
            if (request is not null)
            {
                return request.StatusCode is null
                    ? request.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded
                    : request.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or
                        HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
            }
            return error is IOException or OperationCanceledException ||
                error is System.Net.WebSockets.WebSocketException { WebSocketErrorCode: System.Net.WebSockets.WebSocketError.ConnectionClosedPrematurely };
        }
    }

    private static ClassroomBootstrap Authenticate(string db, string user)
    {
        byte[] key = IdentityKey();
        try { return ClassroomBootstrap.AuthenticateHost(Path.GetFullPath(db), key, user, Secret("SIMONITOR_HOST_PASSWORD")); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static X509Certificate2? LoadTrustedRoot() => Environment.GetEnvironmentVariable("SIMONITOR_CA_FILE") is { Length: > 0 } path
        ? X509CertificateLoader.LoadCertificateFromFile(path) : null;

    private static string Secret(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new ArgumentException($"Missing {name}.");

    private static byte[] IdentityKey() => Convert.FromBase64String(Secret("SIMONITOR_IDENTITY_KEY"));

    private static int Integer(string value, int minimum, int maximum) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed >= minimum && parsed <= maximum
            ? parsed : throw new ArgumentException($"Expected an integer between {minimum} and {maximum}.");
}
