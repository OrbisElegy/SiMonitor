# ECG 电击转复配置与皮肤接口

[接口总览](README.md) · [波形源](simulation.md#起搏与除颤) · [研究依据](../research/physiology/pacing-defibrillation.md)

实现：[EcgElectricalTherapy](../../src/Monitor.Application/Therapy/EcgElectricalTherapy.cs)、
[本地预览接入](../../src/Monitor.Application/Presentation/LocalMonitorPreviewSession.cs)、
[设置编辑器](../../src/Monitor.Desktop/ElectricalConversionEditor.cs)。

## 模板与规则

配置按稳定标识 `ecgTemplate.tNNN` 保存，不使用翻译后的名称，不挂在当前模板的临时状态上。
目录 `EcgElectricalTherapy.Descriptors` 包含以下模板：

| 模板编号 | 心律 | 电击方式 |
|---|---|---|
| 006–007、066–071 | 房颤及差异传导／短绌变体 | 同步 |
| 008–009、037–039 | 房扑及传导比例变体 | 同步 |
| 023–025 | 窄 QRS、伴 RBBB／LBBB 的室上速 | 同步 |
| 026–029 | 单形、融合、夺获、双向室速 | 非同步；有有效射血时也支持同步 |
| 020–022、030 | 室扑、粗／细室颤、扭转形态室速 | 非同步 |

有效射血上下文取当前已生效源的 `VentricularMechanicalEnabled`，不是根据 ECG 推断脉搏。
该开关不能证明血流动力学稳定，因此不再用于禁止室速的非同步转复响应。室速仍须显式允许
转复且实际单次能量超过对应阈值；本接口响应已发生的电击，不自动选择临床治疗方式。
非室性有组织心律被设置为无有效射血时，按 PEA 示意排除。静止、逸搏、自主心律、单纯室早、
预激形态、起搏及其他未列模板均不支持此响应；仅携带错误的可除颤模板标识也不能让窦律源转复。
这不是自动诊断器，也不判断血流动力学稳定性或决定临床指征。

每个 `ElectricalConversionSettings` 包含 `Enabled`、`MonophasicThresholdJoules`、
`BiphasicThresholdJoules`。默认关闭，200／150 J 为作者设定的可编辑教学初值。
阈值为 0–1000 J 整数，实际能量输入为 1–1000 J 整数；全部验证后才发布。
**实际单次能量必须严格大于对应阈值**；等于阈值不会转复，不累加前次能量，不使用概率。
单相阻尼正弦与单相截断指数使用单相阈值，双相截断指数与直线双相使用双相阈值。

## 应用与持久化

桌面入口：“波形预设 → ECG → 高级参数 → 电击后转复”。选择不同模板保留各自草稿，
“应用”验证完整设置集合并随源切换生效。未应用的编辑不会影响正在运行的源。
无效整数／空值即时标记，其他模板尚存无效草稿时也拒绝整体应用，并提示返回 ECG 高级参数修正。
关闭允许转复开关仍保留能量值；恢复全部默认值清除逐模板配置。

`MonitorGeneratorPreferences.ElectricalConversions` 是完整的独立映射，包括当前未选中的模板。
`EcgElectricalTherapy.Snapshot` 校验支持的标识、非空记录、阈值和条目上限，并返回字典副本。
`DisplayPreferenceStore` 写版本 18，读 1–18；旧文件缺少手动映射时使用关闭的默认规则。
重新打开应用、切换为窦律后保存、再选择原模板都应保留映射。

AED 独立映射为 `MonitorGeneratorPreferences.AedElectricalConversions`，记录相同的开关、单／双相阈值与停顿。
当前 profile 的 `AedSettings` 与手动 `Settings` 分开；`DeliveredElectricalShock.Automated` 标记实际 AED
交付，`Evaluate(..., automated)` 与 `ApplyElectricalShock` 据此选择整份响应配置，包括停顿。
新配置默认关闭；1–17 版的共用响应复制为两份独立映射，保留旧行为。设置界面仅在室扑、
室颤与室速模板显示 AED 编辑器；室速的非同步响应也必须通过独立 AED 许可与能量检查。

皮肤保存自动转复后的编辑器快照时，必须保留此前的完整手动与 AED 映射，不能只保存
窦律对应项或重新初始化映射。当前模板身份、心率等目标源参数由皮肤按目标窦律快照更新；
这与保存所有模板的转复设置是两个独立操作。本地预览类不直接访问磁盘。

## 放电后的调用

`EcgElectricalTherapy.Evaluate` 是无状态判定：输入当前模板 profile、当前生理源配置、实际波形类型、
已执行的同步／非同步方式与实际放电焦耳数。返回 `Eligible` 时携带 `SinusTemplateId`，
其他结果区分不支持、关闭、方式不匹配和能量不足。不会修改源、计时器或偏好。

本地预览构造器可接收 `electricalTherapy` profile。`ScheduleSource` 延迟切换时 profile 随同源
原子生效；判定不会提前读取编辑器草稿。省略 profile 的既有调用保持无电击转复行为。

```csharp
var delivery = new DeliveredElectricalShock(
    deliverySequence, session.SimulationTimeNs,
    DefibrillationWaveformKind.BiphasicTruncatedExponential,
    DefibrillationMode.ManualAsynchronous, actualDeliveredJoules);
var result = session.ApplyElectricalShock(delivery, preparedSinusSession);
```

皮肤仅在治疗权威确认**实际放电**后调用。`DeliveredElectricalShock` 是消费契约，不是放电授权；
充电、按住／释放、同步 QRS、接触状态、能量计算及域层 `ShockDelivered` 事实仍归治疗控制器。
不会从电流波形的峰值、设定充电能量、按钮按下或尚未完成的同步请求推断实际放电。

`preparedSinusSession` 必须是尚未运行的独立源，带 `SinusTemplateId` profile，窦性电活动且
与当前测量开关兼容。皮肤应使用一致的呼吸、氧合、显示及其他非心律参数构建它，并同步更新
自己的十二导联呈现和当前模板标记；适配器不臆定除颤后的血压、呼吸、氧合或循环恢复。

成功返回 `ConversionScheduled` 及最终窦律的 `EffectiveSimTimeNs`，在放电结束后的
200 ms 采集块边界切换；若模板配置了心脏停顿，则先切换无心脏活动源，停顿结束后再在
采集边界转窦律。该序列不增加用户“应用延迟”，不清除历史或重置仿真时钟。
暂停时没有 `Advance`，已排定的转换不会自己推进。存在其他待生效源切换时返回
`SourceChangePending`，由调用方呈现冲突，不偷偷覆盖用户操作。

`DeliverySequence` 在一个本地会话内从正整数单调递增，跨模板切换不重置；相同／更早序号
返回 `DuplicateDelivery`。放电时间必须等于当前仿真时间。已消费的能量不足、方式错误或被
待切换状态阻挡的实际放电也不能改能量后重试；无效输入、无效目标源则原子拒绝，保留状态。
新建会话重置此计数，外部权威仍应验证会话身份与自身事件去重。

## 手动除颤与设备配置

`DefibrillatorConfiguration` 与逐模板 `ElectricalConversionSettings` 分开：前者描述设备档位、
放电波形、充电时间和充满后的自动取消时间，后者只决定模板是否转复。档位是 1–1000 J 的
严格递增整数，最多 64 档；充电时间 100–60000 ms，自动取消 1–300 s。Generic 默认
1、2、5、10、20、30、50、70、100、120、150、200 J，双相截断指数波、3 秒充电、30 秒取消，
仅为教学设备配置。保存能量不在新档位中时选最近档位，等距时取较低值。
`Snapshot` 验证并复制档位集合，`Resolve(editable, deviceOverride)` 优先设备固定配置。

`ManualDefibrillator` 包装 Domain `TherapyController`；实例 ID、交互 ID、单调安全时间及
新采集 QRS 峰值时间由宿主传入。桌面安全时间来自 Stopwatch，与仿真暂停／节流独立。
`BeginCharge` 不产生放电；`Tick` 按配置时长标记充满，进度为 0–1000。
`PressShock` 授予 0.5 秒长按门槛、10 秒截止的交互租约，异步达到门槛才发出交付结果；
同步在达到门槛后继续等待峰值时间晚于该仿真边界的新 QRS，历史检测不用于补触发。
超时优先于 QRS。松手、取消和模式／能量变化释放储能；同一租约只交付一次。
充满超时从计划充满时刻计算，主机长时间未刷新不能延长待放电期限。

`SynchronizationBeats` 是本次推进中新确认的实时 ECG 心搏。它复用现有采样检测器，
保留峰值时间和确认时间，不等待慢速通道的合包延迟；电击不可用标记与起搏脉冲证据
同样参与检测。同步触发与 ECG 倒三角共用这些事件，不能按模板、心率数字或定时器制造 QRS。
倒三角只记录启用同步之后的峰值，按原始峰值时间绘制，遵守当前／前次扫线裁切；
关闭同步或皮肤没有 ECG 接入时清除，历史有界。无 ECG 接入时也不提供同步触发证据。
充电进度在按钮背景内从左向右填充，标签始终可见；充满后保持全填充，取消或放电后清空。

宿主仅消费 `Tick` 返回的 `DeliveredElectricalShock`，跨设备配置更新分配单调交付序号，
再调用 `ApplyElectricalShock`。`PrepareSinusAfterShock` 从当前生效源保留呼吸、压力、
光学氧合、实时通气／耗氧以及手动体征，重建正常窦律心电；不读取未应用的设置草稿。
目标窦律模板的起搏许可取最近应用的配置。转复时十二导联和已应用参数概览一起更新，
已有波形历史保留。选择的放电波形用于单／双相阈值判断及 ECG 电击伪迹，不模拟阻抗。

启动手动充电、同步和放电仅适用于
监护页运行且没有待生效源变更时；暂停、离页、窗口失焦、关闭、应用配置或起搏命令会取消
当前充电／放电租约。任何持久化文件均不保存储能、活动租约或 AED 运行状态。

## 半自动 AED

`AutomatedExternalDefibrillator` 与手动模式共享一个 `ManualDefibrillator`，唯一实际放电来源
仍是后者的 `Tick`。AED 进入时取消旧储能、关闭同步；分析只消费 `LiveEcgEvidence`
中的实时采样读数，不查询模板名称、转复许可、能量阈值或报警是否启用。
`SampleTimeNs` 是最后一个实际 ECG 采样点，不是包尾区间或延迟数值时钟。

| 阶段 | 行为及转移 |
|---|---|
| Off | 手动模式，无 AED 授权 |
| Analyzing | 至少 8 秒连续有效观察，最后的建议连续稳定 2 秒；可电击则自动充电，否则进入 CPR |
| WaitingForSignal | 不可判读、尚未学成、样本过期或数据断流；立即取消储能，恢复后重新完整分析 |
| Charging / ShockAdvised | 持续检查新证据；充满后仅接受按住 0.5 秒的明确确认，绝不自动放电 |
| Cpr | 电击后或不建议电击后显示 120 秒倒计时，再进入全新分析；不生成按压波形 |
| Suspended | 取消、短按松手、暂停、离页、失焦或储能到期后，必须点“重新分析”继续 |

Generic 使用进入 AED 前选择的能量档位，并锁定能量、同步和起搏；复用设备覆盖后的波形、
充电时长和自动取消配置。关闭 AED 释放储能、返回非同步手动模式。应用模板／设备设置
创建新的 AED 所有者，不恢复运行状态。启动起搏与进入 AED 互斥。

分析／CPR 使用仿真时钟；充电、按住确认和储能到期沿用宿主单调安全时钟。证据最多落后
200 ms，未来样本无效；相邻证据间隔超过 200 ms 也清除分析授权。信号失效或不再建议电击
在处理放电阈值之前取消储能。转复自身排队的静止／窦律阶段允许 CPR 继续，外部源切换不保留授权。

判定为成人教学近似：现有实时室颤检测有效时建议电击；快速宽复合波使用测得心率 >150/min、
采样宽度代理 ≥100 ms、最近搏动 ≤600 ms 且非起搏标签。不能依赖监护器的室速报警标签，
监护器另有可配置的连续搏数与速率阈值，其启动宽波筛查独立于参考形态学习。
100 ms 是现有单导联波形算法的宽度代理，
不等同于临床 QRS 起止测量。心搏暂停不建议电击；已持续确认的 SVT 候选（包括具有快速
初始激动线索的宽波 SVT）且无同时 VT 条件时，不由单纯宽度覆盖为电击建议；其余已完成
学习的可计数信号不建议电击。
单导联宽复合波不能确证室速或无脉状态，本模式假设操作者已确认成人心搏骤停教学场景；
不通过隐藏仿真元数据替代脉搏判断。建议电击与实际是否按模板规则转复是两个独立结果。
依据与局限见[调研](../research/physiology/pacing-defibrillation.md#半自动-aed-流程)。

状态灯、中英文文字及 E1/F1 语音随阶段更新，CPR 显示剩余秒数。治疗声音通过单一监护音频流
输出，暂停／取消／退出会撤回旧语音，重连不重播已消费的状态提示；详情见[声音接口](audio-persistence.md#治疗声音)。
运行状态、音频排程、倒计时及储能均不持久化。

## 采样中的放电与恢复

`ApplyElectricalShock(delivery, preparedSinusSession, ecgRecoveryMilliseconds = 1000)` 为每个
非重复、有效的实际放电创建 `DefibrillationEcgArtifact`。转复失败只叠加伪迹，不改变心律。
非法目标源不会发布部分序列或消费交付；重复交付不会重复添加伪迹。

伪迹以实际放电仿真时间定位。全部监护波形从会话开始就使用同一次仿真推进的实时源采样，
不等待慢速通道合包或 2.2 秒呈现缓冲；放电后下一帧即显示伪迹，恢复与转复接续同一源。
`PresentedSamples` 与 `PresentationFrontierNs(channel)` 提供这一显示路径；所有通道前沿均为
`SimulationTimeNs`。ECG、PLETH、ABP／PA／CVP 保留同一心搏的源时间及已有传播／响应延迟，
不额外平移任一通道。各行按所选扫速推进扫线与自动量程周期，同扫速的行同时换扫。
放电不跳动前沿、不改写已显示历史。实时波形与随后采集包使用相同源样本和伪迹函数，
采集包到达不会再次绘制电击。两条路径都保留启动丢弃的时间重定位。
实时样本也保留传感器零点、物理单位缩放和质量标记；一次推进中的通道数据全量验证后
一起发布，失败时不得只推进部分波形。每通道保留有界历史。

`Samples`、`Blocks` 与测量仍使用原有带延迟的采集数据；伪迹修改尚未提交的 ECG 采集样本，
按采样位置生成合并的非零质量范围。恢复区间的不可判读样本保留在扫线中，
但不参与心搏、心律和停搏判读，也不参与自动量程。其余通道不注入电击电流。
扫线标记“电击”，当前位置处于恢复区间时显示“电击后恢复中”。
`ShockArtifacts` 提供保留窗口内只读事件，随对应采集块老化移除，最多保留 128 个事件。

设备 `EcgRecoveryMilliseconds` 为 100–10000 ms、默认 1000 ms，位于除颤器配置且遵守
设备皮肤锁定。逐模板 `ElectricalConversionSettings.PostShockPauseMilliseconds` 为
0–10000 ms、默认 0；位于 ECG 电复律设置，0 表示不额外模拟真实停顿。
二者独立，不从电击能量计算停顿。真实停顿的开始与结束向上对齐采集边界；
`PendingSourceTimeNs` 在第一阶段切换后直接指向最终阶段，宿主不会把暂时的无活动源当作
已完成转复。暂停冻结源序列，显式应用新模板会替换整个待生效序列，旧的自动窦律不得复活。
研究边界见[电击伪迹与真实停顿](../research/physiology/pacing-defibrillation.md#电击伪迹监护恢复与真实停顿)。
