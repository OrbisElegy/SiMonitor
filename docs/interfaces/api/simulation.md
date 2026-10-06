# Monitor.Simulation 公开声明索引

[接口总览](../README.md)

本页按源码路径列出当前工作区中对程序集外可见的 C# `public` 类型和显式声明的公开成员，保留参数、默认值、单位命名、泛型约束及枚举值。接口中省略 `public` 的成员也包括在内。方法体、属性实现与非 const 字段初始化值已省略；省略实现的声明用于查阅，不能直接当作可编译代码。

位置 record 的参数也定义其同名属性；编译器合成的构造器、相等性方法以及继承成员不重复展开。`internal` 类型即使有 `public` 成员也不属于本索引。按开发构建的预处理分支读取；桌面产品构建差异见[桌面与命令行入口](../desktop-tools.md)。行为约束和集成顺序见总览中的分模块文档。

## Acquisition/PeriodicSignalGenerator.cs

源码：[PeriodicSignalGenerator.cs](../../../src/Monitor.Simulation/Acquisition/PeriodicSignalGenerator.cs) · 命名空间：`Monitor.Simulation.Acquisition`

```csharp
public sealed class PeriodicSignalGeneratorException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record PeriodicSignalPlan(string ProfileId, ulong StreamEpoch, long EpochAnchorSimTimeNs, ulong InitialPhaseU64, ulong PhaseIncrementU64, IReadOnlyList<long> TableQ32)
{
}
public sealed record PeriodicSignalState(PeriodicSignalPlan Plan, SignalSampleClockState Clock)
{
}
public readonly record struct GeneratedSignalSample(SignalSampleTick Tick, ulong PhaseU64, long ValueQ32, short NormalizedValue)
{
}
public sealed class PeriodicSignalGenerator
{
    public const int MaximumBatchSampleCount = 1_000_000;
    public static PeriodicSignalGenerator Start(PeriodicSignalPlan plan);
    public PeriodicSignalState CaptureState();
    public GeneratedSignalSample EvaluateAt(ulong sampleIndex);
    public IReadOnlyList<GeneratedSignalSample> EvaluateRange(ulong firstSampleIndex, int sampleCount, CancellationToken cancellationToken = default);
    public static PeriodicSignalGenerator Restore(PeriodicSignalState state);
    public IReadOnlyList<GeneratedSignalSample> GenerateBefore(long exclusiveSimTimeNs, int maximumSamples, CancellationToken cancellationToken = default);
}
```

## Acquisition/PeriodicWaveformGroup.cs

源码：[PeriodicWaveformGroup.cs](../../../src/Monitor.Simulation/Acquisition/PeriodicWaveformGroup.cs) · 命名空间：`Monitor.Simulation.Acquisition`

```csharp
public sealed class PeriodicWaveformGroupException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record PeriodicWaveformChannelPlan(PeriodicSignalPlan Source, WaveformBlockPlaneConfiguration Plane, int DelayCapacity, uint QualityFlags)
{
}
public sealed record PeriodicWaveformChannelState(Guid ChannelId, PeriodicSignalState Generator, SignalAcquisitionDelayState Delay, uint QualityFlags)
{
}
public sealed record PeriodicWaveformGroupState(IReadOnlyList<PeriodicWaveformChannelState> Channels, WaveformBlockAssemblerState Assembler)
{
}
public sealed class PeriodicWaveformGroup
{
    public static PeriodicWaveformGroup Start(Guid sessionId, Guid instanceId, ulong timebaseEpoch, ulong configurationRevision, ulong firstBlockSequence, int maximumBufferedBlocks, IReadOnlyList<PeriodicWaveformChannelPlan> channels);
    public PeriodicWaveformGroupState CaptureState();
    public static PeriodicWaveformGroup Restore(PeriodicWaveformGroupState state);
    public IReadOnlyList<byte[]> AdvanceTo(long simTimeNs, int maximumSamplesPerChannel, int maximumBlocks, CancellationToken cancellationToken = default);
}
```

## Acquisition/PeriodicWaveformPipeline.cs

源码：[PeriodicWaveformPipeline.cs](../../../src/Monitor.Simulation/Acquisition/PeriodicWaveformPipeline.cs) · 命名空间：`Monitor.Simulation.Acquisition`

```csharp
public sealed class PeriodicWaveformPipelineException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record PeriodicWaveformPipelineState(PeriodicSignalState Generator, SignalAcquisitionDelayState Delay, WaveformBlockAssemblerState Assembler, uint QualityFlags)
{
}
public sealed class PeriodicWaveformPipeline
{
    public static PeriodicWaveformPipeline Start(PeriodicSignalPlan source, Guid sessionId, Guid instanceId, ulong timebaseEpoch, ulong configurationRevision, ulong firstBlockSequence, WaveformBlockPlaneConfiguration channel, int delayCapacity, uint qualityFlags);
    public PeriodicWaveformPipelineState CaptureState();
    public static PeriodicWaveformPipeline Restore(PeriodicWaveformPipelineState state);
    public IReadOnlyList<byte[]> AdvanceTo(long simTimeNs, int maximumSamples, int maximumBlocks, CancellationToken cancellationToken = default);
}
```

## Acquisition/SensorFaultStateMachine.cs

源码：[SensorFaultStateMachine.cs](../../../src/Monitor.Simulation/Acquisition/SensorFaultStateMachine.cs) · 命名空间：`Monitor.Simulation.Acquisition`

```csharp
public sealed class SensorFaultException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed class SensorProfileDescriptor
{
    public string ProfileId { get; }
    public string SensorClass { get; }
    public IReadOnlyList<string> CompatibleCategories { get; }
    public IReadOnlyList<string> FailureStates { get; }
    public int FailureStateRank(string failureState);
}
public static class FrozenSensorProfiles
{
    public static SensorProfileDescriptor Get(string profileId);
}
public enum SensorFaultOperation
{
    Start,
    Stop,
}
public enum SensorFaultOrigin
{
    ScenarioOverride,
    SensorModel,
    LatentPhysiology,
}
public sealed record ActiveSensorFault(string FailureState, SensorFaultOrigin Origin, long ActivatedAtSimTimeNs, ulong ActivatedAtRevision)
{
}
public sealed record SensorFaultState(string ProfileId, string SensorInstanceId, ulong Revision, long CursorSimTimeNs, IReadOnlyList<ActiveSensorFault> ActiveFaults)
{
}
public sealed record SensorFaultTransition(string ProfileId, string SensorInstanceId, SensorFaultOperation Operation, string FailureState, SensorFaultOrigin Origin, long SimTimeNs, ulong PreviousRevision, ulong Revision)
{
}
public sealed class SensorFaultStateMachine
{
    public string ProfileId { get; }
    public string SensorInstanceId { get; }
    public ulong Revision { get; private set; }
    public long CursorSimTimeNs { get; private set; }
    public IReadOnlyList<ActiveSensorFault> ActiveFaults { get; }
    public static SensorFaultStateMachine Start(string profileId, string sensorInstanceId, long startSimTimeNs);
    public static SensorFaultStateMachine Restore(SensorFaultState state);
    public SensorFaultTransition Apply(SensorFaultOperation operation, string failureState, SensorFaultOrigin origin, long simTimeNs, ulong expectedRevision);
    public SensorFaultState CaptureState();
}
```

## Acquisition/SignalAcquisitionDelayLine.cs

源码：[SignalAcquisitionDelayLine.cs](../../../src/Monitor.Simulation/Acquisition/SignalAcquisitionDelayLine.cs) · 命名空间：`Monitor.Simulation.Acquisition`

```csharp
public sealed class SignalAcquisitionDelayException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public readonly record struct DelayedSignalSample(string ProfileId, ulong StreamEpoch, ulong SampleIndex, long SourceSimTimeNs, long AvailableSimTimeNs, short NormalizedValue, uint QualityFlags)
{
}
public sealed record SignalAcquisitionDelayState(string ProfileId, ulong StreamEpoch, long EpochAnchorSimTimeNs, ulong NextInputSampleIndex, long ReleaseCursorSimTimeNs, int Capacity, IReadOnlyList<DelayedSignalSample> PendingSamples)
{
}
public sealed class SignalAcquisitionDelayLine
{
    public const int MaximumCapacity = 1_000_000;
    public string ProfileId { get; }
    public ulong StreamEpoch { get; }
    public long EpochAnchorSimTimeNs { get; }
    public ulong NextInputSampleIndex { get; private set; }
    public long ReleaseCursorSimTimeNs { get; private set; }
    public int Capacity { get; }
    public int PendingCount { get; }
    public long LatencyNs { get; }
    public static SignalAcquisitionDelayLine Start(string profileId, ulong streamEpoch, long epochAnchorSimTimeNs, int capacity);
    public static SignalAcquisitionDelayLine Restore(SignalAcquisitionDelayState state);
    public void Enqueue(SignalSampleTick tick, short normalizedValue, uint qualityFlags = 0);
    public IReadOnlyList<DelayedSignalSample> DrainAvailable(long simTimeNs);
    public SignalAcquisitionDelayState CaptureState();
}
```

## Acquisition/SignalSampleClock.cs

源码：[SignalSampleClock.cs](../../../src/Monitor.Simulation/Acquisition/SignalSampleClock.cs) · 命名空间：`Monitor.Simulation.Acquisition`

```csharp
public sealed class SignalSampleClockException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed class SignalAcquisitionProfileDescriptor
{
    public string ProfileId { get; }
    public string ChannelClass { get; }
    public uint SampleRateHz { get; }
    public long LatencyNs { get; }
    public string FilterPrimitive { get; }
    public string Quantization { get; }
    public string EvidenceStatus { get; }
    public long SamplePeriodNs { get; }
}
public static class FrozenSignalAcquisitionProfiles
{
    public static SignalAcquisitionProfileDescriptor Get(string profileId);
}
public readonly record struct SignalSampleTick(string ProfileId, ulong StreamEpoch, ulong SampleIndex, long SimTimeNs)
{
}
public enum FilterStateTransitionPolicy
{
    Reacquire,
    Clear,
    MigrateValidated,
}
public sealed record SignalProfileBoundary(string PreviousProfileId, string NextProfileId, ulong PreviousStreamEpoch, ulong NextStreamEpoch, long FirstSampleSimTimeNs, FilterStateTransitionPolicy FilterStateTransitionPolicy)
{
}
public sealed record SignalSampleClockState(string ProfileId, ulong StreamEpoch, long EpochAnchorSimTimeNs, ulong NextSampleIndex, long NextSampleSimTimeNs, long CursorSimTimeNs)
{
}
public sealed class SignalSampleClock
{
    public string ProfileId { get; private set; }
    public ulong StreamEpoch { get; private set; }
    public long EpochAnchorSimTimeNs { get; private set; }
    public ulong NextSampleIndex { get; private set; }
    public long NextSampleSimTimeNs { get; private set; }
    public long CursorSimTimeNs { get; private set; }
    public uint SampleRateHz { get; }
    public long SamplePeriodNs { get; }
    public static SignalSampleClock Start(string profileId, ulong streamEpoch, long startSimTimeNs);
    public static SignalSampleClock Restore(SignalSampleClockState state);
    public IReadOnlyList<SignalSampleTick> DrainBefore(long exclusiveSimTimeNs);
    public SignalProfileBoundary ApplyProfileBoundary(string nextProfileId, ulong nextStreamEpoch, long boundarySimTimeNs, FilterStateTransitionPolicy filterStateTransitionPolicy, bool standardEcgRecordActive);
    public SignalSampleClockState CaptureState();
}
```

## Acquisition/WaveformBlockAssembler.cs

源码：[WaveformBlockAssembler.cs](../../../src/Monitor.Simulation/Acquisition/WaveformBlockAssembler.cs) · 命名空间：`Monitor.Simulation.Acquisition`

```csharp
public sealed class WaveformBlockAssemblerException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record WaveformBlockPlaneConfiguration(Guid ChannelId, string ProfileId, int ScaleNumerator, uint ScaleDenominator, int OffsetNumerator, uint OffsetDenominator)
{
}
public sealed record WaveformBlockPlaneState(WaveformBlockPlaneConfiguration Configuration, ulong NextInputSampleIndex, IReadOnlyList<DelayedSignalSample> PendingSamples)
{
}
public sealed record WaveformBlockAssemblerState(Guid SessionId, Guid InstanceId, ulong TimebaseEpoch, ulong StreamEpoch, ulong ConfigurationRevision, ulong NextBlockSequence, long EpochAnchorSimTimeNs, long NextBlockStartSimTimeNs, int MaximumBufferedBlocks, IReadOnlyList<WaveformBlockPlaneState> Planes)
{
}
public sealed class WaveformBlockAssembler
{
    public const uint BlockDurationNs = 200_000_000;
    public const int MaximumBufferedBlockCount = 300;
    public Guid SessionId { get; }
    public Guid InstanceId { get; }
    public ulong TimebaseEpoch { get; }
    public ulong StreamEpoch { get; }
    public ulong ConfigurationRevision { get; }
    public ulong NextBlockSequence { get; private set; }
    public long EpochAnchorSimTimeNs { get; }
    public long NextBlockStartSimTimeNs { get; private set; }
    public int MaximumBufferedBlocks { get; }
    public static WaveformBlockAssembler Start(Guid sessionId, Guid instanceId, ulong timebaseEpoch, ulong streamEpoch, ulong configurationRevision, ulong firstBlockSequence, long epochAnchorSimTimeNs, int maximumBufferedBlocks, IReadOnlyList<WaveformBlockPlaneConfiguration> planes);
    public static WaveformBlockAssembler Restore(WaveformBlockAssemblerState state);
    public IReadOnlyList<WaveformEnvelope> Push(Guid channelId, IReadOnlyList<DelayedSignalSample> samples);
    public WaveformBlockAssemblerState CaptureState();
}
```

## Acquisition/WaveformBlockRing.cs

源码：[WaveformBlockRing.cs](../../../src/Monitor.Simulation/Acquisition/WaveformBlockRing.cs) · 命名空间：`Monitor.Simulation.Acquisition`

