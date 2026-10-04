# 报警通知决策接口

`AlarmLifecycleJournal` 在确认器处理有效采样或中断时，同步生成独立的通知决策。生理事件和通知记录分别保留：真实发生、升级和恢复仍由确认器决定，重复通知抑制不能删除、延后或改写它们。

本接口输出可审查的通知意图，已提供显式选择的[单组声音执行通道](alarm-notification-sound.md)。产品默认仍使用原有最高等级持续声音链路；生成意图不等于已播放、被用户听见或已确认。“报警 → 通知策略”提供模式选择和按条件编辑，并通过版本 9 偏好保存；声音输出本身仍须手动启用。

## 配置契约

```csharp
AlarmNotificationPolicy NotificationPolicyFor(string conditionId);
void ConfigureNotifications(string conditionId, AlarmNotificationPolicy policy);
IReadOnlyList<AlarmNotificationRecord> NotificationRecords { get; }
ulong DroppedNotificationCount { get; }
```

这些成员属于 `AlarmLifecycleJournal`。条件 ID 沿用其已有注册，例如 `hr-low`、`hr-high`、`spo2-low`、`co2-no-expiration`。不允许通过配置创建未知条件。每个条件独立配置，运行时默认 `new AlarmNotificationPolicy(0, 0)` 表示单组意图；桌面按所存的默认方式与条件覆盖解析实际时长。

| 属性 | 含义 | 默认 |
| --- | --- | --- |
| RepeatSuppressionMilliseconds | 正常恢复后，同级或较低级别再次发生时，从此条件最近一次通知意图起算的抑制窗口 | 0，无额外抑制 |
| SoundDuration | SingleGroup（短警报）或 Continuous（长警报），独立于等级 | SingleGroup |
| ReminderMilliseconds | 条件持续活动时，从最近一次通知意图起算的提醒间隔 | 0，不产生额外周期意图 |

两个时间字段均为整数毫秒，范围 0–3600000；此上限是项目工程约束。配置完整校验后才发布，负数、超范围、null 和未知条件拒绝，不修改条件、既有决策或生效策略。时间范围不表示音频执行层已支持同样的声音组频率。Continuous 要求两个时间字段均为零；长警报直接跟随活动条件，不使用短警报的抑制或额外提醒。

修改策略只清空此条件的通知计时，不改变确认计时、事件身份和生命周期记录。若修改时条件已活动，下次有效观察产生 PolicyChanged 意图；重复配置相同值不重置。配置改回也不会恢复此前的抑制截止时间。策略作用于本地运行时 owner；偏好只保存下述配置，不保存计时、通知记录或活动事件，也不写入连续性 capsule。

## 设置与偏好映射

```csharp
public enum AlarmPlaybackMode { Continuous, Notifications }
public enum AlarmSoundMode { Inherit, SingleGroup, Continuous }
public enum AlarmSoundDuration { SingleGroup, Continuous }
public sealed record AlarmNotificationSettings(int RepeatSuppressionMilliseconds,
    bool ReminderEnabled, int ReminderMilliseconds)
{
    public AlarmSoundMode SoundMode { get; init; }
}
```

`AlarmNotificationSettings.Validate()` 要求重复抑制为 0–3600000 ms，提醒间隔为 1–3600000 ms（关闭时也须有效）。默认 `(0, false, 30000)`，SoundMode=Inherit。`ToPolicy(defaultMode)` 先解析此条件声音：Inherit 跟随全局 Continuous/Notifications，SingleGroup 和 Continuous 分别显式选择短或长；省略参数使用 Notifications，保持既有直接调用的单组默认。短警报关闭提醒时映射为运行时 `ReminderMilliseconds=0`；长警报映射为 Continuous 与两个零时间，偏好中的短警报配置仍保留。打开提醒与设置间隔分别控制，避免把零间隔当作循环频率。

`MonitorAlarmPreferences.PlaybackMode` 默认为 Continuous，仅作为没有独立覆盖条件的默认。Notifications 表示默认短警报。选择了 SingleGroup/Continuous 的条件不受全局默认更改影响；一个默认下可同时存在不同条件的短、长警报。`Notifications` 是以条件 ID 为键的可选覆盖字典；`NotificationConditionIds` 声明已有数值方向和未检出呼吸条件，`NotificationFor(id)` 对缺省项返回默认设置，对未知条件拒绝。SpO₂ 不接受上限条目。模式、字典和所有条目在保存及恢复前完整校验。

