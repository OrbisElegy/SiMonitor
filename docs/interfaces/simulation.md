# 仿真、采集与波形接口

[接口总览](README.md) · [仿真公开声明](api/simulation.md)

本文对应 `src/Monitor.Simulation` 的现有实现，说明调用边界、数据单位、状态恢复和拒绝语义。
命名空间分别为 `Monitor.Simulation.Determinism`、`.Acquisition`、`.Authoring`、`.Physiology`。
这里的源事件、作者配置和模型氧合值属于教学仿真输入；测量和报警由后续消费层处理。
查找全部公开声明时可结合本目录的 API 清单；形态 LUT 和参考系数不在本文重复展开。

## 1. 时间、数值与生命周期

| 契约 | 含义 |
| --- | --- |
| `SimTimeNs`、`SourceSimTimeNs` | 数据仿真时间，单位 ns；不是墙钟、UI 帧时间或到达时间 |
| `EpochAnchorSimTimeNs` | 本采样网格的时间原点；样本时间为原点加索引乘采样周期 |
| `GenerateBefore`、`DrainBefore`、`AdvanceBefore` | 右端点排除；只生成时间小于 `exclusiveSimTimeNs` 的样本或事件 |
| `DrainAvailable(simTimeNs)` | 释放可用时间小于等于指定时间的样本；与源时间区分 |
| `FromSimTimeNs` / `ToExclusiveSimTimeNs` | 半开积分区间 `[from, to)` |
| `ValueQ32`、`TableQ32` | 有符号整数值乘 `2^32`；量纲由调用的源或平面决定 |
| `PhaseU64` | 一周映射到无符号 64 位相位空间，按模 `2^64` 递增 |
| `Permille` / `Millionths` | 千分比 / 百万分比；`SaturationMilliPercent=98000` 表示 98% |
| `CentiMmHg` / `Microvolts` | 0.01 mmHg / µV；线上的 `short` 本身不携带单位 |

有状态生成器要求单一所有者串行调用。`CaptureState()` 返回恢复数据，`Restore(state)` 会重新校验；
`Fork()` 是已拥有、已验证对象的事务副本，不是面向不可信外部 checkpoint 的校验入口。
这些内存状态 record 不自动形成 JSON、磁盘或网络协议；明确的二进制协议只有本章描述的 codec。
有上限参数的生成调用在超限时拒绝整批，不返回截断结果；调用方应缩短仿真推进跨度。

## 2. 确定性原语与 checkpoint

实现：[FixedPointPrimitives.cs](../../src/Monitor.Simulation/Determinism/FixedPointPrimitives.cs)、
[DeterministicStream.cs](../../src/Monitor.Simulation/Determinism/DeterministicStream.cs)、
[DeterminismCheckpointCodec.cs](../../src/Monitor.Simulation/Determinism/DeterminismCheckpointCodec.cs)。

```csharp
Int128 FixedPointMath.RoundDivideTiesToEven(Int128 numerator, Int128 denominator);
SaturatingInt64 FixedPointMath.Saturate(Int128 value);
SaturatingInt64 PeriodicLutLinear.Interpolate(ReadOnlySpan<long> tableQ32, ulong phaseU64);
DeterministicStreamFactory(ReadOnlySpan<byte> rootSeed);
static DeterministicStreamFactory FromLowercaseHex(string rootSeedHex);
DeterministicRandomSource CreateStream(string streamId);
ulong DeterministicRandomSource.NextUInt64();
ulong DeterministicRandomSource.UniformBelow(ulong bound);
long DeterministicRandomSource.NextNormal12CenteredQ32();
DeterministicStreamState DeterministicRandomSource.CaptureState();
static DeterministicRandomSource Restore(DeterministicStreamState state);
DeterminismCheckpointCodec(ReadOnlySpan<byte> profileContentSha256);
byte[] Serialize(DeterminismCheckpoint checkpoint);
DeterminismCheckpoint Restore(ReadOnlySpan<byte> bytes);
```

定点除法使用 ties-to-even；除零抛 `DeterminismArithmeticException`。
`Add`、`Subtract`、`MultiplyQ32`、`DivideQ32`、`MultiplyCoefficientQ62` 返回
`SaturatingInt64(Value, Saturated)`；调用方需要保留饱和信息。
`FixedBiquadDf2T.Step` 消费 Q62 系数、Q32 输入和 `FixedBiquadState`，返回输出、下一状态和饱和标志。
周期 LUT 长度须是 4–65536 的二次幂，末项之后环绕首项。

随机根种子必须是 32 字节，十六进制入口要求 64 个小写字符。
流 ID 长度 1–128，以小写字母开头，仅含小写字母、数字、`.`、`_`、`-`；分隔符不得相邻或结尾。
工厂以 HMAC-SHA256 和 `MONDET1\0` 域派生独立 xoshiro256** 状态；相同根种子与 ID 重建相同起点。
`UniformBelow` 拒绝零上界，拒绝采样可能消费多次 draw；`NextNormal12CenteredQ32` 固定消费 12 次 draw。
`DeterministicStreamState` 保存 ID、`S0`–`S3` 和 `DrawCount`；全零随机状态在恢复时被替换为规定非零状态。
工厂 `Dispose()` 清零持有的根种子，不能再创建流。

