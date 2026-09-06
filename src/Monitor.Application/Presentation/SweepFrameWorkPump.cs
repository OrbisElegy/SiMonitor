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

    public SweepFrameWorkPump(int maximumSamples, int maximumSegments)
        : this(new SweepFramePublication(maximumSamples, maximumSegments)) { }

    private SweepFrameWorkPump(SweepFramePublication publication) => _publication = publication;

    public ulong Enqueue(SweepFrameReconstructionInput input)
    {
        lock (_gate)
        {
            // Request admission fails before replacing the existing pending slot.
            SweepFrameWork work = _publication.Request(input);
            _pending = work;
            return work.Generation;
        }
    }

    public SweepFramePublicationStatus? ProcessNext()
    {
        SweepFrameWork work;
        lock (_gate)
        {
            if (_processing || _pending is null) { return null; }
            work = _pending;
            _pending = null;
            _processing = true;
        }
        try { return _publication.Complete(work); }
        finally
        {
            lock (_gate) { _processing = false; }
        }
    }

    public PublishedSweepFrame? CapturePublished() => _publication.CapturePublished();

    // In-flight and pending work are transient. Only a completed frame is restored.
    public static SweepFrameWorkPump Restore(int maximumSamples, int maximumSegments,
        SweepFrameReconstructionInput checkpoint) =>
        new(SweepFramePublication.Restore(maximumSamples, maximumSegments, checkpoint));
}
