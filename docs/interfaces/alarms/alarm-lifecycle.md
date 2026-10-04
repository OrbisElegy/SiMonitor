# 本地报警生命周期接口

`ConfirmedLimitNotice.Lifecycle` 与 `ConfirmedNoExpirationNotice.Lifecycle` 暴露同一个 `AlarmLifecycleJournal` 类型。记录由确认器在处理测量证据时同步更新，桌面通过 `MonitorAlertSettings.AlarmLifecycles` 汇总读取；不是从横幅的轮换、声音请求或绘制次数反推事件。`PressureLimitNotice.Lifecycle` 转发底层确认器。

## 身份与条件边界

`AlarmEpisodeId(ConditionId, Occurrence)` 标识一个已确认的发生过程。`ConditionId` 沿用提示 ID，例如 `hr-low`、`hr-high`、`spo2-low`、`co2-no-expiration`。每个数值方向分别记录，SpO₂ 仅有低限；CO₂ 未检出呼吸独立于 CO₂ 呼吸率。某方向被更高优先级提示遮蔽时，仍保留真实状态与记录。

首次确认 Warning 或 Critical 才分配递增的 `Occurrence`。同一方向升级、降级、短暂恢复和恢复中断均保留身份。确认恢复或非恢复中断后再确认，分配新身份。序号作用域是持有确认器的本地 owner，重置不复用；不同 owner 或进程间不是全局唯一。外部保存或汇总若需全局身份，须另外绑定会话/实例上下文。

## 状态与记录

`Conditions` 返回不可变 `AlarmConditionSnapshot` 的只读副本。

| 状态 | 含义 |
| --- | --- |
| Unobserved | 尚未读取有效条件，或配置/会话已重置 |
| Disabled | 此条件关闭 |
| Normal | 当前有效证据没有已确认报警，也无待触发确认 |
| PendingTrigger | 条件成立，尚未完成首次触发确认；没有活动事件身份 |
| Active | 至少一个等级已确认 |
| PendingRecovery | 当前输出等级正在恢复确认，身份与已确认等级保留；完成后可能降级或结束 |
| Indeterminate | 质量、采集连续性或配置无法支持判定，不等同于 Normal |

Warning 活动期间等待 Critical 触发仍为 Active，不额外分配身份；每条等级边界的独立计时继续由原确认器负责。未完成的首次触发被正常证据打断只变为 Normal，不生成已恢复事件。

`Transitions` 返回只读的 `AlarmLifecycleTransition` 副本，包含 owner 内递增的 `Sequence`、时间、条件、事件身份、前后状态和级别、类型及原因：

- `Started`：首次确认；原因 `Confirmed`。
- `SeverityChanged`：同一事件等级确认升级或降级；原因 `Confirmed`。
- `Ended`：活动事件停止；只有有效恢复确认完成才使用 `Recovered`。记录保留结束事件的身份及旧等级，当前快照清空活动身份和等级。
- `StateChanged`：待确认、恢复待确认、中断后状态等变化。

非恢复结束原因分别为 `Disabled`、`ConfigurationChanged`、`InvalidConfiguration`、`DataUnavailable`、`ObservationGap`、`ClockRewind`、`SignalSegmentChanged` 和 `SessionReset`。数据无效时活动事件以相应中断原因结束，条件进入 Indeterminate；质量重新可用后重新确认并分配新身份。这样不会把未知间隔接成一段连续生理事件，也不把提示消失解释为患者恢复。

相同状态和等级的重复读取不追加记录；Indeterminate 的原因改变仍追加状态记录。每个 journal 最多保留 `Capacity = 256` 条最近转换，超过后丢弃最旧项并增加 `DroppedTransitionCount`；事件身份和序号不会因淘汰重用。该缓冲区不是永久审计存储，调用方必须检查丢失数量，不得把剩余片段当作完整历史。

## 时钟、编辑和重置

数值条件转换使用 `LiveMeasurementSnapshot.SampleTimeNs`；未检出呼吸的有效转换使用 `CapnographyActivity.LastSampleNs`，无法读取有效活动时用传入的观察时间记录不可判定。计时和质量规则见[确认配置接口](alarm-confirmation.md)。零确认时长允许同一个采样时刻先记录旧事件中断，再产生新的确认事件。

无参 `Reset()` 等价于 `Reset(SessionReset)`；重置重载只接受 SessionReset、ConfigurationChanged、InvalidConfiguration、Disabled，其他原因拒绝且不修改状态。无新采样的界面操作以最后已知采样/状态时刻记录，不虚构墙钟时间。时间回退后记录时间可以减小，应按 `Sequence` 读取 journal 顺序，不能仅按时间排序。

桌面修改开关、阈值或确认时间立即使相关条件失效，下一帧前改回也保留中断记录。关闭使用 Disabled，其他有效编辑使用 ConfigurationChanged，无效时间草稿使用 InvalidConfiguration。有效恢复偏好使用 ConfigurationChanged；成功从头开始使用 SessionReset。完整校验前拒绝的恢复或失败重启不会改动事件。确认器直接拒绝非法确认时间或负采样/观察时刻时，也保持原状态和历史。

## 与现有投影和连续性的关系

`MonitorNotice` 的字段、相等性和提示仲裁保持原契约；其 ID 对应条件，不能单独作为发生次数的身份。桌面横幅、高亮和声音继续使用已确认的提示。记录既有数值和未检出呼吸条件，不将测试提示、普通信息提示或技术故障提示自动注册为生理事件。

`ContinuityProjectionRevisions.AlarmEpisode` 目前仍是连续性合同中的修订号。本地 journal 不读写它，不修改 capsule、delta 或偏好 JSON 格式，也不通过偏好恢复活动事件。独立的重复抑制和持续提醒[通知决策层](alarm-notification-policy.md)已接入 journal，输出意图和独立有界记录；已提供显式选择的单组声音执行通道，产品默认仍为原持续声音；“通知策略”分组与偏好只保存模式及条件策略，运行时事件与记录不随之持久化。用户确认与保持尚未接入。跨 owner 的全局事件流、连续性 checkpoint、持久审计和用户事件浏览尚未接入。后续接入需明确版本与事件身份映射，不能直接把此有界运行时缓冲区当作连续性权威状态。
