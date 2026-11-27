// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Monitor.Domain.Identity;

namespace Monitor.Network;

internal static partial class Program
{
    private static async Task RunExamAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args is not (["exam", "provision", _] or ["exam", "serve", _] or ["exam", "serve", _, _, _]))
        { throw new ArgumentException(Usage); }
        var config = ExamClusterConfiguration.Load(args[2]);
        if (config.IsWitness && args.Length != 3) { throw new ArgumentException("The witness accepts only authenticated lease requests."); }
        string banner = $"Exam node {config.NodeId:D} listening at {config.Self.Origin}.";
        if (args[1] == "provision" || config.IsWitness)
        {
            using var tls = new ExamClusterTransport(config);
            WarnExpiry(tls.CertificateExpiresUtc);
            using var processLock = config.AcquireProcessLock();
            if (args[1] == "provision")
            {
                if (config.IsWitness) { ExamWitnessStore.Initialize(config.Database, config.ClusterId); }
                else
                {
                    using var keys = ExamDataKeyRing.Open(config.DataKeyRing!);
                    ExamReplicaStore.Provision(config.Database, config.ClusterId, config.NodeId, keys);
                }
                Console.WriteLine("Exam node provisioned.");
                return;
            }

            using var witness = new ExamWitnessStore(config.Database, config.ClusterId);
            await using var app = tls.BuildListener();
            ExamWitnessEndpoints.Map(app, witness, tls.Identify);
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine(banner);
            await app.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        using var admin = args.Length == 5 ? AuthenticateExamAdministrator(args[3], args[4]) : null;
        await using var service = await ExamClusterService.StartAsync(config, cancellationToken).ConfigureAwait(false);
        WarnExpiry(service.CertificateExpiresUtc);
        Console.WriteLine(banner);
        if (admin is null)
        {
            await service.Completion.ConfigureAwait(false);
            return;
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task console = ExamCommandsAsync(service, args[3], args[4], admin.Principal, stop.Token);
        try
        {
            Task finished = await Task.WhenAny(service.Completion, console).ConfigureAwait(false);
            await finished.ConfigureAwait(false);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            try { await console.ConfigureAwait(false); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    private static void WarnExpiry(DateTime expiresUtc)
    {
        if (expiresUtc < DateTime.UtcNow.AddDays(30))
        { Console.Error.WriteLine($"Exam TLS certificate expires {expiresUtc:O}. Provision its replacement with overlapping pins."); }
    }

    private static ClassroomBootstrap AuthenticateExamAdministrator(string database, string username)
    {
        byte[] key = IdentityKey();
        try
        {
            return ClassroomBootstrap.AuthenticateHost(Path.GetFullPath(database), key, username,
                Secret("SIMONITOR_HOST_PASSWORD"), InstitutionAuthenticationContext.FormalExam);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static async Task ExamCommandsAsync(ExamClusterService service, string database, string username,
        LocalPrincipal principal, CancellationToken cancellationToken)
    {
        string operatorName = IdentityPolicy.CanonicalizeUsername(username) + "/" + principal.PrincipalId.ToString("D");
        while (!cancellationToken.IsCancellationRequested)
        {
            // Console.In may perform a synchronous terminal read. Keep lease maintenance on its own task.
            string? line = await Task.Run(() => Console.ReadLine(), cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) { return; }
            string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            static long Number(string value) => long.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
            try
            {
                if (DateTimeOffset.UtcNow >= principal.ExpiresAtUtc) { throw new UnauthorizedAccessException("Exam administration session expired."); }
                if (words is ["stop"]) { return; }
                if (words is ["manual-fence" or "recover" or "requalify", ..])
                {
                    using var confirmation = AuthenticateExamAdministrator(database, username);
                    if (confirmation.Principal.PrincipalId != principal.PrincipalId) { throw new UnauthorizedAccessException("Exam operator changed."); }
                }
                object? result = await service.UseAsync<object?>(async lifetime => words switch
                {
                    ["status"] => service.Node.GetStatus(),
                    ["read", var sequence] => service.Store.Read(Number(sequence)),
                    ["election-audit", var revision] => service.Store.ReadElectionAudit(Number(revision)),
                    ["backup", .. var destination] when destination.Length > 0 => Backup(string.Join(' ', destination)),
                    ["write", var epoch, var token, var request, var kind, var payload] =>
                        await service.Node.WriteAsync(Number(epoch), Number(token), new ExamReplicaCommand(Guid.Parse(request), kind,
                            Convert.FromBase64String(payload)), lifetime).ConfigureAwait(false),
                    ["write-file", var epoch, var token, var request, var kind, .. var file] when file.Length > 0 =>
                        await service.Node.WriteAsync(Number(epoch), Number(token), new ExamReplicaCommand(Guid.Parse(request), kind,
                            ReadPayload(string.Join(' ', file))), lifetime).ConfigureAwait(false),
                    ["recover", var sequence, var hash] =>
                        await service.Node.RecoverAsync(operatorName, new ExamReplicaCursor(Number(sequence), hash), lifetime).ConfigureAwait(false),
                    ["manual-fence", var evidence] => await service.Node.ManualPromoteAsync(operatorName, evidence, lifetime).ConfigureAwait(false),
                    ["requalify"] => await service.Node.RequalifyAsync(operatorName, lifetime).ConfigureAwait(false),
                    _ => throw new ArgumentException("Expected status, write, write-file, read, backup, election-audit, recover, manual-fence, requalify or stop.")
                }, cancellationToken).ConfigureAwait(false);
                Console.WriteLine(JsonSerializer.Serialize(new { ok = true, result }, ExamClusterConfiguration.Json));
            }
            catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or InvalidOperationException or
                InvalidDataException or IOException or HttpRequestException or SqliteException or CryptographicException or UnauthorizedAccessException or
                TimeoutException || error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            { Console.WriteLine(JsonSerializer.Serialize(new { ok = false, reason = error.Message }, ExamClusterConfiguration.Json)); }
        }

        bool Backup(string path)
        {
            service.Store.BackupTo(path);
            return true;
        }
    }

    private static byte[] ReadPayload(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length is < 1 or > ExamReplicaEntry.MaxPayloadBytes) { throw new InvalidDataException("Invalid exam payload size."); }
        byte[] payload = new byte[(int)stream.Length];
        stream.ReadExactly(payload);
        return payload;
    }
}
