// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

// One active build plus one latest pending request. The host calls ProcessNext
// on its worker; no unbounded task queue or dedicated thread is created here.
public sealed class EcgStripWorkPump
{
    private readonly object _gate = new();
    private readonly EcgStripPublication _publication;
    private EcgStripWork? _pending;
    private bool _processing;
    private bool _stopped;

    public EcgStripWorkPump(int maximumSamples, int maximumSegments)
        : this(new EcgStripPublication(maximumSamples, maximumSegments)) { }

    private EcgStripWorkPump(EcgStripPublication publication) => _publication = publication;

    public ulong Enqueue(EcgStripCheckpoint input)
    {
        lock (_gate)
        {
            if (_stopped)
            { throw new EcgStripPublicationException("StripPublication.Stopped", nameof(input)); }
            // Request admission fails before replacing the existing pending slot.
            EcgStripWork work = _publication.Request(input);
            _pending = work;
            return work.Generation;
        }
    }

    public SweepFramePublicationStatus? ProcessNext(CancellationToken cancellationToken = default)
    {
        EcgStripWork work;
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

    public PublishedEcgStrip? CapturePublished() => _publication.CapturePublished();

    // Does not wait for CPU work to finish. The publication fence guarantees it
    // cannot replace the returned snapshot, while finally releases the worker.
    public PublishedEcgStrip? Stop()
    {
        lock (_gate)
        {
            _stopped = true;
            _pending = null;
            return _publication.Stop();
        }
    }

    // In-flight and pending work are transient. Only a completed frame is restored.
    public static EcgStripWorkPump Restore(int maximumSamples, int maximumSegments,
        EcgStripCheckpoint checkpoint) =>
        new(EcgStripPublication.Restore(maximumSamples, maximumSegments, checkpoint));
}