`DeterminismCheckpoint` 包含 `SimTimeNs`、`PhaseU64`、`FilterYQ32`、`Streams`。
二进制格式为 `MDET` + UInt16 主/次版本 `1/0` + 32 字节 profile SHA-256 + 三个 8 字节数值 + UInt16 流数；
固定区共 66 字节。每流为 UInt16 ASCII ID 长度、ID、五个 UInt64（四段状态和 draw count），固定开销 42 字节。
数值均为小端；序列化按流 ID ordinal 排序并拒绝重复 ID；恢复要求严格递增、完整消费数据和 profile hash 匹配。
主要拒绝码是 `Checkpoint.BadMagic`、`UnsupportedVersion`、`ProfileHashMismatch`、`InvalidStreamId`、
`StreamOrder`、`Truncated`、`TrailingBytes`（均带 `Checkpoint.` 前缀）。它不含 CRC，也不是全场景状态格式。

[DeterminismConformanceKernel](../../src/Monitor.Simulation/Determinism/DeterminismConformanceKernel.cs)
提供 `Create(rootSeed)` / `CreateFromLowercaseHex` / `Restore` / `Step` / `CaptureCheckpoint`；
固定步长 4 ms，`DeterminismStepRecord.WriteTo(Span<byte>)` 输出固定 40 字节一致性记录。

## 3. 采样、延迟与 200 ms 组块

实现：[SignalSampleClock.cs](../../src/Monitor.Simulation/Acquisition/SignalSampleClock.cs)、
[SignalAcquisitionDelayLine.cs](../../src/Monitor.Simulation/Acquisition/SignalAcquisitionDelayLine.cs)、
[WaveformBlockAssembler.cs](../../src/Monitor.Simulation/Acquisition/WaveformBlockAssembler.cs)。

`FrozenSignalAcquisitionProfiles.Get(profileId)` 只接受下列冻结 ID：

| Profile ID | Hz / 周期 | 采集延迟 | 类别 |
| --- | --- | --- | --- |
| `AcqECGMonitor250@1` | 250 / 4 ms | 40 ms | ECG-monitor |
| `AcqResp125@1` | 125 / 8 ms | 80 ms | IMP-Resp |
| `AcqPleth125@1` | 125 / 8 ms | 2000 ms | Pleth |
| `AcqPressure125@1` | 125 / 8 ms | 80 ms | Pressure |
| `AcqCO2_100@1` | 100 / 10 ms | 2000 ms | CO2 |

当前都声明 `Identity@1` 滤波、`Int16Normalized` 量化和 `ProjectTeachingProfile` 证据状态。

```csharp
static SignalSampleClock Start(string profileId, ulong streamEpoch, long startSimTimeNs);
IReadOnlyList<SignalSampleTick> DrainBefore(long exclusiveSimTimeNs);
SignalProfileBoundary ApplyProfileBoundary(string nextProfileId, ulong nextStreamEpoch,
    long boundarySimTimeNs, FilterStateTransitionPolicy filterStateTransitionPolicy,
    bool standardEcgRecordActive);
static SignalAcquisitionDelayLine Start(string profileId, ulong streamEpoch,
    long epochAnchorSimTimeNs, int capacity);
void Enqueue(SignalSampleTick tick, short normalizedValue, uint qualityFlags = 0);
IReadOnlyList<DelayedSignalSample> DrainAvailable(long simTimeNs);
static WaveformBlockAssembler Start(Guid sessionId, Guid instanceId, ulong timebaseEpoch,
    ulong streamEpoch, ulong configurationRevision, ulong firstBlockSequence,
    long epochAnchorSimTimeNs, int maximumBufferedBlocks,
    IReadOnlyList<WaveformBlockPlaneConfiguration> planes);
IReadOnlyList<WaveformEnvelope> Push(Guid channelId, IReadOnlyList<DelayedSignalSample> samples);
```

时钟批次最多 1,000,000 个 tick。切换 profile 必须位于当前 cursor、使用不同 profile 和严格递增 stream epoch，
且不得在标准 ECG 记录活动期间进行；切换后样本索引从零开始。
`FilterStateTransitionPolicy` 有 `Reacquire`、`Clear`、`MigrateValidated`，此入口记录所选策略，不迁移实际滤波器。

延迟队列容量 1–1,000,000；`Enqueue` 校验 profile、epoch、连续索引和精确源时间，
计算 `AvailableSimTimeNs = SourceSimTimeNs + LatencyNs`。已经晚于释放 cursor 才提交的输入被拒绝。
调用顺序是先释放当前源时刻可用的旧数据，再加入该时刻新 tick，最后释放至本次推进终点。

组块器接收 1–128 个唯一通道，按 UUID 规范字节顺序排列。
`WaveformBlockPlaneConfiguration` 明确 channel、profile、比例和偏置；比例及偏置的分母均不能为零。
每块固定 `BlockDurationNs=200000000`；每个平面分别有 50、25 或 20 个样本。
只有全部平面具备同一时间段的完整样本才发布块；每平面缓存上限为 `maximumBufferedBlocks × samplesPerBlock`，
其中 `maximumBufferedBlocks` 为 1–300。组块器不会补样、插值或静默跳过缺口。

通用周期形态入口见 [PeriodicSignalGenerator.cs](../../src/Monitor.Simulation/Acquisition/PeriodicSignalGenerator.cs)：
`PeriodicSignalPlan(ProfileId, StreamEpoch, EpochAnchorSimTimeNs, InitialPhaseU64, PhaseIncrementU64, TableQ32)`；
相位步进不得为零，表值必须落在 Int16 对应 Q32 范围内。
`EvaluateAt(ulong sampleIndex)` 与 `EvaluateRange(ulong firstSampleIndex, int sampleCount, CancellationToken = default)`
是纯索引求值，不推进时钟；`GenerateBefore(long exclusiveSimTimeNs, int maximumSamples, CancellationToken = default)` 推进时钟。

