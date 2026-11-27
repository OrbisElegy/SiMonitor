// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Monitor.Domain.Continuity;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;

namespace Monitor.Network;

internal static partial class Program
{
    private static async Task ServeLessonAsync(ClassroomHost host, ClassroomAdmission admission, Guid teacher, IPAddress address,
        Uri origin, string fingerprint, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Console.WriteLine("教师控制台：start | pause | resume | speed N/D | status | operator UUID|off | invite FILE [UUID] | pair FILE UUID | close | reopen [SECONDS] | remove UUID | act ID JSON | rate BPM SECONDS [STUDENT_UUID] | schedule SECONDS ID,... | cancel UUID | fork UUID | return UUID Archive|Discard | end");
        Task pump = host.RunIllustrationAsync(stop.Token);
        Task commands = ReadCommandsAsync(TeacherCommandAsync, stop.Token);
        Task discovery = Environment.GetEnvironmentVariable("SIMONITOR_DISCOVERY_NAME") is { Length: > 0 } name
            ? AdvertiseAsync(name) : Task.CompletedTask;
        try
        {
            Task finished = await Task.WhenAny(pump, commands).ConfigureAwait(false);
            await finished.ConfigureAwait(false);
            if (!stop.IsCancellationRequested) { await pump.ConfigureAwait(false); }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await host.EndSessionAsync().ConfigureAwait(false);
            try { await Task.WhenAll(pump, commands, discovery).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }

        async Task AdvertiseAsync(string name)
        {
            try { await ClassroomDiscovery.AdvertiseAsync(address, new ClassroomDiscoveredHost(name, origin, fingerprint), stop.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is System.Net.Sockets.SocketException or ArgumentException or IOException)
            { Console.Error.WriteLine($"组播发现不可用，请使用邀请中的 HTTPS 地址：{error.Message}"); }
        }

        async Task TeacherCommandAsync(string line)
        {
            string[] words = line.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            switch (words)
            {
                case ["start"]: host.StartClass(); break;
                case ["pause"]: host.SchedulePlayback(false); break;
                case ["resume"]: host.SchedulePlayback(true); break;
                case ["speed", var rate]:
                    string[] parts = rate.Split('/');
                    if (parts.Length != 2) { throw new FormatException("speed N/D"); }
                    host.SchedulePlayback(true, Integer(parts[0], 1, 16), Integer(parts[1], 1, 16)); break;
                case ["status"]:
                    Console.WriteLine(JsonSerializer.Serialize(new { classroom = host.CurrentTelemetrySnapshot().Timeline, host.Members, host.StartingSet, control = host.ReadControlSnapshot() }));
                    break;
                case ["operator", var who]: host.DesignateOperator(who == "off" ? null : Guid.Parse(who)); break;
                case ["close"]: admission.CloseInvitations(); break;
                case ["reopen"]: admission.ReopenInvitations(); break;
                case ["reopen", var seconds]: admission.ReopenInvitations(admission.Clock.GetUtcNow().AddSeconds(Integer(seconds, 1, 86400))); break;
                case ["remove", var seat]: host.RemoveMember(Guid.Parse(seat)); break;
                case ["invite", var file]: WriteInvitations(file, origin, admission, 1); break;
                case ["invite" or "pair", var file, var id]:
                    var student = Guid.Parse(id);
                    using (var output = new StreamWriter(CreateInvitationFile(file)))
                    {
                        string code = admission.IssueInvitation(student);
                        output.WriteLine(words[0] == "pair" ? ClassroomPairingTicket.Create(origin, admission.SessionId, fingerprint, code).Encode()
                            : $"{origin} {student:D} {code}");
                    }
                    break;
                case ["act", var actionId, ..]:
                    var intent = CreateIntent(admission.SessionId, admission.InstanceId, teacher, actionId, host.ReadControlSnapshot().ProjectionRevision, line);
                    Console.WriteLine(JsonSerializer.Serialize(host.SubmitTeacherAction(intent), ClassroomControlWire.Json));
                    break;
                case ["rate", ..] when words.Length is 3 or 4:
                    ClassroomHost target = words.Length == 4 ? host.GetPrivateHost(Guid.Parse(words[3])) : host;
                    var current = target.ReadConfiguration();
                    var content = current.Active with { HeartRateBpm = Integer(words[1], 30, 240) };
                    Guid[] recipients = target.Members.Where(member => member.Prepared).Select(member => member.ClientId).ToArray();
                    var revision = target.ScheduleConfiguration(Guid.NewGuid(), current.ActiveRevision,
                        checked(target.CurrentWaveformFrontierNs + Integer(words[2], 1, 3600) * 1_000_000_000L), content, recipients, recipients.Length);
                    Console.WriteLine($"Scheduled {revision.ChangeId:D}");
                    break;
                case ["schedule", var seconds, var actions]:
                    var snapshot = host.ReadControlSnapshot();
                    Guid[] required = host.Members.Where(member => member.Prepared).Select(member => member.ClientId).ToArray();
                    var change = host.ScheduleActionPolicy(Guid.NewGuid(), snapshot.ConfigurationRevision,
                        checked(host.CurrentWaveformFrontierNs + Integer(seconds, 1, 3600) * 1_000_000_000L), actions.Split(','), required, required.Length);
                    Console.WriteLine($"Scheduled {change.ChangeId:D}");
                    break;
                case ["cancel", var id]: Console.WriteLine(host.CancelActionPolicy(Guid.Parse(id))); break;
                case ["fork", var id]: Console.WriteLine(host.SchedulePrivateBranch(Guid.Parse(id))); break;
                case ["return", var id, var mode] when Enum.TryParse(mode, true, out ClassroomBranchDisposition disposition):
                    Console.WriteLine(host.ScheduleSharedReturn(Guid.Parse(id), disposition)); break;
                case ["end"]: await stop.CancelAsync().ConfigureAwait(false); break;
                default: throw new ArgumentException("未知教师命令。");
            }
        }
    }

    private static async Task ReadCommandsAsync(Func<string, Task> execute, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // Console.In can implement ReadLineAsync synchronously on a terminal.
            string? line = await Task.Run(Console.ReadLine, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); return; }
            if (string.IsNullOrWhiteSpace(line)) { continue; }
            try { await execute(line.Trim()).ConfigureAwait(false); }
            catch (Exception error) when (error is ArgumentException or FormatException or InvalidDataException or IOException or
                InvalidOperationException or JsonException or HttpRequestException)
            { Console.Error.WriteLine(error.Message); }
        }
    }

