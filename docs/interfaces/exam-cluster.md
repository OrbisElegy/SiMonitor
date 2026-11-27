# 正式考试集群

一个考试集群有三个独立进程。两台教师机分别保存答案、控制事务、检查点与审计的本地副本。第三节点只保存集群标识、租约持有者、权威纪元（authority epoch）、fencing token、单调到期时间和修订。初始主节点由配置指定，之后的写权限取决于见证租约。考试应用可通过该传输提交事务；学生入场、当前题离线封存与评分遵循各自的考试应用契约。

## 节点与机构材料

| 节点 | 持久状态 | 开放方向 |
|---|---|---|
| Primary、Standby | 各自本地磁盘上的 SQLite 数据库及 WAL，受保护的数据密钥环 | 两台 Host 之间双向 mTLS。每台 Host 可向见证发起 mTLS 请求 |
| Witness | 单节点本地数据库及同目录的 `.watermark` 修订文件 | 仅接受两台登记 Host 的租约读写 |

两台教师机分别使用独立物理设备。见证机位于第三个电源和网络故障域。信息科为每个节点配置固定 DNS 名、端口、防火墙白名单、准确的系统时间，以及具有对应 DNS SAN 的机构叶证书和私钥。三张证书均须具有 Server Authentication 与 Client Authentication 两种 EKU，并能用配置的受信 CA 验证到根。每个成员有独立的叶证书 SHA-256 指纹。集群地址使用 DNS HTTPS origin，启动时使用明确配置的节点名单。

程序支持机构标准 TLS 1.3 档 `TLS13-InstitutionCA-Standard@1`。配置其他传输档会在开始监听前失败，包括当前未接入的 RFC 8998 档。证书和密钥由机构签发、分发和轮换。将中间证书及吊销资料部署到服务账户可访问的证书环境。`revocation: "Online"` 使用在线吊销检查；离线考场选择 `"Offline"` 前，须把当前有效的吊销资料预置到各节点的系统缓存。过期、吊销、缺少有效链、指纹或 SAN 不匹配均拒绝连接。

## 节点配置

同一 DNS 名同时发布 IPv4 和 IPv6 地址时，Host 并行建立两个地址族的 TCP 连接。首个连通的地址承载 mTLS，证书仍须匹配配置的 DNS 名、节点指纹和机构信任链。节点启动时的只读探测可以等待首次连接；一旦持有租约，续租、写入和接管仍受单调时钟期限约束。

以下 JSON 为 Primary 的配置模板。用现场的 DNS、接口地址、端口、文件以及三张叶证书的完整 SHA-256 指纹（64 个十六进制字符） 替换示例。每台机器使用同一 `clusterId` 和成员表。将另外两份配置的 `nodeId` 改为各自的成员 ID，并指定本地接口、数据库和证书。Standby 配置自身的 `dataKeyRing`，Witness 省略该字段。节点 JSON 中的相对文件路径从该 JSON 所在目录解析。

```json
{
  "clusterId": "70e74833-817b-4963-a180-b4c942831b78",
  "nodeId": "f2259e11-3f85-4e6e-94a9-4ffcad6fb511",
  "listenAddress": "192.0.2.11",
  "database": "data/primary.db",
  "certificate": "certs/primary.pfx",
  "trustAnchor": "certs/exam-root.cer",
  "dataKeyRing": "keys/primary-ring.json",
  "tlsProfile": "TLS13-InstitutionCA-Standard@1",
  "revocation": "Online",
  "primary": {
    "id": "f2259e11-3f85-4e6e-94a9-4ffcad6fb511",
    "origin": "https://exam-a.example.edu:8443",
    "certificateSha256": ["<PRIMARY_LEAF_SHA256>"]
  },
  "standby": {
    "id": "d26ce739-d050-4ee0-aec4-02e31cf6db37",
    "origin": "https://exam-b.example.edu:8443",
    "certificateSha256": ["<STANDBY_LEAF_SHA256>"]
  },
  "witness": {
    "id": "0009e03d-cc25-4ef0-b215-5cd2e7676010",
    "origin": "https://exam-w.example.edu:8443",
    "certificateSha256": ["<WITNESS_LEAF_SHA256>"]
  }
}
```