[PeriodicWaveformPipeline](../../src/Monitor.Simulation/Acquisition/PeriodicWaveformPipeline.cs) 组合单通道源、延迟和组块器；
[PeriodicWaveformGroup](../../src/Monitor.Simulation/Acquisition/PeriodicWaveformGroup.cs) 组合多通道。
二者都有 `Start`、`CaptureState`、`Restore` 和 `AdvanceTo`；单通道参数名为 `maximumSamples`，
组参数名为 `maximumSamplesPerChannel`，随后都是 `maximumBlocks` 和可选取消令牌，输出 `IReadOnlyList<byte[]>`。
恢复时重算待发送样本并校验值、质量、索引和各 cursor 一致性。推进先完成试算，再发布内部状态。

## 4. MWS1 波形二进制 wire

完整实现：[WaveformEnvelopeCodec.cs](../../src/Monitor.Simulation/Acquisition/WaveformEnvelopeCodec.cs)。
入口为 `byte[] EncodeRaw(WaveformEnvelope envelope)` 和 `WaveformEnvelope Decode(ReadOnlySpan<byte> wire)`。
`WaveformEnvelope` 是会话、实例、timebase/stream epoch、块序号、配置修订、源起始时间、时长和多平面集合。
`WaveformPlane` 保存采样率有理数、首样本索引、比例/偏置有理数、Int16 样本和质量区间。
物理值为 `sample × ScaleNumerator / ScaleDenominator + OffsetNumerator / OffsetDenominator`。

wire 顺序为 32 字节 prelude、header、payload。除 UUID 外，多字节数值均为小端；
UUID 使用 `Guid.TryWriteBytes(..., bigEndian: true, ...)` 的规范 16 字节排列。

| Prelude 偏移 | 字段 / 大小 |
| --- | --- |
| 0 | ASCII `MWS1`，4 字节 |
| 4 / 5 / 6 / 7 | major=1、minor=0、messageType=1、codec=0，各 1 字节 |
| 8 / 10 | flags=0 / headerSize，各 UInt16 |
| 12 / 16 | payloadSize / rawPayloadSize，各 UInt32，raw codec 下相等 |
| 20 / 24 | header CRC32C / payload CRC32C，各 UInt32 |
| 28 | reserved=0，UInt32 |

| Header 相对偏移 | 字段 / 大小 |
| --- | --- |
| 0 / 16 | SessionId / InstanceId，各 16 字节 UUID |
| 32 / 40 / 48 / 56 | TimebaseEpoch / StreamEpoch / BlockSequence / ConfigurationRevision，各 UInt64 |
| 64 / 72 | StartSimTimeNs Int64 / DurationNs UInt32 |
| 76 / 78 | planeCount / descriptorSize=72，各 UInt16 |
| 80 | content SHA-256，32 字节；wire 绝对偏移 112 |
| 112 起 | 每平面一个 72 字节 descriptor |

| Descriptor 相对偏移 | 字段 / 大小 |
| --- | --- |
| 0 | ChannelId，16 字节 UUID |
| 16 / 20 / 24 | 采样率分子 UInt32 / 分母 UInt32 / FirstSampleIndex UInt64 |
| 32 / 36 / 40 / 44 | sampleCount / payloadOffset / planeBytes / rawPlaneBytes，各 UInt32 |
| 48 / 52 / 56 / 60 | scale 分子 Int32 / 分母 UInt32 / offset 分子 Int32 / 分母 UInt32 |
| 64 / 65 / 66 / 68 | sampleFormat=1 byte / qualityEncoding byte / qualityRangeCount UInt16 / reserved=0 UInt32 |

每平面 payload 先放 `sampleCount × 2` 字节的 Int16 样本，再放质量区间；每区间 12 字节，
依次是 UInt32 `FirstSampleOffset`、`Count`、`QualityFlags`。
区间须按偏移排列、不重叠、落在样本范围内；标志位由消费者解释。
`WaveformQualityEncoding` 为 `None=0` 或 `Ranges=1`；codec 并不赋予质量位临床含义。
编码器对通道排序并拒绝重复；解码器要求通道 UUID 严格递增，payload 平面紧邻、无洞且恰好覆盖。

固定 header 为 112 字节；header 上限 16384，payload/raw payload 各上限 262144，平面上限 128。
实际 header 必须等于 `112 + 72 × planeCount`。codec 单独不要求 200 ms 或非空平面；
这些生产流约束由组块器、ring 或 archive 进一步验证。

CRC32C 使用 Castagnoli 反射多项式 `0x82f63b78`，初值全 1，结果按位取反。
header CRC 覆盖 prelude（两个 CRC 字段偏移 20–27 均清零）和整个 header；payload CRC 覆盖传输 payload。
content SHA-256 覆盖 header（hash 位置清零）与 raw payload，不包含 prelude。
当前只实现 raw codec 0；codec 1 通过基础 header 格式检查后仍以 `BINARY_CODEC_VECTOR_UNSUPPORTED` 拒绝。
长度、版本、排序、descriptor、CRC 或 SHA 失败分别返回 `WaveformEnvelopeException.ReasonCode`，
如 `BINARY_LENGTH_INVALID`、`SCHEMA_MAJOR_UNSUPPORTED`、`BINARY_HEADER_CRC_MISMATCH`、
`BINARY_PAYLOAD_CRC_MISMATCH`、`BINARY_CONTENT_HASH_MISMATCH`；不能把校验失败的数据继续发布。

## 5. 保留、订阅、恢复与记录归档

