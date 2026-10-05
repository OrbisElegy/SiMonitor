# 测量、通知与波形呈现接口

[接口总览](README.md) · [应用公开声明](api/application.md) · [领域公开声明](api/domain.md) · [基础设施公开声明](api/infrastructure.md)

本文覆盖 `Monitor.Application/Measurements`、`Monitor.Application/Presentation`、
`Monitor.Domain/Presentation` 和 `Monitor.Infrastructure/Presentation` 的集成契约。
接口以当前源码为准，包括可配置数值报警确认；这里的测量器和本地提示用于教学模拟。
`Capture`、`Restore`、`Checkpoint` 默认表示进程内延续状态，不能据名称推断存在稳定文件格式。

## 分层与调用方向

| 层 | 输入 | 输出及责任 |
| --- | --- | --- |
| Measurements | 已采集、编码的 `WaveformEnvelope` | 实测数值、质量、因果确认事件；不读取生成器目标或历史显示 |
| Application.Presentation | 实测快照、当前策略、记录和几何输入 | 通知投影、工作请求、完整显示快照、记录交互 |
| Domain.Presentation | 显式时钟、连续性状态、记录坐标、比例 | 确定性的扫线状态、几何与手工测量计算 |
| Infrastructure.Presentation | 受信任的进程内请求和显示对象 | 后台任务承载、SVG 输出、SVG 输入适配 |

典型实时调用顺序是：采集包 → `LiveWaveformMeasurements.Consume` → 数值/提示/声音意图，
同时采集历史 → 扫线状态和重建请求 → worker → display gate → 当前显示组合。
记录回看通过独立的归档绑定和 study view；回看不能重新喂入实时测量器产生提示音。

源码入口：[测量聚合](../../src/Monitor.Application/Measurements/LiveWaveformMeasurements.cs)、
[本地预览](../../src/Monitor.Application/Presentation/LocalMonitorPreviewSession.cs)、
[扫线显示组合](../../src/Monitor.Application/Presentation/SweepDisplayComposition.cs)。

## 时间、单位与质量

`Ns` 均为纳秒，但时钟含义不能互换：采集测量用样本仿真时间，扫线用呈现时间和显式
live playhead，连续性和静音期限使用调用者提供的单调权威时间。冻结或回看不能冻结患者的
当前安全投影。时间段通常为 `[start, endExclusive)`；方法名中的 `toExclusive` 必须保留。

| 字段/类型 | 单位或含义 |
| --- | --- |
| `MilliBeatsPerMinute`、`MilliBreathsPerMinute`、`RespirationsMilliPerMinute.Value` | 每分钟次数 × 1000 |
| `MeanCentiMmHg`、`EndTidalCentiMmHg.Value` | mmHg × 100 |
| `SaturationMilliPercent`、`PerfusionMilliPercent` | 百分数 × 1000；`97000` 表示 97% |
| `RatioPpm`、`*Permille` | 比率分别乘 1000000、1000 |
| `EcgManualCursor.NumeratorMicrovolts / Denominator` | 精确微伏有理数 |
| `SpeedTenthsMmPerSecond` | mm/s × 10；允许 125、250、500 |
| `SweepPixelPosition`、`ExactPlotCoordinate`、`EcgVerticalPosition` | 逻辑像素有理数，不代表物理屏幕毫米 |

`WaveformMeasurementStatus` 定义于 [CapnographyMeasurement](../../src/Monitor.Application/Measurements/CapnographyMeasurement.cs)：
`WarmingUp`、`Valid`、`Stale`、`NoData`、`PoorSignal`、`Uncountable`、`OutOfRange`。
应同时检查状态和 nullable 数值，不能把 `null`、等待、过期或不可计数当作零。

`MeasurementDisplay.Resolve(source, status, validNumericText, fault)` 是纯显示投影。
有效数值缺少文本会拒绝；技术故障优先显示 `---` 与故障说明；ECG 不可计数显示 `-?-`，
其他不可计数显示 `---`。`MeasurementTechnicalFault` 必须来自显式采集/连接评估，
不能由平直波形或模拟预设推断导联/传感器脱落。
见 [MeasurementDisplay](../../src/Monitor.Application/Measurements/MeasurementDisplay.cs)。

## 实时测量聚合

主要签名：

```csharp
LiveMeasurementSnapshot Consume(ReadOnlySpan<byte> wire);
LiveMeasurementSnapshot Consume(ReadOnlySpan<byte> wire,
    out IReadOnlyList<DetectedEcgBeat> detectedBeats,
    out IReadOnlyList<DetectedPlethPulse> detectedPulses,
    out IReadOnlyList<DetectedEcgRhythmEvent> rhythmEvents);
LiveMeasurementSnapshot Read(long asOfSampleTimeNs);
LiveWaveformMeasurements.Checkpoint Capture();
static LiveWaveformMeasurements Restore(LiveWaveformMeasurements.Checkpoint checkpoint);
```

`LiveWaveformMeasurements` 绑定本地 illustration 通道：0 ECG、1 RESP、2 PLETH、
3 ABP、4 CO₂、5 PA、6 CVP；ABP、PA 另启用脉动压力检测。
`CreateIllustration()` 显式选择教学光学标定；自建实例必须提供 `OpticalSaturationMeasurement`。

`Consume` 要求包长为 200 ms。红光与红外通道必须同时存在或同时缺省；存在时，PLETH
必须与红光具有相同首样本索引和样本数。缺少两路光学数据会重置光学采集测量，
而非沿用上一次 SpO₂。`SampleTimeNs` 为包起点加持续时间减 1 ns。

