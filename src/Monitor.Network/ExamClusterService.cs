// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Network;

/// <summary>Runs one provisioned exam host for a trusted, in-process exam application.</summary>
public sealed class ExamClusterService : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly object _gate = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _operations;
    private Task? _stopping;

    private ExamClusterService(ExamClusterNode node, ExamReplicaStore store, DateTime certificateExpiresUtc, CancellationTokenSource stop)
    {
        Node = node;
        Store = store;
        CertificateExpiresUtc = certificateExpiresUtc;
        _stop = stop;
    }

    internal ExamClusterNode Node { get; }
    internal ExamReplicaStore Store { get; }
    internal DateTime CertificateExpiresUtc { get; }

    /// <summary>Completes after the host stops serving, drains outstanding calls and releases its local database.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>Starts the replication listener. Cancel the lifetime token or dispose the service to stop it.</summary>
    public static Task<ExamClusterService> StartAsync(string configurationPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return StartAsync(ExamClusterConfiguration.Load(configurationPath), cancellationToken);
    }

    internal static async Task<ExamClusterService> StartAsync(ExamClusterConfiguration config, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (config.IsWitness) { throw new ArgumentException("An embedded exam service requires a primary or standby host configuration.", nameof(config)); }
        var ready = new TaskCompletionSource<ExamClusterService>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task lifetime = RunHostAsync(config, ready, cancellationToken);
        await Task.WhenAny(ready.Task, lifetime).ConfigureAwait(false);
        if (!ready.Task.IsCompleted)
        {
            // Propagate startup failures after the listener, keys and file lock have been released.
            await lifetime.ConfigureAwait(false);
            throw new InvalidOperationException("The exam host stopped during startup.");
        }
        var service = await ready.Task.ConfigureAwait(false);
        service.Completion = lifetime;
        return service;
    }

    private static async Task RunHostAsync(ExamClusterConfiguration config, TaskCompletionSource<ExamClusterService> ready,
        CancellationToken cancellationToken)
    {
        using var tls = new ExamClusterTransport(config);
        using var processLock = config.AcquireProcessLock();
        using var keys = ExamDataKeyRing.Open(config.DataKeyRing!);
        using var store = new ExamReplicaStore(config.Database, config.ClusterId, config.NodeId, keys);
        using var peer = tls.Connect(config.OtherHost);
        using var witness = tls.Connect(config.Witness);
        using var node = new ExamClusterNode(config.ClusterId, config.NodeId, config.OtherHost.Id,
            config.NodeId == config.Primary.Id, store, peer, witness);
        await using var app = tls.BuildListener();
        node.MapPeerEndpoints(app);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, app.Lifetime.ApplicationStopping);
        var service = new ExamClusterService(node, store, tls.CertificateExpiresUtc, stop);
        await app.StartAsync(stop.Token).ConfigureAwait(false);
        try
        {
            stop.Token.ThrowIfCancellationRequested();
            Task workers = node.RunAsync(stop.Token);
            ready.SetResult(service);
            await workers.ConfigureAwait(false);
        }
        finally
        {
            try { await service.BeginStop().ConfigureAwait(false); }
            finally
            {
                try { await app.StopAsync(CancellationToken.None).ConfigureAwait(false); }
                finally { await service._drained.Task.ConfigureAwait(false); }
            }
        }
    }

    public ExamClusterNodeStatus GetStatus()
    {
        lock (_gate)
        {
            ThrowIfStopping();
            return Node.GetStatus();
        }
    }

    /// <summary>Returns a durable receipt only after both hosts accept the same transaction under the supplied fence.</summary>
    public Task<ExamReplicaCursor> SubmitAsync(long authorityEpoch, long fencingToken, ExamReplicaCommand command,
        CancellationToken cancellationToken = default) =>
        UseAsync(token => Node.WriteAsync(authorityEpoch, fencingToken, command, token), cancellationToken);

    internal async Task<T> UseAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ThrowIfStopping();
            _operations++;
        }
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            stop.Token.ThrowIfCancellationRequested();
            return await operation(stop.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _operations--;
                if (_operations == 0 && _stopping is not null) { _drained.TrySetResult(); }
            }
        }
    }

    /// <summary>Reads one locally committed command, or null if its sequence has not been committed on this host.</summary>
    public ExamReplicaCommand? ReadCommitted(long sequence)
    {
        lock (_gate)
        {
            ThrowIfStopping();
            return Store.Read(sequence)?.Command;
        }
    }

    private void ThrowIfStopping() => ObjectDisposedException.ThrowIf(_stopping is not null || _stop.IsCancellationRequested, this);

    private Task BeginStop()
    {
        lock (_gate)
        {
            if (_stopping is null)
            {
                _stopping = _stop.CancelAsync();
                if (_operations == 0) { _drained.TrySetResult(); }
            }
            return _stopping;
        }
    }

    public ValueTask DisposeAsync()
    {
        BeginStop();
        return new(Completion);
    }
}