实现：[WaveformBlockRing.cs](../../src/Monitor.Simulation/Acquisition/WaveformBlockRing.cs)、
[WaveformSubscriberOutbox.cs](../../src/Monitor.Simulation/Acquisition/WaveformSubscriberOutbox.cs)、
[WaveformRecoveryPlanner.cs](../../src/Monitor.Simulation/Acquisition/WaveformRecoveryPlanner.cs)。

```csharp
static WaveformBlockRing Start(Guid sessionId, Guid instanceId, ulong timebaseEpoch,
    ulong streamEpoch, ulong firstBlockSequence, long firstBlockStartSimTimeNs, int capacity);
WaveformBlockAppendResult Append(ReadOnlySpan<byte> rawEnvelope);
WaveformBlockReplayResult ReadFrom(ulong firstBlockSequence, int maximumBlocks);
static WaveformSubscriberOutbox Start(Guid subscriberId, WaveformBlockRing hostRing,
    int capacity, ulong firstBlockSequence);
WaveformSubscriberEnqueueResult Enqueue(ulong blockSequence);
WaveformSubscriberReadResult ReadNext();
WaveformSubscriberAcknowledgeResult Acknowledge(ulong streamEpoch,
    ulong blockSequence, string contentSha256);
WaveformSubscriberQueueState ResetAfterResync(WaveformBlockRing hostRing, ulong firstBlockSequence);
static WaveformRecoveryPlan WaveformRecoveryPlanner.Plan(WaveformBlockRing hostRing,
    WaveformRecoveryRequest request);
```

ring 容量 1–300；`HostRetentionBlockCount=300` 为 60 s，`ClientHistoryBlockCount=50` 为 10 s。
`Append` 要求身份、epoch、200 ms 时长、连续序号和源起始时间一致；满时淘汰最旧块并在结果中返回其序号。
保留范围内同序号同 hash 返回 `AlreadyPresent`；冲突副本、过旧块、序号缺口和时间跳变抛异常。
`ReadFrom` 在保留范围内返回 `Available`，恰好读到 next 时返回空集合；落后保留窗口返回
`RecoverySnapshotRequired`，超前返回 `AwaitFutureBlock`。读出的原始字节为独立副本。

outbox 要求 300 块 host ring，订阅者容量 1–50；只保存待确认序号，由 host ring 提供内容。
`ReadNext` 不出队；确认必须是当前队首、同 stream epoch 和 64 字符小写 SHA-256。
重复入队、过期入队或确认通过结果区分；背压满、序号缺口、保留到期、epoch 改变、确认乱序/hash 不符会
清空该订阅者队列并进入 `IsolatedNeedsResync`，后续操作返回 `RequiresResync`。
`IsolateForStreamChange()` 主动隔离；`ResetAfterResync` 只接受已隔离状态、同会话/实例和 host 可接受的序号。

恢复请求携带身份、epoch、`NextBlockSequence`、所需/已有渲染历史 ns。
历史须为 200 ms 整数倍且不超过 60 s；不匹配的会话/实例直接拒绝。
计划返回 `UpToDate`、`OrderedReplay`、`AwaitFutureBlock` 或 `SnapshotThenJoin`；epoch 改变、缺口超出保留或
回放仍不足渲染历史时选择最后一项，给出未来 join 序号、时间和 preroll 起点，提前量至少 1 s。
此接口只计算计划；快照传输、预热和实际加入由调用层执行。

[WaveformRecordArchive](../../src/Monitor.Simulation/Acquisition/WaveformRecordArchive.cs)
提供 `Create(WaveformRecordArchivePlan plan, IReadOnlyList<byte[]> rawEnvelopes)`、`Restore`、
`CapturePlan`、`CaptureState`、`ReadBlocks`；它是内存中拥有独立字节副本的不可变记录，不执行文件 I/O。
plan 包含 group/record 引用、sweep epoch、会话/实例、timebase/stream epoch、配置修订、采样原点、
`RecordStartSimTimeNs` / `RecordEndExclusiveSimTimeNs` 和恰好 12 个不同的非空 channel ID。
接收 1–300 块，要求完整覆盖指定区间、首尾块均有用途、身份/配置固定、块连续，
12 个指定通道具有共同采样网格且各通道比例/偏置在记录内不变；原 envelope 可含额外通道。

[通道读取扩展](../../src/Monitor.Simulation/Acquisition/WaveformRecordArchiveChannelRead.cs) 的入口为：

```csharp
ArchivedWaveformChannelShape ReadChannelShape(Guid channelId);
ArchivedWaveformChannelRead ReadChannel(Guid channelId, long startSimTimeNs,
    long endExclusiveSimTimeNs, int maximumSamples, CancellationToken cancellationToken = default);
```

读取范围必须位于 archive 中且非空；按样本时间裁剪，质量区间同步裁剪并重定位。
不做插值、前驱填充、单位推断或显示授权；`OriginalContentSha256` 标识完整原始块，不能作为裁剪平面的 hash。
超过样本预算拒绝整个读取；archive 恢复中的格式/连续性错误归一为 `WaveformRecordArchive.InvalidCheckpoint`。

## 6. 作者配置、生理事件与波形组

[PhysiologyIllustrationConfiguration](../../src/Monitor.Simulation/Authoring/PhysiologyIllustrationConfiguration.cs)
是面向教学场景的配置 record：必填呼吸周期 ms、吸气 ms、RESP 计数幅度；其余包含 CO₂ 形态、
呼吸暂停、心电形态、传导模式、机械活动、压力形态和带种子变化等。
`Default` 为 3750 ms 呼吸周期、1875 ms 吸气、RESP 1000 counts、启用血管储库。
公开预设包含窦性变化、逸搏、早搏、房颤/房扑、传导阻滞、SVT、VT、AIVR 等；
使用其 `ResolvePlan()` 和 `ResolveCapnogram()` 获取校验后的底层定义，不任意混合互斥形态开关。

