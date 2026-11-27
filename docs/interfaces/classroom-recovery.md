# 课堂校时、连续性与恢复

[课堂网络](classroom-network.md) · [公开声明](api/classroom-recovery.md) · [权威恢复状态机](authority-identity-recovery.md)

## 同步与连接健康

学生可用 `ClassroomSynchronization.StartAsync` 指定必需通道并采集 16 个四时间戳探针，再以生命周期取消令牌运行 `MonitorAsync`。探针沿用已认证的 `/v1/clock`。前 30 秒每秒一次，之后每 5 秒一次，中间每秒确认控制连接。重锁、网络异常或估计超预算时重新进入快速采样。应用从后台回到前台可调用 `RequestRapidCalibration()`。估计器在最近 32 个样本中选低 RTT 样本，以成对斜率和截距的中位数估计漂移和偏移，报告 p95 RTT、p99 抖动和不确定度。呈现时间读取 `PresentationHostTimeNs()`，已有映射每秒最多修正 1 ms，完成重锁时建立新的映射。

共同时延取可用终端建议中的最大值。建议公式为 `p99 抖动 × 4 + p95 RTT / 2 + 100 ms`，默认范围 250–1500 ms。校准不足采用 500 ms。超预算终端单独请求重同步。教师可以指定固定值，将所选值冻结到课堂时间轴。

每次收到并验证波形块调用 `RecordBlock`，收到可信的运行／暂停通知调用 `RecordHeartbeat`。连接关闭、I/O 和 TLS 故障调用 `RecordFailure`。用 `HealthChanges` 读取最新状态，慢消费者不阻塞波形。静默达到 3 秒标记可疑，5 秒标记断连。运行中必需通道各自超过 1 秒无连续块标记停滞，2 秒标记该通道断连。暂停停止累计通道等待时间。重锁时以所有必需通道都已验证的边界块调用 `CompleteRelocking`。

## 同一教师实例的补传与重新加入

保留窗口内用已有学生连接及首个未交付游标调用 `ResumeAsync`。若游标已经移出窗口，可显式调用 `ResynchronizeWaveformsAsync`。正在运行的课堂支持新生通过 `JoinAtFutureBoundaryAsync` 从共同的未来边界加入。教师至少已发布一个块才能生成该快照。

| HTTPS GET | 用途 |
|---|---|
| `/v1/recovery/waveforms` | 仅接受已经移出同一流保留窗口的游标，显式报告遗漏区间。 |
| `/v1/recovery/join` | 为首次加入者生成快照和未来边界。 |

两个端点均带学生令牌，查询参数为 `from`（首个未交付块或清单位置）、`stream`、`timebase` 和 `history`（5–300 个 200 ms 块）。同一次取样包含学生清单、已验证的最后一个原始 MWS1 块、当前时间轴、最新数值和公开投影。教师选择至少 1 s 后的边界。SDK 校验会话、实例、epoch、时间、块序号与内容，接收完整的预卷并以 `RenderHistory` 返回连续、独立的渲染历史。实时播放环仍保留最近 50 块；长扫屏可按需重建最多 60 s 的渲染窗口。返回值的 `NextBlockSequence` 是首次应呈现的块，`Subscription` 由调用方释放；重同步期间也可取消。过期、错流或无法满足本次请求时返回 409，完整快照超过 1 MiB 返回 422。

### 已有呈现方案的学习者快照

应用持有已解析的 `ResolvedPresentationPlan@1` 时，可调用 `ConfigureRecoveryPresentation` 按学生登记该方案及两份规范 UTF-8 状态图像。数值图像是当前公开 NumericFrame 数组；投影图像是包含 `projections`、`control` 和 `activeConfiguration` 的对象，对应同一教师快照的实际内容。教师校验 schema 并逐项比较数据。应用在可见内容变更时更新图像。过期图像不会附加到新快照。

存在匹配图像时可以取得冻结的 `ClientResumeSnapshot@1` 描述符、方案和原始状态图像。描述符绑定方案的内容摘要、两份规范图像的 SHA-256、当前 commit／仿真时间和已保留的实际波形预卷摘要。可用历史为结束于清单 `NextBlockSequence` 的连续块。完整的 60 s 窗口为切换订阅预留 1 s 余量，相应缺口在随后的未来边界预卷中补足。普通重新加入时 SDK 从这段保留历史开始订阅，同时核验其 SHA-256，然后继续预卷到新的共同边界。换代 API 的 `ReadRecoverySnapshotAsync` 验证计划、文档和字节摘要，再通过已认证的波形流取回这一整段保留历史、核对 `waveform_preroll_sha256` 并关闭临时订阅。签发时 `prepared=false`，学习者完成校验才可提交 Prepared。尚未登记方案时，可以直接使用无界面的数值、控制、当前配置与波形快照。