桌面“通知策略”分为“事件声音”和“默认声音”页签。“事件声音”选择器显示各条件当前有效的短／长声音及默认／独立来源；单选按钮直接选择跟随默认、短或长。短警报的重复抑制与提醒放入默认折叠的“短警报高级参数”，长警报仅保留“更多操作”中的恢复默认入口，不堆叠无关字段。有效修改立即发布到对应 journal，下一次采集证据决定新通知；其他条件和生理确认计时不受影响。无效草稿保留上次有效策略，切换条件保留草稿；任一草稿无效时禁止保存或重启应用。恢复条件默认值只作用于该条件的通知配置。切换声音时长不重置活动事件；切成长警报立即依据当前活动条件持续播放，切成短警报等待该条件下一次有效观察生成新策略意图。长警报恢复确认完成、禁用或数据中断即停止，不包含用户确认或保持功能。短警报相关字段在长模式下隐藏并保留输入值；若存在无效草稿，自动展开并允许修正原字段，选择器标记“待修正”且继续显示上次有效声音。切换页签和条件都不清除草稿。

`DisplayPreferenceStore` 写版本 9，版本 1–8 继续读取；版本 8 缺少 SoundMode 时按 Inherit 解析，保留原全局模式及通知字段；更旧文件缺少 PlaybackMode/Notifications 时使用 Continuous 和空覆盖字典，不启用原本关闭的报警或声音输出。点击现有“应用”保存配置；重新打开仅恢复模式与策略，不恢复事件、抑制截止、游标、请求或声音暂停。通知层之外的确认配置与迁移规则不变。

时长依据与厂商语义的边界见[短／长警报调研](../../research/alarms/alarm-sound-duration-research.md)。

## 决策规则

`AlarmNotificationDecision` 包含事件身份、已确认等级、采样时刻、类型、当次生效的不可变策略及剩余抑制纳秒数。`RequestsNotification` 为 false 仅表示 RepeatSuppressed，其余种类表示请求通知。

| Kind | 产生时机 |
| --- | --- |
| FirstOccurrence | 此 owner/条件首次确认，或配置、数据、时钟、会话中断后重新确认 |
| Recurrence | 正常恢复后再次确认，抑制窗口已结束，或等级高于最近一次通知等级 |
| RepeatSuppressed | 同级/较低级别在窗口内再次确认；只记录一次延后决定 |
| DeferredRepeat | 窗口到期且该事件仍活动；沿用事件身份 |
| SeverityEscalation | 同一事件等级确认升级；不受重复抑制影响 |
| Reminder | 活动事件满足独立提醒间隔，且没有尚待到期的重复抑制 |
| PolicyChanged | 活动期间修改策略后的首次有效观察 |

首次确认从不增加抑制延迟。较高等级首次出现及同一事件的确认升级均立即请求通知；降级本身不请求通知，之后再次确认升级仍会请求。较低等级或另一方向的重复抑制不会阻挡它。各参数、方向和不同 owner 不共享计时。

被抑制的事件若持续到窗口边界，恰好到期时请求一次通知；若提前恢复或中断，则取消待发意图。抑制期间不会由更短的 ReminderMilliseconds 绕过窗口。到期通知、升级或提醒都更新“最近一次通知意图”起点。

提醒只针对已确认且仍活动的条件，包括恢复确认期间。首次触发待确认状态不发通知。迟到的观察只产生当前一次提醒，不按错过的周期补发；下一周期从本次意图实际产生时刻起算。零提醒只禁用额外周期意图，不禁用条件或现有持续报警声音。

## 时间、记录与中断

数值决策使用确认器的采样时刻，CO₂ 未检出呼吸使用最后实际采集样本时刻；较新的界面刷新时间不能累积提醒。重复时刻不重复生成决策，暂停仿真不增加时间。时间回退、质量不可用、采集缺口和采集段变化沿用确认器的中断：清空抑制及提醒计时，恢复有效证据后重新确认并允许首次通知。

明确的禁用、配置失效和会话重置也清空通知计时，但保留运行时策略值及有界历史。只有正常恢复保留重复抑制所需的最近通知证据。记录不使用墙钟、音频渲染时钟或声音暂停的交互时钟。

`NotificationRecords` 是只读快照，最多保留 `AlarmLifecycleJournal.Capacity` 条；淘汰最旧记录时增加 `DroppedNotificationCount`。通知序号在 journal 内递增，独立于生理转换序号。重复通知和持续提醒不会制造额外 `AlarmLifecycleTransition`；身份作用域、有界记录及连续性限制见[生命周期接口](alarm-lifecycle.md)。

## 声音执行边界

`AlarmNotificationSoundRouter` 将当前有效意图合成为单组声音请求，支持最高等级仲裁、暂停期间丢弃与恢复重判；`MonitorAlarmSequencer` 依据独立请求序号执行，合并忙碌期间的同/低等级请求，允许更高等级打断。具体规则、记录及执行边界见[声音执行接口](alarm-notification-sound.md)。通知记录、软件排程记录与物理设备交付仍是不同事实，不将其冒充用户确认。