```csharp
public sealed class WaveformBlockRingException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public enum WaveformBlockAppendStatus
{
    Appended,
    AlreadyPresent,
}
public enum WaveformBlockReplayStatus
{
    Available,
    AwaitFutureBlock,
    RecoverySnapshotRequired,
}
public readonly record struct WaveformBlockAppendResult(WaveformBlockAppendStatus Status, ulong BlockSequence, ulong? EvictedBlockSequence)
{
}
public sealed record WaveformRetainedBlock(ulong BlockSequence, ulong ConfigurationRevision, long StartSimTimeNs, string ContentSha256, byte[] RawEnvelope)
{
}
public sealed record WaveformBlockReplayResult(WaveformBlockReplayStatus Status, IReadOnlyList<WaveformRetainedBlock> Blocks)
{
}
public sealed record WaveformBlockRingState(Guid SessionId, Guid InstanceId, ulong TimebaseEpoch, ulong StreamEpoch, int Capacity, ulong NextBlockSequence, long NextBlockStartSimTimeNs, IReadOnlyList<byte[]> RetainedRawBlocks)
{
}
public sealed class WaveformBlockRing
{
    public const int ClientHistoryBlockCount = 50;
    public const int HostRetentionBlockCount = 300;
    public const int MaximumCapacity = HostRetentionBlockCount;
    public Guid SessionId { get; }
    public Guid InstanceId { get; }
    public ulong TimebaseEpoch { get; }
    public ulong StreamEpoch { get; }
    public int Capacity { get; }
    public int Count { get; }
    public ulong NextBlockSequence { get; private set; }
    public long NextBlockStartSimTimeNs { get; private set; }
    public ulong OldestBlockSequence { get; }
    public static WaveformBlockRing Start(Guid sessionId, Guid instanceId, ulong timebaseEpoch, ulong streamEpoch, ulong firstBlockSequence, long firstBlockStartSimTimeNs, int capacity);
    public static WaveformBlockRing Restore(WaveformBlockRingState state);
    public WaveformBlockAppendResult Append(ReadOnlySpan<byte> rawEnvelope);
    public WaveformBlockReplayResult ReadFrom(ulong firstBlockSequence, int maximumBlocks);
    public WaveformBlockRingState CaptureState();
}
```

## Acquisition/WaveformEnvelopeCodec.cs

源码：[WaveformEnvelopeCodec.cs](../../../src/Monitor.Simulation/Acquisition/WaveformEnvelopeCodec.cs) · 命名空间：`Monitor.Simulation.Acquisition`

```csharp
public sealed class WaveformEnvelopeException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public enum WaveformQualityEncoding : byte
{
    None = 0,
    Ranges = 1,
}
public readonly record struct WaveformQualityRange(uint FirstSampleOffset, uint Count, uint QualityFlags)
{
}
public sealed record WaveformPlane(Guid ChannelId, uint SampleRateNumerator, uint SampleRateDenominator, ulong FirstSampleIndex, int ScaleNumerator, uint ScaleDenominator, int OffsetNumerator, uint OffsetDenominator, WaveformQualityEncoding QualityEncoding, IReadOnlyList<short> Samples, IReadOnlyList<WaveformQualityRange> QualityRanges)
{
}
public sealed record WaveformEnvelope(Guid SessionId, Guid InstanceId, ulong TimebaseEpoch, ulong StreamEpoch, ulong BlockSequence, ulong ConfigurationRevision, long StartSimTimeNs, uint DurationNs, IReadOnlyList<WaveformPlane> Planes)
{
}
public static class WaveformEnvelopeCodec
{
    public const int PreludeSize = 32;
    public const int FixedHeaderSize = 112;
    public const int PlaneDescriptorSize = 72;
    public const int QualityRangeSize = 12;
    public const int MaximumHeaderSize = 16_384;
    public const int MaximumPayloadSize = 262_144;
    public const int MaximumPlaneCount = 128;
    public const int ContentSha256Offset = PreludeSize + 80;
    public static byte[] EncodeRaw(WaveformEnvelope envelope);
    public static WaveformEnvelope Decode(ReadOnlySpan<byte> wire);
}
```

## Acquisition/WaveformRecordArchive.cs

源码：[WaveformRecordArchive.cs](../../../src/Monitor.Simulation/Acquisition/WaveformRecordArchive.cs) · 命名空间：`Monitor.Simulation.Acquisition`

```csharp
public sealed class WaveformRecordArchiveException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record WaveformRecordArchivePlan(string GroupId, string RecordRef, ulong SweepEpoch, Guid SessionId, Guid InstanceId, ulong TimebaseEpoch, long EpochAnchorSimTimeNs, ulong StreamEpoch, ulong ConfigurationRevision, long RecordStartSimTimeNs, long RecordEndExclusiveSimTimeNs, IReadOnlyList<Guid> ChannelIds)
{
}
public sealed record ArchivedWaveformBlock(ulong BlockSequence, long StartSimTimeNs, string ContentSha256, byte[] RawEnvelope)
{
}
public sealed record WaveformRecordArchiveState(WaveformRecordArchivePlan Plan, IReadOnlyList<byte[]> RawEnvelopes)
{
}
public sealed partial class WaveformRecordArchive
{
    public const int StandardEcgChannelCount = 12;
    public const int MaximumRecordBlockCount = WaveformBlockRing.HostRetentionBlockCount;
    public string RecordRef { get; }
    public int BlockCount { get; }
    public WaveformRecordArchivePlan CapturePlan();
    public long RecordStartSimTimeNs { get; }
    public long RecordEndExclusiveSimTimeNs { get; }
    public static WaveformRecordArchive Create(WaveformRecordArchivePlan plan, IReadOnlyList<byte[]> rawEnvelopes);
    public static WaveformRecordArchive Restore(WaveformRecordArchiveState state);
    public IReadOnlyList<ArchivedWaveformBlock> ReadBlocks();
    public WaveformRecordArchiveState CaptureState();
}
```

## Acquisition/WaveformRecordArchiveChannelRead.cs

源码：[WaveformRecordArchiveChannelRead.cs](../../../src/Monitor.Simulation/Acquisition/WaveformRecordArchiveChannelRead.cs) · 命名空间：`Monitor.Simulation.Acquisition`

```csharp
public sealed record ArchivedWaveformPlaneBlock(ulong BlockSequence, long OriginalStartSimTimeNs, string OriginalContentSha256, WaveformPlane Plane)
{
}
public sealed record ArchivedWaveformChannelRead(WaveformRecordArchivePlan ArchivePlan, Guid ChannelId, long StartSimTimeNs, long EndExclusiveSimTimeNs, IReadOnlyList<ArchivedWaveformPlaneBlock> Blocks)
{
}
public sealed record ArchivedWaveformChannelShape(Guid ChannelId, uint SampleRateNumerator, uint SampleRateDenominator, int ScaleNumerator, uint ScaleDenominator, int OffsetNumerator, uint OffsetDenominator)
{
}
public sealed partial class WaveformRecordArchive
{
    public ArchivedWaveformChannelShape ReadChannelShape(Guid channelId);
    public ArchivedWaveformChannelRead ReadChannel(Guid channelId, long startSimTimeNs, long endExclusiveSimTimeNs, int maximumSamples, CancellationToken cancellationToken = default);
}
```

## Acquisition/WaveformRecoveryPlanner.cs

源码：[WaveformRecoveryPlanner.cs](../../../src/Monitor.Simulation/Acquisition/WaveformRecoveryPlanner.cs) · 命名空间：`Monitor.Simulation.Acquisition`

```csharp
public sealed class WaveformRecoveryException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
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
public sealed record WaveformRecoveryRequest(Guid SessionId, Guid InstanceId, ulong TimebaseEpoch, ulong StreamEpoch, ulong NextBlockSequence, ulong RequiredRenderHistoryNs, ulong AvailableRenderHistoryNs)
{
}
public sealed record WaveformJoinBoundary(ulong BlockSequence, long SimTimeNs, long PrerollFromSimTimeNs, ulong RequiredRenderHistoryNs, ulong AvailableRenderHistoryNs)
{
}
public sealed record WaveformRecoveryPlan(WaveformRecoveryMode Mode, WaveformRecoveryReason Reason, IReadOnlyList<WaveformRetainedBlock> ReplayBlocks, WaveformJoinBoundary? JoinBoundary)
{
}
public static class WaveformRecoveryPlanner
{
    public const ulong MinimumJoinLeadNs = 1_000_000_000;
    public const int MinimumJoinLeadBlockCount = (int)(MinimumJoinLeadNs / WaveformBlockAssembler.BlockDurationNs);
    public const ulong MaximumRequiredRenderHistoryNs = (ulong)WaveformBlockRing.HostRetentionBlockCount * WaveformBlockAssembler.BlockDurationNs;
    public static WaveformRecoveryPlan Plan(WaveformBlockRing hostRing, WaveformRecoveryRequest request);
}
```

## Acquisition/WaveformSubscriberOutbox.cs

源码：[WaveformSubscriberOutbox.cs](../../../src/Monitor.Simulation/Acquisition/WaveformSubscriberOutbox.cs) · 命名空间：`Monitor.Simulation.Acquisition`

```csharp
public sealed class WaveformSubscriberQueueException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public enum WaveformSubscriberQueueStatus
{
    Active,
    IsolatedNeedsResync,
}
public enum WaveformSubscriberIsolationReason
{
    BackpressureLimitExceeded,
    SequenceGap,
    RetentionExpired,
    StreamChanged,
    AcknowledgementOutOfOrder,
    AcknowledgementHashMismatch,
}
public enum WaveformSubscriberEnqueueStatus
{
    Enqueued,
    AlreadyQueued,
    IgnoredStale,
    AwaitingHostBlock,
    RequiresResync,
}
public enum WaveformSubscriberReadStatus
{
    Available,
    Empty,
    RequiresResync,
}
public enum WaveformSubscriberAcknowledgeStatus
{
    Acknowledged,
    IgnoredStale,
    Empty,
    RequiresResync,
}
public readonly record struct WaveformSubscriberEnqueueResult(WaveformSubscriberEnqueueStatus Status, ulong BlockSequence, WaveformSubscriberIsolationReason? IsolationReason)
{
}
public sealed record WaveformSubscriberReadResult(WaveformSubscriberReadStatus Status, WaveformRetainedBlock? Block, WaveformSubscriberIsolationReason? IsolationReason)
{
}
public readonly record struct WaveformSubscriberAcknowledgeResult(WaveformSubscriberAcknowledgeStatus Status, ulong BlockSequence, WaveformSubscriberIsolationReason? IsolationReason)
{
}
public sealed record WaveformSubscriberQueueState(Guid SubscriberId, Guid SessionId, Guid InstanceId, ulong TimebaseEpoch, ulong StreamEpoch, int Capacity, WaveformSubscriberQueueStatus Status, WaveformSubscriberIsolationReason? IsolationReason, ulong NextEnqueueBlockSequence, IReadOnlyList<ulong> PendingBlockSequences)
{
}
public sealed class WaveformSubscriberOutbox
{
    public const int MaximumCapacity = WaveformBlockRing.ClientHistoryBlockCount;
    public Guid SubscriberId { get; }
    public Guid SessionId { get; }
    public Guid InstanceId { get; }
    public ulong TimebaseEpoch { get; private set; }
    public ulong StreamEpoch { get; private set; }
    public int Capacity { get; }
    public int Count { get; }
    public WaveformSubscriberQueueStatus Status { get; private set; }
    public WaveformSubscriberIsolationReason? IsolationReason { get; private set; }
    public ulong NextEnqueueBlockSequence { get; private set; }
    public static WaveformSubscriberOutbox Start(Guid subscriberId, WaveformBlockRing hostRing, int capacity, ulong firstBlockSequence);
    public static WaveformSubscriberOutbox Restore(WaveformSubscriberQueueState state, WaveformBlockRing hostRing);
    public WaveformSubscriberEnqueueResult Enqueue(ulong blockSequence);
    public WaveformSubscriberReadResult ReadNext();
    public WaveformSubscriberAcknowledgeResult Acknowledge(ulong streamEpoch, ulong blockSequence, string contentSha256);
    public WaveformSubscriberQueueState IsolateForStreamChange();
    public WaveformSubscriberQueueState ResetAfterResync(WaveformBlockRing hostRing, ulong firstBlockSequence);
    public WaveformSubscriberQueueState CaptureState();
}
```

## Authoring/AuthoredConductionSelection.cs

源码：[AuthoredConductionSelection.cs](../../../src/Monitor.Simulation/Authoring/AuthoredConductionSelection.cs) · 命名空间：`Monitor.Simulation.Authoring`

```csharp
public static class AuthoredConductionSelection
{
    public static (int Atrial, int Conducted) Resolve(int index);
    public static AvConductionPattern Pattern(int index);
    public static int Index(int atrial, int conducted, AvConductionPattern pattern = AvConductionPattern.FixedPr);
}
```

## Authoring/IllustrationVentricularTiming.cs

源码：[IllustrationVentricularTiming.cs](../../../src/Monitor.Simulation/Authoring/IllustrationVentricularTiming.cs) · 命名空间：`Monitor.Simulation.Authoring`

```csharp
public static class IllustrationVentricularTiming
{
    public static long ResolveOffset(int? periodMilliseconds, int? offsetMilliseconds, long fallbackNs);
}
```

## Authoring/PhysiologyIllustrationConfiguration.cs

源码：[PhysiologyIllustrationConfiguration.cs](../../../src/Monitor.Simulation/Authoring/PhysiologyIllustrationConfiguration.cs) · 命名空间：`Monitor.Simulation.Authoring`

