// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Presentation;

namespace Monitor.Infrastructure.Presentation;

public sealed class SweepFrameWorker : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SweepFrameWorkPump _pump;
    private readonly SemaphoreSlim _wake;
    private readonly Task _worker;
    private TaskCompletionSource _idle = NewSignal();
    private ulong _latest;
    private bool _stopping;
    private string? _lastFailure;

    public SweepFrameWorker(int maximumSamples, int maximumSegments)
    {
        _pump = new(maximumSamples, maximumSegments);
        _wake = new(0, 1);
        _idle.SetResult();
        _worker = Task.Run(RunAsync);
    }

    public ulong Enqueue(SweepFrameReconstructionInput input)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            ulong generation = _pump.Enqueue(input);
            _latest = generation;
            if (_idle.Task.IsCompleted) { _idle = NewSignal(); }
            Signal();
            return generation;
        }
    }

    // Covers requests admitted before this call; later producers may start more work.
    public Task WaitForIdleAsync()
    {
        lock (_gate) { return _idle.Task; }
    }

    public PublishedSweepFrame? CapturePublished() => _pump.CapturePublished();
    public string? LastFailureCode { get { lock (_gate) { return _lastFailure; } } }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (!_stopping)
            {
                _stopping = true;
                _pump.Stop();
                Signal();
            }
        }
        return new ValueTask(_worker);
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                await _wake.WaitAsync().ConfigureAwait(false);
                while (true)
                {
                    ulong observed;
                    lock (_gate)
                    {
                        if (_stopping) { _idle.TrySetResult(); return; }
                        observed = _latest;
                    }
                    SweepFramePublicationStatus? result;
                    try { result = _pump.ProcessNext(); }
                    catch (ArgumentException exception) when (FailureCode(exception) is not null)
                    {
                        lock (_gate) { _lastFailure = FailureCode(exception); }
                        continue;
                    }
                    lock (_gate)
                    {
                        if (result == SweepFramePublicationStatus.Published) { _lastFailure = null; }
                        if (result is null && observed == _latest)
                        {
                            _idle.TrySetResult();
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _stopping = true;
                _pump.Stop();
                _idle.TrySetException(exception);
            }
            throw;
        }
        finally { _wake.Dispose(); }
    }

    // Called only under admission lock, preventing duplicate releases or disposal races.
    private void Signal()
    {
        if (_wake.CurrentCount == 0) { _wake.Release(); }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string? FailureCode(ArgumentException exception) => exception switch
    {
        SweepPathException value => value.ReasonCode,
        SweepSegmentClipException value => value.ReasonCode,
        SweepFramePathException value => value.ReasonCode,
        SweepFrameReconstructionException value => value.ReasonCode,
        ArgumentNullException => "FrameWorker.NullSample",
        _ => null,
    };
}