聚合器先复制各测量器状态，全部通道消费和读取成功后才替换状态、输出事件。
因此单包失败不会发布半更新的数值或一次性事件；调用方应串行消费。
`Read` 只投影现有状态，不能传入早于最后聚合样本时间的时间，也不会重放事件。
恢复 checkpoint 同样不重放旧事件。checkpoint 不暴露可编辑的测量内部状态。

### 单通道契约

各测量器遵循 `Consume(wire)`、`Read(asOfSampleTimeNs)`、`Capture()`、`Restore(checkpoint)`
形式，但 `Consume` 的返回类型不同，见下表。构造器中的通道 ID 不得为空。

| 测量器 | 输入采样与物理含义 | `Consume` / `Read` 输出及窗口 |
| --- | --- | --- |
| [EcgHeartRateMeasurement](../../src/Monitor.Application/Measurements/EcgHeartRateMeasurement.cs) | 250 Hz，换算后微伏 | 确认心搏列表 / 心率；20 s 窗口，最多 8 个间隔；5 s 无可接受心搏后过期或不可计数 |
| [ImpedanceRespirationMeasurement](../../src/Monitor.Application/Measurements/ImpedanceRespirationMeasurement.cs) | 125 Hz，相对幅值计数 | 确认呼吸列表 / 呼吸率；30 s，最多 4 个间隔；20 s 事件过期 |
| [PlethPulseRateMeasurement](../../src/Monitor.Application/Measurements/PlethPulseRateMeasurement.cs) | 125 Hz，相对幅值计数 | 确认脉搏列表 / 脉率；20 s，最多 8 个间隔；5 s 事件过期 |
| [CapnographyMeasurement](../../src/Monitor.Application/Measurements/CapnographyMeasurement.cs) | 100 Hz，输入换算为 mmHg | 呼气列表 / EtCO₂ 与 CO₂ 呼吸率；速率窗口 20 s，最多 4 个间隔，10 s 结果新鲜度 |
| [MeanPressureMeasurement](../../src/Monitor.Application/Measurements/MeanPressureMeasurement.cs) | 125 Hz，输入换算为 mmHg | 平均压；500 点窗口；`detectPulse` 可附加 `PulsePressureReading` |
| [OpticalSaturationAcquisition](../../src/Monitor.Application/Measurements/OpticalSaturationAcquisition.cs) | 同时刻红光/红外，125 Hz | `OpticalSaturationReading`；保存最多 500 个光学对 |

同一 session/instance 下 epoch 或配置版本倒退会拒绝；同一时钟流的重复、重叠、倒序
包会拒绝。身份变化或前向不连续会开始新的分析状态，不跨缺口续算事件间隔。
固定采样率、包时间与样本数不匹配、缺少指定通道均拒绝。各 `Read` 禁止倒查到最后
样本之前；最后样本距查询时间超过 500 ms 时报告 `NoData`，不同于仍有采样但事件过期。

样本质量区间中的非零 flags、ADC 极值或超出测量器允许物理范围会使样本不可用。
质量恢复通常需要重新积累干净证据；例如平均压要重新积满窗口。
压力允许有效的平直平均值，脉动压力可单独无效，不能把“无脉动”直接当平均压无数据。
[PressurePulseTracker](../../src/Monitor.Application/Measurements/PressurePulseTracker.cs) 定义
`PulsePressureReading` 的收缩压、舒张压、最后峰值时刻和其独立状态。

### 光学血氧与脉率

[OpticalSaturationMeasurement](../../src/Monitor.Application/Measurements/OpticalSaturationMeasurement.cs)
的 `Estimate(IReadOnlyList<OpticalSample> samples, long asOfSampleTimeNs)` 接受最多 500 对、
相邻间隔严格 8 ms 的正值透射光。红光和红外必须经过暗光/环境光修正，来自同一传感器
同一时刻；一条 AC-only PLETH 显示波形不能替代这两路输入。

构造时提供 `CalibrationId` 与 2–256 个 `SaturationCalibrationPoint`；比值须严格递增，
饱和度严格递减。`CreateIllustration()` 使用教学模型专属曲线并显式处理 0/100% 端点。
普通自定义标定不自动外推或使用“通用默认曲线”。

输出同时包含 `RatioPpm`、`MeasuredAtNs`、可空 PI。窗口不足为 `WarmingUp`；
低脉动、非相关或不合格信号为 `PoorSignal`，越过标定适用范围可为 `OutOfRange`。
`IsQuestionable` 表示有效数值且 PI < 0.3%，不是把该值改成无效。

[PulseOximeterMeasurement](../../src/Monitor.Application/Measurements/PulseOximeterMeasurement.cs)
将同一传感器包原子地送入脉率和光学测量，返回 `PulseOximeterReading(PulseRate, SpO2)`。
它检查 PLETH 与光学首索引/样本数配对，禁止通道冲突；既不输入饱和度目标，也不由 HR
代替 PR。相关模型说明见 [氧合报告](oxygenation/oxygenation-reporting.md)、
[氧合传输](oxygenation/oxygenation-transport.md)。

### ECG 节律证据与事件

[EcgRhythmAnalysis](../../src/Monitor.Application/Measurements/EcgRhythmAnalysis.cs) 的分析器是
internal；公开边界是 `EcgHeartRateMeasurement.ReadRhythm`、`Consume` 的额外事件输出和
`LiveMeasurementSnapshot.EcgRhythm`。

`EcgRhythmReading` 包含状态、可空 `IrregularRhythm` / `SuspectedAtrialFibrillation` 以及
`EcgRhythmEvidence`，后者包含 RR 数量、不规则变化比例、RR 分箱数、心房窗数量、
心房一致性和 RMS 微伏。`null` 表示证据不可用；不得解释为阴性。