`certificate` 接受 PFX 文件、PEM 叶证书或 Windows `store:<thumbprint>` 引用，证书库存放在服务账户的 CurrentUser/My。PFX 密码从 `SIMONITOR_TLS_PASSWORD` 读取。使用 PEM 时通过 `SIMONITOR_TLS_PEM_KEY` 指定私钥，已加密的私钥同样使用该密码。另行限制配置、私钥及数据库目录的访问权。数据库使用节点专属本地磁盘，禁止把数据库和 WAL 放在 SMB、NAS 或由多个节点同时挂载的盘上。

每个成员可登记一个或两个证书指纹。轮换时先向所有节点发布包含新旧指纹的名单，再按角色重启进程加载配置并切换叶证书，核对 mTLS 及角色资格后退出旧指纹。机构档要求至少 24 小时轮换重叠，程序在本地传输证书距到期少于 30 天时输出提示。

## Host 数据密钥环

用机构 RSA 包装证书的公钥分别加密 32 字节的稳定查找密钥和 32 字节的各版本负载密钥，算法为 RSA-OAEP-SHA-256。每个 JSON 二进制字段都是对应密文的 Base64。两台 Host 可持有针对各自包装证书加密的密钥环副本。包装证书须有本机账户可用的 RSA 私钥，最小 2048 位；来源为 PFX 或 CurrentUser/My，PFX 密码通过 `SIMONITOR_EXAM_KEY_PASSWORD` 提供。文件路径相对密钥环 JSON 解析。[密钥环实现](../../src/Monitor.Network/ExamDataKeyRing.cs)

```json
{
  "certificate": "store:<WRAPPING_CERTIFICATE_THUMBPRINT>",
  "active": "cc27cffd-a315-48d8-9269-87282632a7bc",
  "lookupKey": "<BASE64_WRAPPED_LOOKUP_KEY>",
  "keys": [
    {
      "id": "cc27cffd-a315-48d8-9269-87282632a7bc",
      "ciphertext": "<BASE64_WRAPPED_PAYLOAD_KEY>"
    }
  ]
}
```

`active` 必须引用列表中的一个 UUID，可配置 1–16 个唯一版本。新记录使用活动密钥经 AES-GCM 保护，旧记录和备份按其中的密钥版本读取。轮换时保留原查找密钥及读取既存数据所需的所有负载密钥；重新启动 Host 以加载更新的环。见证机无需任何数据密钥。

## 初始化与启动

从仓库根目录构建 Release 配置，在各自节点创建配置引用的目录，并由相应服务账户运行以下命令。独立程序需要 .NET 10 和 ASP.NET Core 10 运行时。每个节点只对全新数据库执行一次 `provision`。

```sh
dotnet src/Monitor.Network/bin/Release/net10.0/Monitor.Network.dll exam provision witness.json
dotnet src/Monitor.Network/bin/Release/net10.0/Monitor.Network.dll exam provision standby.json
dotnet src/Monitor.Network/bin/Release/net10.0/Monitor.Network.dll exam provision primary.json
```

