# 普通课堂的设备动作与配置准备

教师为每个教学实例指定一个可操作的学生终端。其余学生可以查看同一份设备投影。教师本地调用和学生 HTTPS 调用都经过相同的动作目录、配置权限、修订检查和 SQLite 提交。动作目录与权威输入输出取自 [Domain.Contracts 清单](../../src/Monitor.Network/Contracts/contract-manifest.json)和[动作 schema](../../src/Monitor.Network/Contracts/actions.schema.json)。部署与入场见[课堂网络](classroom-network.md)，方法签名见[控制接口声明](api/classroom-control.md)。

## 操作流程

1. 教师在已认证的 `ClassroomHost` 上调用 `DesignateOperator(clientId)`。传入 `null` 收回操作权。学生每次根据要提交的动作 ID 获取 `ActionProjection@1`，检查 `availability` 和 `projection_revision`。
2. 学生提交 `LearnerActionIntent@1`。其中的会话、终端和实例必须匹配连接的凭据与目标实例。64 位整数用规范十进制字符串，数量使用明确的缩放和单位。`client_monotonic_ns` 仅用于审计。服务端决定动作的提交顺序和生效的仿真时刻。
3. 成功时 `LearnerActionCommitted@1` 包含动作 ID、更新后的投影修订、提交序号和仿真时间。设备更新与回执在同一 SQLite 事务内落盘。提交后 Host 发布 `action.learner.committed` 供该实例观察者读取。拒绝时回执提供机器可读的原因、字段路径和当前修订。
4. 应答丢失时可按 intent UUID 查询自己的回执，或原样重送请求。重复请求沿用原结果。相同幂等键搭配其他意图、参数或基线得到 `IDEMPOTENCY_CONFLICT`。收到 `STALE_PROJECTION` 时重新读取投影，由操作者确认新操作并创建新的 intent 和键。

| 方法与路径 | 目的 |
|---|---|
| `GET /v1/actions/projection/{actionId}` | 当前终端对于指定动作的权限、交互方式、参数 schema 和修订 |
| `POST /v1/actions` | 用学生 Bearer 凭据提交注册的设备操作 |
| `GET /v1/actions/results/{intentId}` | 读取本终端的已记录回执。未知 UUID 或其他终端的 UUID 返回 404 |
| `GET /v1/actions/state` | 带设备 profile、操作修订和权限配置修订的学习者可见状态 |
| `GET /v1/configuration/actions` | 读取当前待准备的权限修订或最近一次修订的终态 |
| `POST /v1/configuration/actions/prepared` | 用 `change_id` 和 `public_diff_sha256` 确认本机已取得的完整策略 |

例如，操作者从当前投影获得修订后可发送以下动作。以下 UUID 均为示例值；运行时须使用入场清单里的会话、终端和实例 ID。

```json
{
  "schema_id": "LearnerActionIntent@1",
  "intent_id": "33333333-3333-4333-8333-333333333333",
  "idempotency_key": "action.33333333-3333-4333-8333-333333333333",
  "session_id": "44444444-4444-4444-8444-444444444444",
  "client_id": "55555555-5555-4555-8555-555555555555",
  "instance_id": "11111111-1111-4111-8111-111111111111",
  "action_id": "therapy.defib.energy.set",
  "projection_revision": "8",
  "client_monotonic_ns": "9920000000",
  "control_id": "therapy.energy.selector",
  "skin_id": null,
  "parameters": {"energy":{"mantissa_i64":"200","exponent10":0,"unit_code":"JOULE"}}
}
```

格式、单位错误返回 400，身份与目标冒用返回 403。受理的业务意图通过 `accepted` 和 `rejection` 表达结果。观察者强行提交会收到 `OPERATOR_REQUIRED`。每个教学实例最多记录 65,536 个首次受理的意图和业务拒绝；到达上限后仍可查询及重试已有记录。

## 设备能力

目录覆盖 30 项注册动作。最终可用集合是当前设备实现、活动课程权限表及终端角色的交集。`ClassroomBedsideControls@1` 内置以下操作。

| 类别 | 已接入的动作与效果 |
|---|---|
| NIBP | 开始、取消、配置自动周期。使用 `NibpMeasurement` 的充气、放气、完成及读数过期规则。演示袖带目标为 120/80 mmHg、平均压 93 mmHg。完成测量后才向学生发布该读数。设备接受 1、2、3、5、10、15 分钟自动间隔。 |
| 显示 | 显式选取 ThreeRows、FourRows、FiveRows、SevenRows 布局，或 MonitorDarkGreen、PaperGridBlack 十二导联主题。结果进入共享设备投影。 |
| 报警音频 | 显式开始限时暂停、恢复。投影记录恢复的仿真纳秒边界。 |
| 除颤设置 | 从现有 `DefibrillatorConfiguration.Default` 的能量档中选择焦耳值，切换 Async 或 Sync。两者均只改变模拟设备设置。 |