`DetectedEcgRhythmEvent` 有 `Started`、`Ended`、`Interrupted` 三类转换。
`EvidenceFromNs` 是分析窗起点，`ConfirmedAtNs` 是采集时间轴上的因果确认时刻，
不能把前者标成真实生理发作起点。心搏/脉搏的 `PeakTimeNs` 与 `ConfirmedAtNs` 也须区分。

当前单导联筛查使用约 30 s RR 支持窗和 5 s 持续确认；疑似房颤还要求足够的非一致
心房活动证据。缺少 P 波本身不足以给出疑似房颤。信号不可用、流不连续、RR 或心房证据
不足可中断活动状态；中断不是正常恢复。消费、查询或恢复时不要自行合成转换事件。
这些分析结果不属于当前数值报警登记条件，不进入独立报警声音链路；模拟波形模板名称也不作为报警检测证据。

## 本地预览会话与显示偏好

`LocalMonitorPreviewSession` 是本地展示运行时，不替代产品权威 gateway。
构造参数接受 `PhysiologyIllustrationConfiguration`、`MonitorDisplayConfiguration`，
以及可选测量开关、固定光学饱和度、光学变化、外部氧合源或实时氧合配置。
外部氧合源与实时氧合配置互斥；氧合测量要求启用测量，且与固定饱和度/光学变化互斥。

| 入口 | 生命周期与边界 |
| --- | --- |
| `DiscardStartup()` | 仅初始状态可调用；预运行并丢弃 12 s，随后对外时间重新从零计 |
| `Advance(long deltaNs)` | `0 < deltaNs <= 250 ms`，内部最多 50 ms 一段；本次调用事件替换上次批次 |
| `ScheduleSource(definition, delayNs)` | fresh definition、同测量启用状态；0–60 s；生效时间向上对齐 200 ms；返回生效仿真时间 |
| `UpdateOxygenationVentilation(ventilation, oxygenDemandMultiplier)` | 只用于启用实时氧合的会话；完整验证后同时安排通气与波形变化，返回生效时间 |
| `UpdateDisplay(display)` | 替换显示槽和量程，按已有显示 frontier 重建量程投影 |
| `Samples(channel, fromSimTimeNs, toExclusiveSimTimeNs)` | 从保留包按半开区间枚举换算后的物理值 |

`ScheduleSource` 不接受自身、已经推进/丢弃启动段/已有待应用修改的 definition。
连续实时氧合源不能热改 `OxygenationParameters` 基线；需重新开始会话。
源码通过试算验证源可延续性后发布 pending 请求；一个 pending 槽保存最新定义。

`SimulationTimeNs` 是本地推进时间，`FrontierNs` 是可显示的前沿，二者相差目标 2.2 s
呈现延迟。最多保留 202 个波形包。`DataRevision` 随有新包的内部批次递增，不能当成 UI
帧编号。`Measurements` 使用采集前沿而非显示延迟时钟；声音的本批确认事件仅保留距
测量前沿 250 ms 内的心搏/脉搏，节律事件按本次推进批次输出。

[MonitorDisplayConfiguration](../../src/Monitor.Application/Presentation/MonitorDisplayConfiguration.cs)
支持 3/5/7 行，每槽指定通道、自动量程、参考量程与走速；默认顺序为 ECG、PLETH、ABP、
CO₂、RESP、PA、CVP。25 mm/s 对应 10 s 显示宽度；其他走速反比改变宽度。
`MonitorSweepRanges.Advance` 仅在行扫线边界从已经完成的前一扫线更新自动量程，
不使用缓冲中的未来样本或当前帧极值；frontier 回退会拒绝。

[PreviewContour.Interpolate](../../src/Monitor.Application/Presentation/PreviewContour.cs)
提供显示用单调 Hermite 插值，原采样点保持为精确节点，平直段保持平直且不超调；
最多 10000 点、1–8 分段，时间严格递增、数值有限。插值结果不得成为测量输入。

偏好对象仅保存编辑意图，不能作为仿真 checkpoint：

- [MonitorGeneratorPreferences](../../src/Monitor.Application/Presentation/MonitorGeneratorPreferences.cs)：
  预设索引/名称、seed 文本、数值/开关/选项字典；`ApplyDelaySeconds` 为 0–60 s、0.1 s 步进。
- [OxygenationEditorPreferences](../../src/Monitor.Application/Presentation/OxygenationEditorPreferences.cs)：
  `CreateVentilation()` / `CreateConfiguration()` 映射显式通气、患者参数和耗氧倍率；先 `Validate()`。
- [OxygenationPatientPreferences](../../src/Monitor.Application/Presentation/OxygenationPatientPreferences.cs)：
  保存 profile、override、resolved 值与参考来源；重新解析不一致会拒绝。

氧合默认值和保存含义参见 [氧合默认参数](oxygenation/oxygenation-defaults.md)。

## 数值通知与确认时间

`MonitorNotice(Id, Level, Text)` 是本地通知投影，`Numeric` 标识相关数值，`Audible` 控制
是否有声音意图。等级为 `Info`、`Notice`、`Warning`、`Critical`。它不携带患者报警事件
身份、确认/锁存状态或恢复原因。

[MeasuredLimitNotice](../../src/Monitor.Application/Presentation/MeasuredLimitNotice.cs) 提供七类
描述符和瞬时 `Evaluate(numeric, limits, snapshot)`。`MeasurementLimits` 顺序为
`Enabled, CriticalLow, WarningLow, WarningHigh, CriticalHigh`；值和阈值使用相同原生整数单位。
启用时须满足严格递增及描述符范围。非法阈值产生 `Info` 设置提示；质量无效/数值不可用
不产生生理越界提示。严格 `<` 下限、`>` 上限才越界，等于阈值属于正常一侧。