```csharp
static PhysiologyWaveformGroup PhysiologyIllustrationSource.Create(
    PhysiologyIllustrationConfiguration? configuration = null,
    int abpZeroOffsetCentiMmHg = 0, int paZeroOffsetCentiMmHg = 0,
    int cvpZeroOffsetCentiMmHg = 0, VentilationTransportPlan? ventilation = null);
static PhysiologyTransportSource PhysiologyIllustrationSource.CreateTransport(
    PhysiologyIllustrationConfiguration configuration, VentilationTransportPlan ventilation,
    int referenceStrokeVolumeMicroliters, long ejectionDurationNs = 240_000_000);
```

`ChannelId(row)` 将 0–6 映射为 ECG II、RESP、Pleth、ABP、CO₂、PA、CVP；其他行拒绝。
作者组使用固定教学会话/实例 ID 和固定通道 ID；独立生产身份需调用底层 `Start` 自行提供。
三个压力零偏范围为 ±1000 centi-mmHg，CVP 基线为 -500–3000，ABP/PA 脉幅倍率为 500–2000 permille。
零偏在真实源之后、wire 量化之前加入；超出 Int16 时整批拒绝，不截断压力。

[RegularPhysiologyTimeline](../../src/Monitor.Simulation/Physiology/RegularPhysiologyTimeline.cs)
提供 `Start(RegularPhysiologyPlan)`、`Restore(RegularPhysiologyState)`、`CaptureState()` 和
`AdvanceBefore(long exclusiveSimTimeNs, int maximumEvents, CancellationToken cancellationToken = default)`。
plan 明确心房周期、电/机械偏移、呼吸周期与暂停、传导比和模式、独立心室周期、机械开关/时间段。
`HeartPeriodNs` 通常是心房周期；命名 AF 插图用它表示作者心室时间网格，特殊节律有各自约束。
输出 `PhysiologyCycleEvent(SimTimeNs, Kind, CycleIndex)` 是源事件，不是检测到的 QRS、呼吸或报警。
时间不回退，事件预算最多 1,000,000；具体命名模式的允许组合以该类型构造校验为准。

[EventWaveformComposition](../../src/Monitor.Simulation/Physiology/EventWaveformComposition.cs)
通过 `Restore(EventWaveformState)`、`CaptureState()`、`EvaluateAt(long simTimeNs, CancellationToken = default)`
组合有限事件窗口，输出 Q32 值。每个 `EventWaveformBand` 定义 trigger、延迟、持续时间、LUT 和可选相位点/周期门控。
最多 32 个 band、4096 个事件；LUT 长度 4–65536 且为二次幂、首项为零；事件身份唯一并按时间/种类排序。
`TriggerCycleLimit` 为排除端点，只阻止新触发，已触发尾部仍完成；`TriggerCycleResume` 沿原周期索引恢复。
超出 band 支撑区间贡献为零，叠加超出 Int16 对应 Q32 范围会抛 `EventWaveform.AmplitudeOverflow`。

[PhysiologySignalGenerator](../../src/Monitor.Simulation/Physiology/PhysiologySignalGenerator.cs)
将事件定义采样为 `PhysiologySignalSample(Tick, ValueQ32, NormalizedValue)`；主要签名为：

```csharp
static PhysiologySignalGenerator Start(RegularPhysiologyPlan plan, string profileId,
    ulong streamEpoch, IReadOnlyList<EventWaveformBand> bands,
    VascularPressurePlan? vascularPressure = null, PlethRunoffPlan? plethRunoff = null);
IReadOnlyList<PhysiologySignalSample> GenerateBefore(long exclusiveSimTimeNs,
    int maximumSamples, int maximumEvents, CancellationToken cancellationToken = default);
static PhysiologyWaveformGroup Start(Guid sessionId, Guid instanceId, ulong timebaseEpoch,
    ulong streamEpoch, ulong configurationRevision, ulong firstBlockSequence,
    int maximumBufferedBlocks, IReadOnlyList<PhysiologyWaveformChannelPlan> channels);
IReadOnlyList<byte[]> AdvanceTo(long simTimeNs, int maximumSamplesPerChannel,
    int maximumBlocks, int maximumEvents, CancellationToken cancellationToken = default);
void ContinueWith(PhysiologyWaveformGroup definition);
```

`PhysiologyWaveformChannelPlan` 把 physiology、plane、bands、delay capacity、quality flags、可选 pressure/pleth 源、零偏和 `PressureBaselineCentiMmHg` 关联。
同组共享同一 physiology plan、epoch 和 cursor；压力储库源、Pleth runoff 源、普通 band 三种路径互斥。
单通道样本预算最多 1,000,000，事件预算最多 4096，输出块预算 1–300。
组块 `AdvanceTo` 返回已满足采集延迟的完整 raw envelope，可能暂时为空。

`ContinueWith` 是同一流内接续：通道 ID、质量、零偏和 plane 配置必须完全一致，否则 `PhysiologyGroup.ChannelMismatch`。
它保留样本时钟、索引、延迟队列和已组装数据，新定义仅处理当前 cursor 之后的新事件；旧事件已触发的尾部继续衰减。
`PhysiologySignalState` 的 `ActiveFromEventTimeNs` 与 `History` 保存接续分段，外部恢复最多接收 512 个历史段。
新参数对每个 generator 完成校验后才整体发布；需要改变通道形状、profile 或零偏时不能复用此接口。

