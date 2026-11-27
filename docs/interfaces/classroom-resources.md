# 课堂配对、资源与私有练习分支

[课堂网络接口](classroom-network.md) · [公开声明](api/classroom-resources.md)

## 发现与配对

`ClassroomDiscovery.BrowseAsync` 在指定本机 IPv4 或 IPv6 接口向 `_simonitor._tcp.local.` 发送 DNS-SD PTR 查询，最多等待十秒并返回最多 64 个课堂。教师用 `AdvertiseAsync` 在该接口应答。DNS 消息携带公开显示名、HTTPS origin、协议主版本、安全标志、叶证书 SHA-256；教师为广播选择面向全班的名称。客户端用 SRV、TXT 与 PTR 记录关联结果。手动输入 HTTPS 地址适用于关闭组播的校园 VLAN。线格式依据 [RFC 6762 §6.7](https://www.rfc-editor.org/rfc/rfc6762#section-6.7) 和 [RFC 6763](https://www.rfc-editor.org/rfc/rfc6763)。

教师将课堂 ID、HTTPS 地址、当前叶证书的 SHA-256 和该学生的短期邀请码交给 `ClassroomPairingTicket.Create`。`Encode()` 返回可供二维码编码器或本地邀请文件使用的 `simonitor://join/v1#…` 文本。整个链接是一次性凭据。学生先 `Parse`，再调用 `JoinAsync(ticket, root)`；TLS 握手既检验证书链与 SAN，也匹配票据指纹。教师在兑换前检查票据的课堂 ID，一次私有配对不会消耗其他课堂的邀请。证书轮换或教师重开课堂后应发放新票据。

## 经教师发布的学习资源

教师调用 `PublishLearnerImage` 发布已批准的学习者 PNG，或用 `PublishStudyRecord` 发布本课堂当前实例的十二导联捕获记录。传入学生 ID 仅对该席位可见，省略则全班可见。同一内容按 SHA-256 去重，多次发布可追加席位授权。教师可调用 `RemoveArtifact` 撤下该内容；已经接收的副本由接收端持有。机构品牌包仍按学期预分发及批准流程安装，程序从课堂传输取得的文件只作为数据消费。

| HTTP 接口 | 语义 |
|---|---|
| `GET /v1/artifacts` | 当前令牌有权读取的描述符数组，按小写 SHA-256 排序。每项包含 `sha256`、`lengthBytes` 与 `mediaType`。 |
| `GET /v1/artifacts/{sha256}` | 当前席位获准的不可变字节。支持字节 Range、If-Range 和以 SHA-256 构成的强 ETag。未授权和不存在均为 404。 |
| `HEAD /v1/artifacts/{sha256}` | 与 GET 相同的授权和响应头。 |

单项上限 16 MiB，每个实例的活动目录最多 128 项、总计 64 MiB。每个实例同时传输最多 8 项。SDK 对响应的类型、长度、ETag、范围和最终整体摘要依次校验；中途 I/O 断开会从首个缺失字节继续，最多重试两次，完整校验成功后才交付字节。

`application/vnd.simonitor.waveform-record.v1` 使用 MSR1 封装。偏移 0 为四字节 `MSR1`，偏移 4、12、20、28 依次是小端 UInt64 的 sweep epoch 和小端 Int64 的采样锚点、记录起始、记录结束（不含）的仿真纳秒。偏移 36 起是按原导联顺序排列的 12 个 UUID（各 16 字节，网络字节序）。随后每个块由小端 Int32 字节长度和完整 MWS1 块组成。导出只接受块边界对齐的记录，仅保留被授权的十二个采集通道。下载端重建归档前检查课堂、实例、覆盖区间、顺序和逐导联网格。学生引用使用 `record:<内容哈希>`，教师本地的病例、答案、记录名称和备注均由教师侧保管。

## 脱离和重返共享演示

带有教师生成器的运行中课堂支持 `SchedulePrivateBranch`。教师指定已入场的终端，可显式选择未来的 200 ms 控制边界，默认选下一块边界。到点后教师复制当前生理生成器、采集相位、估计器历史、设备状态、数值投影和保留波形。新实例拥有独立 ID、内容哈希、令牌、发包队列、事件流和持久化控制作用域。分配成功后该学生成为私有实例的指定操作端。未完成的按住操作和待生效控制事务会阻止不安全的分叉。共享实例、活动和待建的私有实例合计上限为 40。

学生用原连接查询 `GET /v1/branch/assignment`。状态为 `Detached` 时，`ConnectAssignedBranchAsync` 调用 `POST /v1/branch/enter/{instanceId}` 领取给该终端的新邀请，在同一 HTTPS/WSS 端口重新配对、协商并接收私有波形。私有请求及 WSS 握手均带 `X-SiMonitor-Instance: <uuid>`。已脱离学生的共享数据请求返回 409；其原课堂令牌仍能查询分配、接入私有实例和续期。其他终端可持续订阅共享实例。

教师调用 `ScheduleSharedReturn`，明确选择 `Archive` 或 `Discard`。归档保存私有生成器最终检查点、设备状态、数值投影、保留波形及事件窗口；记录在教师本地 SQLite 中按课堂和私有实例绑定加密。原教师以后重新登录可用课堂 ID 和私有实例 ID 读取归档。丢弃会清理该实例的私有控制状态，停用后的标记用于防止重复接纳。原学生连接在返回后重新获取共享清单并重新协商，再用 `JoinAtFutureBoundaryAsync` 取得共享恢复快照和预卷。在该订阅的逐块 ACK 到达约定边界前，直接开实时流或提交设备操作会收到 412。共享患者始终沿自己的时间线推进。

教师移除座位会立即撤销共享和私有凭据并中止该座位的连接，私有实例在最后一个完整波形块处归档。若归档写入失败，教师收到提示，可再次移除同一座位以重试保存。冻结的实例在此期间停止采样并关闭网络访问。

`ForkPending`、`ArchivePending`、`DiscardPending` 表示下一边界待处理，`ForkRejected` 和 `ReturnRejected` 携带失败原因。返回失败时私有实例继续有效。教师可以使用 `GetPrivateTelemetry` 和 `GetPrivateControlSnapshot` 查看该学生的独立呈现状态，使用 `SchedulePrivateConfiguration` 给指定实例排程心率或配置变更。