有状态确认入口：

```csharp
ConfirmedLimitNotice(MonitorNumeric numeric);
MonitorNotice? Evaluate(MeasurementLimits limits, LiveMeasurementSnapshot snapshot,
    MeasurementConfirmationTiming? timing = null);
void Reset();
```

上述构造器、`Evaluate` 和 `Reset` 属于 `ConfirmedLimitNotice`。每个实时会话、每个数值持有一个
实例；支持 HR、SpO₂、RR·RESP、PR·PLETH、EtCO₂、RR·CO₂、ABP/PA/CVP 平均压。
完整定义见 [ConfirmedLimitNotice](../../src/Monitor.Application/Presentation/ConfirmedLimitNotice.cs)、
[MeasurementConfirmationTiming](../../src/Monitor.Application/Presentation/MeasurementConfirmationTiming.cs)。

`MeasurementConfirmationTiming` 的四个边界各有触发/恢复时间，单位整数毫秒、0–600000。
0 表示首个有效证据立即确认。压力默认下限触发 4000 ms、上限 10000 ms、恢复 3000 ms；
其余数值均为 0 ms；SpO₂ 仅支持下限，两个上限阈值必须为空、上限确认时间必须为零。Warning 和 Critical 边界独立积累，波动到另一严重程度不会清除
仍连续成立的 Warning 证据。输出优先级为 CriticalLow、CriticalHigh、WarningLow、WarningHigh。

计时只使用 `snapshot.SampleTimeNs`。重复样本时间不增时；回退、相邻观察间隔大于
500 ms、质量无效、开关/阈值/时间配置变化都会清空旧证据。不可用后移除提示不代表
患者恢复事件；确认器的 `Lifecycle` 会记录相应中断原因和不可判定状态。非法确认配置在修改内部状态前抛出异常；显式 `Reset()` 结束旧会话证据。

[PressureLimitNotice](../../src/Monitor.Application/Presentation/PressureLimitNotice.cs) 保留原压力
专用构造器、`Evaluate(limits, snapshot)`、`Reset()`，委托给通用确认器和压力默认时间。
[MonitorAlarmPreferences](../../src/Monitor.Application/Presentation/MonitorAlarmPreferences.cs)
保存报警开关/阈值及 `ConfirmationTimings` 覆盖字典，`ConfirmationFor(numeric)` 回退到默认值；
空覆盖字典合法，未知数值或空 timing 非法。这里保存配置，不保存活动确认状态。

[ConfirmedNoExpirationNotice](../../src/Monitor.Application/Presentation/ConfirmedNoExpirationNotice.cs)
提供 `Evaluate(enabled, delaySeconds, nowNs, activity, timing)` 和 `Reset()`。呼吸等待时限为
5–120 秒整数，达到时限后从首次观察到条件成立的 CO₂ 采样时刻积累额外触发确认。
恢复检测到呼气后开始恢复确认，期间再次达到等待时限会取消恢复。两种确认均为 0–600000 ms，
默认 0/0；通过独立的 `MonitorAlarmPreferences.NoExpirationConfirmation` 保存。
计时只使用 `activity.LastSampleNs`，不使用刷新时钟或缺失的数值呼吸率。无效活动、采集段变化、
回退及大于 500 ms 的间隔清空旧状态。详细接口和配置行为见[报警确认配置](alarms/alarm-confirmation.md)。

上述确认器的 `Lifecycle` 为 `AlarmLifecycleJournal`，提供只读的条件快照、最近转换及淘汰数量。
每个方向独立分配 owner 内身份，Warning/Critical 升降级保留身份，恢复与数据中断使用不同原因。
记录由确认状态同步生成，不改变 `MonitorNotice` 相等性、横幅轮换或声音请求。详细状态、
有界保留和连续性边界见[本地报警生命周期接口](alarms/alarm-lifecycle.md)。
其 `Attention` 提供独立的用户确认、恢复保持和旧修订拒绝契约，见[确认与保持状态接口](alarms/alarm-attention.md)；已确认标记、恢复后视觉保持和声音仲裁使用该状态；默认监护皮肤不添加操作按钮，确认入口留待后续皮肤接口；偏好只保存保持策略。
`ConfigureNotifications` 可为已有条件配置独立重复抑制及持续提醒；`NotificationRecords`
提供有界通知意图，`DroppedNotificationCount` 明确淘汰情况。首次及确认升级不额外延迟，
同级重复只延后通知，持续提醒不改变事件。显式单组声音通道可消费这些意图，产品默认仍为原持续声音；详见[通知决策接口](alarms/alarm-notification-policy.md)及[声音执行接口](alarms/alarm-notification-sound.md)。

其他独立入口：

- [SpO2LimitNotice.Evaluate](../../src/Monitor.Application/Presentation/SpO2LimitNotice.cs)：
  当前有效 SpO₂ 的即时下限提示，不能沿用最后一次有效低值；Critical < Warning。
- [NoExpirationNotice.Evaluate](../../src/Monitor.Application/Presentation/NoExpirationNotice.cs)：
  连续可用 CO₂ 采样中 5–120 s 未确认呼气；以最后有效样本与连续起点/上次呼气计时。
  质量不可用或最后样本超过 500 ms 时不输出；非法设置的 Info 提示 `Audible = false`。

配置存储版本、编辑失效和毫秒精度详见 [音频与本地偏好接口](audio-persistence.md)。

## 通知轮换与声音意图