CVP 的 `PressureBaselineCentiMmHg` 属于事件叠加信号源，并随当前定义与历史段保存。
接续时新基线从生效 cursor 开始作用于新采样，已采集数据仍按旧基线恢复；基线不作为旧事件尾部再次叠加。
它仅用于 `AcqPressure125@1` 的 band 源，不能与 pressure/pleth 储库源同时使用。
`CentralVenousPressurePlan` 将基线计入有符号 16 位幅度预算，超限在构造通道时拒绝。
新建 CVP 通道固定使用 scale `1/100`、offset `0/1`，样本包含基线，单位为 centi-mmHg。
这改变了此前“样本为增量、offset 保存基线”的编码表示；消费者应继续按 plane 的 scale/offset 换算，不能假定样本不含基线。

十二导联走 [ElectrodeSignalGenerator](../../src/Monitor.Simulation/Physiology/ElectrodeSignalGenerator.cs)
和 [ElectrodeWaveformGroup](../../src/Monitor.Simulation/Physiology/ElectrodeWaveformGroup.cs)：
generator 的 `Start(plan, profileId, streamEpoch, electrodes, placement = Standard)` 只接受 250 Hz ECG profile，
输出 `ExactLeads` 及按 `EcgLead` 枚举顺序排列的 12 个 `MicrovoltValues`，只在最终 Int16 µV 量化时舍入。
group 的 `Start` 在通用身份/预算参数后接 `RegularPhysiologyPlan physiology`、
`IReadOnlyList<ElectrodeWaveformPlan> electrodes`、`IReadOnlyList<ElectrodeChannelPlan> channels` 和可选 placement；
必须恰好覆盖 12 导联。`AdvanceTo(simTimeNs, maximumSamples, maximumBlocks, maximumEvents, cancellationToken)`
输出同步十二平面块，支持 `CaptureState`、`Restore` 和 `Fork`。
电极到导联投影、精确值倍率和放置枚举见 [EcgElectrodeProjection.cs](../../src/Monitor.Simulation/Physiology/EcgElectrodeProjection.cs)。

## 7. 压力、灌注与呼吸形态扩展

| 入口 | 输出及集成约束 |
| --- | --- |
| `VascularPressureSource.Create(RegularPhysiologyPlan, VascularPressurePlan)` / `EvaluateAt(long, CancellationToken = default)` | centi-mmHg 的 Q32 值；储库衰减保留跨缺搏历史，最多 4096 次射血重建，固定 1 µs 网格 |
| `VascularPressureSource.SolveTarget(RegularPhysiologyPlan, VascularPressurePlan, int systolicCentiMmHg, int diastolicCentiMmHg)` | 带形态的储库按目标求 `R*Q` 和脉搏形态高度；用无脉搏与有脉搏两次探测的完整搏动起点、峰值解线性方程，无法表示时返回 null |
| `PhysiologyIllustrationConfiguration.AbpTarget` / `PaTarget`（`VascularPressureTarget`） | 收缩压／舒张压目标，替代同一通道的脉搏分量倍率；无心室机械活动且未安排后续机械活动时保留原无射血源，不执行目标校准；超出范围、脉压小于 5 mmHg、无储库形态或无法生成时抛出 `Physiology.PressureTarget*` 原因 |
| `PlethRunoffSource.Create(RegularPhysiologyPlan, PlethRunoffPlan)` / `EvaluateAt(long, CancellationToken = default)` | counts 的 Q32 值；有限 LUT 加有界衰减尾部，公开 `SupportNs` 和 `MaximumHistoryEvents` |
| `ArterialPulsePlan` / `PulmonaryArteryPulsePlan` / `CentralVenousPressurePlan` / `VascularPressurePlan.CreateChannel` | 返回压力 `PhysiologyWaveformChannelPlan`；当前 wire 比例 1/100，量纲 mmHg |
| `RespirationPlan.CreateChannel(RegularPhysiologyPlan, Guid, uint)` | 125 Hz 呼吸形态，幅度 counts，可叠加独立心源伪差 |
| `CapnogramPlan.CreateChannel(RegularPhysiologyPlan, Guid, uint)` | 100 Hz CO₂，比例 1/100，加 `BaselineMmHg` 偏置；形态运输延迟与采集延迟分开 |
| `SeededCardiacRate` / `SeededExpirationPressure` / `SeededOpticalSaturation` | 由种子准备有界变化，公开 prepared stream 状态；索引/时间查询不依赖调用历史 |

源定义见 [VascularPressurePlan.cs](../../src/Monitor.Simulation/Physiology/VascularPressurePlan.cs)、
[PlethRunoffSource.cs](../../src/Monitor.Simulation/Physiology/PlethRunoffSource.cs)、
[RespirationPlan.cs](../../src/Monitor.Simulation/Physiology/RespirationPlan.cs)、
[CapnogramPlan.cs](../../src/Monitor.Simulation/Physiology/CapnogramPlan.cs)。
早搏、AF、传导房扑、充盈和固定节律灌注通过各自 `Supports` / `IsPattern` / `GainPermille` 或预设选取；
不应由显示波幅反推出搏出量。形态参考类通常提供 `CreateLeadIIBands`、`CreateElectrodes` 或专用 plan，
全部具体重载以声明清单和对应 `*Reference.cs` / `*Plan.cs` 为准，生成的 `*Tables.cs` 保留其来源。