    private static ClassroomActionIntent CreateIntent(Guid session, Guid instance, Guid actor, string action, ulong revision, string line)
    {
        string[] parts = line.Split(' ', 3, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) { throw new ArgumentException("act ACTION_ID JSON"); }
        using var doc = JsonDocument.Parse(parts[2], new JsonDocumentOptions { MaxDepth = 16 });
        var id = Guid.NewGuid();
        long now = (long)((Int128)System.Diagnostics.Stopwatch.GetTimestamp() * 1_000_000_000 / System.Diagnostics.Stopwatch.Frequency);
        return new("LearnerActionIntent@1", id, $"action.{id:N}", session, actor, instance, action,
            revision, now, "Console.Command", null, doc.RootElement.Clone());
    }

    private static async Task ObserveLessonAsync(ClassroomStudentClient shared, int count, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var input = Channel.CreateBounded<string>(16);
        Task reader = ReadCommandsAsync(line => input.Writer.WriteAsync(line, stop.Token).AsTask(), stop.Token);
        var sharedManifest = await shared.GetSessionAsync(stop.Token).ConfigureAwait(false);
        int remaining = count;
        try
        {
            while (remaining > 0)
            {
                ClassroomBranchAssignment assignment = await shared.GetBranchAssignmentAsync(stop.Token).ConfigureAwait(false);
                Guid instance = ActiveInstance(assignment);
                using ClassroomStudentClient? branch = instance == sharedManifest.InstanceId ? null :
                    await shared.ConnectAssignedBranchAsync(stop.Token).ConfigureAwait(false);
                var active = branch ?? shared;
                using var selected = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                await active.NegotiateAsync(await active.GetSessionAsync(selected.Token).ConfigureAwait(false), null, selected.Token).ConfigureAwait(false);
                Task observe = ObserveInstanceAsync(active, remaining, input.Reader, () => Interlocked.Decrement(ref remaining), selected.Token);
                Task watch = WatchAssignmentAsync(instance, selected.Token);
                try
                {
                    await (await Task.WhenAny(observe, watch).ConfigureAwait(false)).ConfigureAwait(false);
                }
                catch (Exception error) when (error is HttpRequestException or IOException or System.Net.WebSockets.WebSocketException)
                {
                    if (ActiveInstance(await shared.GetBranchAssignmentAsync(stop.Token).ConfigureAwait(false)) == instance) { throw; }
                }
                finally
                {
                    await selected.CancelAsync().ConfigureAwait(false);
                    try { await Task.WhenAll(observe, watch).ConfigureAwait(false); }
                    catch (Exception error) when (selected.IsCancellationRequested && error is OperationCanceledException or
                        HttpRequestException or IOException or System.Net.WebSockets.WebSocketException)
                    { }
                }
                if (remaining > 0) { Console.WriteLine("学生实例已切换，正在读取新实例的权威状态和预卷。"); }
            }
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            input.Writer.TryComplete();
            try { await reader.ConfigureAwait(false); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }

        async Task WatchAssignmentAsync(Guid current, CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try
                {
                    if (ActiveInstance(await shared.GetBranchAssignmentAsync(token).ConfigureAwait(false)) != current) { return; }
                    if (current != sharedManifest.InstanceId && shared.RefreshDue) { await shared.RefreshAccessAsync(token).ConfigureAwait(false); }
                }
                catch (Exception error) when (!token.IsCancellationRequested && IsTransientStreamError(error)) { }
            }
        }

        static Guid ActiveInstance(ClassroomBranchAssignment a) =>
            a.State is "Detached" or "ArchivePending" or "DiscardPending" or "ReturnRejected" ? a.InstanceId : a.SharedInstanceId;
    }