[MonitorNoticeRotation.Update(notices, simulationTimeNs)](../../src/Monitor.Application/Presentation/MonitorNoticeRotation.cs)
接受最多 64 条、ID 唯一的通知，拒绝时间倒退和无效集合。`Current` 为当前文字，
`Highest` 为最高活动级别。按等级分配 2/4/6/8 s 轮换，组内按 ordinal ID 排序轮流选取；
严重度提升会立即切换。`CriticalElapsedNs(id)` 跟踪每个 Critical 条件，包含当前没显示的
条目，降级或消失结束该区间。

[MonitorSoundTiming](../../src/Monitor.Application/Presentation/MonitorSoundPattern.cs) 定义等级重复
周期：Info 5–120 s、Notice 1.5–60 s、Warning 3.5–60 s、Critical 0.25–2 s。
`MonitorSoundPattern.OnsetsMilliseconds` 返回本地创作的音簇起点；它不是 PCM 播放器。
[MonitorSoundPreferences](../../src/Monitor.Application/Presentation/MonitorSoundPreferences.cs)
保存总音量/心搏音量 0–100、心搏开关、来源选择、暂停秒数和 timing，不保存实时播放状态。
当前桌面映射 `BeatSource` 为 0 ECG、1 PLETH、2 Auto，`PitchSource` 为 0 固定、1 SpO₂；
参见 [SoundSettingsPanel](../../src/Monitor.Desktop/SoundSettingsPanel.cs)。

[MonitorAudioPause](../../src/Monitor.Application/Presentation/MonitorAudioPause.cs) 的
`Start(authorityTimeNs, durationSeconds)`、`Resume(authorityTimeNs)`、`RemainingSeconds(authorityTimeNs)`
使用不回退的本地单调权威时间；时长 1–3600 s、剩余秒数向上取整，到期清除期限。
它只抑制报警音，不确认或清除患者条件，也不跟随扫线冻结。

[MonitorBeatSource](../../src/Monitor.Application/Presentation/MonitorBeatSource.cs) 将来源选择与
具体事件分开：先 `Update(mode, ecgStatus, plethStatus, nowNs)`，再为新事件调用
`Accept(origin, confirmedAtNs)`。Ecg/Pleth 模式显式选源；Auto 需要连续有效 1 s，
从 PLETH 回 ECG 需 ECG 连续有效 3 s。质量为 Valid 本身不会生成心搏事件。

`Accept` 只接纳当前来源、已确认且距现在不超过 250 ms 的事件；拒绝重复/倒序、未来
事件，并在切换来源时防止 300 ms 内重复出声。`Changes` 保留最近 64 条来源变化。
`Reset()` 清除选择与去重历史。

[MonitorBeatPitch](../../src/Monitor.Application/Presentation/MonitorBeatPitch.cs) 从有效光学
测量决定音高投影，和心搏时机/音量独立。测量年龄超过 5 s、未来时间或不可用值会重置并
标记 `Unavailable`；有效值限制到 70–97% 音高区间，重复测量时间不重复滤波。

## 扫线状态、几何与计划调度

[SweepStateProjectionStateMachine](../../src/Monitor.Domain/Presentation/SweepStateProjectionStateMachine.cs)
管理 `Start`/`Restore`、`Advance`、`ChangeRunState`、`SynchronizeContinuity`、冻结进入/退出、
历史回看进入/定位/退出、`ReplacePlanAtCycleBoundary`。
`CaptureProjection()` 是显示快照；`CaptureState()` 保留可恢复的时钟与连续性状态。

`SessionRunState` 的 Running/Paused/Stopped 和 `TemporalViewMode` 的 LiveSweep、
FrozenSnapshot、HistoricalReview 分别表达运行和观察方式；AcquisitionFill/CapturedRecord
由后述填满保持流程管理。历史显示固定原范围，`TransientReplayPolicy.Suppress` 阻止
历史瞬态重放；实时患者连续性仍独立更新。

[NoDataSweepStateMachine](../../src/Monitor.Domain/Presentation/NoDataSweepStateMachine.cs) 输出
`NoDataSweepCoverage`，保留百万分相位与精确覆盖区间。
[SweepTraceComposition](../../src/Monitor.Domain/Presentation/SweepTraceComposition.cs) 合成当前轨迹
区域，[SweepPlotGeometry](../../src/Monitor.Domain/Presentation/SweepPlotGeometry.cs) 将精确时间
范围映射到像素；不要只凭四舍五入后的相位决定区域相等。

[SweepPlanScheduler](../../src/Monitor.Application/Presentation/SweepPlanScheduler.cs) 的
`NextBoundary()` / `Schedule(plan, planRevision, sweepRevision)` 仅在 Running、LiveSweep、
数据和权威均 Authoritative 时可用，且只能有一个 pending 计划。
`Advance(presentationNs, livePlayheadDataSimTimeNs)` 必须在预约边界被调用：越过边界报
`SweepSchedule.BoundarySkipped`。预约时仅验证，不推测未来患者时间；宿主负责按时调用，
不能用下一次 UI 渲染补做权威计划变更。

几何辅助接口按职责组合：

| 接口 | 合同 |
| --- | --- |
| [SweepPathBuilder](../../src/Monitor.Domain/Presentation/SweepPathBuilder.cs) | 从带来源/索引/质量的样本增量生成段；断源、缺口不可跨越连线 |
| [SweepSegmentClipper](../../src/Monitor.Domain/Presentation/SweepSegmentClipper.cs) | 精确有理数裁剪，返回可空线段 |
| [SweepColumnCoverage](../../src/Monitor.Domain/Presentation/SweepColumnCoverage.cs)、[SweepColumnSegmentSplitter](../../src/Monitor.Domain/Presentation/SweepColumnSegmentSplitter.cs) | 区域像素列归属与线段分列；保留边界所有权 |
| [EcgVerticalGeometry](../../src/Monitor.Domain/Presentation/EcgVerticalGeometry.cs) | 微伏有理数 → 垂直位置与边界关系；不把越界值悄悄改为边界值 |
| [EcgCalibrationGeometry](../../src/Monitor.Domain/Presentation/EcgCalibrationGeometry.cs) | 当前比例下 1 mV × 200 ms 校准脉冲及 gutter 几何 |
| [EcgPaperGridCalibration](../../src/Monitor.Domain/Presentation/EcgPaperGridCalibration.cs)、[EcgPaperGridGeometry](../../src/Monitor.Domain/Presentation/EcgPaperGridGeometry.cs) | 用走速/增益与像素比例解析纸格；轴比例不一致拒绝，生成有行数限额且可取消 |

