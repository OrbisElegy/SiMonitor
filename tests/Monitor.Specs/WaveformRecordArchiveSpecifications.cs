// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Domain.Continuity;
using Monitor.Domain.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Specs;

internal static class WaveformRecordArchiveSpecifications
{
    private static readonly Guid SessionId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid InstanceId =
        Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid[] ChannelIds =
    [
        Guid.Parse("30000000-0000-4000-8000-000000000001"),
        Guid.Parse("30000000-0000-4000-8000-000000000002"),
        Guid.Parse("30000000-0000-4000-8000-000000000003"),
        Guid.Parse("30000000-0000-4000-8000-000000000004"),
        Guid.Parse("30000000-0000-4000-8000-000000000005"),
        Guid.Parse("30000000-0000-4000-8000-000000000006"),
        Guid.Parse("30000000-0000-4000-8000-000000000007"),
        Guid.Parse("30000000-0000-4000-8000-000000000008"),
        Guid.Parse("30000000-0000-4000-8000-000000000009"),
        Guid.Parse("30000000-0000-4000-8000-00000000000a"),
        Guid.Parse("30000000-0000-4000-8000-00000000000b"),
        Guid.Parse("30000000-0000-4000-8000-00000000000c"),
    ];

    public static Specification[] All =>
    [
        new(nameof(RecordMeasurementUsesBoundCursorValues), RecordMeasurementUsesBoundCursorValues),
        new(nameof(RecordMeasurementEnforcesHalfOpenRange), RecordMeasurementEnforcesHalfOpenRange),
        new(nameof(RecordMeasurementRejectsForeignCursorsWithoutMutation), RecordMeasurementRejectsForeignCursorsWithoutMutation),
        new(nameof(RestoredRecordMeasurementRequiresFreshCursors), RestoredRecordMeasurementRequiresFreshCursors),
        new(nameof(CompletedRecordBindsExplicitSlots), CompletedRecordBindsExplicitSlots),
        new(nameof(RecordBindingRejectsIncompleteAndMismatchedInputs), RecordBindingRejectsIncompleteAndMismatchedInputs),
        new(nameof(RecordBindingCheckpointIsDefensive), RecordBindingCheckpointIsDefensive),
        new(nameof(StandardRecordArchivesFiftySharedBlocks),
            StandardRecordArchivesFiftySharedBlocks),
        new(nameof(ArchivedRecordSurvivesLiveRingEviction),
            ArchivedRecordSurvivesLiveRingEviction),
        new(nameof(UnalignedRangeUsesOnlyItsMinimalCover),
            UnalignedRangeUsesOnlyItsMinimalCover),
        new(nameof(IdentityConfigurationAndChannelSetFailClosed),
            IdentityConfigurationAndChannelSetFailClosed),
        new(nameof(EveryLeadKeepsOneSampleGridAndScale),
            EveryLeadKeepsOneSampleGridAndScale),
        new(nameof(DiscontinuousBlocksCannotFormARecord),
            DiscontinuousBlocksCannotFormARecord),
        new(nameof(ArchiveCheckpointAndReadsAreDefensive),
            ArchiveCheckpointAndReadsAreDefensive),
    ];

    private static CapturedRecordBinding MeasurementRecord() => CapturedRecordBinding.Create(
        BindingPresentation().CaptureState(), BindingArchive(), BindingSlots());