    private static async Task ObserveInstanceAsync(ClassroomStudentClient client, int count, ChannelReader<string> input,
        Action delivered, CancellationToken cancellationToken)
    {
        ClassroomManifest manifest = await client.GetSessionAsync(cancellationToken).ConfigureAwait(false);
        ClassroomTelemetrySnapshot snapshot = await client.GetTelemetryAsync(cancellationToken).ConfigureAwait(false);
        Guid[] planes = Enumerable.Range(0, 7).Select(PhysiologyIllustrationSource.ChannelId).ToArray();
        var sync = await ClassroomSynchronization.StartAsync(client, planes, snapshot.Timeline.RunState == "Running", cancellationToken).ConfigureAwait(false);
        await client.ReportPreparedAsync(!sync.Estimate.RequiresResynchronization, sync.Estimate.RecommendedPlayoutDelayNs, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Session {manifest.SessionId:D}, instance {manifest.InstanceId:D}, student {manifest.StudentId:D}, clock RTT {sync.Estimate.RoundTripP95Ns} ns.");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ClassroomTimebase timeline = snapshot.Timeline;
        ClassroomTimebase? pendingTimeline = snapshot.PendingTimeline;
        WaveformRetainedBlock? lastDelivered = null;
        using var configuration = new ClassroomConfigurationObserver(client);
        object receiverGate = new();
        CancellationTokenSource? receiving = null;
        Task samples = ReceiveBlocksAsync();
        Task monitor = sync.MonitorAsync(stop.Token);
        Task feed = ReadFeedAsync();
        Task health = ObserveHealthAsync();
        Task commands = ConsumeCommandsAsync();
        try
        {
            await (await Task.WhenAny(samples, monitor, feed, health, commands).ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(samples, monitor, feed, health, commands).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }

        async Task ReceiveBlocksAsync()
        {
            ulong cursor = manifest.NextBlockSequence;
            int received = 0, retries = 0;
            while (received < count)
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                try
                {
                    ClassroomWaveformSubscription subscription;
                    try
                    {
                        if (retries != 0) { subscription = await client.ResumeAsync(manifest, cursor, stop.Token).ConfigureAwait(false); }
                        else if (manifest.NextBlockSequence - manifest.OldestBlockSequence >= 5 && snapshot.Timeline.RunState == "Running")
                        {
                            var join = await client.JoinAtFutureBoundaryAsync(manifest, 5, stop.Token).ConfigureAwait(false);
                            subscription = join.Subscription;
                            cursor = join.NextBlockSequence;
                        }
                        else { subscription = await client.SubscribeAsync(manifest, cursor, stop.Token).ConfigureAwait(false); }
                    }
                    catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.Conflict)
                    {
                        var recovery = await client.ResynchronizeWaveformsAsync(manifest, cursor, stop.Token).ConfigureAwait(false);
                        subscription = recovery.Subscription;
                        cursor = recovery.NextBlockSequence;
                        Console.Error.WriteLine($"保留窗口已过，重新同步到块 {cursor}；缺失区间不作补播。");
                    }
                    await using (subscription.ConfigureAwait(false))
                    {
                        lock (receiverGate) { receiving = attempt; }
                        bool relocking = retries != 0;
                        if (relocking) { sync.BeginRelocking(); }
                        while (received < count)
                        {
                            var block = await subscription.ReceiveAsync(attempt.Token).ConfigureAwait(false) ?? throw new EndOfStreamException("教师已关闭波形流。");
                            if (relocking) { sync.CompleteRelocking(block); relocking = false; }
                            else { sync.RecordBlock(block); }
                            cursor = subscription.NextBlockSequence;
                            long end = checked(block.StartSimTimeNs + block.DurationNs);
                            ClassroomTimebase mapping = Volatile.Read(ref pendingTimeline) is { } next && next.SimAnchorNs <= block.StartSimTimeNs
                                ? next : Volatile.Read(ref timeline);
                            long remainingNs = mapping.PresentationDeadlineNs(end) - sync.PresentationHostTimeNs();
                            if (remainingNs > 0) { await Task.Delay(TimeSpan.FromTicks(remainingNs / 100), stop.Token).ConfigureAwait(false); }
                            Volatile.Write(ref lastDelivered, subscription.ReadBufferedBlocks(block.BlockSequence, 1).Blocks.Single());
                            received++;
                            delivered();
                            retries = 0;
                            Console.WriteLine(FormattableString.Invariant($"Block {block.BlockSequence} at {block.StartSimTimeNs} ns, {block.Planes.Count} planes."));
                            long observedSim = Math.Max(block.StartSimTimeNs, mapping.SimTimeAt(sync.PresentationHostTimeNs()));
                            await configuration.ObserveDeliveredAsync(block, observedSim, stop.Token).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception error) when (!stop.IsCancellationRequested && IsTransientStreamError(error))
                {
                    sync.RecordFailure(ImmediateTransportFailure.IoError);
                    if (++retries > 3) { throw new IOException($"波形连接中断，首个未交付块为 {cursor}。", error); }
                    await Task.Delay(TimeSpan.FromSeconds(1 << (retries - 1)), stop.Token).ConfigureAwait(false);
                }
                finally { lock (receiverGate) { receiving = null; } }
            }
        }

        async Task ReadFeedAsync()
        {
            ulong cursor = snapshot.NextEventSequence;
            long lastStatus = 0;
            bool unsynchronized = sync.Estimate.RequiresResynchronization;
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var page = await client.ReadEventsAsync(manifest, cursor, stop.Token).ConfigureAwait(false);
                    foreach (var item in page.Events)
                    {
                        if (item.GetProperty("event_type").GetString() == "monitoring.numeric.published")
                        {
                            var numeric = item.GetProperty("payload");
                            string id = numeric.GetProperty("measurement_channel_ref").GetProperty("parameter_id").GetString()!;
                            if (id == "HR.ECG")
                            { Console.WriteLine($"HR.ECG {numeric.GetProperty("quality").GetProperty("state")} {numeric.GetProperty("quantity")}"); }
                        }
                    }
                    cursor = page.NextSequence;
                    long now = Environment.TickCount64;
                    if (now - lastStatus >= 1000 || lastStatus == 0)
                    {
                        var telemetry = await client.GetTelemetryAsync(stop.Token).ConfigureAwait(false);
                        Volatile.Write(ref timeline, telemetry.Timeline);
                        Volatile.Write(ref pendingTimeline, telemetry.PendingTimeline);
                        sync.RecordHeartbeat(telemetry.Timeline.RunState == "Running");
                        if (Volatile.Read(ref lastDelivered) is { } last)
                        {
                            var clock = sync.Estimate;
                            var state = sync.Health;
                            var report = new ClassroomReceiverProgress(manifest.StudentId, manifest.SessionId, manifest.InstanceId, client.Negotiation!.AuthorityEpoch,
                                manifest.StreamEpoch, last.BlockSequence, last.StartSimTimeNs, last.ContentSha256, last.ConfigurationRevision, cursor,
                                clock.HostOffsetNs, clock.UncertaintyNs, state.ConnectionState.ToString(), state.RequiredPlanes.Max(plane => plane.Health).ToString());
                            await client.ReportReceiverProgressAsync(report, stop.Token).ConfigureAwait(false);
                        }
                        await configuration.PollAsync(stop.Token).ConfigureAwait(false);
                        if (sync.Estimate.RequiresResynchronization != unsynchronized)
                        {
                            unsynchronized = sync.Estimate.RequiresResynchronization;
                            await client.ReportPreparedAsync(!unsynchronized, Math.Clamp(sync.Estimate.RecommendedPlayoutDelayNs, 100_000_000, 1_500_000_000), stop.Token).ConfigureAwait(false);
                        }
                        if (client.RefreshDue) { await client.RefreshAccessAsync(stop.Token).ConfigureAwait(false); }
                        lastStatus = now;
                    }
                }
                catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.Conflict)
                {
                    cursor = (await client.GetTelemetryAsync(stop.Token).ConfigureAwait(false)).NextEventSequence;
                    Console.Error.WriteLine("控制事件已超出保留窗口，已读取当前投影。");
                }
                catch (Exception error) when (!stop.IsCancellationRequested && IsTransientStreamError(error))
                { await Task.Delay(TimeSpan.FromMilliseconds(500), stop.Token).ConfigureAwait(false); }
            }
        }

