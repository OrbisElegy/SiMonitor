# ECG 增强监测接口

[接口总览](../README.md) · [测量与呈现](../measurements-presentation.md) · [依据与能力边界](../../research/physiology/ecg-monitoring.md)

## 调用与发布

`EcgHeartRateMeasurement(Guid, EcgMonitoringSettings)` 接收同一条 250 Hz、可换算为微伏的
已采集 ECG。构造时完整验证设置；原有构造器使用教学默认值。它不读取作者心律名称、
发生器目标值、源端生理事件、皮肤设置或墙钟。

```csharp
var detector = new EcgHeartRateMeasurement(channelId, settings);
var beats = detector.Consume(wire, out var rhythms, out var monitoring, pacingEvidence);
var reading = detector.ReadMonitoring(asOfSampleTimeNs);
```

`beats` 保持原有 QRS 峰值／确认时间契约。逐搏形态判断在
`reading.LastBeat` 中给出最近一搏的 `Label`、采样形态宽度（ms）和模板差异（千分比）。
标签包含学习中、正常、室上性早搏候选、室性候选、起搏和未知。
形态判定等待峰后至少 160 ms 的完整轮廓；QRS 提示时刻不变。
这不是诊断性 QRS 时限，也不把所有宽 QRS 自动解释为室性心搏。

`LiveWaveformMeasurements` 的新构造重载接受 ECG 设置；完整 `Consume` 重载额外返回
`monitoringEvents`，可选接收 `EcgPacingEvidence`。所有通道成功后才一起发布状态和事件。
`LiveMeasurementSnapshot.EcgMonitoring` 提供当前状态，
`LocalMonitorPreviewSession.DetectedMonitoringEvents` 提供本次 `Advance` 的事件；无新包时为空。
旧重载保持可用。预览的现有采集器没有起搏脉冲旁路数据。

`Capture` / `Restore` 深复制可变样本、事件状态和计时器；模板／历史记录只在替换时更新。
输入包重复、重叠、回退、校准溢出或起搏证据无效时，状态不变且所有事件输出为空。
时间跳跃、采集身份或配置修订改变会中断旧事件并重新学习。

## 读数与事件

`EcgMonitoringReading` 包含学习状态、`ActiveConditions` 位集、最近一分钟室性候选数、
最近心搏形态、ST/QT 读数和 `PacingEvidenceAvailable`。没有数据或质量不可用时不暴露活动条件，
不以零替代未知数值。顶层 Valid 表示采样可分析；具体 ST/QT 是否可测以其独立状态为准，
不能由顶层 Valid 推断所有项目都正常。心率的 Valid/Stale/Uncountable 仍读原有 `HeartRate`。

`DetectedEcgMonitoringEvent` 的 `Condition` 始终只有一位；`EvidenceFromNs` 是本实现使用的
证据开始时间，`ConfirmedAtNs` 是已采集样本上的因果确认时间，不是回填的真实发病时间。

- `Started`：持续条件首次成立。
- `Ended`：有可用证据表明持续条件结束。
- `Interrupted`：质量、连续性或所需证据中断；不表示恢复。
- `Occurred`：已完成的成对室早、非持续室速或 R-on-T 事件；只发一次，不占活动条件位。

同一批事件按采样推进顺序返回。`Read` 不产生事件、不推进延迟，也不重放上次事件。
皮肤应保留 Occurred 事件供通知或回顾，不能仅根据最新 `ActiveConditions` 重建所有历史事件。

## 分析和设置

| 范围 | 输出与本实现约束 |
|---|---|
| 节律学习 | 首 15 个可提取形态的 QRS 选择主导模板；模板相关条件在学习后生效。`Relearn(out rhythmEvents)` 清空形态／节律证据，返回中断事件，保留独立 QRS/HR 测量。 |
| 危急条件 | 静默 ECG 的 Asystole；连续频率证据的 SuspectedVentricularFibrillation；极低／极高 HR。VF 是单导联疑似筛查。 |
| 间歇 | Pause、MissedBeat；有在途 QRS 候选时等待其确认；Asystole 与 Pause 同阈值时只报告 Asystole。 |
| 室性候选 | VT、完成后确认的 NSVT、室性节律、成串与成对室早、二／三联律、多形室早、滚动 PVC/min、R-on-T。形态不确定时用 Unknown，不填成 Normal。 |
| 室上性候选 | 学习后出现提前的模板匹配心搏，并持续快速窄 QRS 时筛查 SVT；单纯稳定窦性心动过速仍走 HR 条件。 |
| 起搏 | `PacedMode` 加可信起搏脉冲旁路证据支持 PacerNotCaptured / PacerNotPacing；该模式不产生 MissedBeat。 |
| ST | 选定单导联、正常窄 QRS、稳定基线；在估计 QRS 结束后 60 或 80 ms 取样，报告微伏与持续超限事件。 |
| QT | 正常、未受提前 QRS 污染的搏动；T 波不可辨认、过快、宽 QRS、起搏或基线不稳时无值。支持 Bazett / Fridericia 整数校正、QTc 与 ΔQTc。 |
| 原有房颤／不齐 | 仍由 `EcgRhythm` 与 `DetectedEcgRhythmEvent` 提供。`RhythmEndDelayMilliseconds` 可配置恢复确认延迟，默认 5 s 保持旧行为。 |

阈值的单位、范围与默认值以 `EcgMonitoringSettings` 为准。其默认值是本项目教学配置，
不声称是厂商某机型／患者类别的出厂配置。持续 ST 超限需要超过 60 s；QTc／ΔQTc 超限需要
超过 300 s 的连续可测证据。采样中断或不可测会清除待确认时间；已确认条件用 Interrupted
结束。ST/QT 读数超过 3 s 未更新即失效。

QTc 基线可在构造设置中指定；未指定时在连续有效 QT 监测 5 分钟后自动建立。
质量中断或手动重新学习保留已经建立的 QTc 基线；采集身份改变后的新分析器重新初始化。
当前自动 T 终点是教学近似，不替代十二导联手工测量；没有 T 波时不输出 QT=0。

## 起搏旁路证据

`EcgPacingEvidence.PulseTimesNs` 必须来自采集端脉冲检测器，并覆盖整个当前包。
非空对象但空列表表示已检查且无脉冲；`null` 表示检测能力／覆盖不可用，禁止据此生成
“未起搏”。时间戳须在包内、严格递增且落在 4 ms 采样网格；输入列表在处理前快照复制。
仅连续覆盖最近一搏及后续间隔后，才可区分有脉冲无 QRS 与无脉冲无 QRS。

250 Hz ECG 不能保证保留真实起搏器的亚毫秒脉冲，所以不从波形毛刺猜测旁路证据。
该契约不改变 MWS1，也不授权把发生器的起搏设定当成实际脉冲检测结果。

## 皮肤接入边界

检测器不选择声音、不锁存／确认报警、不执行报警链抑制或重提醒；这些属于已有通知与
生命周期层。`ActiveConditions` 可以同时包含不同检测组的条件；皮肤应结合自己的能力与
通知策略消费事件，而不是给每个位独立无限重播声音。

当前接口为单导联。多导联主／次导联回退、相邻导联 ST Multi/STE、十二导联 ST index，
以及独立的电极脱落硬件状态尚不支持；不得将单导联 STHigh 标为多导联 STE 报警。
实际厂商的学习、波形分类和 ST/QT 代表搏动算法没有在用户手册中公开，不能把本接口称为
厂商实现或宣称达到其临床检测性能。