宿主可在新实例开始操作前绑定 `IClassroomActionReducer` 实现其他已注册动作。实现必须是相同输入产生相同结果的状态变换，只返回本次应提交的学习者可见状态。事务提交前不得执行 I/O、播放设备效果或修改外部对象。未接入的动作投影为 Disabled。应用层须按实际状态开放动作并返回稳定的拒绝码。

PressHoldRelease 动作还要求设备实现 `IClassroomMomentaryActionReducer`，并在任一时刻只持有一个活动交互。学生端必须保持实例波形连接，续租和释放均携带同一 interaction ID。有效期为最近一次授予或续租后的一秒，由独立单调安全时钟执行。断连、收回操作权、会话取消、超时或宿主恢复时取消。绑定恢复的设备实现会先解除保存的交互，再接收新输入。

## 课堂配置排程

教师通过 `ScheduleConfiguration` 提交完整的 `ClassroomConfigurationContent`。其中包括动作权限集、可选的授课目标心率以及具名的学习者可见投影。演示生理源接受 40–180 bpm，空值恢复演示病例的默认心率。受支持的命名投影包括已按冻结 schema 校验的 `DisplayRoleResolutionProjection@1`。键 `display.settings` 可带 `{"schema_id":"ClassroomDisplaySettings@1","layout_id":"FourRows","ecg12_theme_id":"PaperGridBlack"}`，在相同边界切换布局和十二导联主题。

活动配置修订、所有待生效变更及最近八项终态从 `GET /v1/configuration` 取得。排队上限八项。响应的 `required_changes` 只列出当前终端须确认的 change ID 和自己已提交的状态。配置全文上限 8 KiB，最多八个具名投影。内容 SHA-256 输入为受支持字段的规范 JSON，按 ordinal 排列动作 ID、投影名、投影内对象属性，保留数组顺序。客户端通过 `ComputeHash()` 验证。

| 行为 | 生效条件 |
|---|---|
| RejectOnConflict | 有待生效变更或活动基线不匹配即拒绝。 |
| Queue | 后续变更显式基于它的前序配置修订，目标边界晚于队尾。 |
| ReplacePending | 取消原有等待队列，替换项必须以当前活动修订为基线；可从状态或事件流查看取消结果。 |
| 准备门 | 零 ACK 表示 BestEffortTeachingOnly，全部表示 RequirePreparedAll，中间值表示 RequirePreparedQuorum。可叠加教师放行，使用 `ConfirmConfiguration(changeId)`。 |

普通课堂配置的目标是至少提前一秒的未来 200 ms 块边界。教师可在生效前用 `CancelActionPolicy(changeId)` 取消变更及依赖它的后续变更。到达边界时若准备不足、教师尚未放行或基线失效，变更进入 Rejected。满足门槛时，Host 同时切换生理源、许可集和具名公有投影。MWS1 配置修订从下一块起改变，先前已发的样本保持原修订。晚到的学生通过当前配置和波形块修订识别版本。`configuration.change.scheduled` 与 `configuration.active.changed` 使用已注册的域事件。

学生通过 `POST /v1/configuration/ack` 或 SDK 的 `ReportConfigurationAckAsync` 提交以下阶段，均须匹配 change ID、公开内容摘要和 Required 成员身份。

| 阶段 | 携带的信息 |
|---|---|
| Received | 已读取并核验的内容摘要。 |
| Prepared | 已完成准备。重复同一确认不会增加票数。 |
| Applied | 实际观察到该修订的仿真纳秒和首个已交付的波形块序号。教师可读取与目标边界的偏差 `deviation_ns`。 |
| Rejected | 稳定原因码。拒绝的终端不会计入准备票。 |

命令行接收器使用 `ClassroomConfigurationObserver`，只对分配给当前终端并通过校验的内容上报 Received／Prepared。在实际输出对应的首块后，它返回本次命令行观察的时间和块序号。未来图形呈现端可在自己的显示回调中使用相同接口。`ScheduleActionPolicy`、`GetScheduledActionPolicyAsync` 和 `AcknowledgeActionPolicyAsync` 提供仅调整动作许可集的便捷路径，它们共享配置队列和持久化回执。

## 存储与实例隔离

设备状态、配置、操作权和意图回执按 session 与 instance 在 SQLite 中分开保存。状态与回执使用身份主密钥派生的 AES-GCM 保护，幂等键索引用带用途绑定的查找值。实例分叉时复制已提交的内置设备状态与活动配置，给新分支分配独立序列、回执和操作员；未完成的排程、活动瞬时交互及未经适配的设备 profile 会阻止该分叉。
