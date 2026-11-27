// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Sockets;

namespace Monitor.Specs;

// This relay carries only the test primary's TLS connection to the witness.
// Stopping it closes established connections as well as the route for new probes.
internal sealed class ExamWitnessLinkFixture(int targetPort) : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private Task? _run;
    private readonly List<Task> _connections = [];

    internal int Start()
    {
        _listener.Start();
        _run = RunAsync();
        return ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                _connections.RemoveAll(task => task.IsCompletedSuccessfully);
                _connections.Add(ForwardAsync(client));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ForwardAsync(TcpClient incoming)
    {
        using (incoming)
        using (var outgoing = new TcpClient())
        using (var connection = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            try
            {
                await outgoing.ConnectAsync(IPAddress.Loopback, targetPort, connection.Token).ConfigureAwait(false);
                using var source = incoming.GetStream();
                using var destination = outgoing.GetStream();
                Task requests = source.CopyToAsync(destination, connection.Token);
                Task replies = destination.CopyToAsync(source, connection.Token);
                await Task.WhenAny(requests, replies).ConfigureAwait(false);
                await connection.CancelAsync().ConfigureAwait(false);
                await Task.WhenAll(requests, replies).ConfigureAwait(false);
            }
            catch (Exception error) when (error is SocketException or IOException or OperationCanceledException) { }
        }
    }

    internal async Task CutAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_run is not null) { await _run.ConfigureAwait(false); }
        _listener.Stop();
        await Task.WhenAll(_connections).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await CutAsync().ConfigureAwait(false);
        _stop.Dispose();
    }
}