        async Task ObserveHealthAsync()
        {
            (ConnectionState, SamplePlaneProgressHealth)? previous = null;
            await foreach (var state in sync.HealthChanges.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                var current = (state.ConnectionState, state.RequiredPlanes.Max(plane => plane.Health));
                if (current == previous) { continue; }
                previous = current;
                if (state.SessionRunning && current.Item2 == SamplePlaneProgressHealth.Disconnected)
                {
                    lock (receiverGate) { receiving?.Cancel(); }
                }
                Console.Error.WriteLine($"课堂连接 {current.ConnectionState}，采样通道 {current.Item2}。");
            }
        }

        async Task ConsumeCommandsAsync()
        {
            await foreach (string command in input.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                try { await StudentCommandAsync(command).ConfigureAwait(false); }
                catch (Exception error) when (error is ArgumentException or FormatException or InvalidDataException or
                    InvalidOperationException or JsonException or HttpRequestException or IOException)
                { Console.Error.WriteLine(error.Message); }
            }
        }

        async Task StudentCommandAsync(string line)
        {
            string[] words = line.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            switch (words)
            {
                case ["status"]:
                    Console.WriteLine(JsonSerializer.Serialize(await client.GetControlSnapshotAsync(stop.Token).ConfigureAwait(false)));
                    break;
                case ["act", var action, ..]:
                    var projection = await client.GetActionProjectionAsync(action, stop.Token).ConfigureAwait(false);
                    var intent = CreateIntent(manifest.SessionId, manifest.InstanceId, manifest.StudentId, action, projection.ProjectionRevision, line);
                    Console.WriteLine(JsonSerializer.Serialize(await client.SubmitActionAsync(intent, stop.Token).ConfigureAwait(false), ClassroomControlWire.Json));
                    break;
                case ["artifacts"]: Console.WriteLine(JsonSerializer.Serialize(await client.ListArtifactsAsync(stop.Token).ConfigureAwait(false))); break;
                case ["download", var hash, var path]:
                    var items = await client.ListArtifactsAsync(stop.Token).ConfigureAwait(false);
                    var descriptor = items.Single(item => item.Sha256 == hash);
                    byte[] bytes = await client.DownloadArtifactAsync(descriptor, stop.Token).ConfigureAwait(false);
                    await using (var file = CreateInvitationFile(path)) { await file.WriteAsync(bytes, stop.Token).ConfigureAwait(false); }
                    break;
                default: throw new ArgumentException("学生命令：status | act ACTION_ID JSON | artifacts | download SHA256 FILE");
            }
        }
    }
}