`RespiratoryActivity.Breathing`、`EffortOnly`、`Absent` 是源活动设置；努力不等于有呼出气。
`RespiratoryPattern` 为 `Regular`、`CheyneStokesIllustration`、`IntermittentIllustration`。
CO₂ 相位包括死腔、上升、平台和下一次吸气下降；可配置 1:2:1 三路径色散。
非规则深度的 CO₂ 教学响应通过内部 `RespiratoryCo2Response` 形成，不是公开服务或完整 CO₂ 质量守恒求解器。
向 `PhysiologyIllustrationSource.Create` 传 ventilation 后，内部耦合按 VT 缩放 RESP，按 VD/VT 调整 CO₂ 死腔段；
气道关闭或单次潮气量不超过死腔时保留基线但无呼出 CO₂ excursion。

模型依据与更多限制见[压力衰减](../research/physiology/vascular-pressure-runoff-research.md)、[Pleth 衰减](../research/physiology/pleth-runoff-research.md)、
[逐搏充盈](../research/physiology/cardiac-filling-perfusion.md)、[SVT 灌注](../research/physiology/svt-perfusion.md)、[潮式呼吸 CO₂](../research/physiology/cheyne-stokes-co2-coupling.md)。

## 8. 通气、血流、氧合与光学采样

实现：[PhysiologyTransportSource.cs](../../src/Monitor.Simulation/Physiology/PhysiologyTransportSource.cs)、
[OxygenReservoirModel.cs](../../src/Monitor.Simulation/Physiology/OxygenReservoirModel.cs)、
[RealtimeOxygenationSource.cs](../../src/Monitor.Simulation/Physiology/RealtimeOxygenationSource.cs)。

```csharp
static PhysiologyTransportSource Create(RegularPhysiologyPlan physiology,
    VentilationTransportPlan ventilation, BloodFlowTransportPlan bloodFlow);
PhysiologyTransportInterval Integrate(long fromSimTimeNs, long toExclusiveSimTimeNs,
    int maximumEvents = MaximumEvents, CancellationToken cancellationToken = default);
static OxygenReservoirState OxygenReservoirModel.ReferenceState(OxygenReservoirParameters parameters);
static OxygenReservoirState OxygenReservoirModel.Step(OxygenReservoirState state,
    OxygenReservoirParameters parameters, PhysiologyTransportInterval input,
    decimal oxygenDemandMultiplier = 1);
RealtimeOxygenationSource(PhysiologyTransportSource transport, OxygenReservoirParameters parameters,
    decimal oxygenDemandMultiplier = 1, long? initialSimTimeNs = null);
void RealtimeOxygenationSource.AdvanceTo(long toSimTimeNs);
long RealtimeOxygenationSource.ChangeVentilation(VentilationTransportPlan ventilation,
    long atSimTimeNs, decimal? oxygenDemandMultiplier = null);
void RealtimeOxygenationSource.ChangeTransport(PhysiologyTransportSource transport,
    long atSimTimeNs, decimal oxygenDemandMultiplier);
ArterialOxygenationSample IArterialOxygenationSource.ReadAt(long sourceSimTimeNs);
```

`VentilationTransportPlan` 是 VT/VD（µL BTPS）、FiO₂（millionths）及气道开关。
transport 层允许 VT 0–3000000、VD 0–1000000、FiO₂ 100000–1000000；
实时氧合控制层更窄，VT 上限 1500000、VD 上限 500000，并检查 8 ms 内送气上限。
`BloodFlowTransportPlan` 含每搏量 0–250000 µL、射血时长 1–1000 ms、`StrokeVolumeResponse` 和 AF 脉搏短绌开关；
response 必须适配 physiology。`Integrate` 最多 60 s / 4096 事件，返回送气、肺泡通气、有效血流体积 nL。
它是纯索引积分，任意划分区间的整数前缀差保留总体积；`EffortOnly`/`Absent`、零深度和关闭气道均不送气。

`OxygenReservoirParameters` 使用肺容积 mL(BTPS)、液体血容量 mL、Hb g/dL、基础耗氧 mL(STPD)/min。
储库气量和氧量使用 mL(STPD)，BTPS/STPD 换算仅由求解器负责；不能把血容量做气体换算。
模型为 decimal 固定 8 ms RK4，每个 `Step` 输入必须恰好 8 ms；送气最多 100000000 nL、血流最多 16000000 nL。
耗氧 multiplier 为 1–4。参数范围、参考平衡和每步储库状态都必须有效，否则拒绝，不将负库存裁成零。
`ArterialSaturationMilliPercent` 从动脉氧含量反算 0–100000 饱和度；`ConservedOxygenMl` 和 snapshot 残差提供收支数据。

实时源从参考储库开始，初始时间必须不早于 physiology epoch 且落在 8 ms 全局网格上。
`AdvanceTo` 不回退且一次最多推进 1 s；只执行完整 8 ms 步，尾部不足一步保留到下一次调用。
历史最多 1024 点，覆盖 8.184 s；`ReadAt` 只读且可做显式线性插值，不能推进源或用最新状态代替缺失历史。
`ChangeVentilation` 只接受 `[SourceSimTimeNs, SourceSimTimeNs + 8 ms)` 的时间，返回实际生效边界：
当前网格点或下一个尚未开始的网格点。更晚修改保留较早尚未积分的修改；同一生效点以新定义替换。
`ChangeTransport` 要求恰好当前源网格点，保留现有储库并排队替换 transport；`Fork()` 可用于上层整体事务。
实时源目前没有公开完整 `CaptureState/Restore`，`Snapshot` 是观测值，不能据此恢复待生效修改和全部历史。

