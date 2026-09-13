// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Application.Presentation;

public sealed class CapturedRecordBindingException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed record RecordSlotBinding(string SlotId, Guid ChannelId);

public sealed record CapturedRecordBindingState(
    FillOnceThenHoldState Presentation,
    WaveformRecordArchiveState Archive,
    IReadOnlyList<RecordSlotBinding> Slots);

// An immutable publication boundary: no partially bound record can escape.
public sealed class CapturedRecordBinding
{
    private readonly FillOnceThenHoldStateMachine _presentation;
    private readonly WaveformRecordArchive _archive;
    private readonly IReadOnlyList<RecordSlotBinding> _slots;

    private CapturedRecordBinding(
        FillOnceThenHoldStateMachine presentation,
        WaveformRecordArchive archive,
        IReadOnlyList<RecordSlotBinding> slots)
    {
        _presentation = presentation;
        _archive = archive;
        _slots = slots;
    }

    public static CapturedRecordBinding Create(
        FillOnceThenHoldState presentation,
        WaveformRecordArchive archive,
        IReadOnlyList<RecordSlotBinding> slots)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(archive);
        var restored =
            FillOnceThenHoldStateMachine.Restore(presentation);
        if (restored.CaptureProjection().TemporalViewMode !=
            TemporalViewMode.CapturedRecord)
        {
            throw Error("CapturedRecordBinding.RecordNotCaptured", nameof(presentation));
        }

        PinnedRecordRange range = restored.CapturePinnedRecordRange();
        WaveformRecordArchivePlan plan = archive.CapturePlan();
        if (range.GroupId != plan.GroupId ||
            range.RecordRef != plan.RecordRef ||
            range.SweepEpoch != plan.SweepEpoch ||
            range.StartDataSimTimeNs != plan.RecordStartSimTimeNs ||
            range.EndExclusiveDataSimTimeNs != plan.RecordEndExclusiveSimTimeNs)
        {
            throw Error("CapturedRecordBinding.IdentityMismatch", nameof(archive));
        }

        if (slots is null || slots.Count != FillOnceThenHoldStateMachine.StandardEcgSlotCount)
        {
            throw Error("CapturedRecordBinding.InvalidSlots", nameof(slots));
        }

        RecordSlotBinding[] copied = slots.ToArray();
        if (copied.Any(static slot => slot is null) ||
            !copied.Select(static slot => slot.SlotId).SequenceEqual(range.SlotIds) ||
            copied.Select(static slot => slot.ChannelId).Distinct().Count() != copied.Length ||
            !copied.Select(static slot => slot.ChannelId).ToHashSet().SetEquals(plan.ChannelIds))
        {
            throw Error("CapturedRecordBinding.InvalidSlots", nameof(slots));
        }

        return new CapturedRecordBinding(restored, archive, Array.AsReadOnly(copied));
    }

    public static CapturedRecordBinding Restore(CapturedRecordBindingState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            return Create(state.Presentation, WaveformRecordArchive.Restore(state.Archive), state.Slots);
        }
        catch (ArgumentException)
        {
            throw Error("CapturedRecordBinding.InvalidCheckpoint", nameof(state));
        }
    }

    public SweepStateProjectionSnapshot CaptureProjection() => _presentation.CaptureProjection();

    public PinnedRecordRange CapturePinnedRecordRange() => _presentation.CapturePinnedRecordRange();

    public IReadOnlyList<RecordSlotBinding> Slots => _slots;

    public IReadOnlyList<ArchivedWaveformBlock> ReadBlocks() => _archive.ReadBlocks();

    internal ArchivedWaveformChannelShape ReadChannelShape(Guid channelId) => _archive.ReadChannelShape(channelId);

    internal ArchivedWaveformChannelRead ReadChannel(Guid channelId, long startSimTimeNs,
        long endExclusiveSimTimeNs, int maximumSamples, CancellationToken cancellationToken) =>
        _archive.ReadChannel(channelId, startSimTimeNs, endExclusiveSimTimeNs, maximumSamples, cancellationToken);

    public CapturedRecordBindingState CaptureState() => new(
        _presentation.CaptureState(), _archive.CaptureState(), _slots);

    private static CapturedRecordBindingException Error(string reasonCode, string parameterName) =>
        new(reasonCode, parameterName);
}
