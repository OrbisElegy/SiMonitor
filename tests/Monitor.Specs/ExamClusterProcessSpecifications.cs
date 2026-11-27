// SPDX-License-Identifier: AGPL-3.0-or-later
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using Monitor.Network;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class ExamClusterProcessSpecifications
{
    internal static Specification[] All =>
    [
        new(nameof(ThreeProcessesFenceAndRecoverTheAcknowledgedExamFrontier),
            () => ThreeProcessesFenceAndRecoverTheAcknowledgedExamFrontier().GetAwaiter().GetResult()),
        new(nameof(EmbeddedExamHostRetainsTheSameDurableApplicationReceipt),
            () => EmbeddedExamHostRetainsTheSameDurableApplicationReceipt().GetAwaiter().GetResult())
    ];

    private static async Task EmbeddedExamHostRetainsTheSameDurableApplicationReceipt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        CancellationToken ct = timeout.Token;
        var certificates = await ClassroomCertificateFixture.CreateAsync(ct, ExamClusterProcessFixture.IssueTestCertificates).ConfigureAwait(false);
        await using var cluster = new ExamClusterProcessFixture(certificates);
        await cluster.PrepareAsync(ct).ConfigureAwait(false);
        await cluster.ServeAsync("witness", ct).ConfigureAwait(false);
        var standby = await cluster.ServeAsync("standby", ct).ConfigureAwait(false);
        string path = Path.Combine(cluster.DirectoryPath, "primary.json");
        await using var service = await ExamClusterService.StartAsync(path, ct).ConfigureAwait(false);
        await standby.RoleAsync("Standby", ct).ConfigureAwait(false);
        var command = new ExamReplicaCommand(Guid.NewGuid(), "Control", "open current question"u8.ToArray());
        ExamReplicaCursor receipt;
        while (true)
        {
            var status = service.GetStatus();
            try
            {
                receipt = await service.SubmitAsync(status.AuthorityEpoch, status.FencingToken, command, ct).ConfigureAwait(false);
                break;
            }
            catch (Exception error) when (!ct.IsCancellationRequested && error is HttpRequestException or OperationCanceledException)
            { await Task.Delay(100, ct).ConfigureAwait(false); }
        }
        var committed = service.ReadCommitted(receipt.Sequence);
        Check.That(committed?.RequestId == command.RequestId && committed.Payload.AsSpan().SequenceEqual(command.Payload) &&
            Success(await standby.CommandAsync("read 1", ct).ConfigureAwait(false)).GetProperty("hash").GetString() == receipt.Hash &&
            service.ReadCommitted(receipt.Sequence + 1) is null, "an embedded authority publishes only committed application bytes");
        Task<bool> administration = service.UseAsync(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
            return true;
        }, ct);
        await Task.WhenAll(service.DisposeAsync().AsTask(), service.DisposeAsync().AsTask()).ConfigureAwait(false);
        Check.That(service.Completion.IsCompletedSuccessfully && administration.IsCanceled,
            "disposing the embedded exam host drains operations and stops its lease and replication workers");
        await using var reopened = await ExamClusterService.StartAsync(path, ct).ConfigureAwait(false);
        Check.That(reopened.ReadCommitted(receipt.Sequence)?.RequestId == command.RequestId, "the stopped node released the port, lock and committed storage");
    }

    private static async Task ThreeProcessesFenceAndRecoverTheAcknowledgedExamFrontier()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(110));
        CancellationToken ct = timeout.Token;
        var pki = await ClassroomCertificateFixture.CreateAsync(ct, ExamClusterProcessFixture.IssueTestCertificates).ConfigureAwait(false);
        await using var cluster = new ExamClusterProcessFixture(pki);
        await cluster.PrepareAsync(ct).ConfigureAwait(false);
        string standbyPath = Path.Combine(cluster.DirectoryPath, "standby.json");
        var standbyConfig = ExamClusterConfiguration.ReadFile<ExamClusterConfiguration>(standbyPath);
        await using var link = new ExamWitnessLinkFixture(standbyConfig.Witness.Origin.Port);
        var routedWitness = standbyConfig.Witness with { Origin = new UriBuilder(standbyConfig.Witness.Origin) { Port = link.Start() }.Uri };
        File.WriteAllText(standbyPath, JsonSerializer.Serialize(standbyConfig with { Witness = routedWitness }, ExamClusterConfiguration.Json));
        var witness = await cluster.ServeAsync("witness", ct).ConfigureAwait(false);
        var standby = await cluster.ServeAsync("standby", ct).ConfigureAwait(false);
        var primary = await cluster.ServeAsync("primary", ct).ConfigureAwait(false);
        await CheckRoleSeparationAsync(cluster.DirectoryPath, ct).ConfigureAwait(false);
        JsonElement first = await primary.RoleAsync("Primary", ct).ConfigureAwait(false);
        await standby.RoleAsync("Standby", ct).ConfigureAwait(false);
        string payload = Convert.ToBase64String("synthetic ECG answer A"u8);
        string write = Write(first, Guid.NewGuid(), payload);
        JsonElement ack = await RetryCommandAsync(primary, write, ct).ConfigureAwait(false);
        string hash = ack.GetProperty("hash").GetString()!;
        Check.That(ack.GetProperty("sequence").GetInt64() == 1 &&
            (await RetryCommandAsync(primary, write, ct).ConfigureAwait(false)).GetProperty("hash").GetString() == hash,
            "an answered request can be retried with the same durable receipt");
        Check.That(Success(await standby.CommandAsync("read 1", ct).ConfigureAwait(false)).GetProperty("hash").GetString() == hash,
            "the standby already has the exact acknowledged answer");

        var rto = Stopwatch.StartNew();
        primary.Kill();
        JsonElement second = await standby.RoleAsync("Primary", ct).ConfigureAwait(false);
        rto.Stop();
        Check.That(rto.Elapsed <= TimeSpan.FromSeconds(5) && second.GetProperty("authorityEpoch").GetInt64() > first.GetProperty("authorityEpoch").GetInt64() &&
            second.GetProperty("fencingToken").GetInt64() > first.GetProperty("fencingToken").GetInt64(),
            $"standby receives a new, exclusive term within the five-second objective (measured {rto.Elapsed.TotalMilliseconds:F0} ms)");
        Console.WriteLine($"Exam process-termination RTO {rto.Elapsed.TotalMilliseconds:F0} ms; acknowledged answer hash preserved.");
        string next = Write(second, Guid.NewGuid(), payload);
        Check.That(!(await standby.CommandAsync(next, ct).ConfigureAwait(false)).GetProperty("ok").GetBoolean(),
            "one durable node cannot acknowledge a new answer");

        cluster.RotatePrimaryKeys();
        primary = await cluster.ServeAsync("primary", ct).ConfigureAwait(false);
        await primary.RoleAsync("Recovering", ct).ConfigureAwait(false);
        Check.That(!(await primary.CommandAsync(write, ct).ConfigureAwait(false)).GetProperty("ok").GetBoolean(),
            "restarted former primary cannot accept even a previously successful stale-term write");
        await RetryCommandAsync(primary, "requalify", ct).ConfigureAwait(false);
        await primary.RoleAsync("Standby", ct).ConfigureAwait(false);
        JsonElement ack2 = await RetryCommandAsync(standby, next, ct).ConfigureAwait(false);
        JsonElement copy = Success(await primary.CommandAsync("read 2", ct).ConfigureAwait(false));
        Check.That(copy.GetProperty("hash").GetString() == ack2.GetProperty("hash").GetString() &&
            copy.GetProperty("command").GetProperty("payload").GetString() == payload, "requalified host provides the second durable copy");

        witness.Kill();
        await Task.Delay(TimeSpan.FromMilliseconds(3200), ct).ConfigureAwait(false);
        JsonElement paused = Success(await standby.CommandAsync("status", ct).ConfigureAwait(false));
        Check.That(paused.GetProperty("role").GetString() == "Primary" && !paused.GetProperty("leaseValid").GetBoolean() &&
            !(await standby.CommandAsync(Write(second, Guid.NewGuid(), payload), ct).ConfigureAwait(false)).GetProperty("ok").GetBoolean(),
            "the incumbent retains its role but cannot acknowledge new work after lease expiry");
        standby.Kill();
        await Task.Delay(TimeSpan.FromMilliseconds(1000), ct).ConfigureAwait(false);
        Check.That(Success(await primary.CommandAsync("status", ct).ConfigureAwait(false)).GetProperty("role").GetString() != "Primary",
            "witness loss precludes automatic takeover");
        Check.That(!(await primary.CommandAsync("manual-fence missing-evidence", ct).ConfigureAwait(false)).GetProperty("ok").GetBoolean(),
            "manual fencing requires a physical isolation record");
        JsonElement manual = await RetryCommandAsync(primary, "manual-fence PoweredOff:fixture-power-record", ct).ConfigureAwait(false);
        Check.That(manual.GetProperty("role").GetString() == "ManualPrimary" &&
            !manual.GetProperty("leaseValid").GetBoolean(), "named and audited manual fencing does not waive the durable write gate");

        witness = await cluster.ServeAsync("witness", ct).ConfigureAwait(false);
        JsonElement restored = await primary.RoleAsync("Primary", ct).ConfigureAwait(false);
        Check.That(restored.GetProperty("authorityEpoch").GetInt64() > manual.GetProperty("authorityEpoch").GetInt64(),
            "restarted witness fences above the independently audited manual term");
        standby = await cluster.ServeAsync("standby", ct).ConfigureAwait(false);
        await RetryCommandAsync(standby, "requalify", ct).ConfigureAwait(false);
        var source = PhysiologyIllustrationSource.CreateForSession(Guid.NewGuid(), Guid.NewGuid(), PhysiologyIllustrationConfiguration.Default);
        byte[] checkpoint = ExamReplicaCheckpoint.Capture(source);
        await File.WriteAllBytesAsync(Path.Combine(cluster.DirectoryPath, "case.checkpoint"), checkpoint, ct).ConfigureAwait(false);
        string checkpointWrite = $"write-file {restored.GetProperty("authorityEpoch")} {restored.GetProperty("fencingToken")} {Guid.NewGuid():D} Checkpoint case.checkpoint";
        JsonElement checkpointHead = await RetryCommandAsync(primary, checkpointWrite, ct).ConfigureAwait(false);
        string finalHash = checkpointHead.GetProperty("hash").GetString()!;
        Check.That(checkpointHead.GetProperty("sequence").GetInt64() == 3 &&
            Success(await standby.CommandAsync("read 3", ct).ConfigureAwait(false)).GetProperty("hash").GetString() == finalHash,
            "both participants independently accept and retain the deterministically replayed checkpoint");
        Success(await primary.CommandAsync("backup " + Path.Combine(cluster.DirectoryPath, "primary-backup.db"), ct).ConfigureAwait(false));

        primary.Kill();
        standby.Kill();
        primary = await cluster.ServeAsync("primary", ct).ConfigureAwait(false);
        standby = await cluster.ServeAsync("standby", ct).ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(3200), ct).ConfigureAwait(false);
        await primary.RoleAsync("Recovering", ct).ConfigureAwait(false);
        await standby.RoleAsync("Recovering", ct).ConfigureAwait(false);
        Check.That(!(await primary.CommandAsync("recover 2 " + hash, ct).ConfigureAwait(false)).GetProperty("ok").GetBoolean(),
            "the hosts refuse a stale external recovery frontier");
        JsonElement fresh = await RetryCommandAsync(standby, "recover 3 " + finalHash, ct).ConfigureAwait(false);
        await RetryCommandAsync(primary, "requalify", ct).ConfigureAwait(false);
        Check.That(fresh.GetProperty("authorityEpoch").GetInt64() > restored.GetProperty("authorityEpoch").GetInt64() &&
            Success(await primary.CommandAsync("read 1", ct).ConfigureAwait(false)).GetProperty("hash").GetString() == hash,
            "cold recovery uses the external frontier and preserves history across both process and key rotations");
        Check.That((await RetryCommandAsync(standby, Write(fresh, Guid.NewGuid(), payload), ct).ConfigureAwait(false))
            .GetProperty("sequence").GetInt64() == 4, "cold-recovered hosts resume dual-durable writes");

        // Isolate only the incumbent's witness route. Its process and peer replication
        // listener remain reachable, so a TCP health check alone cannot authorize failover.
        await link.CutAsync().ConfigureAwait(false);
        JsonElement replacement = await primary.RoleAsync("Primary", ct).ConfigureAwait(false);
        JsonElement isolated = Success(await standby.CommandAsync("status", ct).ConfigureAwait(false));
        Check.That(replacement.GetProperty("fencingToken").GetInt64() > fresh.GetProperty("fencingToken").GetInt64() &&
            !isolated.GetProperty("leaseValid").GetBoolean() && isolated.GetProperty("head").GetProperty("sequence").GetInt64() == 4 &&
            !(await standby.CommandAsync(Write(fresh, Guid.NewGuid(), payload), ct).ConfigureAwait(false)).GetProperty("ok").GetBoolean(),
            "an isolated but reachable former leader cannot acknowledge stale writes after witnessed takeover");
    }

    private static string Write(JsonElement leader, Guid requestId, string payload) =>
        $"write {leader.GetProperty("authorityEpoch").GetInt64()} {leader.GetProperty("fencingToken").GetInt64()} {requestId:D} Answer {payload}";

    private static async Task CheckRoleSeparationAsync(string directory, CancellationToken ct)
    {
        var config = ExamClusterConfiguration.Load(Path.Combine(directory, "witness.json"));
        var standby = ExamClusterConfiguration.Load(Path.Combine(directory, "standby.json"));
        using var peerTransport = new ExamClusterTransport(standby);
        using var peer = peerTransport.Connect(config.Primary);
        using var authorized = await peer.GetAsync("/v1/exam/peer/status", ct).ConfigureAwait(false);
        authorized.EnsureSuccessStatusCode();
        using var transport = new ExamClusterTransport(config);
        using var witnessAsClient = transport.Connect(config.Primary);
        try
        {
            using var response = await witnessAsClient.GetAsync("/v1/exam/peer/status", ct).ConfigureAwait(false);
            Check.That(response.StatusCode == HttpStatusCode.Forbidden, "the witness cannot read application replication data");
        }
        // Schannel can report a rejected client certificate during the first HTTP read.
        catch (HttpRequestException error) when (error.HttpRequestError is HttpRequestError.SecureConnectionError or
            HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded || error.InnerException is AuthenticationException or
            IOException { InnerException: SocketException { SocketErrorCode: SocketError.ConnectionAborted or SocketError.ConnectionReset } } ||
            OperatingSystem.IsWindows() && error.InnerException is IOException
            {
                InnerException: Win32Exception { NativeErrorCode: unchecked((int)0x80090327) } // SEC_E_CERT_UNKNOWN
            })
        { }
        var badPin = config.Witness with { CertificateSha256 = [new string('f', 64)] };
        try
        {
            using var unexpected = new ExamClusterTransport(config with { Witness = badPin });
            throw new InvalidOperationException("The local exam certificate was used under an unregistered pin.");
        }
        catch (CryptographicException) { }
    }

    private static JsonElement Success(JsonElement reply)
    {
        Check.That(reply.GetProperty("ok").GetBoolean(), "exam command failed: " + reply);
        return reply.GetProperty("result");
    }

    private static async Task<JsonElement> RetryCommandAsync(ExamClusterProcessFixture.Child child, string command, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        JsonElement reply;
        do
        {
            reply = await child.CommandAsync(command, ct).ConfigureAwait(false);
            if (reply.GetProperty("ok").GetBoolean()) { return reply.GetProperty("result"); }
            await Task.Delay(220, ct).ConfigureAwait(false);
        } while (elapsed.Elapsed < TimeSpan.FromSeconds(12));
        return Success(reply);
    }
}