[SampledArterialOxygenation](../../src/Monitor.Simulation/Physiology/ArterialOxygenationSource.cs)
是离线轨迹适配器，构造参数 `SampledArterialOxygenationState(StartSimTimeNs, SampleStepNs, SaturationMilliPercent, ModelId)`。
要求 2–450001 点、步长 1 ms–1 s、值 0–100000，模型 ID 为 `SampledArterialOxygenation@1`；
提供 `CaptureState` 和同样的 `ReadAt`，线性插值、ties-to-even，不外推。不可用历史抛 `Oxygenation.HistoryUnavailable`。

[OxygenationPatientDefaults.Resolve](../../src/Monitor.Simulation/Physiology/OxygenationPatientDefaults.cs)
接受 `OxygenationPatientProfile` 和可选 `OxygenationBaselineOverrides`，返回有效参数以及每项数值、单位、来源和中心定义。
成人入口范围：18–90 岁、140–210 cm、40–150 kg；超出入口范围直接拒绝，override 不能绕过。
范围内不满足某项预测公式条件时，必须为该项显式 override，例如血容量预测要求 BMI `[18.5, 30)`。
解析会验证完整参数组合可形成参考平衡。
既有来源、人口范围和覆写规则见[成人氧合默认值](oxygenation/oxygenation-defaults.md)。

[PulseOximeterIllustrationSource](../../src/Monitor.Simulation/Authoring/PulseOximeterIllustrationSource.cs)
有固定目标/可选种子变化和 `IArterialOxygenationSource` 两个构造重载；共同参数为
`Guid plethChannel, Guid acquisitionInstance, Guid sensorInstance`，`modulationPermille=1000`（范围 100–2000）。
`byte[] ConvertAcquiredPulse(ReadOnlySpan<byte> wire)` 接收包含指定 Pleth 的 125 Hz 完整块，验证源 instance，
输出原 Pleth 加固定 Red/Infrared 通道，改为 sensor instance，保留时间、序号、revision、质量及样本索引。
改变光学模型/目标应使用新的 sensor instance；该 ID 非空且不能等于 acquisition instance。
输入每点按原始 `block.StartSimTimeNs + index × 8 ms` 查询氧合；返回时间必须精确一致，值须在 0–100000。
不再加采集延迟或脉搏传播延迟；无脉动/无有效光学信号是否可报告，由下游光学测量器判断。

建议调用顺序是创建一致的 waveform/transport 定义，先将氧合源推进到消费所需源时刻，再生成已延迟的 Pleth 块，
最后调用光学转换并交给测量层。一次长时间补帧可能越过氧合历史窗口，调用方应分小步推进整条流水线。
物理输入、观测与报告边界见[氧合传输接口](oxygenation/oxygenation-transport.md)、[原型模型](../research/oxygenation/oxygenation-prototype.md)、
[低氧数值报告](oxygenation/oxygenation-reporting.md)。

## 9. 传感器故障状态

[SensorFaultStateMachine](../../src/Monitor.Simulation/Acquisition/SensorFaultStateMachine.cs)
提供 `Start(profileId, sensorInstanceId, startSimTimeNs)`、`Restore`、`CaptureState` 和
`Apply(SensorFaultOperation operation, string failureState, SensorFaultOrigin origin, long simTimeNs, ulong expectedRevision)`。
operation 为 `Start/Stop`，origin 为 `ScenarioOverride/SensorModel/LatentPhysiology`。
成功返回含前后 revision 的 `SensorFaultTransition`；要求单调时间与匹配 revision，故障名须属于 profile。
`LowPerfusion` 只接受 `SensorModel/LatentPhysiology`，其余故障只接受 `ScenarioOverride/SensorModel`；
重复启动、停止未活动故障或停止时来源不匹配均拒绝，不作为幂等成功。

| Profile ID | 故障状态 |
| --- | --- |
| `SensorECGGeneric@1` | LeadOff、Noise、Disconnected |
| `SensorSpO2Generic@1` | LowPerfusion、Motion、SensorOff、Disconnected |
| `SensorNIBPCuffGeneric@1` | LooseCuff、Overpressure、Timeout、Disconnected |
| `SensorPressureGeneric@1` | NotZeroed、Overdamped、Underdamped、Disconnected |
| `SensorCO2Generic@1` | Occluded、SampleLineOff、NoBreath、Disconnected |
| `SensorTempGeneric@1` | ProbeOff、OutOfRange、Disconnected |

`FrozenSensorProfiles.Get` 返回上述定义和兼容类别 Adult/Pediatric/Neonatal。
该状态机管理故障与来源，不自行修改波形或产生测量报警；调用层按 transition 连接相应行为。

## 10. 错误处理边界

大部分仿真/采集异常继承 `ArgumentException` 并公开 `ReasonCode`，便于调用层稳定映射错误；
空值也可能抛 `ArgumentNullException`，数值边界可能抛 `ArgumentOutOfRangeException` 或 `OverflowException`。
作者配置、transport 和氧合部分使用带稳定文本的普通参数异常，不能假设所有异常都有 `ReasonCode` 属性。
取消通过 `OperationCanceledException` 表达；带试算副本的生成/组块调用取消或失败后不发布本批新状态。
`Restore` 拒绝不相容、被篡改或内部游标不一致的状态；不得用捕获异常后补零/跳样来继续同一 stream。
订阅背压和回放缺口等正常协议分支则用结果枚举表达，消费者应按状态进入恢复流程。
