// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Acquisition;

public sealed class WaveformRecoveryException(
    string reasonCode,
    string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

public enum WaveformRecoveryMode
{
    UpToDate,
    OrderedReplay,
    AwaitFutureBlock,
    SnapshotThenJoin,
}

public enum WaveformRecoveryReason
{
    None,
    GapWithinRetention,
    ClientCursorAhead,
    EpochChanged,
    GapOutsideRetention,
    RenderHistoryIncomplete,
}

public sealed record WaveformRecoveryRequest(
    Guid SessionId,
    Guid InstanceId,
    ulong TimebaseEpoch,
    ulong StreamEpoch,
    ulong NextBlockSequence,
    ulong RequiredRenderHistoryNs,
    ulong AvailableRenderHistoryNs);

public sealed record WaveformJoinBoundary(
    ulong BlockSequence,
    long SimTimeNs,
    long PrerollFromSimTimeNs,
    ulong RequiredRenderHistoryNs,
    ulong AvailableRenderHistoryNs);

public sealed record WaveformRecoveryPlan(
    WaveformRecoveryMode Mode,
    WaveformRecoveryReason Reason,
    IReadOnlyList<WaveformRetainedBlock> ReplayBlocks,
    WaveformJoinBoundary? JoinBoundary);

public static class WaveformRecoveryPlanner
{
    public const ulong MinimumJoinLeadNs = 1_000_000_000;
    public const int MinimumJoinLeadBlockCount =
        (int)(MinimumJoinLeadNs / WaveformBlockAssembler.BlockDurationNs);
    public const ulong MaximumRequiredRenderHistoryNs =
        (ulong)WaveformBlockRing.HostRetentionBlockCount *
        WaveformBlockAssembler.BlockDurationNs;

    public static WaveformRecoveryPlan Plan(
        WaveformBlockRing hostRing,
        WaveformRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(hostRing);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(hostRing, request);

        if (request.TimebaseEpoch != hostRing.TimebaseEpoch ||
            request.StreamEpoch != hostRing.StreamEpoch)
        {
            return SnapshotPlan(
                hostRing,
                request.RequiredRenderHistoryNs,
                WaveformRecoveryReason.EpochChanged);
        }

        WaveformBlockReplayResult replay = hostRing.ReadFrom(
            request.NextBlockSequence,
            WaveformBlockRing.HostRetentionBlockCount);
        if (replay.Status == WaveformBlockReplayStatus.Available &&
            !HasRequiredRenderHistory(request, replay.Blocks.Count))
        {
            return SnapshotPlan(
                hostRing,
                request.RequiredRenderHistoryNs,
                WaveformRecoveryReason.RenderHistoryIncomplete);
        }

        return replay.Status switch
        {
            WaveformBlockReplayStatus.RecoverySnapshotRequired => SnapshotPlan(
                hostRing,
                request.RequiredRenderHistoryNs,
                WaveformRecoveryReason.GapOutsideRetention),
            WaveformBlockReplayStatus.AwaitFutureBlock => new WaveformRecoveryPlan(
                WaveformRecoveryMode.AwaitFutureBlock,
                WaveformRecoveryReason.ClientCursorAhead,
                Array.Empty<WaveformRetainedBlock>(),
                null),
            WaveformBlockReplayStatus.Available when replay.Blocks.Count == 0 =>
                new WaveformRecoveryPlan(
                    WaveformRecoveryMode.UpToDate,
                    WaveformRecoveryReason.None,
                    Array.Empty<WaveformRetainedBlock>(),
                    null),
            WaveformBlockReplayStatus.Available => new WaveformRecoveryPlan(
                WaveformRecoveryMode.OrderedReplay,
                WaveformRecoveryReason.GapWithinRetention,
                replay.Blocks,
                null),
            _ => throw new InvalidOperationException("unknown waveform replay status"),
        };
    }

    private static bool HasRequiredRenderHistory(
        WaveformRecoveryRequest request,
        int replayBlockCount)
    {
        ulong replayHistoryNs = checked(
            (ulong)replayBlockCount * WaveformBlockAssembler.BlockDurationNs);
        ulong availableAfterReplay = Math.Min(
            MaximumRequiredRenderHistoryNs,
            checked(request.AvailableRenderHistoryNs + replayHistoryNs));
        return availableAfterReplay >= request.RequiredRenderHistoryNs;
    }

    private static WaveformRecoveryPlan SnapshotPlan(
        WaveformBlockRing hostRing,
        ulong requiredRenderHistoryNs,
        WaveformRecoveryReason reason)
    {
        int requiredBlocks = checked((int)(
            requiredRenderHistoryNs / WaveformBlockAssembler.BlockDurationNs));
        int missingHistoryBlocks = Math.Max(0, requiredBlocks - hostRing.Count);
        int leadBlocks = Math.Max(MinimumJoinLeadBlockCount, missingHistoryBlocks);
        UInt128 joinSequenceWide = (UInt128)hostRing.NextBlockSequence +
            checked((uint)leadBlocks);
        Int128 joinTimeWide = (Int128)hostRing.NextBlockStartSimTimeNs +
            (Int128)leadBlocks * WaveformBlockAssembler.BlockDurationNs;
        Int128 prerollFromWide = joinTimeWide - requiredRenderHistoryNs;
        if (joinSequenceWide > ulong.MaxValue ||
            joinTimeWide > long.MaxValue ||
            prerollFromWide < 0)
        {
            throw Error("WaveformRecovery.StateOverflow", nameof(hostRing));
        }

        int availableBlocks = Math.Min(
            WaveformBlockRing.HostRetentionBlockCount,
            checked(hostRing.Count + leadBlocks));
        ulong availableHistoryNs = checked(
            (ulong)availableBlocks * WaveformBlockAssembler.BlockDurationNs);
        if (availableHistoryNs < requiredRenderHistoryNs)
        {
            throw new InvalidOperationException(
                "recovery boundary cannot satisfy required render history");
        }

        return new WaveformRecoveryPlan(
            WaveformRecoveryMode.SnapshotThenJoin,
            reason,
            Array.Empty<WaveformRetainedBlock>(),
            new WaveformJoinBoundary(
                (ulong)joinSequenceWide,
                (long)joinTimeWide,
                (long)prerollFromWide,
                requiredRenderHistoryNs,
                availableHistoryNs));
    }

    private static void ValidateRequest(
        WaveformBlockRing hostRing,
        WaveformRecoveryRequest request)
    {
        if (hostRing.Capacity != WaveformBlockRing.HostRetentionBlockCount)
        {
            throw Error("WaveformRecovery.HostRingRequired", nameof(hostRing));
        }

        if (request.SessionId == Guid.Empty ||
            request.InstanceId == Guid.Empty ||
            request.RequiredRenderHistoryNs > MaximumRequiredRenderHistoryNs ||
            request.AvailableRenderHistoryNs > MaximumRequiredRenderHistoryNs ||
            request.RequiredRenderHistoryNs %
                WaveformBlockAssembler.BlockDurationNs != 0 ||
            request.AvailableRenderHistoryNs %
                WaveformBlockAssembler.BlockDurationNs != 0)
        {
            throw Error("WaveformRecovery.InvalidRequest", nameof(request));
        }

        if (request.SessionId != hostRing.SessionId ||
            request.InstanceId != hostRing.InstanceId)
        {
            throw Error("WaveformRecovery.IdentityMismatch", nameof(request));
        }
    }

    private static WaveformRecoveryException Error(string reasonCode, string parameterName) =>
        new(reasonCode, parameterName);
}
