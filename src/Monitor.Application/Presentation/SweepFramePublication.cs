// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed class SweepFramePublicationException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public enum SweepFramePublicationStatus { Published, Superseded, AlreadyPublished, Stopped }

public sealed class SweepFrameWork
{
    internal SweepFrameWork(object owner, ulong generation, SweepFrameReconstructionInput input)
    { Owner = owner; Generation = generation; Input = input; }
    internal object Owner { get; }
    internal SweepFrameReconstructionInput Input { get; }
    public ulong Generation { get; }
}

public sealed record PublishedSweepFrame(
    ulong LocalGeneration, ReconstructedSweepFrame Frame,
    SweepFrameReconstructionInput Checkpoint);

public sealed class SweepFramePublication
{
    private readonly object _gate = new();
    private readonly int _maximumSamples;
    private readonly int _maximumSegments;
    private ulong _latestGeneration;
    private PublishedSweepFrame? _current;
    private bool _stopped;

    public SweepFramePublication(int maximumSamples, int maximumSegments)
    {
        _ = new SweepFrameReconstructor(maximumSamples, maximumSegments);
        _maximumSamples = maximumSamples;
        _maximumSegments = maximumSegments;
    }

    public SweepFrameWork Request(SweepFrameReconstructionInput input)
    {
        lock (_gate)
        {
            if (_stopped) { throw Error("FramePublication.Stopped", nameof(input)); }
        }
        ArgumentNullException.ThrowIfNull(input);
        if (input.Frame is null || input.Frame.Previous is not null || input.Samples is null)
        { throw Error("FramePublication.InvalidInput", nameof(input)); }
        if (input.Samples.Count > _maximumSamples)
        { throw Error("FramePublication.SampleLimitExceeded", nameof(input)); }
        SweepPathSample[] samples = input.Samples.ToArray();
        SweepFramePathState frame = SweepFramePathBuilder.Restore(input.Frame).CaptureState();
        SweepFrameReconstructionInput owned = new(frame, Array.AsReadOnly(samples));
        lock (_gate)
        {
            if (_stopped) { throw Error("FramePublication.Stopped", nameof(input)); }
            if (_latestGeneration == ulong.MaxValue)
            { throw Error("FramePublication.GenerationExhausted", nameof(input)); }
            return new SweepFrameWork(_gate, ++_latestGeneration, owned);
        }
    }

    // May run on a worker thread. No callback, UI dispatcher or Task is held under
    // the lock; the caller chooses execution and marshals the resulting snapshot.
    public SweepFramePublicationStatus Complete(SweepFrameWork work, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(work);
        if (!ReferenceEquals(work.Owner, _gate))
        { throw Error("FramePublication.ForeignWork", nameof(work)); }
        lock (_gate)
        {
            SweepFramePublicationStatus? status = ExistingStatus(work);
            if (status is not null) { return status.Value; }
        }
        SweepFrameReconstructor reconstruction = new(_maximumSamples, _maximumSegments);
        ReconstructedSweepFrame frame = reconstruction.Replace(work.Input, cancellationToken);
        PublishedSweepFrame completed = new(work.Generation, frame, reconstruction.CaptureCheckpoint()!);
        lock (_gate)
        {
            SweepFramePublicationStatus? status = ExistingStatus(work);
            if (status is not null) { return status.Value; }
            cancellationToken.ThrowIfCancellationRequested();
            _current = completed;
            return SweepFramePublicationStatus.Published;
        }
    }

    public PublishedSweepFrame? CapturePublished()
    {
        lock (_gate) { return _current; }
    }

    // Idempotently fences all tickets, including work already reconstructing.
    // Returns the final completed snapshot; it cannot change after this returns.
    public PublishedSweepFrame? Stop()
    {
        lock (_gate)
        {
            _stopped = true;
            return _current;
        }
    }

    public static SweepFramePublication Restore(int maximumSamples, int maximumSegments,
        SweepFrameReconstructionInput checkpoint)
    {
        var validated = SweepFrameReconstructor.Restore(maximumSamples, maximumSegments, checkpoint);
        SweepFramePublication result = new(maximumSamples, maximumSegments);
        result._latestGeneration = 1;
        result._current = new(1, validated.Current!, validated.CaptureCheckpoint()!);
        return result;
    }

    private SweepFramePublicationStatus? ExistingStatus(SweepFrameWork work) =>
        _stopped ? SweepFramePublicationStatus.Stopped :
        work.Generation != _latestGeneration ? SweepFramePublicationStatus.Superseded :
        _current?.LocalGeneration == work.Generation ? SweepFramePublicationStatus.AlreadyPublished : null;

    private static SweepFramePublicationException Error(string reasonCode, string parameterName) => new(reasonCode, parameterName);
}