```csharp
public sealed record PhysiologyIllustrationConfiguration(int BreathPeriodMilliseconds, int InspirationMilliseconds, int RespAmplitudeCounts, int? Co2PlateauStartCentiMmHg = null, int Co2BaselineMmHg = 0, int Co2EndExpiratoryMmHg = 40, int Co2DeadSpaceMilliseconds = 125, int Co2RiseMilliseconds = 250, int Co2FallMilliseconds = 200, int Co2TransportDelayMilliseconds = 0, int Co2DispersionStepMilliseconds = 0, int InspiratoryPauseMilliseconds = 0, int ExpiratoryPauseMilliseconds = 0, int RespCardiacArtifactCounts = 0, RespiratoryActivity RespiratoryActivity = RespiratoryActivity.Breathing, int? ActivityAfterBreaths = null, int? ActivityDurationBreaths = null, int VentricularConductionRatio = 1, CardiacActivity CardiacActivity = CardiacActivity.AtrialAndVentricular, bool VentricularMechanicalEnabled = true, int? MechanicalAfterCycles = null, int? MechanicalDurationCycles = null, int MechanicalEveryCycles = 1, bool UseVascularReservoir = false, int? IndependentVentricularPeriodMilliseconds = null, int? IndependentVentricularOffsetMilliseconds = null, RespiratoryPattern RespiratoryPattern = RespiratoryPattern.Regular, int ConductedBeatsPerGroup = 1, AvConductionPattern ConductionPattern = AvConductionPattern.FixedPr, EcgBundleBlockIllustration BundleBlock = EcgBundleBlockIllustration.Reference, bool IllustrateAfSystemicPulseDeficit = false, bool IllustrateAfAberrancy = false, bool Wpw = false, bool WpwNegativeV1 = false, bool ShortPr = false, bool NormalPrDelta = false, bool WpwSmallerDelta = false, bool ProlongedPrDelta = false, bool Svt = false, bool Vt = false, bool VtFusion = false, bool VtCapture = false, bool VtBidirectional = false, bool VtTwisting = false, bool Aivr = false, bool Ajr = false, bool Aar = false, bool SvtRbbb = false, bool SvtLbbb = false, bool AivrFusion = false, bool AivrCapture = false, bool AtrialEscape = false)
{
    public EcgChestInfarctionPlan? Infarction { get; init; }
    public EcgInfarctionZones? Zones { get; init; }
    public EcgTContourPlan? TContour { get; init; }
    public EcgVentricularIllustration VentricularShape { get; init; }
    public EcgAtrialIllustration AtrialShape { get; init; }
    public QuinidineIllustration Quinidine { get; init; }
    public bool QuinidineNotchedP { get; init; }
    public bool DigitalisEffect { get; init; }
    public DigitalisTShape DigitalisShape { get; init; }
    public CalciumIllustration Calcium { get; init; }
    public bool HypokalemiaRepolarization { get; init; }
    public bool HypokalemiaInvertedT { get; init; }
    public bool HypokalemiaTuFusion { get; init; }
    public bool HypokalemiaConduction { get; init; }
    public bool HyperkalemiaRepolarization { get; init; }
    public bool HyperkalemiaConduction { get; init; }
    public bool HyperkalemiaAbsentP { get; init; }
    public bool HyperkalemiaFusion { get; init; }
    public int AbpPulsePermille { get; init; }
    public int PaPulsePermille { get; init; }
    public int CvpBaselineCentiMmHg { get; init; }
    public VascularPressureTarget? AbpTarget { get; init; }
    public VascularPressureTarget? PaTarget { get; init; }
    public VascularPressureVariation? PressureVariation { get; init; }
    public SeededExpirationPressure? SeededCo2 { get; init; }
    public SeededCardiacRate? SeededRate { get; init; }
    public CardiacRateAdjustment? RateAdjustment { get; init; }
    public SeededRhythmSchedule? RhythmSchedule { get; init; }
    public static PhysiologyIllustrationConfiguration Default { get; }
    public static PhysiologyIllustrationConfiguration SinusArrhythmiaPreset { get; }
    public static PhysiologyIllustrationConfiguration SinusArrestPreset { get; }
    public static PhysiologyIllustrationConfiguration AtrialEscapePreset { get; }
    public static PhysiologyIllustrationConfiguration AarPreset { get; }
    public static PhysiologyIllustrationConfiguration AjrPreset { get; }
    public static PhysiologyIllustrationConfiguration AivrPreset { get; }
    public static PhysiologyIllustrationConfiguration VtPreset { get; }
    public static PhysiologyIllustrationConfiguration SvtPreset { get; }
    public static PhysiologyIllustrationConfiguration NormalPrDeltaPreset { get; }
    public static PhysiologyIllustrationConfiguration ShortPrPreset { get; }
    public static PhysiologyIllustrationConfiguration WpwPreset { get; }
    public static PhysiologyIllustrationConfiguration PrematureAtrial { get; }
    public static PhysiologyIllustrationConfiguration BlockedPrematureAtrial { get; }
    public static PhysiologyIllustrationConfiguration AberrantPrematureAtrial { get; }
    public static PhysiologyIllustrationConfiguration PrematureVentricular { get; }
    public static PhysiologyIllustrationConfiguration PrematureJunctional { get; }
    public static PhysiologyIllustrationConfiguration JunctionalEscape { get; }
    public static PhysiologyIllustrationConfiguration VentricularEscape { get; }
    public static PhysiologyIllustrationConfiguration VariableFlutter { get; }
    public static PhysiologyIllustrationConfiguration Flutter(int ratio);
    public static PhysiologyIllustrationConfiguration Disorganized(AvConductionPattern pattern);
    public static PhysiologyIllustrationConfiguration Fibrillation(bool fine = false);
    public CapnogramPlan ResolveCapnogram();
    public RegularPhysiologyPlan ResolvePlan();
}
```

## Authoring/PhysiologyIllustrationSource.cs

源码：[PhysiologyIllustrationSource.cs](../../../src/Monitor.Simulation/Authoring/PhysiologyIllustrationSource.cs) · 命名空间：`Monitor.Simulation.Authoring`

```csharp
public static class PhysiologyIllustrationSource
{
    public static Guid ChannelId(int row);
    public static PhysiologyTransportSource CreateTransport(PhysiologyIllustrationConfiguration configuration, VentilationTransportPlan ventilation, int referenceStrokeVolumeMicroliters, long ejectionDurationNs = 240_000_000);
    public static PhysiologyWaveformGroup Create(PhysiologyIllustrationConfiguration? configuration = null, int abpZeroOffsetCentiMmHg = 0, int paZeroOffsetCentiMmHg = 0, int cvpZeroOffsetCentiMmHg = 0, VentilationTransportPlan? ventilation = null);
}
```

## Authoring/PulseOximeterIllustrationSource.cs

源码：[PulseOximeterIllustrationSource.cs](../../../src/Monitor.Simulation/Authoring/PulseOximeterIllustrationSource.cs) · 命名空间：`Monitor.Simulation.Authoring`

```csharp
public sealed class PulseOximeterIllustrationSource
{
    public const string ModelId = "PulseOximeterAttenuationIllustration@2";
    public static Guid RedChannelId { get; }
    public static Guid InfraredChannelId { get; }
    public PulseOximeterIllustrationSource(Guid plethChannel, Guid acquisitionInstance, Guid sensorInstance, IArterialOxygenationSource oxygenation, int modulationPermille = 1000);
    public PulseOximeterIllustrationSource(Guid plethChannel, Guid acquisitionInstance, Guid sensorInstance, int saturationMilliPercent, int modulationPermille = 1000, SeededOpticalSaturation? variation = null);
    public byte[] ConvertAcquiredPulse(ReadOnlySpan<byte> wire);
}
```

## Authoring/SeededOpticalSaturation.cs

源码：[SeededOpticalSaturation.cs](../../../src/Monitor.Simulation/Authoring/SeededOpticalSaturation.cs) · 命名空间：`Monitor.Simulation.Authoring`

```csharp
public sealed class SeededOpticalSaturation
{
    public const string PatternId = "SeededOpticalExcursions@4";
    public const long KnotPeriodNs = 30_000_000_000;
    public const int KnotCount = 64;
    public int TargetMilliPercent { get; }
    public int AmplitudeMilliPercent { get; }
    public DeterministicStreamState PreparedState { get; }
    public SeededOpticalSaturation(int targetMilliPercent, int amplitudeMilliPercent, string seedHex);
    public int At(long sampleTimeNs);
}
```

## Authoring/VascularPressureVariation.cs

源码：[VascularPressureVariation.cs](../../../src/Monitor.Simulation/Authoring/VascularPressureVariation.cs) · 命名空间：`Monitor.Simulation.Authoring`

```csharp
public sealed record VascularPressureVariation(int AbpAmplitudeCentiMmHg, int PaAmplitudeCentiMmHg, string SeedHex)
{
    public static (int MinimumCentiMmHg, int MaximumCentiMmHg) AbpAmplitudeRange { get; }
    public static (int MinimumCentiMmHg, int MaximumCentiMmHg) PaAmplitudeRange { get; }
}
```

## Authoring/VascularPressureTarget.cs

源码：[VascularPressureTarget.cs](../../../src/Monitor.Simulation/Authoring/VascularPressureTarget.cs) · 命名空间：`Monitor.Simulation.Authoring`

```csharp
public sealed record VascularPressureTarget(int SystolicCentiMmHg, int DiastolicCentiMmHg)
{
    public static (int MinimumCentiMmHg, int MaximumCentiMmHg) ArterialSystolicRange { get; }
    public static (int MinimumCentiMmHg, int MaximumCentiMmHg) ArterialDiastolicRange { get; }
    public static (int MinimumCentiMmHg, int MaximumCentiMmHg) PulmonarySystolicRange { get; }
    public static (int MinimumCentiMmHg, int MaximumCentiMmHg) PulmonaryDiastolicRange { get; }
    public const int MinimumPulseCentiMmHg = 500;
}
```

## Determinism/DeterminismCheckpointCodec.cs

源码：[DeterminismCheckpointCodec.cs](../../../src/Monitor.Simulation/Determinism/DeterminismCheckpointCodec.cs) · 命名空间：`Monitor.Simulation.Determinism`

```csharp
public sealed record DeterminismCheckpoint(long SimTimeNs, ulong PhaseU64, long FilterYQ32, IReadOnlyList<DeterministicStreamState> Streams)
{
}
public sealed class DeterminismCheckpointException(string reasonCode) : Exception(reasonCode)
{
    public string ReasonCode { get; }
}
public sealed class DeterminismCheckpointCodec
{
    public DeterminismCheckpointCodec(ReadOnlySpan<byte> profileContentSha256);
    public byte[] Serialize(DeterminismCheckpoint checkpoint);
    public DeterminismCheckpoint Restore(ReadOnlySpan<byte> bytes);
}
```

## Determinism/DeterminismConformanceKernel.cs

源码：[DeterminismConformanceKernel.cs](../../../src/Monitor.Simulation/Determinism/DeterminismConformanceKernel.cs) · 命名空间：`Monitor.Simulation.Determinism`

```csharp
public readonly record struct DeterminismStepRecord(long SimTimeNs, ulong PhaseU64, long FilterYQ32, long TargetQ32, ulong OutcomeU64)
{
    public const int EncodedSize = 40;
    public void WriteTo(Span<byte> destination);
}
public sealed class DeterminismConformanceKernel
{
    public const long StepDurationNs = 4_000_000;
    public long SimTimeNs { get; private set; }
    public ulong PhaseU64 { get; private set; }
    public long FilterYQ32 { get; private set; }
    public ulong StepIndex { get; }
    public static DeterminismConformanceKernel Create(ReadOnlySpan<byte> rootSeed);
    public static DeterminismConformanceKernel CreateFromLowercaseHex(string rootSeedHex);
    public static DeterminismConformanceKernel Restore(DeterminismCheckpoint checkpoint);
    public DeterminismStepRecord Step();
    public byte[] Run(int stepCount);
    public DeterminismCheckpoint CaptureCheckpoint();
}
```

## Determinism/DeterminismExceptions.cs

源码：[DeterminismExceptions.cs](../../../src/Monitor.Simulation/Determinism/DeterminismExceptions.cs) · 命名空间：`Monitor.Simulation.Determinism`

```csharp
public sealed class DeterminismArithmeticException(string reasonCode) : ArithmeticException(reasonCode)
{
    public string ReasonCode { get; }
}
public sealed class DeterminismConfigurationException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
```

## Determinism/DeterministicStream.cs

源码：[DeterministicStream.cs](../../../src/Monitor.Simulation/Determinism/DeterministicStream.cs) · 命名空间：`Monitor.Simulation.Determinism`

```csharp
public readonly record struct DeterministicStreamState(string StreamId, ulong S0, ulong S1, ulong S2, ulong S3, ulong DrawCount)
{
}
public sealed class DeterministicRandomSource
{
    public string StreamId { get; }
    public ulong DrawCount { get; private set; }
    public static DeterministicRandomSource Restore(DeterministicStreamState state);
    public ulong NextUInt64();
    public ulong UniformBelow(ulong bound);
    public long NextNormal12CenteredQ32();
    public DeterministicStreamState CaptureState();
}
public sealed class DeterministicStreamFactory : IDisposable
{
    public const int RootSeedSize = 32;
    public DeterministicStreamFactory(ReadOnlySpan<byte> rootSeed);
    public static DeterministicStreamFactory FromLowercaseHex(string rootSeedHex);
    public DeterministicRandomSource CreateStream(string streamId);
    public void Dispose();
}
```

## Determinism/FixedPointPrimitives.cs

源码：[FixedPointPrimitives.cs](../../../src/Monitor.Simulation/Determinism/FixedPointPrimitives.cs) · 命名空间：`Monitor.Simulation.Determinism`