## 重建、发布、pump、worker 与 gate

[SweepFrameReconstructor](../../src/Monitor.Application/Presentation/SweepFrameReconstructor.cs)
接受 `SweepFrameReconstructionInput(Frame, Samples)`，用 `Replace(input, cancellationToken)`
创建完整 `ReconstructedSweepFrame`。调用者指定最大样本/线段数量；输入须从无 Previous
样本的帧种子开始。构建失败或取消不替换 `Current`，成功后可 `CaptureCheckpoint()`。

[SweepFramePathBuilder](../../src/Monitor.Application/Presentation/SweepFramePathBuilder.cs)
管理跨区域 append；[SweepFrameHorizontalResize](../../src/Monitor.Application/Presentation/SweepFrameHorizontalResize.cs)
从 checkpoint 中的原 offset 证据重建横向布局，缺少该证据报 `FrameResize.OffsetEvidenceRequired`。
不能缩放旧像素结果来替代按新布局重建。

[EcgStripReconstructor](../../src/Monitor.Application/Presentation/EcgStripReconstructor.cs)
以 `EcgStripCheckpoint(Source, GutterLeftPixels, PulseLeftPixels)` 一次发布患者帧、校准脉冲
及可选列归约；要求 ECG 电压比例，`ResizeHorizontal` 也替换整个 strip。
`EcgColumnReductionLimits` 分别约束分列片段和包络数量。

列归约族包括 [SweepColumnFrameReconstructor](../../src/Monitor.Application/Presentation/SweepColumnFrameReconstructor.cs)、
[SweepColumnEnvelopeReduction](../../src/Monitor.Application/Presentation/SweepColumnEnvelopeReduction.cs)、
[SweepRegionColumnEnvelopeReduction](../../src/Monitor.Application/Presentation/SweepRegionColumnEnvelopeReduction.cs)、
[SweepClippedColumnReduction](../../src/Monitor.Application/Presentation/SweepClippedColumnReduction.cs)。
它们保留来源、区域与裁剪边界，在受限预算下生成显示包络，不改变测量样本。

并发集成分为三个边界，两套 sweep/ECG strip 实现具有相同生命周期：

1. [SweepFramePublication](../../src/Monitor.Application/Presentation/SweepFramePublication.cs) /
   [EcgStripPublication](../../src/Monitor.Application/Presentation/EcgStripPublication.cs)：
   `Request(input)` 拷贝/接管输入并分配本地 generation；`Complete(work, token)` 可在后台执行。
   外来 owner 的 work 拒绝；旧 generation 返回 `Superseded`，重复已发布返回 `AlreadyPublished`。
   `Stop()` 幂等封住所有未完成 ticket，返回最后快照，返回后它不会再被后台结果替换。
2. [SweepFrameWorkPump](../../src/Monitor.Application/Presentation/SweepFrameWorkPump.cs) /
   [EcgStripWorkPump](../../src/Monitor.Application/Presentation/EcgStripWorkPump.cs)：
   `Enqueue`、`ProcessNext`、`CapturePublished`、`Stop`；至多一个正在构建和一个最新待处理请求。
   新请求 admission 失败不会覆盖原 pending。pump 不创建线程或无限队列；恢复仅恢复已完成帧。
3. [SweepFrameWorker](../../src/Monitor.Infrastructure/Presentation/SweepFrameWorker.cs) /
   [EcgStripWorker](../../src/Monitor.Infrastructure/Presentation/EcgStripWorker.cs)：
   以后台 Task 承载 pump，提供 `Enqueue`、`WaitForIdleAsync`、`CapturePublished`、`LastFailureCode`、
   `DisposeAsync`。等待只覆盖调用前已接纳工作，后续生产者仍可提交；dispose 先封住发布再取消/等待。

已知构建参数错误保存在 `LastFailureCode`，后续成功发布会清除；非预期异常使 worker 停止，
idle task 以异常完成。宿主应观察等待/释放异常，不能只读最后一帧判断 worker 仍健康。

[SweepFrameDisplayGate.Select](../../src/Monitor.Application/Presentation/SweepFrameDisplayGate.cs)
比较当前状态、viewport、垂直比例、显示时钟与精确区域，返回 `Matched` 或 Missing/
ViewportMismatch/ScaleMismatch/PresentationMismatch 的 reason code；不匹配时 Frame 为 null。
[EcgStripDisplayGate](../../src/Monitor.Application/Presentation/EcgStripDisplayGate.cs) 另外验证
校准几何和可选列归约要求，按完整 strip 选择。gate 只接受受信任的进程内产物，
不负责来源认证、样本新鲜度或证明传入样本实际遵循声明比例。

最后使用 `SweepDisplayComposition.Compose` 或
[EcgStripDisplayComposition.Compose](../../src/Monitor.Application/Presentation/EcgStripDisplayComposition.cs)
组合当前安全、连接 banner、区域/校准几何与 gate 选择结果。
当前安全和比例永远不能从缓存 worker 帧读取；缺少 strip 也不是删除保留历史的命令。
渲染端应替换完整组合结果，明确处理 null source，避免拼接新状态和过期几何。

