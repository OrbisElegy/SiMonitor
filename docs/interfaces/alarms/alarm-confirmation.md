# 数值报警确认配置接口

`ConfirmedLimitNotice` 为 ECG HR、SpO₂、RR · RESP、PR · PLETH、EtCO₂、RR · CO₂、ABP/PA/CVP 平均压提供连续越界及恢复确认。每个通道、每个实时会话持有一个实例；输入仅为实测快照、阈值与确认配置，不读取模拟预设或生成器目标值。`PressureLimitNotice` 保留原压力接口并委托给此实现。

## 配置与单位

`MeasurementConfirmationTiming` 包含 `CriticalLow`、`WarningLow`、`WarningHigh`、`CriticalHigh` 四个独立的 `BoundaryConfirmationTiming`。每个边界的 `TriggerMilliseconds`、`RecoveryMilliseconds` 均为整数毫秒，范围为 0–600000。0 在首个有效证据时刻确认，不代表禁用；范围上限是项目自行定义的工程约束。

`MonitorAlarmPreferences.ConfirmationTimings` 按 `MonitorNumeric` 保存覆盖项，`ConfirmationFor(numeric)` 返回生效配置。不支持的通道、空字典对象、空边界或超出范围的时间拒绝发布；空字典表示全部使用默认值。当前接口支持上述九个数值通道；CO₂ 未检出呼吸时限不接受此配置。SpO₂ 只有下限报警：其阈值对象的两个上限必须为 `null`，确认时间对象中的两个上限必须为零；`ValidateFor(numeric)` 拒绝非零上限时间。桌面只展示两条下限控件。每个参数按“阈值”“触发确认”“恢复确认”页签分组，开关在页签外始终可用；EtCO₂ 另有“未检出呼吸”页签。切换页签复用同一批控件，不修改参数或重置报警计时。

| 通道 | 下限触发 | 上限触发 | 恢复 |
| --- | --- | --- | --- |
| ABP/PA/CVP 平均压 | 4000 ms | 10000 ms | 3000 ms |
| HR、RESP、PLETH、EtCO₂、CO₂ 呼吸率 | 0 ms | 0 ms | 0 ms |
| SpO₂ | 0 ms | 不支持 | 0 ms |

默认值分别应用于 Warning 和 Critical，保留原教学行为；独立覆盖后各边界互不借用计时。报警启用状态仍由原 `MeasurementLimits.Enabled` 或 `SpO2Enabled` 控制。HR 和 SpO₂ 的原提示 ID、文字、严格边界及零延迟行为保持不变。

## 时间与生命周期

比较使用测量原生整数单位，严格低于下限或高于上限为越界，等于阈值属于恢复范围。条件连续成立达到所配时间时触发，恢复连续成立达到所配时间时解除；任一过程被相反证据打断就清空该过程的待确认起点。Warning 证据持续成立时，Critical 附近的波动不会清空 Warning 起点。严重程度仍沿用已有仲裁规则。

确认使用 `LiveMeasurementSnapshot.SampleTimeNs`。重复时刻不累加时间；时间回退、超过 500 ms 的相邻观察间隔、无效质量及不可用数值清空该通道旧证据。该间隔限制沿用现有采集连续性要求，不能用未观测时间推算越界或恢复。失效后当前生理提示不再输出，但这不是患者恢复事件。本接口仍只返回提示投影，不提供事件身份、结束原因、不可判定事件记录、确认、保持或提醒策略。

`MeasuredLimitNotice.Descriptors` 继续只列出七个附加编辑器通道；专用 HR/SpO₂ 描述可通过 `Describe` 或相应描述属性获取，避免重复创建设置分类。

配置完整校验后才能改变过滤器状态；非法确认配置抛出 `ArgumentException`，保留之前的过滤器状态。有效的阈值、开关或时间变化会重新确认该通道。桌面编辑器在控件值变化时立即清空该通道证据，即使下一帧前又改回原值；无效草稿显示设置错误并清空该通道的提示状态。其他通道不受影响。会话重置、恢复配置不恢复活动状态。

## 本地保存格式

`DisplayPreferenceStore` 写入版本 6，保存 `Alarms.ConfirmationTimings`，不保存待确认起点或活动提示。版本 5–6 要求存在 `Generator` 字段，但允许其值为 `null`，与 `DisplayPreferences` 接口一致；非空值完整校验，字段丢失视为损坏。版本 1–5 继续按原必需字段规则读取，其中版本 4 仍要求 `Generator`。版本 5 原有七个通道的覆盖项继续保留；缺少 HR/SpO₂ 覆盖项时使用即时默认值。旧配置缺少确认覆盖项时使用上表默认值，原开关及阈值不变。

桌面只保存不同于通道默认值的覆盖项；恢复某参数的默认确认时间会清除此覆盖。编辑即时影响当前提示，点击应用后保存。时间控件显示秒，最多三位小数，往返保存保持毫秒精度。此接口尚不提供教学预设分层或独立导入/导出。
