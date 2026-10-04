# 报警确认配置接口

`ConfirmedLimitNotice` 为 ECG HR、SpO₂、RR · RESP、PR · PLETH、EtCO₂、RR · CO₂、ABP/PA/CVP 平均压提供连续越界及恢复确认。每个通道、每个实时会话持有一个实例；输入仅为实测快照、阈值与确认配置，不读取模拟预设或生成器目标值。`PressureLimitNotice` 保留原压力接口并委托给此实现。

## 配置与单位

`MeasurementConfirmationTiming` 包含 `CriticalLow`、`WarningLow`、`WarningHigh`、`CriticalHigh` 四个独立的 `BoundaryConfirmationTiming`。每个边界的 `TriggerMilliseconds`、`RecoveryMilliseconds` 均为整数毫秒，范围为 0–600000。0 在首个有效证据时刻确认，不代表禁用；范围上限是项目自行定义的工程约束。

`MonitorAlarmPreferences.ConfirmationTimings` 按 `MonitorNumeric` 保存覆盖项，`ConfirmationFor(numeric)` 返回生效配置。不支持的通道、空字典对象、空边界或超出范围的时间拒绝发布；空字典表示全部使用默认值。此字典支持上述九个数值通道；CO₂ 未检出呼吸使用独立的 `NoExpirationConfirmation`，不复用 CO₂ 呼吸率的数值边界。SpO₂ 只有下限报警：其阈值对象的两个上限必须为 `null`，确认时间对象中的两个上限必须为零；`ValidateFor(numeric)` 拒绝非零上限时间。桌面只展示两条下限控件。每个参数按“阈值”“触发确认”“恢复确认”页签分组，开关在页签外始终可用；EtCO₂ 另有“未检出呼吸”页签。切换页签复用同一批控件，不修改参数或重置报警计时。

| 通道 | 下限触发 | 上限触发 | 恢复 |
| --- | --- | --- | --- |
| ABP/PA/CVP 平均压 | 4000 ms | 10000 ms | 3000 ms |
| HR、RESP、PLETH、EtCO₂、CO₂ 呼吸率 | 0 ms | 0 ms | 0 ms |
| SpO₂ | 0 ms | 不支持 | 0 ms |

默认值分别应用于 Warning 和 Critical，保留原教学行为；独立覆盖后各边界互不借用计时。报警启用状态仍由原 `MeasurementLimits.Enabled` 或 `SpO2Enabled` 控制。HR 和 SpO₂ 的原提示 ID、文字、严格边界及零延迟行为保持不变。

## 时间与生命周期

比较使用测量原生整数单位，严格低于下限或高于上限为越界，等于阈值属于恢复范围。条件连续成立达到所配时间时触发，恢复连续成立达到所配时间时解除；任一过程被相反证据打断就清空该过程的待确认起点。Warning 证据持续成立时，Critical 附近的波动不会清空 Warning 起点。严重程度仍沿用已有仲裁规则。

确认使用 `LiveMeasurementSnapshot.SampleTimeNs`。重复时刻不累加时间；时间回退、超过 500 ms 的相邻观察间隔、无效质量及不可用数值清空该通道旧证据。该间隔限制沿用现有采集连续性要求，不能用未观测时间推算越界或恢复。失效后当前生理提示不再输出，但这不是患者恢复事件。提示返回值继续保持原投影契约；确认器的 `Lifecycle` 另提供本地事件身份、结束原因及不可判定转换，见[生命周期接口](alarm-lifecycle.md)。确认、保持和提醒策略尚未实现。

`MeasuredLimitNotice.Descriptors` 继续只列出七个附加编辑器通道；专用 HR/SpO₂ 描述可通过 `Describe` 或相应描述属性获取，避免重复创建设置分类。

配置完整校验后才能改变过滤器状态；非法确认配置抛出 `ArgumentException`，保留之前的过滤器状态。有效的阈值、开关或时间变化会重新确认该通道。桌面编辑器在控件值变化时立即清空该通道证据，即使下一帧前又改回原值；无效草稿显示设置错误并清空该通道的提示状态。其他通道不受影响。会话重置、恢复配置不恢复活动状态。

## CO₂ 未检出呼吸条件

`ConfirmedNoExpirationNotice.Evaluate(enabled, delaySeconds, nowNs, activity, timing)` 对 `NoExpirationNotice` 的原始条件增加确认。每个实时会话持有一个实例，和数值边界共用内部 `BoundaryConfirmation`。原静态入口保留瞬时语义；提示 ID、Critical 等级及关联的 `Co2RespirationRate` 数值保持不变。

`delaySeconds` 是 5–120 秒整数的呼吸等待时限，默认 20 秒。从 `ContinuousUsableSinceNs` 和最近一次已确认完整呼气 `LastExpirationNs` 中较晚者起算，`LastSampleNs` 达到等待时限（含相等）即成立。`MonitorAlarmPreferences.NoExpirationConfirmation` 为独立 `BoundaryConfirmationTiming`，额外触发和恢复时间默认均为 0 ms；缺失此属性按默认值读取，显式 null 或非法范围拒绝发布。

额外触发从首次观察到原始条件成立的采样时刻起算，不倒推之前未观察的时间。呼气恢复后原始条件不成立，开始恢复确认；期间再次达到呼吸等待时限会取消恢复。等待 20 秒、额外触发 2 秒表示先满足 20 秒检测条件，再积累 2 秒证据；实际输出还受采集和观察粒度影响。恢复时间可以长于等待时限，此时只有持续足够的呼气证据才能完成恢复。

确认仅使用 `CapnographyActivity.LastSampleNs`，不能以较新的 `nowNs` 延长证据。数值呼吸率过期不妨碍连续有效活动证据；状态无效、样本缺失或陈旧、非法活动时间、连续有效采集起点变化、采样/观察回退或大于 500 ms 的间隔清空旧状态。无效状态以中断原因结束活动事件并记录为不可判定，不被记录为患者恢复事件。禁用、修改检测或确认配置、恢复设置和会话重置也清空该条件的计时；不会改变其他数值报警。非法确认参数在改变状态前抛出异常，非法等待时限则沿用静默 Info 设置提示并清空旧证据。

桌面仍在 EtCO₂ 的“未检出呼吸”页签中显示开关及按可用宽度换行的字段：呼吸等待时限、额外触发确认、恢复确认。默认确认按钮只将后两项设为零；编辑即时生效，应用后保存。详细规则位于可搜索帮助，输入错误直接显示在该分组中。

## 本地保存格式

`DisplayPreferenceStore` 写入版本 7，保存 `Alarms.ConfirmationTimings` 和独立的 `Alarms.NoExpirationConfirmation`，不保存待确认起点或活动提示。版本 5–7 要求存在 `Generator` 字段，但允许其值为 `null`，与 `DisplayPreferences` 接口一致；非空值完整校验，字段丢失视为损坏。版本 1–6 继续按原必需字段规则读取，其中版本 4 仍要求非空 `Generator`。旧文件缺少确认覆盖项时使用对应默认值；缺少未检出呼吸确认属性时使用 0/0 ms。原开关、呼吸等待时限、数值阈值及已有覆盖项不变。

数值通道只保存不同于通道默认值的覆盖项；恢复某参数的默认确认时间会清除此覆盖。编辑即时影响当前提示，点击应用后保存。时间控件显示秒，最多三位小数，往返保存保持毫秒精度。此接口尚不提供教学预设分层或独立导入/导出。
