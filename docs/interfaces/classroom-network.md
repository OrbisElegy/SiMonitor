# 教师与学生课堂网络接口

普通教学中，教师进程运行共享仿真、设备操作权威和保留环。学生进程校时、接收数值与事件，并按共同的播放延迟呈现波形。教师可指定操作端，或让指定学生在独立分支中练习。所有接入能力都可在无图形界面时调用。

| 接口 | 说明 |
|---|---|
| [课堂控制与配置](classroom-control.md) | 设备操作、持久化回执、排程、准备和生效 ACK |
| [校时、连接健康与恢复](classroom-recovery.md) | 采样进度、晚加入、预卷、恢复握手及签名续算材料 |
| [配对、课堂资源与私有分支](classroom-resources.md) | 发现、配对票据、授权下载、分叉和归档 |
| [Monitor.Network 公开 API](api/network.md) | 教师、学生、会话与数据流入口 |

## 启动课堂

从仓库根目录构建 Release 配置。独立运行已构建的程序需要 .NET 10 和 ASP.NET Core 10 运行时。教师选择学生能访问的本机 IP，准备带私钥、有效 DNS 或 IP SAN 以及 Server Authentication EKU 的机构证书。两端校验 TLS 1.3、证书链和吊销状态，使用系统信任库或以 `SIMONITOR_CA_FILE` 指定受信根证书。部署中间证书并确保机构吊销服务可达。Windows 课堂网络需要 Windows 11 或 Windows Server 2022 及更高版本提供的 TLS 1.3。[Microsoft Schannel 支持表](https://learn.microsoft.com/en-us/windows/win32/secauthn/protocols-in-tls-ssl--schannel-ssp-)

以下命令假设教师机使用 `192.168.1.20`，其证书覆盖 `teacher.lab.example`，学生机能解析这个名称。将示例路径替换为机构保管的文件。数据库和邀请文件放在教师专属目录中，Windows 上用该目录的 ACL 限制访问。

1. 在教师机本地设置 `SIMONITOR_IDENTITY_KEY`（随机 32 字节经 Base64 编码）、`SIMONITOR_BOOTSTRAP_PASSWORD` 和 `SIMONITOR_HOST_PASSWORD`。保留数据库对应的密钥。首次初始化指定新管理员名和机构禁用密码表。表中每行是一个禁用密码的 UTF-8 字节的完整 SHA-256 十六进制摘要。初始化会核验新密码并永久停用初始 root 凭据。

   ```sh
   dotnet run --project src/Monitor.Network -c Release --no-build -- init classroom.db lab-admin blocked-passwords.sha256
   ```

2. 教师保留身份密钥及登录密码，启动监听并等待学生准备。以下示例发放两个座位，端口填 0 可由系统分配。可选的座位数范围为 1–36，默认 1。PFX 密码来自 `SIMONITOR_TLS_PASSWORD`。使用 PEM 时将命令中的证书改为 PEM 文件，以 `SIMONITOR_TLS_PEM_KEY` 指定私钥；使用 Windows 证书库时传入 `store:<thumbprint>`（CurrentUser/My）。

   ```sh
   dotnet run --project src/Monitor.Network -c Release --no-build -- teacher classroom.db lab-admin 192.168.1.20 8443 teacher.lab.example teacher.pfx invitations.txt 2
   ```

   命令最后可加 `--start`，表示教师明确要求无人值守启动。交互模式下学生入场后，教师输入 `status` 查看准备情况，再输入 `start`。教师端记录当时健康且已准备的端点和共同的播放延迟，并选择至少一秒后的开始边界。

   进程要求本地机构用户拥有 `deployment.manage` 或 `session.manage` 权限。邀请文件必须是新文件，Unix 下仅所有者可读写，Windows 下文件 ACL 仅授予当前教师账户访问权。每行依次为 HTTPS 地址、学生 UUID、一次性邀请码。教师私下将各自的地址和邀请码交给对应学生。每次启动产生新的课堂和实例 ID，旧邀请码及令牌随原进程退出失效。

3. 学生机设置自己的 `SIMONITOR_JOIN_CODE` 并启动接收。下面的命令打印会话 ID、学生 ID、校时往返延迟和十个连续块的序号、仿真时间与通道数。它同时读取权威数值、监听配置、上报已交付块与时钟质量。省略数量可持续接收。教师和学生都可用 Ctrl+C 退出。

   ```sh
   dotnet run --project src/Monitor.Network -c Release --no-build -- student https://teacher.lab.example:8443 10
   ```

## 课堂控制台

| 使用者 | 输入 | 效果 |
|---|---|---|
| 教师 | `status`、`start`、`pause`、`resume`、`speed 3/2` | 查询准备名单或在未来块边界控制全班时间轴 |
| 教师 | `operator UUID`、`operator off` | 指定共享设备的操作端或撤回权限 |
| 教师 | `rate 120 4` | 发布四秒后生效的心率配置，等待所需端点确认 |
| 教师 | `schedule 4 ID1,ID2`、`cancel CHANGE_UUID` | 排程或取消学习者可用动作清单 |
| 教师 | `fork UUID`、`rate 90 4 UUID`、`return UUID Archive` 或 `return UUID Discard` | 在安全边界分出独立实例、修改私有实例、选择归档或丢弃后回到共享课堂 |
| 教师 | `invite FILE [UUID]`、`pair FILE UUID` | 为新座位或原座位写入私密邀请文件；pair 文件内是可编码成二维码的配对票据 |
| 教师 | `close`、`reopen [SECONDS]`、`remove UUID`、`end` | 关闭或限时重新开放入场、撤销座位、结束课堂并撤销会话 epoch |
| 教师／指定操作端 | `act ACTION_ID JSON` | 提交带唯一幂等键和投影修订的设备操作，输出提交结果或结构化拒绝 |
| 学生 | `status`、`artifacts`、`download SHA256 FILE` | 读取设备状态、列出获授权资源或将校验后的资源写入新文件 |

例如 `act therapy.defib.energy.set {"energy":{"mantissa_i64":"200","exponent10":0,"unit_code":"JOULE"}}` 修改课堂模拟设备的能量选择。教师机可设置 `SIMONITOR_DISCOVERY_NAME` 启用指定接口的 DNS-SD 公告。学生可执行 `discover INTERFACE_IP` 搜索，或将私下领取的票据置于 `SIMONITOR_PAIRING_TICKET` 后运行 `student ticket`。机构根信任和主机名验证同样适用于发现和票据连接。

## HTTPS 控制接口

控制端点均使用同一 HTTPS origin。入场后以 `Authorization: Bearer <token>` 发送凭据。原始调用者先获取座位绑定，再协商协议，随后才可读课堂数据。SDK 在 `JoinAsync` 内完成这些步骤。每个学生最多占用一个活动波形订阅，只有指定操作端能改变共享设备。教师命令由已通过本地机构身份验证的 Host 调用。

| 请求 | 协议行为 |
|---|---|
| `POST /v1/join` | 提交 `{"protocolVersion":1,"joinCode":"…"}`。配对票据还校验课堂 ID。无效、过期、重用或超过尝试额度的码返回 401。 |
| `GET /v1/session` | 取得学生、课堂、实例、epoch，以及保留环最老和下一个块的位置。 |
| `POST /v1/hello` | 核对稳定座位、协议和 Domain.Contracts 版本指纹、raw-int16 采样格式，协商可选的逐 plane LZ4。协商结果分别绑定 cohort、课堂、实例和学生。图形客户端还可提交完整 ClientHello@1，核对所需资源、输入能力和实际可用 viewport；无头接收端单独声明。 |
| `GET /v1/clock?t1=N` | 教师回传单调纳秒 t1、t2、t3，学生记录 t4。初次校时和运行中采样均使用同一 Host 单调时钟。 |
| `POST /v1/ready`、`POST /v1/progress`、`POST /v1/progress/receiver` | 报告准备、图形 ClientProgress@1 或无头接收进度。教师用保留块摘要核对接收进度。 |
| `GET /v1/telemetry` | 读取时间轴、待生效时间轴、权威数值、当前学习者可见投影和设备配置，以及恢复事件读取所需的游标。 |
| `GET /v1/events?from=N` | 按序长轮询学习者可见的 DomainEventEnvelope@1。每次最多 12 个，事件日志上限为 1024 条且不超过 4 MiB。超窗返回 409，客户端读取当前状态并重新定位。 |
| `POST /v1/refresh` | 在允许的续期窗口换取新令牌，每个旧令牌最多产生一个新令牌，同一续期请求重试可取回原回复。默认邀请码五分钟、一次兑换，令牌一小时，最后十分钟可续期。 |

`ClassroomAdmissionPolicy` 可在有界范围内调整码长、有效期及整班／来源失败额度。`close` 立即废止未兑换邀请；`reopen` 只接纳新邀请码。`end` 递增 epoch、撤销所有凭据并关闭活动连接。

自建客户端可从 `ClassroomWire.ProtocolId`、`ProtocolVersion`、`ProtocolSha256` 和 `DomainContractsSha256` 取得协商所需的标识、版本与摘要。协议指纹取自随程序发布的[课堂协议清单](../../src/Monitor.Network/Contracts/classroom-protocol.json)，其中登记了波形布局摘要和可选的压缩编码。

Domain.Contracts 1.0.0 的领域消息使用 UTF-8、snake_case 和十进制字符串表示 64 位整数。通用协议的枚举、宽度和必填字段由随程序发布的[冻结协议清单](../../src/Monitor.Network/Contracts/contract-manifest.json)校验。数值来自教师端的波形测量，每一项携带源、缩放、单位和质量；采集未就绪或不可用时 quantity 为 null。学生通过同一事件序号读取相同内容。网络健康与患者测量质量独立报告。

## WSS 波形接口

`GET /v1/waveforms?from=N` 升级 WebSocket，子协议为 `simonitor.waveforms.v1`。握手使用同一 Bearer 令牌。N 是首个未交付块。省略 N 从注册订阅时的实时位置开始。已协商压缩的接收端可以添加 `codec=lz4`，同一教学块的 raw/LZ4 形式具有相同的内容摘要。

- 每个二进制消息携带一个 200 ms 的 MWS1 块。客户端先完成分片、验证 CRC、内容摘要、实例、epoch、序号和时间，再把规范 raw 消息写入本机 50 块缓冲。ACK 为 48 字节，依次是小端流 epoch、小端块序号及 32 字节内容 SHA-256。
- 教师保留最近 300 块，每个学生独立排队 50 块，发送和 ACK 期限五秒。落后学生可以独立重同步。逻辑负载达到 1024 字节且同时节省至少 128 字节和 12.5% 时，才传压缩形式。[MWS1 采集契约](simulation.md)
- `ResumeAsync` 从未交付游标回放。超出保留窗时，命令行学生明确报告缺口，验证快照和预卷，再从约定边界继续。样本、一次性事件与当前设备状态分别恢复。[恢复协议](classroom-recovery.md)

网络层为后续教师与学生图形界面提供状态、时钟、控制和订阅接口。本页对应普通教学；正式考试的名册、主备复制和见证部署由考试契约另行覆盖。