```csharp
public readonly record struct SaturatingInt64(long Value, bool Saturated)
{
}
public static class FixedPointMath
{
    public const long Q32One = 1L << 32;
    public const long Q62One = 1L << 62;
    public static Int128 RoundDivideTiesToEven(Int128 numerator, Int128 denominator);
    public static SaturatingInt64 Saturate(Int128 value);
    public static SaturatingInt64 Add(long left, long right);
    public static SaturatingInt64 Subtract(long left, long right);
    public static SaturatingInt64 MultiplyQ32(long leftQ32, long rightQ32);
    public static SaturatingInt64 DivideQ32(long numeratorQ32, long denominatorQ32);
    public static SaturatingInt64 MultiplyCoefficientQ62(long coefficientQ62, long valueQ32);
}
public static class PeriodicLutLinear
{
    public static SaturatingInt64 Interpolate(ReadOnlySpan<long> tableQ32, ulong phaseU64);
}
public readonly record struct FixedBiquadCoefficients(long B0Q62, long B1Q62, long B2Q62, long A1Q62, long A2Q62)
{
}
public readonly record struct FixedBiquadState(long Z1Q32, long Z2Q32)
{
}
public readonly record struct FixedBiquadStep(long OutputQ32, FixedBiquadState State, bool Saturated)
{
}
public static class FixedBiquadDf2T
{
    public static FixedBiquadStep Step(FixedBiquadState state, FixedBiquadCoefficients coefficients, long inputQ32);
}
```

## Physiology/AcceleratedAtrialReference.cs

源码：[AcceleratedAtrialReference.cs](../../../src/Monitor.Simulation/Physiology/AcceleratedAtrialReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class AcceleratedAtrialReference
{
    public const string EvidenceId = "AcceleratedAtrialIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
}
```

## Physiology/AcceleratedJunctionalReference.cs

源码：[AcceleratedJunctionalReference.cs](../../../src/Monitor.Simulation/Physiology/AcceleratedJunctionalReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class AcceleratedJunctionalReference
{
    public const string EvidenceId = "AcceleratedJunctionalIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
}
```

## Physiology/AcceleratedVentricularReference.cs

源码：[AcceleratedVentricularReference.cs](../../../src/Monitor.Simulation/Physiology/AcceleratedVentricularReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class AcceleratedVentricularReference
{
    public const string EvidenceId = "AcceleratedVentricularIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan();
    public const string CaptureEvidenceId = "AcceleratedVentricularCoincidentCaptureIllustration@1";
    public const string FusionEvidenceId = "AcceleratedVentricularFusionIllustration@1";
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool fusion = false, bool capture = false);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool fusion = false, bool capture = false);
}
```

## Physiology/ArterialOxygenationSource.cs

源码：[ArterialOxygenationSource.cs](../../../src/Monitor.Simulation/Physiology/ArterialOxygenationSource.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public readonly record struct ArterialOxygenationSample(long SourceSimTimeNs, int SaturationMilliPercent)
{
}
public interface IArterialOxygenationSource
{
    public ArterialOxygenationSample ReadAt(long sourceSimTimeNs);
}
public sealed record SampledArterialOxygenationState(long StartSimTimeNs, long SampleStepNs, IReadOnlyList<int> SaturationMilliPercent, string ModelId = "SampledArterialOxygenation@1")
{
}
public sealed class SampledArterialOxygenation : IArterialOxygenationSource
{
    public const int MaximumSamples = 450001;
    public long StartSimTimeNs { get; }
    public long SampleStepNs { get; }
    public long EndSimTimeNs { get; }
    public SampledArterialOxygenation(SampledArterialOxygenationState state);
    public SampledArterialOxygenationState CaptureState();
    public ArterialOxygenationSample ReadAt(long sourceSimTimeNs);
}
```

## Physiology/ArterialPulsePlan.cs

源码：[ArterialPulsePlan.cs](../../../src/Monitor.Simulation/Physiology/ArterialPulsePlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record ArterialPulsePlan(long TransitDelayNs, long DurationNs, int BaselineMmHg, int PulseHeightMmHg)
{
    public const string EvidenceId = "InfirmaryArterialPulseDraft@1";
    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology, Guid channelId, uint qualityFlags);
}
```

## Physiology/AtrialEscapeReference.cs

源码：[AtrialEscapeReference.cs](../../../src/Monitor.Simulation/Physiology/AtrialEscapeReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class AtrialEscapeReference
{
    public const string EvidenceId = "AtrialEscapeIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
}
```

## Physiology/AtrialFibrillationPerfusion.cs