## 填满保持、12 导联记录与准入

[FillOnceThenHoldStateMachine](../../src/Monitor.Domain/Presentation/FillOnceThenHoldStateMachine.cs)
以 `FillOnceThenHoldPlan` 固定 group、recordRef、sweep epoch、记录起点/长度、历史需求和
12 个槽位。`Start` 的 playhead 必须等于记录起点；`Advance`、`ChangeRunState`、
`SynchronizeContinuity` 更新投影，填满后变为 CapturedRecord 并抑制瞬态重放。
`CaptureCoverage` 可随时读取；只有捕获完成后才能 `CapturePinnedRecordRange`。

[CapturedRecordBinding.Create(presentation, archive, slots)](../../src/Monitor.Application/Presentation/CapturedRecordBinding.cs)
是不可变记录发布边界：presentation 必须已捕获，归档 group、recordRef、epoch、起止
范围必须完全一致；必须恰有 12 个按记录槽位顺序排列、channel 唯一且集合匹配归档的绑定。
全量验证后才返回，不能发布“部分导联已绑好”的记录。`CaptureState` / `Restore` 包含
呈现状态、归档与槽位；`ReadBlocks()` 暴露记录证据读取。

[Ecg12ViewAdmission.Evaluate(context, viewMode, canPreserveGlobalSafetyOverlay)](../../src/Monitor.Domain/Presentation/Ecg12ViewAdmission.cs)
必须根据当前 shell 能力求值。`ActiveInstance` 包括其固定历史，仍要求全局安全叠层并
继承患者报警聚合；无法保留叠层时 `MayEnter = false`。`IndependentCapturedRecord` 只允许
CapturedRecord，不继承患者当前报警，也不要求该叠层。准入能力不得从保存的会话恢复。

[CapturedRecordPagination.Resolve](../../src/Monitor.Application/Presentation/CapturedRecordPagination.cs)
是不可变查询：`pageDurationNs > 0`、pageIndex 不越界，末页允许不足整页。
[CapturedRecordNavigation](../../src/Monitor.Application/Presentation/CapturedRecordNavigation.cs)
提供 `SelectPage` / `NextPage` / `PreviousPage` 和 `CaptureDisplay()`，命令服从
`SystemViewCommandAssessmentPolicy.Enabled/Disabled/CourseLocked`；页面边界返回明确拒绝码。
恢复时传入当前策略，不沿用旧权限；view 与 navigation 必须来自同一个记录对象。

## 记录投影、手工测量与交互

[CapturedRecordStudyView](../../src/Monitor.Application/Presentation/CapturedRecordStudyView.cs)
按当前页/槽串行合成，从 `CapturePageDisplay` 到 waveform → horizontal → voltage → quality →
path 逐层增加证据。每次用当前准入结果，拒绝时不返回旧记录/测量投影。
完整路径入口为 `CapturePathPageDisplay(...)`，调用者提供样本/线段预算与 cancellation token。

- [CapturedRecordVoltageBinding](../../src/Monitor.Application/Presentation/CapturedRecordVoltageBinding.cs)
  绑定受信任的通道采样率和 `EcgRawVoltageCalibration`，要求与归档采样率、比例、偏移一致；
  外来 record/slot、profile 不符均拒绝，不负责认证外部导入。
- [CapturedRecordQualityBinding](../../src/Monitor.Application/Presentation/CapturedRecordQualityBinding.cs)
  采用严格递增的 `RecordQualityRule`，按 flags 完整字精确匹配，未知值拒绝；零值也需显式规则。
  不作子集 mask 推断。不可绘制样本断开路径，路径仅连接连续样本索引。
- [CapturedRecordWaveformHorizontalProjection](../../src/Monitor.Application/Presentation/CapturedRecordWaveformHorizontalProjection.cs)
  保留页面时间和精确横坐标；[CapturedRecordPathProjection](../../src/Monitor.Application/Presentation/CapturedRecordPathProjection.cs)
  的结果是未裁剪源线段，不是原生绘图命令。

[EcgRawVoltageCalibration](../../src/Monitor.Domain/Presentation/EcgRawVoltageCalibration.cs)
按 `raw × scale + offset` 解析原始值，只接受 `MICROVOLT` / `MILLIVOLT` 单位，分母须非零，
转换结果必须能表示为 `long` 分子、`uint` 分母的微伏有理数。

[CapturedRecordMeasurement](../../src/Monitor.Application/Presentation/CapturedRecordMeasurement.cs)
拥有当前槽和 cursor pair，通过 `CreateCursor` / `ReplacePair` / `MoveCursor` / `Calculate`
校验策略、共同 owner 和记录范围。`EcgManualCursor` 时间须在记录半开区间内；第二个
cursor 不得早于第一个。不能把另一测量实例的 cursor 混入当前实例。

[EcgManualMeasurement.Calculate(first, second, allowAuxiliaryRate)](../../src/Monitor.Domain/Presentation/EcgManualMeasurement.cs)
返回约分后的 `ElapsedMilliseconds`、有符号 `AmplitudeChangeMillivolts`（第二点减第一点），
以及可选 `AuxiliaryRatePerMinute`。辅助频率仅在允许且时间差大于零时提供。
Domain 纯计算不校验权限或共同记录，这由 Application wrapper 负责。

`CreateCursorFromPoint` 接受精确有理数坐标，不吸附到波形、不隐式量化；若无法精确表示
整数 ns 或有界微伏有理数则拒绝。`ProjectCursor` 在 cursor 有效但不属于当前半开页时
返回 null；这是不在当前页，不是源数据缺失。像素布局不会写回 cursor 证据。

