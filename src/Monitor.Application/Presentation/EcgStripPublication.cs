// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;

namespace Monitor.Application.Presentation;

public sealed class EcgStripPublicationException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed class EcgStripWork
{
    internal EcgStripWork(object owner, ulong generation, EcgStripCheckpoint input)
    { Owner = owner; Generation = generation; Input = input; }
    internal object Owner { get; }
    internal EcgStripCheckpoint Input { get; }
    public ulong Generation { get; }
}

public sealed record PublishedEcgStrip(
    ulong LocalGeneration, ReconstructedEcgStrip Strip);

public sealed class EcgStripPublication
{
    private readonly object _gate = new();
    private readonly int _maximumSamples;
    private readonly int _maximumSegments;
    private ulong _latestGeneration;
    private PublishedEcgStrip? _current;
    private bool _stopped;

    public EcgStripPublication(int maximumSamples, int maximumSegments)
    {
        _ = new SweepFrameReconstructor(maximumSamples, maximumSegments);
        _maximumSamples = maximumSamples;
        _maximumSegments = maximumSegments;
    }

    public EcgStripWork Request(EcgStripCheckpoint input)
    {
        lock (_gate)
        {
            if (_stopped) { throw Error("StripPublication.Stopped", nameof(input)); }
        }
        ArgumentNullException.ThrowIfNull(input);
        if (input.Source is null || input.Source.Frame is null || input.Source.Frame.Previous is not null || input.Source.Samples is null)
        { throw Error("StripPublication.InvalidInput", nameof(input)); }
        int count = input.Source.Samples.Count;
        if (count < 0 || count > _maximumSamples)
        { throw Error("StripPublication.SampleLimitExceeded", nameof(input)); }
        SweepPathSample[] samples = new SweepPathSample[count];
        for (int index = 0; index < count; index++) { samples[index] = input.Source.Samples[index]; }
        SweepFramePathState frame = SweepFramePathBuilder.Restore(input.Source.Frame).CaptureState();
        EcgVerticalScale scale = frame.VerticalScale ??
            throw Error("StripPublication.VoltageScaleRequired", nameof(input));
        _ = EcgCalibrationGeometry.Compose(frame.Presentation, frame.PlotLeftPixels, frame.PlotWidthPixels,
            scale, input.GutterLeftPixels, input.PulseLeftPixels);
        EcgStripCheckpoint owned = new(new(frame, Array.AsReadOnly(samples)), input.GutterLeftPixels, input.PulseLeftPixels);
        lock (_gate)
        {
            if (_stopped) { throw Error("StripPublication.Stopped", nameof(input)); }
            if (_latestGeneration == ulong.MaxValue)
            { throw Error("StripPublication.GenerationExhausted", nameof(input)); }
            return new EcgStripWork(_gate, ++_latestGeneration, owned);
        }
    }

    // May run on a worker thread. No callback, UI dispatcher or Task is held under
    // the lock; the caller chooses execution and marshals the resulting snapshot.
    public SweepFramePublicationStatus Complete(EcgStripWork work, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(work);
        if (!ReferenceEquals(work.Owner, _gate))
        { throw Error("StripPublication.ForeignWork", nameof(work)); }
        lock (_gate)
        {
            SweepFramePublicationStatus? status = ExistingStatus(work);
            if (status is not null) { return status.Value; }
        }
        EcgStripReconstructor reconstruction = new(_maximumSamples, _maximumSegments);
        ReconstructedEcgStrip strip = reconstruction.Replace(work.Input, cancellationToken);
        PublishedEcgStrip completed = new(work.Generation, strip);
        lock (_gate)
        {
            SweepFramePublicationStatus? status = ExistingStatus(work);
            if (status is not null) { return status.Value; }
            cancellationToken.ThrowIfCancellationRequested();
            _current = completed;
            return SweepFramePublicationStatus.Published;
        }
    }

    public PublishedEcgStrip? CapturePublished()
    {
        lock (_gate) { return _current; }
    }

    // Idempotently fences all tickets, including work already reconstructing.
    // Returns the final completed snapshot; it cannot change after this returns.
    public PublishedEcgStrip? Stop()
    {
        lock (_gate)
        {
            _stopped = true;
            return _current;
        }
    }

    public static EcgStripPublication Restore(int maximumSamples, int maximumSegments,
        EcgStripCheckpoint checkpoint)
    {
        EcgStripReconstructor validated = EcgStripReconstructor.Restore(maximumSamples, maximumSegments, checkpoint);
        EcgStripPublication result = new(maximumSamples, maximumSegments);
        result._latestGeneration = 1;
        result._current = new(1, validated.Current!);
        return result;
    }

    private SweepFramePublicationStatus? ExistingStatus(EcgStripWork work) =>
        _stopped ? SweepFramePublicationStatus.Stopped :
        work.Generation != _latestGeneration ? SweepFramePublicationStatus.Superseded :
        _current?.LocalGeneration == work.Generation ? SweepFramePublicationStatus.AlreadyPublished : null;

    private static EcgStripPublicationException Error(string reasonCode, string parameterName) => new(reasonCode, parameterName);
}
