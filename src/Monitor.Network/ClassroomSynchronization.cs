// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Security.Authentication;
using System.Threading.Channels;
using Monitor.Domain.Continuity;
using Monitor.Simulation.Acquisition;

namespace Monitor.Network;

public sealed class ClassroomSynchronization
{
    private readonly object _gate = new();
    private readonly ClassroomStudentClient _client;
    private readonly ClassroomClockCalibration _clock;
    private readonly TimeProvider _time;
    private readonly ConnectionHealthStateMachine _health;
    private readonly HashSet<Guid> _planes;
    private readonly Channel<ConnectionHealthState> _changes = Channel.CreateBounded<ConnectionHealthState>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private long _fastSinceNs;
    private long? _lastPresentationClientNs;
    private long _lastPresentationHostNs;
    private int _monitoring;

    internal ClassroomSynchronization(ClassroomStudentClient client, ClassroomClockCalibration clock, TimeProvider time,
        IReadOnlyList<Guid> requiredPlaneIds, bool sessionRunning)
    {
        _client = client;
        _clock = clock;
        _time = time;
        _planes = [.. requiredPlaneIds];
        _fastSinceNs = NowNs();
        _health = ConnectionHealthStateMachine.Start(RealtimeConnectionHealthProfile.Default, _fastSinceNs, sessionRunning, requiredPlaneIds);
    }

    public static async Task<ClassroomSynchronization> StartAsync(ClassroomStudentClient client, IReadOnlyList<Guid> requiredPlaneIds,
        bool sessionRunning, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var calibration = new ClassroomClockCalibration();
        for (int i = 0; i < ClassroomClockCalibration.InitialSampleCount; i++)
        {
            calibration.Add(await client.SynchronizeClockAsync(cancellationToken).ConfigureAwait(false));
        }
        return new ClassroomSynchronization(client, calibration, TimeProvider.System, requiredPlaneIds, sessionRunning);
    }

    public ClassroomClockEstimate Estimate { get { lock (_gate) { return _clock.Estimate; } } }
    public ConnectionHealthState Health { get { lock (_gate) { return _health.CaptureState(); } } }
    public ChannelReader<ConnectionHealthState> HealthChanges => _changes.Reader;

    public void RecordHeartbeat(bool sessionRunning)
    {
        lock (_gate)
        {
            long now = NowNs();
            _health.Advance(now, sessionRunning);
            Notify(_health.RecordServerActivity(now));
        }
    }

    public void RecordBlock(WaveformEnvelope acceptedBlock)
    {
        ArgumentNullException.ThrowIfNull(acceptedBlock);
        lock (_gate)
        {
            long now = NowNs();
            foreach (WaveformPlane plane in acceptedBlock.Planes)
            {
                if (_planes.Contains(plane.ChannelId)) { _health.RecordPlaneProgress(plane.ChannelId, acceptedBlock.StreamEpoch, acceptedBlock.BlockSequence, now); }
            }
            Notify(_health.CaptureState());
        }
    }

    public void RequestRapidCalibration()
    {
        lock (_gate) { _fastSinceNs = NowNs(); }
    }

    public void RecordFailure(ImmediateTransportFailure failure)
    {
        lock (_gate)
        {
            _fastSinceNs = NowNs();
            Notify(_health.RecordImmediateFailure(failure, _fastSinceNs));
        }
    }

    public void BeginRelocking()
    {
        lock (_gate)
        {
            long now = NowNs();
            Notify(_health.BeginRelocking(now));
            _fastSinceNs = now;
        }
    }

    public void CompleteRelocking(WaveformEnvelope verifiedBoundary)
    {
        ArgumentNullException.ThrowIfNull(verifiedBoundary);
        lock (_gate)
        {
            long now = NowNs();
            if (_health.ConnectionState != ConnectionState.Relocking ||
                !_planes.IsSubsetOf(verifiedBoundary.Planes.Select(plane => plane.ChannelId)))
            {
                throw new InvalidOperationException("Relock requires a verified boundary for each required plane.");
            }
            foreach (Guid plane in _planes)
            {
                _health.ResetPlaneAfterResync(plane, verifiedBoundary.StreamEpoch, verifiedBoundary.BlockSequence, now);
            }
            _lastPresentationClientNs = null;
            _fastSinceNs = now;
            Notify(_health.CompleteRelocking(now));
        }
    }

    // Slew an established presentation mapping by at most 1 ms per elapsed second.
    public long PresentationHostTimeNs()
    {
        lock (_gate)
        {
            long now = NowNs();
            long estimated = Math.Max(0, _clock.Estimate.HostTimeAt(now));
            if (_lastPresentationClientNs is long previous)
            {
                long elapsed = now - previous;
                long nominal = checked(_lastPresentationHostNs + elapsed);
                long maximumSlew = elapsed / 1000;
                estimated = checked(nominal + Math.Clamp(estimated - nominal, -maximumSlew, maximumSlew));
            }
            _lastPresentationClientNs = now;
            _lastPresentationHostNs = estimated;
            return estimated;
        }
    }

    public async Task MonitorAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _monitoring, 1) != 0) { throw new InvalidOperationException("Clock monitoring is already active."); }
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task probe = ProbeAsync(shutdown.Token);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250), _time);
            while (await timer.WaitForNextTickAsync(shutdown.Token).ConfigureAwait(false))
            {
                if (probe.IsCompleted) { await probe.ConfigureAwait(false); break; }
                lock (_gate) { Notify(_health.Advance(NowNs(), _health.SessionRunning)); }
            }
        }
        finally
        {
            await shutdown.CancelAsync().ConfigureAwait(false);
            try { await probe.ConfigureAwait(false); }
            finally { Volatile.Write(ref _monitoring, 0); }
        }
    }

    private async Task ProbeAsync(CancellationToken cancellationToken)
    {
        long lastProbe = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _time);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            bool probe;
            lock (_gate)
            {
                long now = NowNs();
                probe = now - _fastSinceNs < 30_000_000_000 || now - lastProbe >= 5_000_000_000;
            }
            try
            {
                // A control request continues to measure connectivity while the simulation is paused.
                if (probe)
                {
                    ClassroomClockSample sample = await _client.SynchronizeClockAsync(cancellationToken).ConfigureAwait(false);
                    lock (_gate)
                    {
                        ClassroomClockEstimate estimate = _clock.Add(sample);
                        lastProbe = NowNs();
                        if (estimate.RequiresResynchronization) { _fastSinceNs = lastProbe; }
                        Notify(_health.RecordServerActivity(lastProbe));
                    }
                }
                else
                {
                    _ = await _client.GetSessionAsync(cancellationToken).ConfigureAwait(false);
                    lock (_gate) { Notify(_health.RecordServerActivity(NowNs())); }
                }
            }
            catch (HttpRequestException error)
            {
                RecordFailure(error.HttpRequestError == HttpRequestError.SecureConnectionError ?
                    ImmediateTransportFailure.TlsFailure : ImmediateTransportFailure.IoError);
                if (error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { throw; }
            }
            catch (AuthenticationException) { RecordFailure(ImmediateTransportFailure.TlsFailure); throw; }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                RecordFailure(ImmediateTransportFailure.IoError);
            }
        }
    }

    private void Notify(ConnectionHealthState state) => _changes.Writer.TryWrite(state);
    private long NowNs() => checked((long)((Int128)_time.GetTimestamp() * 1_000_000_000 / _time.TimestampFrequency));
}