[CapturedRecordDrag](../../src/Monitor.Application/Presentation/CapturedRecordDrag.cs) 是单次串行手势：
`Preview` 更新当前 pair，`Commit` 完成，`Cancel` 恢复初始 pair。外部替换/清除 pair 后
报 `DragSuperseded`；完成后再次操作报 `DragFinished`；布局改变会锁住该手势的布局失效。
view/zoom 包装层还检查当前页、槽、准入和变换，pointer 方法保留原始抓取偏移。
`Cancel` 仍要求当前手工测量策略允许操作，但不要求当前页可见或通过 view 准入。
见 [CapturedRecordStudyDrag](../../src/Monitor.Application/Presentation/CapturedRecordStudyDrag.cs)、
[CapturedRecordZoomedDrag](../../src/Monitor.Application/Presentation/CapturedRecordZoomedDrag.cs)。

`UpdateCommandPolicies` / `UpdateThemedCommandPolicies` / `UpdateZoomedCommandPolicies` 先完整
校验再更新各组策略。[CapturedRecordStudySession](../../src/Monitor.Application/Presentation/CapturedRecordStudySession.cs)
保存记录导航、选定槽和可选完整 cursor pair，并可扩展主题/zoom；恢复必须重新提供当前
context 和各策略。权限、显示像素、正在拖动的手势不属于保存状态。

### 十二导联纸图手动测量

[Ecg12PaperLayout](../../src/Monitor.Application/Presentation/Ecg12PaperLayout.cs) 是十二导联纸图的
唯一几何：25 mm/s、10 mm/mV、每毫米 4 px，3 × 4 或 6 × 2 短导联加底部长 II。桌面纸图绘制和卡尺
共用它，保证指针落点与绘制的导联、时间一致。导联序号 0–11 按 `EcgLead` 顺序，12 为长 II 节律条
（`SourceLead` 为 II）。每列前 30 px 为定标脉冲，不属于任何导联；`HitTest` 在定标、页边和行间空白返回 null。

`Ecg12PaperMeasurement(layout, blocks, channelOfLead, policy, allowAuxiliaryRate=true)` 在一份冻结纸图上放置
手动测量点：`Hover` 把悬停点吸附到指针下导联的最近实际采样（放置第二点时停留在第一点的导联并夹在其首尾采样），
`EndHover` 移除悬停点；`Place` 依次放置第一点和第二点，第二点放置后再次 `Place` 开始新的测量。`Nudge` 以整采样
移动最近放置的点，`Clear` 移除。采样值按平面 `raw × scale + offset` 换算为精确微伏有理数。`Display` 按时间排序
两点（不论先放哪一点），用 `EcgManualMeasurement.Calculate` 给出 Δt（水平距离）、较晚一点减较早一点的 ΔV（垂直
距离）和可选辅助频率；只有第一点时 `HoverResult` 给出按悬停点放置的预览。`ReasonCode` 为 Idle、Placing、Ready、
Disabled 或 CourseLocked。策略非 Enabled 时撤除测量点并拒绝新操作。不识别波形起止点，不保存测量点，也不写回记录。

### 主题、zoom 与 SVG 发布

[Ecg12ThemeSelection](../../src/Monitor.Domain/Presentation/Ecg12ThemeSelection.cs) 支持
MonitorDarkGreen / PaperGridBlack，选择同时服从课程策略与本地选择许可。
[Ecg12ZoomSelection](../../src/Monitor.Domain/Presentation/Ecg12ZoomSelection.cs) 支持 FitPage、
ActualSize、ExplicitScale，比例必须为正并约分；前两者状态比例固定为 1/1。
它们只保存显示意图，不能改变记录坐标或声称提供物理打印比例。

[Ecg12ScreenTransform](../../src/Monitor.Domain/Presentation/Ecg12ScreenTransform.cs) 解析当前页/可用
尺寸，提供精确 `Forward`/`Inverse` 及带原点版本。整个页面只应用一次 transform；
内容坐标仍保持未缩放值。

[CapturedRecordSvgLayers](../../src/Monitor.Infrastructure/Presentation/CapturedRecordSvgLayers.cs)
组合 grid、cursor overlay 和 zoom；独立的
[EcgPaperGridSvg](../../src/Monitor.Infrastructure/Presentation/EcgPaperGridSvg.cs) 与
[EcgManualCursorSvg](../../src/Monitor.Infrastructure/Presentation/EcgManualCursorSvg.cs) 生成字符串。
[EcgStripSvgPreview.Render](../../src/Monitor.Infrastructure/Presentation/EcgStripSvgPreview.cs)
提供教学几何诊断预览，逻辑像素输出不等于物理校准导出。

[CapturedRecordSvgInputSession](../../src/Monitor.Infrastructure/Presentation/CapturedRecordSvgInputSession.cs)
将一份已渲染显示和输入绑定在同一布局上，提供 Refresh、HitTest、PlacePair、MoveCursor、
BeginDrag；[CapturedRecordSvgDrag](../../src/Monitor.Infrastructure/Presentation/CapturedRecordSvgDrag.cs)
将指针坐标转换交给 view-bound 手势。

[CapturedRecordSvgPresentation](../../src/Monitor.Infrastructure/Presentation/CapturedRecordSvgPresentation.cs)
是宿主应绑定的当前发布槽：`Refresh(...)` 在校验/取消检查前先把旧 `Current` 清空，成功
才发布 Ready；准入失败为 Denied，取消为 Cancelled，其他失败为 Failed，均不保留旧输入。
`Withdraw()` 幂等撤下当前发布，但不删除记录，也不替调用方提交或取消保留的 drag。
旧不可变 snapshot 本身不会被撤销；原生控件须同时从当前槽读取图像与新输入入口。