    private static void RecordMeasurementUsesBoundCursorValues()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord());
        CapturedRecordCursor first = measurement.CreateCursor(new(0, 0, 1));
        CapturedRecordCursor second = measurement.CreateCursor(new(100_000_000, 1000, 1));
        EcgManualMeasurementResult result = measurement.Calculate(first, second, true);
        Check.That(result.ElapsedMilliseconds == new EcgMeasurementRatio(100, 1) &&
            result.AmplitudeChangeMillivolts == new EcgMeasurementRatio(1, 1) &&
            result.AuxiliaryRatePerMinute == new EcgMeasurementRatio(600, 1) &&
            measurement.Calculate(first, second, false).AuxiliaryRatePerMinute is null,
            "bound manual values preserve exact calculation and explicit auxiliary-rate permission");
    }

    private static void RecordMeasurementEnforcesHalfOpenRange()
    {
        CapturedRecordMeasurement measurement = new(MeasurementRecord());
        Check.That(measurement.CreateCursor(new(0, 0, 1)).Value.DataTimeNs == 0 &&
            measurement.CreateCursor(new(199_999_999, 0, 1)).Value.DataTimeNs == 199_999_999,
            "record start and last included nanosecond accept manual cursors");
        foreach (long time in new long[] { 200_000_000, long.MaxValue })
        {
            try { _ = measurement.CreateCursor(new(time, 0, 1)); throw new InvalidOperationException("outside cursor accepted"); }
            catch (CapturedRecordMeasurementException exception) { Check.That(exception.ReasonCode == "RecordMeasurement.CursorOutsideRecord", "exclusive end and later times reject"); }
        }
        try { _ = measurement.CreateCursor(new(0, 1, 0)); throw new InvalidOperationException("invalid voltage accepted"); }
        catch (EcgManualMeasurementException exception) { Check.That(exception.ReasonCode == "ManualMeasurement.InvalidCursor", "bound cursors still validate voltage"); }
    }

    private static void RecordMeasurementRejectsForeignCursorsWithoutMutation()
    {
        CapturedRecordBinding record = MeasurementRecord();
        CapturedRecordMeasurement measurement = new(record), other = new(record);
        CapturedRecordCursor first = measurement.CreateCursor(new(0, 0, 1)), second = measurement.CreateCursor(new(1, 1, 1));
        EcgManualMeasurementResult accepted = measurement.Calculate(first, second, false);
        CapturedRecordCursor[] foreign = [null!, other.CreateCursor(second.Value)];
        foreach (CapturedRecordCursor cursor in foreign)
        {
            try { _ = measurement.Calculate(first, cursor, true); throw new InvalidOperationException("foreign cursor accepted"); }
            catch (CapturedRecordMeasurementException exception) { Check.That(exception.ReasonCode == "RecordMeasurement.ForeignCursor", "foreign ownership rejects even with equal record identity"); }
        }
        Check.That(measurement.Calculate(first, second, false) == accepted, "rejected foreign values leave issued cursors unchanged");
    }

    private static void RestoredRecordMeasurementRequiresFreshCursors()
    {
        CapturedRecordBinding record = MeasurementRecord();
        CapturedRecordMeasurement original = new(record);
        CapturedRecordCursor first = original.CreateCursor(new(0, 1000, 3)), second = original.CreateCursor(new(199_999_999, -1000, 3));
        CapturedRecordMeasurement restored = new(CapturedRecordBinding.Restore(record.CaptureState()));
        try { _ = restored.Calculate(first, second, true); throw new InvalidOperationException("old handles accepted"); }
        catch (CapturedRecordMeasurementException exception) { Check.That(exception.ReasonCode == "RecordMeasurement.ForeignCursor", "restored ownership rejects old handles"); }
        Check.That(restored.Calculate(restored.CreateCursor(first.Value), restored.CreateCursor(second.Value), true) ==
            original.Calculate(first, second, true), "explicit reissue revalidates values and recreates exact measurement after record restore");
    }

    private static FillOnceThenHoldStateMachine BindingPresentation(long playhead = 200_000_000)
    {
        FillOnceThenHoldStateMachine machine = FillOnceThenHoldStateMachine.Start(
            new FillOnceThenHoldPlan("ecg12.standard", "record.ecg12-7", 7, 11,
                0, 200_000_000, 200_000_000,
                Enumerable.Range(0, 12).Select(index => $"ecg.slot{index}").ToArray()),
            13, 17, SessionRunState.Running,
            DataContinuityStateMachine.Start(LocalContinuationPolicy.DefaultDuration, 0).CaptureState(),
            0, 0);
        machine.Advance(1, playhead);
        return machine;
    }

    private static RecordSlotBinding[] BindingSlots() => Enumerable.Range(0, 12)
        .Select(index => new RecordSlotBinding($"ecg.slot{index}", ChannelIds[11 - index])).ToArray();

    private static WaveformRecordArchive BindingArchive() => WaveformRecordArchive.Create(
        Plan(endExclusiveSimTimeNs: 200_000_000), [Wire(100, 0)]);

    private static void CompletedRecordBindsExplicitSlots()
    {
        foreach (long playhead in new long[] { 200_000_000, 900_000_000 })
        {
            FillOnceThenHoldStateMachine machine = BindingPresentation(playhead);
            CapturedRecordBinding binding = CapturedRecordBinding.Create(
                machine.CaptureState(), BindingArchive(), BindingSlots());
            machine.Advance(2, 1_000_000_000);
            Check.That(binding.Slots[0].ChannelId == ChannelIds[11] &&
                binding.CapturePinnedRecordRange().EndExclusiveDataSimTimeNs == 200_000_000 &&
                binding.CaptureProjection().TransientReplayPolicy == TransientReplayPolicy.Suppress &&
                binding.ReadBlocks().Count == 1,
                "exact and skipped completion bind explicit channels, never positional channel order");
        }
    }

    private static void RecordBindingRejectsIncompleteAndMismatchedInputs()
    {
        FillOnceThenHoldStateMachine machine = BindingPresentation();
        FillOnceThenHoldState state = machine.CaptureState();
        WaveformRecordArchive archive = BindingArchive();
        RecordSlotBinding[] slots = BindingSlots();
        Check.That(BindingReason(() => CapturedRecordBinding.Create(
            BindingPresentation(199_999_999).CaptureState(), archive, slots)) ==
            "CapturedRecordBinding.RecordNotCaptured", "one nanosecond before completion cannot bind");
        foreach (FillOnceThenHoldPlan plan in new[]
        {
            state.Plan with { GroupId = "other" }, state.Plan with { RecordRef = "other" },
            state.Plan with { SweepEpoch = 8 }, state.Plan with { RecordStartDataSimTimeNs = 1, RecordDurationNs = 199_999_999 },
            state.Plan with { RecordDurationNs = 199_999_999 },
        })
        {
            Check.That(BindingReason(() => CapturedRecordBinding.Create(state with { Plan = plan }, archive, slots)) ==
                "CapturedRecordBinding.IdentityMismatch", "every pinned identity must match");
        }

        foreach (RecordSlotBinding[] invalid in new[]
        {
            slots.Take(11).ToArray(), slots.Reverse().ToArray(),
            slots.Select((slot, index) => index == 11 ? slot with { ChannelId = slots[0].ChannelId } : slot).ToArray(),
            slots.Select((slot, index) => index == 0 ? slot with { ChannelId = Guid.Empty } : slot).ToArray(),
            slots.Select((slot, index) => index == 0 ? null! : slot).ToArray(),
        })
        {
            Check.That(BindingReason(() => CapturedRecordBinding.Create(state, archive, invalid)) ==
                "CapturedRecordBinding.InvalidSlots", "partial, reordered, duplicated and unknown mappings reject");
        }

        Check.That(machine.CaptureProjection().SweepRevision == state.SweepRevision &&
            CapturedRecordBinding.Create(state, archive, slots).Slots.Count == 12,
            "failed publication leaves source state usable for an exact retry");
    }

    private static void RecordBindingCheckpointIsDefensive()
    {
        RecordSlotBinding[] slots = BindingSlots();
        CapturedRecordBinding binding = CapturedRecordBinding.Create(
            BindingPresentation().CaptureState(), BindingArchive(), slots);
        slots[0] = slots[0] with { ChannelId = Guid.Empty };
        CapturedRecordBindingState state = binding.CaptureState();
        CapturedRecordBinding restored = CapturedRecordBinding.Restore(state);
        state.Archive.RawEnvelopes[0][0] ^= 1;
        Check.That(binding.Slots[0].ChannelId == ChannelIds[11] &&
            Equivalent(binding.ReadBlocks(), restored.ReadBlocks()) &&
            BindingReason(() => CapturedRecordBinding.Restore(state)) == "CapturedRecordBinding.InvalidCheckpoint" &&
            BindingReason(() => CapturedRecordBinding.Restore(binding.CaptureState() with { Slots = slots })) ==
                "CapturedRecordBinding.InvalidCheckpoint",
            "restore revalidates mapping and bytes without exposing accepted state to caller mutation");
    }

    private static string? BindingReason(Action action)
    {
        try { action(); return null; }
        catch (CapturedRecordBindingException exception) { return exception.ReasonCode; }
    }

    private static void StandardRecordArchivesFiftySharedBlocks()
    {
        byte[][] wires = StandardWires();
        WaveformRecordArchive archive = WaveformRecordArchive.Create(
            Plan(),
            wires);

        IReadOnlyList<ArchivedWaveformBlock> blocks = archive.ReadBlocks();
        string firstContentHash = Convert.ToHexStringLower(wires[0].AsSpan(
            WaveformEnvelopeCodec.ContentSha256Offset,
            32));
        Check.That(
            archive.RecordRef == "record.ecg12-7" &&
            archive.RecordStartSimTimeNs == 0 &&
            archive.RecordEndExclusiveSimTimeNs == 10_000_000_000 &&
            archive.BlockCount == 50 &&
            blocks[0].BlockSequence == 100 &&
            blocks[0].StartSimTimeNs == 0 &&
            blocks[0].ContentSha256 == firstContentHash &&
            blocks[^1].BlockSequence == 149 &&
            blocks[^1].StartSimTimeNs == 9_800_000_000,
            "one standard record must retain fifty exact shared 200 ms blocks");
    }

    private static void ArchivedRecordSurvivesLiveRingEviction()
    {
        byte[][] wires = StandardWires();
        WaveformBlockRing live = WaveformBlockRing.Start(
            SessionId,
            InstanceId,
            timebaseEpoch: 3,
            streamEpoch: 7,
            firstBlockSequence: 100,
            firstBlockStartSimTimeNs: 0,
            WaveformBlockRing.ClientHistoryBlockCount);
        foreach (byte[] wire in wires)
        {
            _ = live.Append(wire);
        }

        WaveformRecordArchive archive = WaveformRecordArchive.Create(
            Plan(),
            live.CaptureState().RetainedRawBlocks);
        byte[] archivedFirst = archive.ReadBlocks()[0].RawEnvelope;
        for (ulong sequence = 150; sequence < 202; sequence++)
        {
            long start = checked((long)(sequence - 100) * 200_000_000);
            _ = live.Append(Wire(sequence, start));
        }

        Check.That(
            live.OldestBlockSequence == 152 &&
            live.ReadFrom(100, 1).Status ==
                WaveformBlockReplayStatus.RecoverySnapshotRequired &&
            archive.BlockCount == 50 &&
            archive.ReadBlocks()[0].RawEnvelope.SequenceEqual(archivedFirst),
            "pinned record bytes must outlive eviction from the Live ring");
    }

    private static void UnalignedRangeUsesOnlyItsMinimalCover()
    {
        WaveformRecordArchivePlan plan = Plan(
            startSimTimeNs: 100_000_000,
            endExclusiveSimTimeNs: 550_000_000);
        byte[][] exact =
        [
            Wire(100, 0),
            Wire(101, 200_000_000),
            Wire(102, 400_000_000),
        ];

        WaveformRecordArchive archive = WaveformRecordArchive.Create(
            plan,
            exact);
        Check.That(
            archive.BlockCount == 3 &&
            Reason(() => WaveformRecordArchive.Create(
                plan,
                exact.Take(2).ToArray())) ==
                "WaveformRecordArchive.RangeNotCovered" &&
            Reason(() => WaveformRecordArchive.Create(
                plan,
                [.. exact, Wire(103, 600_000_000)])) ==
                "WaveformRecordArchive.RangeNotCovered",
            "an unaligned interval must retain only its minimal covering blocks");
    }

    private static void IdentityConfigurationAndChannelSetFailClosed()
    {
        WaveformRecordArchivePlan shortPlan = Plan(
            endExclusiveSimTimeNs: 200_000_000);
        byte[] wrongIdentity = Wire(
            100,
            0,
            sessionId: Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"));
        byte[] wrongConfiguration = Wire(
            100,
            0,
            configurationRevision: 12);
        byte[] missingLead = Wire(100, 0, channelCount: 11);
        Guid[] duplicateChannels = ChannelIds.ToArray();
        duplicateChannels[^1] = duplicateChannels[0];
        byte[] corrupt = Wire(100, 0);
        corrupt[^1] ^= 1;

        Check.That(
            Reason(() => WaveformRecordArchive.Create(
                shortPlan,
                [wrongIdentity])) ==
                "WaveformRecordArchive.IdentityMismatch" &&
            Reason(() => WaveformRecordArchive.Create(
                shortPlan,
                [wrongConfiguration])) ==
                "WaveformRecordArchive.ConfigurationChanged" &&
            Reason(() => WaveformRecordArchive.Create(
                shortPlan,
                [missingLead])) ==
                "WaveformRecordArchive.ChannelSetIncomplete" &&
            Reason(() => WaveformRecordArchive.Create(
                Plan(
                    endExclusiveSimTimeNs: 200_000_000,
                    channelIds: duplicateChannels),
                [Wire(100, 0)])) ==
                "WaveformRecordArchive.InvalidPlan" &&
            Reason(() => WaveformRecordArchive.Create(
                shortPlan,
                [corrupt])) == "WaveformRecordArchive.InvalidEnvelope",
            "record creation must bind identity, configuration and all leads");
    }

    private static void EveryLeadKeepsOneSampleGridAndScale()
    {
        WaveformRecordArchivePlan plan = Plan(
            endExclusiveSimTimeNs: 400_000_000);
        byte[] first = Wire(100, 0);
        byte[] sampleGap = Wire(
            101,
            200_000_000,
            firstSampleIndex: 101);
        byte[] rateChange = Wire(
            101,
            200_000_000,
            sampleRateNumerator: 250,
            firstSampleIndex: 100);
        byte[] scaleChange = Wire(
            101,
            200_000_000,
            scaleNumerator: 2);

        Check.That(
            Reason(() => WaveformRecordArchive.Create(
                plan,
                [first, sampleGap])) ==
                "WaveformRecordArchive.ChannelGridChanged" &&
            Reason(() => WaveformRecordArchive.Create(
                plan,
                [first, rateChange])) ==
                "WaveformRecordArchive.ChannelGridChanged" &&
            Reason(() => WaveformRecordArchive.Create(
                plan,
                [first, scaleChange])) ==
                "WaveformRecordArchive.ChannelGridChanged",
            "all twelve leads must retain one sample frontier, rate and scale");
    }

    private static void DiscontinuousBlocksCannotFormARecord()
    {
        WaveformRecordArchivePlan plan = Plan(
            endExclusiveSimTimeNs: 400_000_000);
        byte[] first = Wire(100, 0);
        byte[] sequenceGap = Wire(102, 200_000_000);
        byte[] timeGap = Wire(101, 400_000_000);

        Check.That(
            Reason(() => WaveformRecordArchive.Create(
                plan,
                [first, sequenceGap])) ==
                "WaveformRecordArchive.BlockDiscontinuous" &&
            Reason(() => WaveformRecordArchive.Create(
                plan,
                [first, timeGap])) ==
                "WaveformRecordArchive.BlockDiscontinuous",
            "missing sequence or source-time slots cannot form a record");
    }

    private static void ArchiveCheckpointAndReadsAreDefensive()
    {
        byte[][] source = StandardWires();
        WaveformRecordArchive archive = WaveformRecordArchive.Create(
            Plan(),
            source);
        byte expected = archive.ReadBlocks()[0].RawEnvelope[0];
        source[0][0] ^= 0xff;
        IReadOnlyList<ArchivedWaveformBlock> firstRead = archive.ReadBlocks();
        firstRead[0].RawEnvelope[0] ^= 0xff;

        WaveformRecordArchiveState checkpoint = archive.CaptureState();
        WaveformRecordArchive restored = WaveformRecordArchive.Restore(checkpoint);
        byte[][] corrupt = checkpoint.RawEnvelopes
            .Select(static wire => wire.ToArray())
            .ToArray();
        corrupt[0][^1] ^= 1;
        WaveformRecordArchiveState corruptState = checkpoint with
        {
            RawEnvelopes = corrupt,
        };

        Check.That(
            archive.ReadBlocks()[0].RawEnvelope[0] == expected &&
            Equivalent(archive.ReadBlocks(), restored.ReadBlocks()) &&
            Reason(() => WaveformRecordArchive.Restore(corruptState)) ==
                "WaveformRecordArchive.InvalidCheckpoint",
            "source, read and checkpoint mutation must not alter archived bytes");
    }

    private static WaveformRecordArchivePlan Plan(
        long startSimTimeNs = 0,
        long endExclusiveSimTimeNs = 10_000_000_000,
        IReadOnlyList<Guid>? channelIds = null) => new(
        "ecg12.standard",
        "record.ecg12-7",
        SweepEpoch: 7,
        SessionId,
        InstanceId,
        TimebaseEpoch: 3,
        EpochAnchorSimTimeNs: 0,
        StreamEpoch: 7,
        ConfigurationRevision: 11,
        startSimTimeNs,
        endExclusiveSimTimeNs,
        channelIds ?? Array.AsReadOnly(ChannelIds.ToArray()));

    private static byte[][] StandardWires() => Enumerable
        .Range(0, 50)
        .Select(index => Wire(
            100 + checked((ulong)index),
            index * 200_000_000L))
        .ToArray();

    private static byte[] Wire(
        ulong sequence,
        long startSimTimeNs,
        Guid? sessionId = null,
        ulong configurationRevision = 11,
        int channelCount = 12,
        uint sampleRateNumerator = 500,
        ulong? firstSampleIndex = null,
        int scaleNumerator = 1)
    {
        int sampleCount = checked((int)(sampleRateNumerator / 5));
        ulong firstIndex = firstSampleIndex ?? checked((ulong)(
            (UInt128)checked((ulong)startSimTimeNs) *
            sampleRateNumerator /
            1_000_000_000U));
        WaveformPlane[] planes = ChannelIds
            .Take(channelCount)
            .Select((channelId, channelIndex) => new WaveformPlane(
                channelId,
                sampleRateNumerator,
                1,
                firstIndex,
                scaleNumerator,
                1,
                0,
                1,
                WaveformQualityEncoding.None,
                Array.AsReadOnly(Enumerable
                    .Repeat(checked((short)(sequence +
                        (ulong)channelIndex)), sampleCount)
                    .ToArray()),
                Array.Empty<WaveformQualityRange>()))
            .ToArray();
        return WaveformEnvelopeCodec.EncodeRaw(new WaveformEnvelope(
            sessionId ?? SessionId,
            InstanceId,
            TimebaseEpoch: 3,
            StreamEpoch: 7,
            sequence,
            configurationRevision,
            startSimTimeNs,
            WaveformBlockAssembler.BlockDurationNs,
            Array.AsReadOnly(planes)));
    }

    private static bool Equivalent(
        IReadOnlyList<ArchivedWaveformBlock> left,
        IReadOnlyList<ArchivedWaveformBlock> right) =>
        left.Count == right.Count &&
        left.Zip(right).All(pair =>
            pair.First.BlockSequence == pair.Second.BlockSequence &&
            pair.First.StartSimTimeNs == pair.Second.StartSimTimeNs &&
            pair.First.ContentSha256 == pair.Second.ContentSha256 &&
            pair.First.RawEnvelope.SequenceEqual(pair.Second.RawEnvelope));

    private static string? Reason(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (WaveformRecordArchiveException exception)
        {
            return exception.ReasonCode;
        }
    }
}
