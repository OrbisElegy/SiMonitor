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
| 026–029 | 单形、融合、夺获、双向室速 | 有有效射血时同步；无有效射血时非同步 |
| 020–022、030 | 室扑、粗／细室颤、扭转形态室速 | 非同步 |

有效射血上下文取当前已生效源的 `VentricularMechanicalEnabled`，不是根据 ECG 推断脉搏。
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
`DisplayPreferenceStore` 写版本 14，读 1–14；旧文件缺少该字段时为空映射、使用关闭的默认规则。
重新打开应用、切换为窦律后保存、再选择原模板都应保留映射。

皮肤保存自动转复后的编辑器快照时，必须保留此前的完整 `ElectricalConversions`，不能只保存
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

成功返回 `ConversionScheduled` 及 `EffectiveSimTimeNs`，复用 `ScheduleSource(..., 0)` 在当前或
下一个 200 ms 采集块边界切换，不增加用户“应用延迟”，不清除历史或重置仿真时钟。
暂停时没有 `Advance`，已排定的转换不会自己推进。存在其他待生效源切换时返回
`SourceChangePending`，由调用方呈现冲突，不偷偷覆盖用户操作。

`DeliverySequence` 在一个本地会话内从正整数单调递增，跨模板切换不重置；相同／更早序号
返回 `DuplicateDelivery`。放电时间必须等于当前仿真时间。已消费的能量不足、方式错误或被
待切换状态阻挡的实际放电也不能改能量后重试；无效输入、无效目标源则原子拒绝，保留状态。
新建会话重置此计数，外部权威仍应验证会话身份与自身事件去重。

本轮没有新增除颤按钮、充电动画或电击伪迹叠加，以上接口供后续皮肤组合调用。
