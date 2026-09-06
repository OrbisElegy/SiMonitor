// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

// One active build plus one latest pending request. The host calls ProcessNext
// on its worker; no unbounded task queue or dedicated thread is created here.
public sealed class SweepFrameWorkPump
{
    private readonly object _gate = new();
    private readonly SweepFramePublication _publication;
    private SweepFrameWork? _pending;
    private bool _processing;
    private bool _stopped;

    public SweepFrameWorkPump(int maximumSamples, int maximumSegments)
        : this(new SweepFramePublication(maximumSamples, maximumSegments)) { }

    private SweepFrameWorkPump(SweepFramePublication publication) => _publication = publication;

    public ulong Enqueue(SweepFrameReconstructionInput input)
    {
        lock (_gate)
        {
            if (_stopped)
            { throw new SweepFramePublicationException("FramePublication.Stopped", nameof(input)); }
            // Request admission fails before replacing the existing pending slot.
            SweepFrameWork work = _publication.Request(input);
            _pending = work;
            return work.Generation;
        }
    }

    public SweepFramePublicationStatus? ProcessNext(CancellationToken cancellationToken = default)
    {
        SweepFrameWork work;
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_stopped || _processing || _pending is null) { return null; }
            work = _pending;
            _pending = null;
            _processing = true;
        }
        try { return _publication.Complete(work, cancellationToken); }
        finally
        {
            lock (_gate) { _processing = false; }
        }
    }

    public PublishedSweepFrame? CapturePublished() => _publication.CapturePublished();

    // Does not wait for CPU work to finish. The publication fence guarantees it
    // cannot replace the returned snapshot, while finally releases the worker.
    public PublishedSweepFrame? Stop()
    {
        lock (_gate)
        {
            _stopped = true;
            _pending = null;
            return _publication.Stop();
        }
    }

    // In-flight and pending work are transient. Only a completed frame is restored.
    public static SweepFrameWorkPump Restore(int maximumSamples, int maximumSegments,
        SweepFrameReconstructionInput checkpoint) =>
        new(SweepFramePublication.Restore(maximumSamples, maximumSegments, checkpoint));
}
