# 报警确认与保持状态接口

## 范围与参考

`AlarmLifecycleJournal.Attention` 提供独立的运行时提示状态和确认记录，复用现有条件及 owner 内的 `AlarmEpisodeId`。生理事件仍由触发／恢复确认器决定；确认操作不修改测量值、生理事件、通知意图或触发／恢复计时。

参考 [Philips PIC iX Release 4.4 使用说明书](https://www.documents.philips.com/assets/Instruction%20for%20Use/20250321/d80393498eb1407e8912b2a7015f378e.pdf?feed=ifu_docs_feed)，文档号 4536 650 29231，2025 年 2 月第一版，印刷页 6-7～6-10：确认、条件恢复、指示保持和声音提醒是不同机制，具体行为依报警类型、来源和配置而异。该资料不能推出所有条件统一的保持时间或暂停即确认规则。

本接口使用项目自己的教学规则：默认非保持；可按条件选择恢复后保留未确认提示；确认仅针对一个 owner 中的一个事件版本。升级重新要求确认、再次发生替换旧保持提示、数据中断清除提示均为本项目明确选择，不宣称逐项复现厂商规则。

本阶段实现状态与命令契约，尚未将确认状态用于声音仲裁、横幅保持或桌面确认按钮。当前声音暂停不生成确认记录；也不把通知、软件渲染结束或设备输出当作用户确认。保持配置和运行时状态均未纳入偏好保存或连续性恢复。

## 入口与快照

```csharp
public AlarmAttentionJournal AlarmLifecycleJournal.Attention { get; }
public IReadOnlyList<AlarmAttentionSnapshot> AlarmAttentionJournal.Conditions { get; }
public IReadOnlyList<AlarmAttentionRecord> AlarmAttentionJournal.Records { get; }
public void Configure(string conditionId, AlarmLatchingMode mode);
public bool Acknowledge(AlarmEpisodeId episode, ulong expectedRevision);
```

`AlarmAttentionSnapshot` 包含条件 ID、该条件内单调递增的 `Revision`、事件身份、当前指示等级、提示状态和保持策略。快照与记录均为只读副本，不随后续运行修改。

| 状态 | 语义 |
| --- | --- |
| None | 无提示身份和等级，无待确认提示 |
| ActiveUnacknowledged | 生理事件仍活动，尚待确认 |
| ActiveAcknowledged | 生理事件仍活动，已明确确认 |
| RecoveredUnacknowledged | 生理事件已经正常恢复，仅保留尚未确认的提示身份及恢复前等级 |

`NeedsAcknowledgement` 仅在 ActiveUnacknowledged 和 RecoveredUnacknowledged 时为 true。PendingTrigger 没有已确认生理事件，不产生可确认提示。PendingRecovery 仍保留活动提示，确认不会提前完成恢复。

## 转换与策略

- 首次确认产生 ActiveUnacknowledged。重复采样或相同状态刷新不增加记录或修订。
- 确认活动提示变为 ActiveAcknowledged；其生理身份、等级和确认器状态保持不变。降级保留已确认状态；任何已确认等级升级重新要求确认，同一生理身份继续沿用。
- 默认 NonLatching：正常恢复即清除提示。UntilAcknowledged：未确认时恢复进入 RecoveredUnacknowledged；恢复前已经确认则直接清除。
- 确认恢复后保持的提示会清除该提示。每个条件只保留一个当前指示；若再次发生，新生理身份以 Replaced 记录替换旧保持指示，新事件重新要求确认。记录保留替换前后的身份，不把多次发生合成同一个生理事件。
- 禁用、配置失效、质量不可用、采集缺口、时间回退、采集段变化和会话重置清除活动或已恢复保持的提示，记录 Interrupted 及真实原因，不写成正常恢复。后续重新确认分配新身份；保持策略本身不因中断丢失。
- Configure 完整检查条件和枚举后修改。启用保持不会复活已结束事件；关闭保持会以 PolicyChanged 清除已有恢复保持提示。活动条件的保持策略变化不重置生理事件或已有确认，但更新提示修订。相同策略重复应用不产生记录。

## 确认命令与时序

调用方必须把用户所见快照的 `Episode` 和 `Revision` 原样传给同一个 owner 的 journal。事件被替换、恢复、升级、降级、策略改变或已经确认后，旧修订不能继续确认；此时返回 false，不生成成功确认记录。null 和未知条件抛出参数异常，状态与历史均保留。

身份只在 owner 内有效；不同 owner 可能产生数值相同的身份和修订，因此调用方还必须保持 owner 绑定。本阶段不提供跨 owner 全局身份、批量确认或远程命令凭据。

操作与观察需串行调用。观察使用确认器的采集／仿真时间；无新采样的 Configure 和 Acknowledge 使用本 journal 最后观察时间，默认初始值为零，不采集墙钟或虚构新证据。CO₂ 未检出呼吸沿用实际采集时间。相同时间允许多条有序操作，时间回退后仍按记录 Sequence 确定顺序。

## 审查记录与后续接入

`AlarmAttentionRecord` 包含 owner 内递增 Sequence、SampleTimeNs、Kind、Previous、Current 和生理转换原因。Kind 区分 Started、Replaced、SeverityChanged、Acknowledged、Recovered、Interrupted 和 PolicyChanged。记录包含操作时生效的保持策略；确认行为只能由 Acknowledge 明确产生。

最多保留 `Capacity = 256` 条最近记录；淘汰时递增 `DroppedRecordCount`。序号、条件修订和生理发生身份不因淘汰或重置复用。这是有界本地运行记录，不是持久审计或完整历史。

后续声光接入必须使用同一提示快照，并明确已确认活动条件的提醒、恢复后保持声音、混合优先级及暂停／恢复行为；不能仅删除当前声音请求而留下过期通知可重播。配置 UI、保存和用户事件浏览也须与该契约同步开放。