## 权威 Host 恢复后的换代握手

已取得新权威 epoch 的应用调用 `ConfigureRecoveryAuthority`，提供其持久化的检查点序号、提交／事件前沿与当前学习者投影的状态摘要，以及独立重演离线日志的实现。每次安装必须提高 epoch。撤除权威会撤销进行中的握手。此接口供会话持久化和接管模块接入，常规运行中的单机波形缺口使用上一节。应用需为返场学生重新认证并分配相同会话、实例和学生身份。

| HTTPS 接口 | 输入与结果 |
|---|---|
| `POST /v1/recovery/hello?stream=S&from=N` | 请求体为规范 `RecoveryHello@1`。校验身份、旧流和旧权威 epoch、已提交前沿、瞬时输入清空声明。若需要指定的离线报告，返回 204，否则返回规范 `ResyncPlan@1`。 |
| `POST /v1/recovery/report` | 接收规范 `LocalContinuationReport@1` 并调用教师重演。经过状态与操作链摘要核对后才能得到 `CommitVerified`。相同的重试复用既有计划。 |
| `GET /v1/recovery/snapshots/{planId}` | 返回分配给当前学生的时间轴、公开投影、数值和最后一个 MWS1 块。SDK 核对计划中整个传输体的 SHA-256 和内层块身份。 |
| `POST /v1/recovery/prepared` | 提交 `RecoveryPreparedAcknowledgement`。首次准备确认必须在首个边界块发布前抵达。教师登记后暂返 204，首块发布后使用完全相同的确认重试。在既定计划、快照和预卷内容均匹配后，返回带边界首块内容 SHA-256 和权威提交序号的 `RecoveryCommitCommand`。 |

预卷摘要取按序连接的原始 MWS1 块字节的 SHA-256。准备／提交字段采用 camelCase，64 位整数输出为十进制字符串。Hello／Report／Plan 沿用冻结的 snake_case 规范编码。SDK 的准备和提交凭据可交给既有 `ClientRecoverySession`／`RecoveryRelockCoordinator` 推进客户端显示与权威状态。每个学生只保留一个有效握手，最多 36 个并发，计划保留两分钟。尚未配置权威时换代端点返回 503。

## 机构签名的离线投影

教师可按学生安装 `ClassroomContinuityReceiver`，向 `PublishContinuity` 提供已由机构系统签名的基础胶囊、增量或离线能力租约。学生端使用独立配置的受信公钥、会话、实例、branch、epoch、引擎／资源和组件能力，以及批准组件内容的策略建立自己的接收器。生产验签固定为 `SM2-SM3-RS64@1`。密钥来自部署侧，通道没有私钥或签发接口。

`ClassroomSignedContinuity` 传递规范签名 body 各字段、分离的 proof 和原始 payload 字节。payload 是冻结的 `ContinuityCapsuleBase@1`、`ContinuityCapsuleDelta@1` 或 `OfflineCapabilityLease@1` JSON 去掉 `signature` 属性后的原始 UTF-8 字节。签名的 `payload_sha256` 覆盖这些字节，校验时组合 proof 校验完整对象 schema。仅已声明且通过学习者内容检查的状态组件可进入胶囊链。已有链校验引擎与数学 profile、资源、通道、组件哈希、基础／增量引用和单调前沿。Lease 另行校验学生、branch、基础哈希、仿真有效边界和安全时钟期限。考试许可属于独立协议。

`GET /v1/recovery/continuity?after=N` 每次返回一个已授权的包。首次使用 N=0 从当前基础胶囊起步，之后使用上一次的 sequence。204 表示没有可交付的包，409 表示应用须从新的基础链重新同步，410 表示材料已过期。每个学生最多保留当前 Base 和 33 个后续项。学生 `ReadContinuityAsync` 在收到字节之后重新校验签名、时效、身份和状态，再返回可交给影子生成／课程逻辑的学习者状态。调用方提供当前仿真时间和由课堂时钟映射得到的权威单调时间。没有受信材料时只能使用已验证的缓存并在耗尽后表示无数据。