在三个独立终端启动对应角色。以下示例在两台 Host 启用本地管理控制台。管理员须通过 FormalExam 机构认证并拥有 `deployment.manage` 权限。使用本机已初始化的身份数据库，在服务进程环境中提供其 `SIMONITOR_IDENTITY_KEY` 和账户的 `SIMONITOR_HOST_PASSWORD`。首次建立身份数据库的 `init` 命令、密码表与密钥格式见[本地身份初始化](classroom-network.md#启动课堂)。省略身份数据库和账户参数可将 Host 作为后台服务运行。

```sh
dotnet src/Monitor.Network/bin/Release/net10.0/Monitor.Network.dll exam serve witness.json
dotnet src/Monitor.Network/bin/Release/net10.0/Monitor.Network.dll exam serve standby.json identity-b.db exam-admin
dotnet src/Monitor.Network/bin/Release/net10.0/Monitor.Network.dll exam serve primary.json identity-a.db exam-admin
```

首次部署时，初始 Primary 获取首个租约。初始 Standby 核验与 Primary 相同的事务前沿后取得接管资格。在两个 Host 的本地控制台分别输入 `status`。主节点应报告 `role=Primary`、`leaseValid=true`；备节点应报告 `role=Standby`、`qualified=true`，两边的 `head.sequence` 和 `head.hash` 应一致。管理命令每行返回 JSON；`ok` 表示该命令是否成功，`result` 携带结果，`reason` 说明失败原因。

## 在考试应用中运行 Host

考试应用可引用 `Monitor.Network`，在每台教师机自己的进程内启动一个 `ExamClusterService`。先用 `exam provision` 初始化该节点数据库。嵌入服务和 `exam serve` 使用同一个数据库锁，每个节点只选择一个入口。见证继续在第三节点运行 `exam serve witness.json`。[公开接口](api/network.md#examclusterservicecs)

```csharp
await using var service = await ExamClusterService.StartAsync(configurationPath, cancellationToken);
```

传给 `StartAsync` 的取消令牌控制整个 Host 生命周期。应用在考试期间持有该实例。取消生命周期令牌或调用 `DisposeAsync()` 会启动关闭。`Completion` 在监听停止、在途调用退出并释放本地数据库后完成。`GetStatus()` 返回本机角色、租约和前沿。只有 `Role == "Primary"` 且 `LeaseValid` 的节点能受理提交。读取状态与写入之间可能发生换代，调用者将刚读到的 epoch 与 token 一并交给服务核对。

```csharp
static Task<ExamReplicaCursor> SubmitAnswerAsync(ExamClusterService service, Guid requestId,
    byte[] answerBytes, CancellationToken cancellationToken)
{
    var status = service.GetStatus();
    if (status.Role != "Primary" || !status.LeaseValid)
    {
        throw new InvalidOperationException("当前节点尚未取得有效主租约。");
    }
    var command = new ExamReplicaCommand(requestId, "Answer", answerBytes);
    return service.SubmitAsync(status.AuthorityEpoch, status.FencingToken, command, cancellationToken);
}
```

由考试应用持久保存每次逻辑提交的 `requestId`、类型和原始字节。调用成功后保存返回的 `Sequence` 和 `Hash`。发生超时或连接中断时，应用可向当前主节点重送同一个请求。两台 Host 均可用 `ReadCommitted(sequence)` 读取本地已提交事务。该位置尚不可读时返回 `null`。状态中的 `Pending` 表示本地已准备的单笔事务，最终确认以 `SubmitAsync` 的双端持久化回执为准。

宿主应用掌管调用者的机构身份、考生与题目绑定、答案访问和提交权限，在调用这些本地接口前完成授权。节点之间的 mTLS 认证用于复制与见证。人工隔离、双机冷恢复及重新授予备机资格仍从具名管理员的本地控制台执行，并重新核验该管理员身份。

## 写入、复制与查询

从主节点 `status` 输出取得当前 `authorityEpoch` 和 `fencingToken`。若首个租约两值均为 1，以下命令提交 UTF-8 内容 `example` 作为演练用控制事务，随后读取第一个已提交条目。

```text
write 1 1 dcec2963-eeb8-42f0-9406-3e33c0ef0215 Control ZXhhbXBsZQ==
read 1
```

`write EPOCH TOKEN REQUEST_UUID KIND BASE64` 和 `write-file EPOCH TOKEN REQUEST_UUID KIND FILE` 接收 `Answer`、`Control`、`Audit` 或 `Checkpoint`；原始负载为 1 字节至 1 MiB。对答案、控制和审计事务，应用提供原始负载；考试集群保留其字节并验证事务顺序和哈希。波形检查点 `ExamWaveformCheckpoint@1` 需使用完整 200 ms 块边界上的生成器状态和下一块内容摘要；备节点独立恢复生成器、重演下一块并对比摘要。波形采样流保存在仿真与呈现路径。[事务格式](../../src/Monitor.Network/ExamReplicaTransaction.cs) · [检查点格式](../../src/Monitor.Network/ExamReplicaCheckpoint.cs)

Primary 先在自己的 SQLite WAL 以 `synchronous=FULL` 持久化准备记录。Standby 校验集群、当前租约、序号、前一条哈希、内容哈希及检查点后，在自己的 WAL 持久化事务，返回 `sequence` 和 `hash`。Primary 核对回执、确认本地前沿并再次验证租约后才向调用方报告成功。保存成功的前沿供故障后核对。遇到超时、连接中断或进程切换，同内容原样使用同一 request UUID 重试；复用 UUID 但更换内容会被拒绝。单节点不可用期间不产生双端持久化回执。如果续租中断导致任期变化，原主须先核对备机在新任期报告的持久前沿。备机已有同一准备记录时按原哈希确认，两端都未确认该记录时才能释放旧准备并在新任期重试。

| mTLS 接口 | 行为 |
|---|---|
| `GET /v1/exam/peer/status` | 对端 Host 查看节点身份、角色、租约、资格和已提交前沿 |
| `POST /v1/exam/peer/replicate` | Primary 提交带当前 epoch/token 的版本化事务，Standby 返回持久化游标 |
| `GET /v1/exam/peer/entries/{sequence}?epoch=E&token=T` | 从持有对应租约的 Primary 拉取已提交条目，供恢复节点追赶 |
| `GET /v1/exam/witness` | 已认证 Host 读取租约和剩余毫秒。Witness 时钟使用本机单调时间 |
| `POST /v1/exam/witness/acquire`、`POST /v1/exam/witness/renew` | 用 `clusterId`、`expectedRevision`、`authorityEpoch` 和 `fencingToken` 做持久化 CAS。持有者由 mTLS 身份确定；成功返回 200，租约或修订冲突返回 409 和当前租约 |

## 故障切换、重新授予资格与恢复

| 场景 | 运行行为与操作 |
|---|---|
| Primary 失联、备机已核对前沿、见证可用 | 租约有效期 3000 ms，Host 每 500 ms 轮询和续约。旧租约到期后，合格备机确认旧主失联，向见证申请更高的 epoch 与 token。恢复双机持久化后，新主才可成功答复写入。 |
| Primary 与 Standby 之间分区或备机落盘失败 | 当前主的单机持久化保留为待确认记录。调用方没有成功回执，原样使用同一 request UUID 重试。旧主、旧纪元或旧令牌的请求均被拒绝。 |
| Witness 暂时不可达 | 健康主保留角色；现有写入资格随已持有的租约到期。备机不能自动提升。见证恢复后按持久化修订重新获取有效租约。 |
| Witness 重启 | 打开既有数据库和修订文件，核对集群身份及修订。此前有租约时等待一个完整租期，再发放更高的 epoch/token。任一文件缺失或单文件回滚时停止授租。 |
| 旧主或重启 Host 重新加入 | 先以恢复角色与现任主核对有序哈希链、重放缺失条目。待两端前沿一致后，在返回节点的本地管理控制台输入 `requalify`，记录具名审核并重新取得备机资格。旧主不会自动抢回主角色。 |
| 见证失联且需人工接管 | 信息科先确认旧主已断电或完成网络隔离，记录工单。合格备机等到租约到期、见证与旧主均不可达后，具名管理员在本地执行 `manual-fence PoweredOff:TICKET-123` 或 `manual-fence NetworkIsolated:TICKET-123`。该步骤记录新纪元、令牌、审批人和隔离证据；`ManualPrimary` 状态等待见证恢复，仍需有效见证租约和备机回执才能成功答复写入。 |
| 两台 Host 都停止或从备份恢复 | 保留原见证及其修订。恢复各 Host 自己的节点数据库和能解封的密钥环，启动后按 `status` 核验两端已提交前沿和机构保存的最后确认回执。待旧租约到期，两台 Host 在线且已核对时，由具名管理员在指定新主输入 `recover SEQ HASH`，在另一台执行 `requalify`。若一侧比共同前沿恰好多提交一条，且这条记录的序号与哈希精确匹配另一侧的持久准备记录，可在任一侧执行 `recover`。它会核验独立确认的共同前沿或两份持久记录的下一条摘要，取得新租约后恢复。如果全停前已经人工接管，在保存该人工审批的节点执行 `recover`，它会申请高于人工纪元的见证租约。不一致时暂停恢复并保留证据。 |

`backup NEW_PATH` 在本地创建并验证加密数据库备份，目标须是新的本机绝对路径。机构须同时保存可解封的历史密钥环、回执前沿和节点身份。`read SEQ` 可核对已提交记录，`election-audit REVISION` 可逐条读取本机选举审计并由机构归档。控制台输出可能含答案和操作人身份，应按考试资料权限处理。[命令入口](../../src/Monitor.Network/ExamClusterConsole.cs)

## 机构现场验收

本接口对应 `ExamInfrastructure 1.1.0`。正式使用须由信息科验收两台物理教师机及第三故障域、DNS／VLAN／防火墙、证书与吊销、密钥轮换和实际 TLS 互操作。按机构冻结的演练合同记录断电、链路分区、见证停机、过期令牌、磁盘满、损坏检查点、证书与密钥轮换、人工隔离及备份恢复。以成功回执的序号和哈希核对确认写 RPO=0，以实际故障发生到新主取得有效写权限的时间测量自动 RTO≤5 s；双端恢复成功回执的时刻需单独记录。现场考试启用仍取决于机构演练和容量验收。