源码：[AtrialFibrillationPerfusion.cs](../../../src/Monitor.Simulation/Physiology/AtrialFibrillationPerfusion.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class AtrialFibrillationPerfusion
{
    public const string EvidenceId = "AtrialFibrillationPerfusionIllustration@3";
    public static int GainPermille(AvConductionPattern pattern, ulong ordinal, bool systemicPulseDeficit = false);
    public static int GainPermille(RegularPhysiologyPlan plan, ulong ordinal, bool systemicPulseDeficit = false);
}
```

## Physiology/AtrialFibrillationReference.cs

源码：[AtrialFibrillationReference.cs](../../../src/Monitor.Simulation/Physiology/AtrialFibrillationReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class AtrialFibrillationReference
{
    public const string EvidenceId = "AtrialFibrillationIllustrationDraft@1";
    public const long MinimumRrNs = 440_000_000;
    public static long SegmentDurationNs { get; }
    public static EcgCycleTiming Timing { get; }
    public static bool IsPattern(AvConductionPattern pattern);
    public static RegularPhysiologyPlan CreatePlan(bool fine = false);
    public static bool IsLongShortBeat(ulong ordinal);
    public static bool IsLongShortBeat(RegularPhysiologyPlan plan, ulong ordinal);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool fine = false, bool aberrancy = false);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool fine = false, bool aberrancy = false);
}
```

## Physiology/AtrialFlutterMechanics.cs

源码：[AtrialFlutterMechanics.cs](../../../src/Monitor.Simulation/Physiology/AtrialFlutterMechanics.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class AtrialFlutterMechanics
{
    public const string EvidenceId = "AtrialFlutterMechanicsIllustration@1";
    public const long ContractionDurationNs = 80_000_000;
    public const int TransportPermille = 400;
    public static long EffectiveAtrialDurationNs(RegularPhysiologyPlan plan, ulong ordinal, long nonFillingDurationNs);
}
```

## Physiology/AtrialFlutterReference.cs

源码：[AtrialFlutterReference.cs](../../../src/Monitor.Simulation/Physiology/AtrialFlutterReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class AtrialFlutterReference
{
    public const string EvidenceId = "AtrialFlutterIllustrationDraft@3";
    public const string OneToOneEvidenceId = "AtrialFlutterOneToOneIllustration@1";
    public static bool IsPattern(AvConductionPattern pattern);
    public static RegularPhysiologyPlan CreateVariablePlan();
    public static EcgCycleTiming Timing(int ratio);
    public static RegularPhysiologyPlan CreatePlan(int ratio = 4);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(int ratio = 4);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(int ratio = 4);
}
```

## Physiology/AuthoredQrsMeasurement.cs

源码：[AuthoredQrsMeasurement.cs](../../../src/Monitor.Simulation/Physiology/AuthoredQrsMeasurement.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum AuthoredQrsKind
{
    Flat,
    RFirst,
    QThenR,
    QS
}
public sealed record AuthoredQrsMeasurement(AuthoredQrsKind Kind, long QDurationNs, long QDepthQ32, long RPeakQ32, bool MeetsQIllustrationCriteria)
{
}
public static class AuthoredQrsMeasurements
{
    public static AuthoredQrsMeasurement Measure(IReadOnlyList<long> samplesQ32, long stepNs);
    public static IReadOnlyList<AuthoredQrsMeasurement> Project(IReadOnlyList<ElectrodeWaveformPlan> electrodes, EcgLimbPlacement placement = EcgLimbPlacement.Standard);
}
```

## Physiology/BidirectionalVtReference.cs

源码：[BidirectionalVtReference.cs](../../../src/Monitor.Simulation/Physiology/BidirectionalVtReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class BidirectionalVtReference
{
    public const string EvidenceId = "BidirectionalVtIllustration@1";
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
}
```

## Physiology/BundleBlockReference.cs

源码：[BundleBlockReference.cs](../../../src/Monitor.Simulation/Physiology/BundleBlockReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum EcgBundleBlockIllustration
{
    Reference,
    CompleteRight,
    IncompleteRight,
    CompleteLeft,
    IncompleteLeft,
    LeftAnteriorFascicular,
    LeftPosteriorFascicular
}
public static class BundleBlockReference
{
    public const string EvidenceId = "StandaloneBundleBlockIllustrationDraft@1";
    public static EcgCycleTiming Timing(EcgBundleBlockIllustration mode);
    public static RegularPhysiologyPlan CreatePlan(EcgBundleBlockIllustration mode);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(EcgBundleBlockIllustration mode);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(EcgBundleBlockIllustration mode);
}
```

## Physiology/CalciumRepolarizationReference.cs

源码：[CalciumRepolarizationReference.cs](../../../src/Monitor.Simulation/Physiology/CalciumRepolarizationReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum CalciumIllustration
{
    Reference,
    High,
    Low,
    HighAbsentSt,
    LowFlatT,
    LowInvertedT
}
public static class CalciumRepolarizationReference
{
    public const string VariantsEvidenceId = "CalciumRepolarizationVariantsIllustration@1";
    public const string EvidenceId = "CalciumRepolarizationIllustration@1";
    public static EcgCycleTiming Timing(CalciumIllustration mode);
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(CalciumIllustration mode);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(CalciumIllustration mode);
}
```

## Physiology/CapnogramPlan.cs

源码：[CapnogramPlan.cs](../../../src/Monitor.Simulation/Physiology/CapnogramPlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record CapnogramPlan(long DeadSpaceNs, long RiseNs, long InspiratoryFallNs, int BaselineMmHg, int EndExpiratoryMmHg, int? PlateauStartCentiMmHg = null, long TransportDelayNs = 0, long DispersionStepNs = 0)
{
    public SeededExpirationPressure? SeededPressure { get; init; }
    public const string EvidenceId = "InfirmaryCapnogramDraft@1";
    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology, Guid channelId, uint qualityFlags);
}
```

## Physiology/CardiacRateAdjustment.cs

源码：[CardiacRateAdjustment.cs](../../../src/Monitor.Simulation/Physiology/CardiacRateAdjustment.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record CardiacRateAdjustment
{
    public SeededCardiacRate Rate { get; }
    public SeededCardiacRate? AtrialRate { get; }
    public CardiacRateAdjustment(int rateBpm, int? atrialRateBpm, string seedHex, int variationPermille);
    public static bool Supports(RegularPhysiologyPlan plan);
    public void Validate(RegularPhysiologyPlan plan);
}
```

## Physiology/CardiacFillingPerfusion.cs

源码：[CardiacFillingPerfusion.cs](../../../src/Monitor.Simulation/Physiology/CardiacFillingPerfusion.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class CardiacFillingPerfusion
{
    public const string EvidenceId = "CardiacFillingPerfusionIllustration@2";
    public const long ReferencePeriodNs = 800_000_000;
    public const long NonFillingDurationNs = 300_000_000;
    public const long FillingConstantNs = 200_000_000;
    public const long AtrialContractionDurationNs = 120_000_000;
    public const int AtrialContributionPermille = 250;
    public static bool Supports(RegularPhysiologyPlan plan);
    public static int StrokeVolumePermille(long periodNs, long effectiveAtrialDurationNs, long nonFillingDurationNs = NonFillingDurationNs);
    public static int GainPermille(RegularPhysiologyPlan plan, ulong cycleIndex);
}
```

## Physiology/CentralVenousPressurePlan.cs

源码：[CentralVenousPressurePlan.cs](../../../src/Monitor.Simulation/Physiology/CentralVenousPressurePlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record CvpWaveComponent(long DelayNs, long DurationNs, int MagnitudeCentiMmHg)
{
}
public sealed record CentralVenousPressurePlan(int BaselineCentiMmHg, CvpWaveComponent A, CvpWaveComponent C, CvpWaveComponent X, CvpWaveComponent V, CvpWaveComponent Y, int RespiratoryDeltaCentiMmHg, int MaximumComponentOverlap = 1)
{
    public const string EvidenceId = "InfirmaryCvpComponentsDraft@2";
    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology, Guid channelId, uint qualityFlags);
}
```

## Physiology/CompleteAvBlockJunctionalReference.cs

源码：[CompleteAvBlockJunctionalReference.cs](../../../src/Monitor.Simulation/Physiology/CompleteAvBlockJunctionalReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class CompleteAvBlockJunctionalReference
{
    public const string EvidenceId = "CompleteAvBlockJunctionalIllustrationDraft@1";
    public static RegularPhysiologyPlan CreatePlan(long atrialPeriodNs = 800_000_000, long escapePeriodNs = 1_200_000_000, long firstQrsNs = 400_000_000);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
}
```

## Physiology/CompleteAvBlockVentricularReference.cs

源码：[CompleteAvBlockVentricularReference.cs](../../../src/Monitor.Simulation/Physiology/CompleteAvBlockVentricularReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class CompleteAvBlockVentricularReference
{
    public const string EvidenceId = "CompleteAvBlockVentricularIllustrationDraft@1";
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan(long atrialPeriodNs = 800_000_000, long escapePeriodNs = 2_000_000_000, long firstQrsNs = 400_000_000);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
}
```

## Physiology/ConductedFlutterPerfusion.cs

源码：[ConductedFlutterPerfusion.cs](../../../src/Monitor.Simulation/Physiology/ConductedFlutterPerfusion.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class ConductedFlutterPerfusion
{
    public static bool Supports(RegularPhysiologyPlan plan);
    public static int GainPermille(RegularPhysiologyPlan plan, ulong ordinal);
}
```

## Physiology/DigitalisEffectReference.cs

源码：[DigitalisEffectReference.cs](../../../src/Monitor.Simulation/Physiology/DigitalisEffectReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum DigitalisTShape
{
    FishHook,
    LowT,
    InvertedT
}
public static class DigitalisEffectReference
{
    public const string VariantsEvidenceId = "DigitalisTVariantsIllustration@1";
    public const string EvidenceId = "DigitalisEffectIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(DigitalisTShape shape = DigitalisTShape.FishHook);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(DigitalisTShape shape = DigitalisTShape.FishHook);
}
```

## Physiology/EcgAtrialIllustration.cs

源码：[EcgAtrialIllustration.cs](../../../src/Monitor.Simulation/Physiology/EcgAtrialIllustration.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum EcgAtrialIllustration
{
    Reference,
    LeftAtrialAbnormality,
    RightAtrialAbnormality,
    BiatrialAbnormality,
}
public static class EcgAtrialIllustrations
{
    public const string EvidenceId = "LeftAtrialIllustrationDraft@1";
    public const long LeftPDurationNs = 140_000_000;
    public const string RightAndBiatrialEvidenceId = "RightAndBiatrialIllustrationDraft@1";
    public static long? PDurationNs(EcgAtrialIllustration illustration);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(EcgAtrialIllustration illustration);
}
```

## Physiology/EcgChestInfarctionPlan.cs

源码：[EcgChestInfarctionPlan.cs](../../../src/Monitor.Simulation/Physiology/EcgChestInfarctionPlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum InfarctionIllustrationStage
{
    None,
    HyperacuteT,
    HyperacuteInjury,
    AcuteMonophasic,
    AcuteQInvertedT,
    AcuteQsInvertedT,
    SubacuteDeepT,
    SubacuteRecoveringT,
    OldQNormalT,
    OldQInvertedT,
    OldQLowT,
}
public enum InfarctionTerritory
{
    CustomChest,
    Inferior,
    Lateral,
    Anteroseptal,
    Anterior,
    ExtensiveAnterior
}
public sealed record EcgChestInfarctionPlan(int ChestMask, InfarctionIllustrationStage Stage, InfarctionTerritory Territory = InfarctionTerritory.CustomChest, long RepolarizationDelayNs = 0, EcgInfarctionComponents? Components = null)
{
    public const string EvidenceId = "ChestInfarctionIllustrationDraft@6";
    public IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
    public bool HasActiveRegion { get; }
}
```

## Physiology/EcgCycleTiming.cs

源码：[EcgCycleTiming.cs](../../../src/Monitor.Simulation/Physiology/EcgCycleTiming.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record EcgCycleTiming(long RrIntervalNs, long PDurationNs, long PrIntervalNs, long QrsDurationNs, long QtIntervalNs, long TDurationNs)
{
    public long TOffsetFromQrsNs { get; }
    public long StDurationNs { get; }
    public void Validate();
}
public static class TextbookEcgReference
{
    public const string EvidenceId = "TextbookEcgReferenceDraft@3";
    public const string SourceValueUnit = "microvolt";
    public static EcgCycleTiming Timing { get; }
    public static IReadOnlyList<EventWaveformBand> CreateBands(EcgCycleTiming? timing = null);
}
```

## Physiology/EcgElectrodeProjection.cs

源码：[EcgElectrodeProjection.cs](../../../src/Monitor.Simulation/Physiology/EcgElectrodeProjection.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum EcgElectrode
{
    RA,
    LA,
    RL,
    LL,
    C1,
    C2,
    C3,
    C4,
    C5,
    C6
}
public enum EcgLead
{
    I,
    II,
    III,
    AVR,
    AVL,
    AVF,
    V1,
    V2,
    V3,
    V4,
    V5,
    V6
}
public readonly record struct ExactEcgPotential(Int128 Numerator)
{
    public long ToQ32();
}
public sealed record EcgElectrodePotentials(long RA, long LA, long RL, long LL, long C1, long C2, long C3, long C4, long C5, long C6)
{
}
public sealed class EcgLeadProjection
{
    public ExactEcgPotential WilsonCentralTerminal { get; }
    public ExactEcgPotential this[EcgLead lead] { get; }
    public static EcgLeadProjection Project(EcgElectrodePotentials electrodes);
}
public sealed record ElectrodeWaveformPlan(EcgElectrode Electrode, IReadOnlyList<EventWaveformBand> Bands)
{
}
public sealed record ElectrodeWaveformState(IReadOnlyList<ElectrodeWaveformPlan> Electrodes, IReadOnlyList<PhysiologyCycleEvent> Events, EcgLimbPlacement Placement = EcgLimbPlacement.Standard)
{
}
public sealed record ProjectedEcgSample(long SimTimeNs, EcgLeadProjection Leads)
{
}
public sealed class ElectrodeWaveformComposition
{
    public static ElectrodeWaveformComposition Restore(ElectrodeWaveformState state);
    public ElectrodeWaveformState CaptureState();
    public ProjectedEcgSample EvaluateAt(long simTimeNs, CancellationToken cancellationToken = default);
}
```

## Physiology/EcgInfarctionComponents.cs

源码：[EcgInfarctionComponents.cs](../../../src/Monitor.Simulation/Physiology/EcgInfarctionComponents.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum NecrosisIllustrationShape
{
    Reference,
    QWithReducedR,
    QS
}
public sealed record EcgInfarctionComponents(NecrosisIllustrationShape Necrosis = NecrosisIllustrationShape.Reference, int? TPeakMicrovolts = null, int JMicrovolts = 0, int StEndMicrovolts = 0, int StArchMicrovolts = 0, int QrsTemplatePermille = 1000, EcgQrsContributionLoss? ContributionLoss = null)
{
}
```

## Physiology/EcgInfarctionZones.cs

源码：[EcgInfarctionZones.cs](../../../src/Monitor.Simulation/Physiology/EcgInfarctionZones.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record EcgInfarctionRegion(int ChestMask = 0, InfarctionTerritory Territory = InfarctionTerritory.CustomChest)
{
}
public sealed record EcgInfarctionZones(EcgInfarctionRegion Ischemia, EcgInfarctionRegion Injury, EcgInfarctionRegion Necrosis, EcgInfarctionComponents Components, long RepolarizationDelayNs = 0)
{
    public IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
}
```

## Physiology/EcgLimbPlacement.cs

源码：[EcgLimbPlacement.cs](../../../src/Monitor.Simulation/Physiology/EcgLimbPlacement.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum EcgLimbPlacement
{
    Standard,
    SwapRaLa,
    SwapRaLl,
    SwapLaLl
}
public static class EcgLimbWiring
{
    public static EcgElectrodePotentials Apply(EcgElectrodePotentials source, EcgLimbPlacement placement);
}
```

## Physiology/EcgPWavePlan.cs

源码：[EcgPWavePlan.cs](../../../src/Monitor.Simulation/Physiology/EcgPWavePlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record EcgPWaveComponents(int EarlyMicrovolts, int LateMicrovolts)
{
}
public sealed record EcgPWavePlan(IReadOnlyList<EcgPWaveComponents?> Electrodes, int SeparationPermille = 375)
{
    public const string EvidenceId = "PWaveComponentsIllustrationDraft@2";
}
```

## Physiology/EcgQrsContributionLoss.cs

源码：[EcgQrsContributionLoss.cs](../../../src/Monitor.Simulation/Physiology/EcgQrsContributionLoss.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record EcgQrsContributionLoss(int AmplitudeMicrovolts = 1200, int DurationPermille = 750, int LossPermille = 1000)
{
    public bool IsActive { get; }
}
```

## Physiology/EcgQtCorrection.cs

源码：[EcgQtCorrection.cs](../../../src/Monitor.Simulation/Physiology/EcgQtCorrection.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record EcgQtCorrection(string MethodId, long QtcIntervalNs, long RrIntervalNs)
{
    public const string Bazett = "Bazett@1";
    public const string Fridericia = "Fridericia@1";
    public const string FormulaRrUnit = "second";
    public const string RoundingId = "NearestNanosecondTiesToEven@1";
    public long ResolveQtIntervalNs();
    public EcgCycleTiming ResolveTiming(EcgCycleTiming shapeTiming);
}
```

## Physiology/EcgStSegmentPlan.cs

源码：[EcgStSegmentPlan.cs](../../../src/Monitor.Simulation/Physiology/EcgStSegmentPlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record EcgStSegmentPlan(IReadOnlyList<int> JMicrovolts, IReadOnlyList<int> EndMicrovolts, IReadOnlyList<int>? ArchMicrovolts = null)
{
    public const string EvidenceId = "StSegmentIllustrationDraft@2";
}
```

## Physiology/EcgStTFusionPlan.cs

源码：[EcgStTFusionPlan.cs](../../../src/Monitor.Simulation/Physiology/EcgStTFusionPlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record EcgStTFusionContour(int JMicrovolts, int PeakMicrovolts, int PeakPositionPermille)
{
}
public sealed record EcgStTFusionPlan(IReadOnlyList<EcgStTFusionContour?> Electrodes)
{
    public const string EvidenceId = "StTFusionIllustrationDraft@1";
}
```

## Physiology/EcgTContourPlan.cs

源码：[EcgTContourPlan.cs](../../../src/Monitor.Simulation/Physiology/EcgTContourPlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum EcgTContourTarget
{
    Chest,
    I,
    II,
    III,
    AVR,
    AVL,
    AVF
}
public enum EcgTContourShape
{
    PositiveNegative = 1,
    NegativePositive,
    Notched,
    SymmetricInverted,
    PeakedUpright,
    BroadUpright,
    ReferenceUpright,
    ReferenceInverted
}
public sealed record EcgTContourPlan(int ChestMask, EcgTContourShape Shape, int PeakMicrovolts, EcgTContourTarget Target = EcgTContourTarget.Chest, int? CrossingPositionPermille = null, int? SecondPeakMicrovolts = null)
{
    public const string EvidenceId = "TContourIllustrationDraft@8";
    public IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
}
```

## Physiology/EcgTWaveScalePlan.cs

源码：[EcgTWaveScalePlan.cs](../../../src/Monitor.Simulation/Physiology/EcgTWaveScalePlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record EcgTWaveScalePlan(IReadOnlyList<int> ElectrodeScalePermille)
{
}
```

## Physiology/EcgTWaveShapePlan.cs

源码：[EcgTWaveShapePlan.cs](../../../src/Monitor.Simulation/Physiology/EcgTWaveShapePlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record EcgTWaveShapePlan(int PeakPositionPermille)
{
}
```

## Physiology/EcgUWavePlan.cs

源码：[EcgUWavePlan.cs](../../../src/Monitor.Simulation/Physiology/EcgUWavePlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record EcgUWavePlan(long DelayAfterTNs, long DurationNs, IReadOnlyList<long> ElectrodeAmplitudesMicrovolts)
{
    public void Validate(EcgCycleTiming timing);
}
```

## Physiology/EcgVentricularIllustration.cs

源码：[EcgVentricularIllustration.cs](../../../src/Monitor.Simulation/Physiology/EcgVentricularIllustration.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum EcgVentricularIllustration
{
    Reference,
    LeftHypertrophyWithStrain,
    RightHypertrophyWithStrain,
    BiventricularCombinedSigns,
    SevereRightQr,
    PulmonaryHeartSigns
}
public static class EcgVentricularIllustrations
{
    public const string EvidenceId = "LeftVentricularIllustrationDraft@1";
    public const string BiventricularEvidenceId = "BiventricularIllustrationDraft@1";
    public const string SevereRightEvidenceId = "SevereRightVentricularIllustrationDraft@1";
    public const string PulmonaryHeartEvidenceId = "PulmonaryHeartIllustrationDraft@1";
    public const string RightEvidenceId = "RightVentricularIllustrationDraft@1";
    public static long? QrsDurationNs(EcgVentricularIllustration illustration);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(EcgVentricularIllustration illustration);
}
```

## Physiology/ElectrodeSignalGenerator.cs

源码：[ElectrodeSignalGenerator.cs](../../../src/Monitor.Simulation/Physiology/ElectrodeSignalGenerator.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record ElectrodeSignalState(RegularPhysiologyState Timeline, SignalSampleClockState Clock, IReadOnlyList<ElectrodeWaveformPlan> Electrodes, EcgLimbPlacement Placement = EcgLimbPlacement.Standard)
{
}
public sealed record ElectrodeSignalSample(SignalSampleTick Tick, EcgLeadProjection ExactLeads, IReadOnlyList<short> MicrovoltValues)
{
}
public sealed class ElectrodeSignalException(string reason, string parameter) : ArgumentException(reason, parameter)
{
    public string ReasonCode { get; }
}
public sealed class ElectrodeSignalGenerator
{
    public static ElectrodeSignalGenerator Start(RegularPhysiologyPlan plan, string profileId, ulong streamEpoch, IReadOnlyList<ElectrodeWaveformPlan> electrodes, EcgLimbPlacement placement = EcgLimbPlacement.Standard);
    public static ElectrodeSignalGenerator Restore(ElectrodeSignalState state);
    public ElectrodeSignalState CaptureState();
    public IReadOnlyList<ElectrodeSignalSample> GenerateBefore(long exclusiveSimTimeNs, int maximumSamples, int maximumEvents, CancellationToken cancellationToken = default);
}
```

## Physiology/ElectrodeWaveformGroup.cs

源码：[ElectrodeWaveformGroup.cs](../../../src/Monitor.Simulation/Physiology/ElectrodeWaveformGroup.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed class ElectrodeWaveformGroupException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record ElectrodeChannelPlan(EcgLead Lead, Guid ChannelId, int DelayCapacity, uint QualityFlags)
{
}
public sealed record ElectrodeChannelState(EcgLead Lead, Guid ChannelId, SignalAcquisitionDelayState Delay, uint QualityFlags)
{
}
public sealed record ElectrodeWaveformGroupState(ElectrodeSignalState Generator, IReadOnlyList<ElectrodeChannelState> Channels, WaveformBlockAssemblerState Assembler)
{
}
public sealed class ElectrodeWaveformGroup
{
    public ElectrodeWaveformGroup Fork();
    public static ElectrodeWaveformGroup Start(Guid sessionId, Guid instanceId, ulong timebaseEpoch, ulong streamEpoch, ulong configurationRevision, ulong firstBlockSequence, int maximumBufferedBlocks, RegularPhysiologyPlan physiology, IReadOnlyList<ElectrodeWaveformPlan> electrodes, IReadOnlyList<ElectrodeChannelPlan> channels, EcgLimbPlacement placement = EcgLimbPlacement.Standard);
    public ElectrodeWaveformGroupState CaptureState();
    public static ElectrodeWaveformGroup Restore(ElectrodeWaveformGroupState state);
    public IReadOnlyList<byte[]> AdvanceTo(long simTimeNs, int maximumSamples, int maximumBlocks, int maximumEvents, CancellationToken cancellationToken = default);
}
```

## Physiology/EventWaveformComposition.cs

源码：[EventWaveformComposition.cs](../../../src/Monitor.Simulation/Physiology/EventWaveformComposition.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum AtrialFibrillationBeatSelection
{
    Ordinary,
    LongShort
}
public readonly record struct EventWaveformPhasePoint(long OffsetNs, int TableIndex)
{
}
public readonly record struct VentricularCyclePattern(int Length, ulong IncludedSlots)
{
}
public sealed record EventWaveformBand(PhysiologyCycleEventKind Trigger, long DelayNs, long DurationNs, IReadOnlyList<long> TableQ32, IReadOnlyList<EventWaveformPhasePoint>? PhasePoints = null, ulong? TriggerCycleLimit = null, ulong? TriggerCycleResume = null, RespiratoryPattern DepthPattern = RespiratoryPattern.Regular, IReadOnlyList<int>? ExpirationCycleGainsPermille = null, VentricularCyclePattern? VentricularCycles = null, AvConductionPattern? EjectionIllustration = null, AtrialFibrillationBeatSelection? AfBeatSelection = null)
{
    public RegularPhysiologyPlan? AfTiming { get; init; }
    public SeededCardiacRate? CycleDurationRate { get; init; }
}
public sealed record EventWaveformState(IReadOnlyList<EventWaveformBand> Bands, IReadOnlyList<PhysiologyCycleEvent> Events)
{
}
public sealed class EventWaveformException(string reason, string parameter) : ArgumentException(reason, parameter)
{
    public string ReasonCode { get; }
}
public sealed class EventWaveformComposition
{
    public const int MaximumBandCount = 32;
    public const int MaximumEventCount = 4096;
    public static EventWaveformComposition Restore(EventWaveformState state);
    public EventWaveformState CaptureState();
    public long EvaluateAt(long simTimeNs, CancellationToken cancellationToken = default);
}
```

## Physiology/FillingLimitedEjection.cs

源码：[FillingLimitedEjection.cs](../../../src/Monitor.Simulation/Physiology/FillingLimitedEjection.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class FillingLimitedEjection
{
    public const long ReferencePeriodNs = 800_000_000;
    public const long NonFillingDurationNs = 240_000_000;
    public const long FillingConstantNs = 200_000_000;
    public static int StrokeVolumePermille(long periodNs);
    public static int StrokeVolumePermille(long periodNs, long nonFillingDurationNs);
}
```

## Physiology/FixedPerfusionPresets.cs

源码：[FixedPerfusionPresets.cs](../../../src/Monitor.Simulation/Physiology/FixedPerfusionPresets.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record FixedPerfusionPreset(PlethRunoffPlan Pleth, VascularPressurePlan Arterial, VascularPressurePlan Pulmonary, CentralVenousPressurePlan Venous)
{
}
public static class FixedPerfusionPresets
{
    public static FixedPerfusionPreset SinglePulse { get; }
    public static FixedPerfusionPreset PulmonaryOverlap { get; }
    public static FixedPerfusionPreset FillingSinglePulse { get; }
    public static FixedPerfusionPreset FillingPulmonaryOverlap { get; }
    public static FixedPerfusionPreset AcceleratedVentricular { get; }
    public static FixedPerfusionPreset AcceleratedSupraventricular { get; }
}
```

## Physiology/FlutterOneToOnePerfusionReference.cs

源码：[FlutterOneToOnePerfusionReference.cs](../../../src/Monitor.Simulation/Physiology/FlutterOneToOnePerfusionReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class FlutterOneToOnePerfusionReference
{
    public const string EvidenceId = "FlutterOneToOnePerfusionIllustration@3";
    public const long NonFillingDurationNs = 160_000_000;
    public static int StrokeVolumePermille { get; }
    public static PlethRunoffPlan Pleth { get; }
    public static VascularPressurePlan Arterial { get; }
    public static VascularPressurePlan Pulmonary { get; }
    public static CentralVenousPressurePlan Venous { get; }
}
```

## Physiology/HyperkalemiaConductionReference.cs

源码：[HyperkalemiaConductionReference.cs](../../../src/Monitor.Simulation/Physiology/HyperkalemiaConductionReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class HyperkalemiaConductionReference
{
    public const string EvidenceId = "HyperkalemiaConductionIllustration@1";
    public const string QrsVoltageEvidenceId = "HyperkalemiaQrsVoltageIllustration@1";
    public const string AbsentPEvidenceId = "HyperkalemiaAbsentPIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan(bool absentP = false);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool absentP = false);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool absentP = false);
}
```

## Physiology/HyperkalemiaFusionReference.cs

源码：[HyperkalemiaFusionReference.cs](../../../src/Monitor.Simulation/Physiology/HyperkalemiaFusionReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class HyperkalemiaFusionReference
{
    public const string EvidenceId = "HyperkalemiaFusionIllustration@1";
    public const long CompoundDurationNs = 720_000_000;
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
}
```

## Physiology/HyperkalemiaRepolarizationReference.cs

源码：[HyperkalemiaRepolarizationReference.cs](../../../src/Monitor.Simulation/Physiology/HyperkalemiaRepolarizationReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class HyperkalemiaRepolarizationReference
{
    public const string EvidenceId = "HyperkalemiaRepolarizationIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
}
```

## Physiology/HypokalemiaRepolarizationReference.cs

源码：[HypokalemiaRepolarizationReference.cs](../../../src/Monitor.Simulation/Physiology/HypokalemiaRepolarizationReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class HypokalemiaRepolarizationReference
{
    public const string EvidenceId = "HypokalemiaRepolarizationIllustration@1";
    public const string InvertedTEvidenceId = "HypokalemiaInvertedTIllustration@1";
    public const string ConductionEvidenceId = "HypokalemiaConductionIllustration@1";
    public const string FusionEvidenceId = "HypokalemiaTuFusionIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static EcgCycleTiming ConductionTiming { get; }
    public const long QuIntervalNs = 650_000_000;
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool fuseTu = false, bool invertT = false, bool conduction = false);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool fuseTu = false, bool invertT = false, bool conduction = false);
}
```

## Physiology/LeftAnteriorFascicularReference.cs

源码：[LeftAnteriorFascicularReference.cs](../../../src/Monitor.Simulation/Physiology/LeftAnteriorFascicularReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class LeftAnteriorFascicularReference
{
    public const string EvidenceId = "LeftAnteriorFascicularIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
}
```

## Physiology/LeftBundleBlockReference.cs

源码：[LeftBundleBlockReference.cs](../../../src/Monitor.Simulation/Physiology/LeftBundleBlockReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class LeftBundleBlockReference
{
    public const string EvidenceId = "LeftBundleBlockIllustrationDraft@1";
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
}
```

## Physiology/LeftPosteriorFascicularReference.cs

源码：[LeftPosteriorFascicularReference.cs](../../../src/Monitor.Simulation/Physiology/LeftPosteriorFascicularReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class LeftPosteriorFascicularReference
{
    public const string EvidenceId = "LeftPosteriorFascicularIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
}
```

## Physiology/NormalPrDeltaReference.cs

源码：[NormalPrDeltaReference.cs](../../../src/Monitor.Simulation/Physiology/NormalPrDeltaReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class NormalPrDeltaReference
{
    public const string EvidenceId = "NormalPrDeltaIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static EcgCycleTiming ResolveTiming(bool prolongedPr);
    public static RegularPhysiologyPlan CreatePlan(bool prolongedPr = false);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool prolongedPr = false);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool prolongedPr = false);
}
```

## Physiology/OxygenReservoirModel.cs

源码：[OxygenReservoirModel.cs](../../../src/Monitor.Simulation/Physiology/OxygenReservoirModel.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record OxygenReservoirParameters(decimal FrcMlBtps = 2200m, decimal BloodVolumeMl = 4823.7546875m, decimal ArterialVolumeFraction = .2m, decimal HemoglobinGramsPerDl = 15m, decimal ShuntFraction = .02m, decimal OxygenDemandMlStpdPerMinute = 250m, decimal RespiratoryQuotient = .8m, decimal ConsumptionFloorMlPerDl = 2m, decimal VolumeRecoverySeconds = 2m)
{
    public decimal ArterialVolumeMl { get; }
    public decimal VenousVolumeMl { get; }
    public void Validate();
}
public readonly record struct OxygenReservoirState(decimal AlveolarOxygenMl, decimal AlveolarCarbonDioxideMl, decimal AlveolarInertGasMl, decimal ArterialOxygenMl, decimal VenousOxygenMl, decimal InspiredOxygenMl = 0, decimal ExpiredOxygenMl = 0, decimal ConsumedOxygenMl = 0, decimal UnmetOxygenDemandMl = 0)
{
    public decimal GasMlStpd { get; }
    public decimal ConservedOxygenMl { get; }
}
public static class OxygenReservoirModel
{
    public const string ModelId = "OxygenTransportDecimal@2";
    public const long StepNs = 8_000_000;
    public const decimal DryPressureMmHg = 713m;
    public static decimal BtpsToStpd { get; }
    public static decimal SaturationFraction(decimal pressureMmHg);
    public static int ArterialSaturationMilliPercent(OxygenReservoirState state, OxygenReservoirParameters parameters);
    public static OxygenReservoirState ReferenceState(OxygenReservoirParameters parameters);
    public static OxygenReservoirState Step(OxygenReservoirState state, OxygenReservoirParameters parameters, PhysiologyTransportInterval input, decimal oxygenDemandMultiplier = 1);
    public static void ValidateOxygenDemandMultiplier(decimal oxygenDemandMultiplier);
    public static void ValidateState(OxygenReservoirState state, OxygenReservoirParameters parameters);
}
```

## Physiology/OxygenationPatientDefaults.cs

源码：[OxygenationPatientDefaults.cs](../../../src/Monitor.Simulation/Physiology/OxygenationPatientDefaults.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum OxygenationReferenceSex
{
    Male,
    Female
}
public sealed record OxygenationPatientProfile(decimal AgeYears, OxygenationReferenceSex Sex, decimal HeightCm, decimal WeightKg)
{
    public static OxygenationPatientProfile Default { get; }
    public decimal BodyMassIndex { get; }
    public void Validate();
}
public sealed record OxygenationBaselineOverrides(decimal? BloodVolumeMl = null, decimal? FrcMlBtps = null, decimal? HemoglobinGramsPerDl = null, decimal? BasalOxygenDemandMlStpdPerMinute = null)
{
}
public enum OxygenationBaselineOrigin
{
    ReferenceCenter,
    ReferencePrediction,
    Explicit
}
public sealed record OxygenationBaselineValue(decimal Value, string Unit, OxygenationBaselineOrigin Origin, string ReferenceId, string CenterSelection)
{
}
public sealed record OxygenationBaselineResolution(string ReferenceSetId, OxygenReservoirParameters Parameters, OxygenationBaselineValue BloodVolume, OxygenationBaselineValue Frc, OxygenationBaselineValue Hemoglobin, OxygenationBaselineValue BasalOxygenDemand)
{
}
public static class OxygenationPatientDefaults
{
    public const string ReferenceSetId = OxygenationReferenceData.SetId;
    public static OxygenationBaselineResolution Resolve(OxygenationPatientProfile profile, OxygenationBaselineOverrides? overrides = null);
    public static decimal PredictBloodVolumeMl(OxygenationPatientProfile profile);
    public static decimal PredictFrcMlBtps(OxygenationPatientProfile profile);
    public static decimal ReferenceHemoglobinGramsPerDl(OxygenationPatientProfile profile);
    public static decimal PredictBasalOxygenDemandMlStpdPerMinute(OxygenationPatientProfile profile);
}
```

## Physiology/PhysiologySignalContinuation.cs

源码：[PhysiologySignalContinuation.cs](../../../src/Monitor.Simulation/Physiology/PhysiologySignalContinuation.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record PhysiologySignalSegment(RegularPhysiologyPlan Plan, IReadOnlyList<EventWaveformBand> Bands, VascularPressurePlan? Pressure, PlethRunoffPlan? Pleth, long FromEventTimeNs, long ToExclusiveEventTimeNs, bool IncludeInitialPressure, int PressureBaselineCentiMmHg = 0)
{
}
```

## Physiology/PhysiologySignalGenerator.cs

源码：[PhysiologySignalGenerator.cs](../../../src/Monitor.Simulation/Physiology/PhysiologySignalGenerator.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record PhysiologySignalState(RegularPhysiologyState Timeline, SignalSampleClockState Clock, IReadOnlyList<EventWaveformBand> Bands, VascularPressurePlan? VascularPressure = null, PlethRunoffPlan? PlethRunoff = null, int PressureBaselineCentiMmHg = 0)
{
    public long? ActiveFromEventTimeNs { get; init; }
    public IReadOnlyList<PhysiologySignalSegment> History { get; init; }
}
public readonly record struct PhysiologySignalSample(SignalSampleTick Tick, long ValueQ32, short NormalizedValue)
{
}
public sealed class PhysiologySignalException(string reason, string parameter) : ArgumentException(reason, parameter)
{
    public string ReasonCode { get; }
}
public sealed class PhysiologySignalGenerator
{
    public static PhysiologySignalGenerator Start(RegularPhysiologyPlan plan, string profileId, ulong streamEpoch, IReadOnlyList<EventWaveformBand> bands, VascularPressurePlan? vascularPressure = null, PlethRunoffPlan? plethRunoff = null, int pressureBaselineCentiMmHg = 0);
    public static PhysiologySignalGenerator Restore(PhysiologySignalState state);
    public PhysiologySignalState CaptureState();
    public IReadOnlyList<PhysiologySignalSample> GenerateBefore(long exclusiveSimTimeNs, int maximumSamples, int maximumEvents, CancellationToken cancellationToken = default);
}
```

## Physiology/PhysiologyTransportSource.cs

源码：[PhysiologyTransportSource.cs](../../../src/Monitor.Simulation/Physiology/PhysiologyTransportSource.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record VentilationTransportPlan(int TidalVolumeMicrolitersBtps, int DeadSpaceMicrolitersBtps, int InspiredOxygenMillionths, bool AirwayOpen = true)
{
}
public enum StrokeVolumeResponse
{
    Constant,
    CardiacFilling,
    PrematureBeat,
    AtrialFibrillation,
    ConductedFlutter,
    SeededRate
}
public sealed record BloodFlowTransportPlan(int ReferenceStrokeVolumeMicroliters, long EjectionDurationNs, StrokeVolumeResponse Response = StrokeVolumeResponse.Constant, bool IllustrateAfSystemicPulseDeficit = false)
{
}
public readonly record struct PhysiologyTransportInterval(long FromSimTimeNs, long ToExclusiveSimTimeNs, long DeliveredVolumeNanolitersBtps, long AlveolarVentilationNanolitersBtps, long EffectiveBloodVolumeNanoliters, int InspiredOxygenMillionths, bool AirwayOpen)
{
}
public sealed record PhysiologyTransportState(RegularPhysiologyPlan Physiology, VentilationTransportPlan Ventilation, BloodFlowTransportPlan BloodFlow, string ModelId = "PhysiologyTransportInputs@1")
{
}
public sealed class PhysiologyTransportSource
{
    public const string ModelId = "PhysiologyTransportInputs@1";
    public const int MaximumEvents = 4096;
    public const long MaximumIntervalNs = 60_000_000_000;
    public static PhysiologyTransportSource Create(RegularPhysiologyPlan physiology, VentilationTransportPlan ventilation, BloodFlowTransportPlan bloodFlow);
    public PhysiologyTransportState CaptureState();
    public static PhysiologyTransportSource Restore(PhysiologyTransportState state);
    public PhysiologyTransportInterval Integrate(long fromSimTimeNs, long toExclusiveSimTimeNs, int maximumEvents = MaximumEvents, CancellationToken cancellationToken = default);
}
```

## Physiology/PhysiologyWaveformGroup.cs

源码：[PhysiologyWaveformGroup.cs](../../../src/Monitor.Simulation/Physiology/PhysiologyWaveformGroup.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed class PhysiologyWaveformGroupException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record PhysiologyWaveformChannelPlan(RegularPhysiologyPlan Physiology, WaveformBlockPlaneConfiguration Plane, IReadOnlyList<EventWaveformBand> Bands, int DelayCapacity, uint QualityFlags, VascularPressurePlan? VascularPressure = null, PlethRunoffPlan? PlethRunoff = null, int PressureZeroOffsetCentiMmHg = 0, int PressureBaselineCentiMmHg = 0)
{
}
public sealed record PhysiologyWaveformChannelState(Guid ChannelId, PhysiologySignalState Generator, SignalAcquisitionDelayState Delay, uint QualityFlags, int PressureZeroOffsetCentiMmHg = 0)
{
}
public sealed record PhysiologyWaveformGroupState(IReadOnlyList<PhysiologyWaveformChannelState> Channels, WaveformBlockAssemblerState Assembler)
{
}
public sealed class PhysiologyWaveformGroup
{
    public PhysiologyWaveformGroup Fork();
    public static PhysiologyWaveformGroup Start(Guid sessionId, Guid instanceId, ulong timebaseEpoch, ulong streamEpoch, ulong configurationRevision, ulong firstBlockSequence, int maximumBufferedBlocks, IReadOnlyList<PhysiologyWaveformChannelPlan> channels);
    public void ContinueWith(PhysiologyWaveformGroup definition);
    public PhysiologyWaveformGroupState CaptureState();
    public static PhysiologyWaveformGroup Restore(PhysiologyWaveformGroupState state);
    public IReadOnlyList<byte[]> AdvanceTo(long simTimeNs, int maximumSamplesPerChannel, int maximumBlocks, int maximumEvents, CancellationToken cancellationToken = default);
}
```

## Physiology/PlethPulsePlan.cs

源码：[PlethPulsePlan.cs](../../../src/Monitor.Simulation/Physiology/PlethPulsePlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record PlethPulsePlan(long TransitDelayNs, long DurationNs, int AmplitudeCounts, bool IncludeNotch = false)
{
    public const string EvidenceId = "PlethPulseIllustrationDraft@2";
    public IReadOnlyList<EventWaveformBand> CreateBands();
}
```

## Physiology/PlethRunoffSource.cs

源码：[PlethRunoffSource.cs](../../../src/Monitor.Simulation/Physiology/PlethRunoffSource.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record PlethRunoffPlan(long TransitDelayNs, long PulseDurationNs, int AmplitudeCounts, long TailTimeConstantNs = 400_000_000, bool IncludeNotch = false, bool UsePrematureBeatPerfusion = false, string ModelId = "PlethSmoothRunoff@1", bool UseAtrialFibrillationPerfusion = false, bool IllustrateAfSystemicPulseDeficit = false, bool UseConductedFlutterPerfusion = false, bool UseCardiacFillingPerfusion = false)
{
}
public sealed class PlethRunoffSource
{
    public const int MaximumEventCount = 4096;
    public long SupportNs { get; }
    public int MaximumHistoryEvents { get; }
    public static PlethRunoffSource Create(RegularPhysiologyPlan physiology, PlethRunoffPlan plan);
    public long EvaluateAt(long simTimeNs, CancellationToken cancellationToken = default);
}
```

## Physiology/PrematureAtrialReference.cs

源码：[PrematureAtrialReference.cs](../../../src/Monitor.Simulation/Physiology/PrematureAtrialReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class PrematureAtrialReference
{
    public const string EvidenceId = "PrematureAtrialIllustrationDraft@3";
    public static EcgCycleTiming Timing { get; }
    public static EcgCycleTiming BlockedTiming { get; }
    public static EcgCycleTiming AberrantTiming { get; }
    public static bool IsPattern(AvConductionPattern pattern);
    public static RegularPhysiologyPlan CreateAberrantPlan();
    public static RegularPhysiologyPlan CreatePlan(bool blocked = false);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool blocked = false);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateAberrantElectrodes();
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool blocked = false);
    public static IReadOnlyList<EventWaveformBand> CreateAberrantLeadIIBands();
}
```

## Physiology/PrematureBeatPerfusion.cs

源码：[PrematureBeatPerfusion.cs](../../../src/Monitor.Simulation/Physiology/PrematureBeatPerfusion.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class PrematureBeatPerfusion
{
    public const string EvidenceId = "PrematureBeatPerfusionIllustration@4";
    public static bool IsPattern(AvConductionPattern pattern);
    public static int GainPermille(AvConductionPattern pattern, ulong ordinal);
    public static long DurationNs(AvConductionPattern pattern, ulong ordinal, long baselineNs);
    public static long MinimumEjectingIntervalNs(AvConductionPattern pattern);
}
```

## Physiology/PrematureJunctionalReference.cs

源码：[PrematureJunctionalReference.cs](../../../src/Monitor.Simulation/Physiology/PrematureJunctionalReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class PrematureJunctionalReference
{
    public const string EvidenceId = "PrematureJunctionalIllustrationDraft@2";
    public static EcgCycleTiming Timing { get; }
    public static bool IsPattern(AvConductionPattern pattern);
    public static long RetrogradeOffsetNs(AvConductionPattern pattern);
    public static RegularPhysiologyPlan CreatePlan(AvConductionPattern pattern = AvConductionPattern.PrematureJunctionalIllustration);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
}
```

## Physiology/PrematureVentricularReference.cs

源码：[PrematureVentricularReference.cs](../../../src/Monitor.Simulation/Physiology/PrematureVentricularReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class PrematureVentricularReference
{
    public const string EvidenceId = "PrematureVentricularIllustrationDraft@8";
    public static bool IsPattern(AvConductionPattern pattern);
    public static int BeatsPerGroup(AvConductionPattern pattern);
    public static long MinimumRrNs(AvConductionPattern pattern);
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan(AvConductionPattern pattern = AvConductionPattern.PrematureVentricularIllustration);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(AvConductionPattern pattern = AvConductionPattern.PrematureVentricularIllustration);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(AvConductionPattern pattern = AvConductionPattern.PrematureVentricularIllustration);
}
```

## Physiology/PulmonaryArteryPulsePlan.cs

源码：[PulmonaryArteryPulsePlan.cs](../../../src/Monitor.Simulation/Physiology/PulmonaryArteryPulsePlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record PulmonaryArteryPulsePlan(long TransitDelayNs, long DurationNs, int BaselineMmHg, int PulseHeightMmHg)
{
    public const string EvidenceId = "InfirmaryPulmonaryArteryDraft@1";
    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology, Guid channelId, uint qualityFlags);
}
```

## Physiology/QuinidineEffectReference.cs

源码：[QuinidineEffectReference.cs](../../../src/Monitor.Simulation/Physiology/QuinidineEffectReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum QuinidineIllustration
{
    Reference,
    LowT,
    InvertedT,
    WideQrsLowT,
    WideQrsInvertedT
}
public static class QuinidineEffectReference
{
    public const string EvidenceId = "QuinidineEffectIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public const long QuIntervalNs = 710_000_000;
    public static EcgCycleTiming ResolveTiming(QuinidineIllustration mode);
    public static long ResolveQuIntervalNs(QuinidineIllustration mode);
    public static RegularPhysiologyPlan CreatePlan(QuinidineIllustration mode = QuinidineIllustration.LowT);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(QuinidineIllustration mode, bool notchedP = false);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(QuinidineIllustration mode, bool notchedP = false);
}
```

## Physiology/RealtimeOxygenationSource.cs

源码：[RealtimeOxygenationSource.cs](../../../src/Monitor.Simulation/Physiology/RealtimeOxygenationSource.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record RealtimeOxygenationConfiguration(VentilationTransportPlan Ventilation, OxygenReservoirParameters Parameters, int ReferenceStrokeVolumeMicroliters = 66667)
{
    public static RealtimeOxygenationConfiguration ReferenceAdult { get; }
    public decimal OxygenDemandMultiplier { get; init; }
}
public readonly record struct RealtimeOxygenationSnapshot(long SourceSimTimeNs, int SaturationMilliPercent, OxygenReservoirState Reservoirs, decimal OxygenBalanceResidualMl, int RetainedSamples, decimal OxygenDemandMultiplier = 1)
{
}
public sealed class RealtimeOxygenationSource : IArterialOxygenationSource
{
    public const int HistoryCapacity = 1024;
    public long SourceSimTimeNs { get; }
    public RealtimeOxygenationSnapshot Snapshot { get; }
    public RealtimeOxygenationSource(PhysiologyTransportSource transport, OxygenReservoirParameters parameters, decimal oxygenDemandMultiplier = 1, long? initialSimTimeNs = null);
    public RealtimeOxygenationSource Fork();
    public void AdvanceTo(long toSimTimeNs);
    public long ChangeVentilation(VentilationTransportPlan ventilation, long atSimTimeNs, decimal? oxygenDemandMultiplier = null);
    public void ChangeTransport(PhysiologyTransportSource transport, long atSimTimeNs, decimal oxygenDemandMultiplier);
    public ArterialOxygenationSample ReadAt(long sourceSimTimeNs);
    public static void ValidateVentilation(VentilationTransportPlan ventilation);
}
```

## Physiology/RegularPhysiologyTimeline.cs

源码：[RegularPhysiologyTimeline.cs](../../../src/Monitor.Simulation/Physiology/RegularPhysiologyTimeline.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum PhysiologyCycleEventKind
{
    AtrialElectrical,
    VentricularElectrical,
    AtrialMechanical,
    VentricularMechanical,
    InspirationStart,
    ExpirationStart,
    AtrialFibrillationSegment,
    VentricularDisorganizationSegment,
    PrematureAtrialElectrical,
    RetrogradeAtrialElectrical,
}
public enum RespiratoryActivity
{
    Breathing,
    EffortOnly,
    Absent
}
public enum CardiacActivity
{
    AtrialAndVentricular,
    AtrialOnly,
    Absent,
    VentricularOnly
}
public enum AvConductionPattern
{
    FixedPr,
    WenckebachFourToThreeIllustration,
    CompleteAvBlockJunctionalIllustration,
    CompleteAvBlockVentricularIllustration,
    AtrialFlutterIllustration,
    AtrialFibrillationCoarseIllustration,
    AtrialFibrillationFineIllustration,
    VentricularFlutterIllustration,
    VentricularFibrillationCoarseIllustration,
    VentricularFibrillationFineIllustration,
    WenckebachThreeToTwoIllustration,
    WenckebachFiveToFourIllustration,
    MobitzTwoThreeToTwoIllustration,
    MobitzTwoFourToThreeIllustration,
    MobitzTwoRbbbFourToThreeIllustration,
    MobitzTwoLbbbFourToThreeIllustration,
    PrematureAtrialIllustration,
    BlockedPrematureAtrialIllustration,
    AberrantPrematureAtrialIllustration,
    PrematureJunctionalIllustration,
    PrematureJunctionalAfterQrsIllustration,
    PrematureJunctionalOverlappingIllustration,
    PrematureVentricularIllustration,
    VentricularBigeminyIllustration,
    VentricularTrigeminyIllustration,
    PolymorphicPvcIllustration,
    MultifocalPvcIllustration,
    InterpolatedPvcIllustration,
    VentricularCoupletIllustration,
    PolymorphicVentricularCoupletIllustration,
    RonTLongQtPvcIllustration,
    ShortCoupledRonTPvcIllustration,
    VariableAtrialFlutterIllustration,
    NarrowComplexSvtIllustration,
    MonomorphicVtIllustration,
    VtCaptureIllustration,
    AcceleratedVentricularIllustration,
    AcceleratedJunctionalIllustration,
    SinusArrhythmiaIllustration,
    SinusArrestIllustration,
}
public sealed record RegularPhysiologyPlan(long EpochAnchorSimTimeNs, long HeartPeriodNs, long VentricularElectricalOffsetNs, long AtrialMechanicalOffsetNs, long VentricularMechanicalOffsetNs, long BreathPeriodNs, long InspirationDurationNs, long InspiratoryPauseNs = 0, long ExpiratoryPauseNs = 0, RespiratoryActivity RespiratoryActivity = RespiratoryActivity.Breathing, ulong? ActivityAfterBreaths = null, ulong? ActivityDurationBreaths = null, int VentricularConductionRatio = 1, CardiacActivity CardiacActivity = CardiacActivity.AtrialAndVentricular, bool VentricularMechanicalEnabled = true, ulong? MechanicalAfterCycles = null, ulong? MechanicalDurationCycles = null, int MechanicalEveryCycles = 1, long? IndependentVentricularPeriodNs = null, RespiratoryPattern RespiratoryPattern = RespiratoryPattern.Regular, int ConductedBeatsPerGroup = 1, AvConductionPattern ConductionPattern = AvConductionPattern.FixedPr)
{
    public SeededCardiacRate? SeededRate { get; init; }
    public CardiacRateAdjustment? RateAdjustment { get; init; }
    public SeededRhythmSchedule? RhythmSchedule { get; init; }
}
public sealed record RegularPhysiologyState(RegularPhysiologyPlan Plan, long CursorSimTimeNs)
{
}
public readonly record struct PhysiologyCycleEvent(long SimTimeNs, PhysiologyCycleEventKind Kind, ulong CycleIndex)
{
}
public sealed class PhysiologyTimelineException(string reason, string parameter) : ArgumentException(reason, parameter)
{
    public string ReasonCode { get; }
}
public sealed class RegularPhysiologyTimeline
{
    public const int MaximumEventCount = 1_000_000;
    public static RegularPhysiologyTimeline Start(RegularPhysiologyPlan plan);
    public static RegularPhysiologyTimeline Restore(RegularPhysiologyState state);
    public RegularPhysiologyState CaptureState();
    public IReadOnlyList<PhysiologyCycleEvent> AdvanceBefore(long exclusiveSimTimeNs, int maximumEvents, CancellationToken cancellationToken = default);
}
```

## Physiology/RespirationPlan.cs

源码：[RespirationPlan.cs](../../../src/Monitor.Simulation/Physiology/RespirationPlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record RespirationPlan(int AmplitudeCounts, int CardiacArtifactCounts = 0)
{
    public const string EvidenceId = "RespirationIllustrationDraft@1";
    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology, Guid channelId, uint qualityFlags);
}
```

## Physiology/RespiratoryPattern.cs

源码：[RespiratoryPattern.cs](../../../src/Monitor.Simulation/Physiology/RespiratoryPattern.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum RespiratoryPattern
{
    Regular,
    CheyneStokesIllustration,
    IntermittentIllustration
}
```

## Physiology/RightBundleBlockReference.cs

源码：[RightBundleBlockReference.cs](../../../src/Monitor.Simulation/Physiology/RightBundleBlockReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class RightBundleBlockReference
{
    public const string EvidenceId = "RightBundleBlockIllustrationDraft@1";
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
}
```

## Physiology/SeededCardiacRate.cs

源码：[SeededCardiacRate.cs](../../../src/Monitor.Simulation/Physiology/SeededCardiacRate.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record SeededCardiacRate
{
    public const string PatternId = "SeededCardiacSlowVariation@2";
    public int HeartRateBpm { get; }
    public string SeedHex { get; }
    public int VariationPermille { get; }
    public DeterministicStreamState PreparedState { get; }
    public long PeriodNs { get; }
    public long MinimumPeriodNs { get; }
    public SeededCardiacRate(int heartRateBpm, string seedHex, int variationPermille);
    public int EjectionGainPermille(ulong ordinal);
    public EcgCycleTiming Timing { get; }
}
```

## Physiology/SeededExpirationPressure.cs

源码：[SeededExpirationPressure.cs](../../../src/Monitor.Simulation/Physiology/SeededExpirationPressure.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed class SeededExpirationPressure
{
    public int TargetMmHg { get; }
    public int AmplitudeCentiMmHg { get; }
    public DeterministicStreamState PreparedState { get; }
    public SeededExpirationPressure(int targetMmHg, int amplitudeCentiMmHg, string seedHex);
}
```

## Physiology/SeededVascularVariation.cs

源码：[SeededVascularVariation.cs](../../../src/Monitor.Simulation/Physiology/SeededVascularVariation.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed class SeededVascularVariation : IEquatable<SeededVascularVariation>
{
    public const string PatternId = "SeededVascularDrift@1";
    public const long KnotPeriodNs = 15_000_000_000;
    public const int KnotCount = 64;
    public const int MaximumAmplitudePermille = 400;
    public int AmplitudePermille { get; }
    public string StreamName { get; }
    public DeterministicStreamState PreparedState { get; }
    public string SeedHex { get; }
    public SeededVascularVariation(int amplitudePermille, string seedHex, string streamName);
    public bool Equals(SeededVascularVariation? other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public int GainPermille(long simTimeNs);
}
```

## Physiology/ShortPrReference.cs

源码：[ShortPrReference.cs](../../../src/Monitor.Simulation/Physiology/ShortPrReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class ShortPrReference
{
    public const string EvidenceId = "ShortPrWithoutDeltaIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
}
```

## Physiology/SinusArrestReference.cs

源码：[SinusArrestReference.cs](../../../src/Monitor.Simulation/Physiology/SinusArrestReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class SinusArrestReference
{
    public const string EvidenceId = "SinusArrestIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
}
```

## Physiology/SinusArrhythmiaReference.cs

源码：[SinusArrhythmiaReference.cs](../../../src/Monitor.Simulation/Physiology/SinusArrhythmiaReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class SinusArrhythmiaReference
{
    public const string EvidenceId = "SinusArrhythmiaIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands();
}
```

## Physiology/SupraventricularTachycardiaReference.cs

源码：[SupraventricularTachycardiaReference.cs](../../../src/Monitor.Simulation/Physiology/SupraventricularTachycardiaReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class SupraventricularTachycardiaReference
{
    public const string EvidenceId = "NarrowComplexSvtIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public const string RightBundleEvidenceId = "SvtRightBundleBlockIllustration@1";
    public const string LeftBundleEvidenceId = "SvtLeftBundleBlockIllustration@1";
    public static EcgCycleTiming ResolveTiming(bool rightBundleBlock = false, bool leftBundleBlock = false);
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool rightBundleBlock = false, bool leftBundleBlock = false);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool rightBundleBlock = false, bool leftBundleBlock = false);
}
```

## Physiology/SvtPerfusionReference.cs

源码：[SvtPerfusionReference.cs](../../../src/Monitor.Simulation/Physiology/SvtPerfusionReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class SvtPerfusionReference
{
    public const string EvidenceId = "SvtPerfusionIllustration@3";
    public static int StrokeVolumePermille(long periodNs);
    public static PlethRunoffPlan Pleth { get; }
    public static VascularPressurePlan Arterial { get; }
    public static VascularPressurePlan Pulmonary { get; }
    public static CentralVenousPressurePlan Venous { get; }
}
```

## Physiology/TextbookElectrodeReference.cs

源码：[TextbookElectrodeReference.cs](../../../src/Monitor.Simulation/Physiology/TextbookElectrodeReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class TextbookElectrodeReference
{
    public const string EvidenceId = "TextbookChestProgressionDraft@3";
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(EcgUWavePlan? uWave = null, EcgCycleTiming? timing = null, EcgTWaveScalePlan? tWave = null, EcgTWaveShapePlan? tShape = null, EcgStSegmentPlan? stSegment = null, EcgPWavePlan? pWave = null, EcgStTFusionPlan? fusion = null, EcgChestInfarctionPlan? infarction = null, EcgInfarctionZones? zones = null, EcgAtrialIllustration atrial = EcgAtrialIllustration.Reference, EcgVentricularIllustration ventricular = EcgVentricularIllustration.Reference, EcgTContourPlan? tContour = null);
}
```

## Physiology/TwistingVtReference.cs

源码：[TwistingVtReference.cs](../../../src/Monitor.Simulation/Physiology/TwistingVtReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class TwistingVtReference
{
    public const string EvidenceId = "TwistingVtMorphologyIllustration@1";
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes();
}
```

## Physiology/VascularPressureMorphologyPlan.cs

源码：[VascularPressureMorphologyPlan.cs](../../../src/Monitor.Simulation/Physiology/VascularPressureMorphologyPlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public enum VascularPressureMorphologyKind
{
    Arterial,
    PulmonaryArtery
}
public sealed record VascularPressureMorphologyPlan(VascularPressureMorphologyKind Kind, long DurationNs, int PulseHeightCentiMmHg, string ModelId = "VascularPressureMorphologyRatio@2", int MaximumPulseOverlap = 1)
{
    public const string EvidenceId = "VascularPressureMorphologyRatio@2";
}
```

## Physiology/VascularPressurePlan.cs

源码：[VascularPressurePlan.cs](../../../src/Monitor.Simulation/Physiology/VascularPressurePlan.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record VascularPressurePlan(long TransitDelayNs, long EjectionDurationNs, long TimeConstantNs, int InitialPressureCentiMmHg, int AsymptoticPressureCentiMmHg, int EjectionEquilibriumCentiMmHg, string ModelId = "VascularPressureRcIllustration@1", VascularPressureMorphologyPlan? Morphology = null, bool UsePrematureBeatPerfusion = false, bool UseAtrialFibrillationPerfusion = false, bool IllustrateAfSystemicPulseDeficit = false, bool UseConductedFlutterPerfusion = false, bool UseCardiacFillingPerfusion = false, SeededVascularVariation? Variation = null)
{
    public const string EvidenceId = "VascularPressureRcIllustration@1";
    public PhysiologyWaveformChannelPlan CreateChannel(RegularPhysiologyPlan physiology, Guid channelId, uint qualityFlags);
}
```

## Physiology/VascularPressureSource.cs

源码：[VascularPressureSource.cs](../../../src/Monitor.Simulation/Physiology/VascularPressureSource.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed class VascularPressureSource
{
    public const string KernelId = "VascularPressureRcTrapezoidal1UsQ62@1";
    public const long MeshStepNs = 1_000;
    public const int MaximumEjectionCount = 4096;
    public static VascularPressureSource Create(RegularPhysiologyPlan physiology, VascularPressurePlan plan);
    public static int? SolveVariationAmplitude(RegularPhysiologyPlan physiology, VascularPressurePlan plan, int amplitudeCentiMmHg);
    public static (int EjectionEquilibriumCentiMmHg, int PulseHeightCentiMmHg)? SolveTarget(RegularPhysiologyPlan physiology, VascularPressurePlan plan, int systolicCentiMmHg, int diastolicCentiMmHg);
    public long EvaluateAt(long simTimeNs, CancellationToken cancellationToken = default);
}
```

## Physiology/VentricularDisorganizationReference.cs

源码：[VentricularDisorganizationReference.cs](../../../src/Monitor.Simulation/Physiology/VentricularDisorganizationReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class VentricularDisorganizationReference
{
    public const string EvidenceId = "VentricularDisorganizationIllustrationDraft@1";
    public static bool IsPattern(AvConductionPattern pattern);
    public static long SegmentDurationNs(AvConductionPattern pattern);
    public static RegularPhysiologyPlan CreatePlan(AvConductionPattern pattern);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(AvConductionPattern pattern);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(AvConductionPattern pattern);
}
```

## Physiology/VentricularTachycardiaReference.cs

源码：[VentricularTachycardiaReference.cs](../../../src/Monitor.Simulation/Physiology/VentricularTachycardiaReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class VentricularTachycardiaReference
{
    public const string EvidenceId = "MonomorphicVtIllustration@1";
    public static EcgCycleTiming Timing { get; }
    public static RegularPhysiologyPlan CreatePlan(bool capture = false);
    public const string CaptureEvidenceId = "VtCaptureIllustration@1";
    public const string FusionEvidenceId = "VtFusionIllustration@1";
    public const int FusionCycleLength = 32;
    public const int FusionCycleSlot = 13;
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool fusion = false, bool capture = false, bool bidirectional = false, bool twisting = false);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool fusion = false, bool capture = false, bool bidirectional = false, bool twisting = false);
}
```

## Physiology/VtPerfusionReference.cs

源码：[VtPerfusionReference.cs](../../../src/Monitor.Simulation/Physiology/VtPerfusionReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class VtPerfusionReference
{
    public const string EvidenceId = "VtPerfusionIllustration@3";
    public static PlethRunoffPlan Pleth { get; }
    public static VascularPressurePlan Arterial { get; }
    public static VascularPressurePlan Pulmonary { get; }
    public static CentralVenousPressurePlan Venous { get; }
}
```

## Physiology/WpwReference.cs

源码：[WpwReference.cs](../../../src/Monitor.Simulation/Physiology/WpwReference.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public static class WpwReference
{
    public const string EvidenceId = "WpwPositiveV1Illustration@1";
    public const string NegativeV1EvidenceId = "WpwNegativeV1Illustration@1";
    public static EcgCycleTiming Timing { get; }
    public static EcgCycleTiming ResolveTiming(bool smallerDelta);
    public static RegularPhysiologyPlan CreatePlan();
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool negativeV1 = false, bool smallerDelta = false);
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool negativeV1 = false, bool smallerDelta = false);
}
```

## Physiology/SeededRhythmSchedule.cs

源码：[SeededRhythmSchedule.cs](../../../src/Monitor.Simulation/Physiology/SeededRhythmSchedule.cs) · 命名空间：`Monitor.Simulation.Physiology`

```csharp
public sealed record SeededRhythmSchedule
{
    public string SeedHex { get; }
    public IReadOnlyList<DeterministicStreamState> PreparedStates { get; }
    public static SeededRhythmSchedule Default { get; }
    public long PauseDurationNs { get; }
    public int ConductionPercent { get; }
    public SeededRhythmSchedule(string seedHex, long pauseDurationNs = 2_000_000_000, int conductionPercent = 33);
    public static bool Supports(AvConductionPattern pattern);
}
```
