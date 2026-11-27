# 教师与学生课堂网络接口

教师进程持有仿真实例和最近一分钟的波形，学生进程凭独立邀请码接入。两端用 HTTPS 建立会话并采样时钟，用 WSS 接收同一份 MWS1 波形。命令行提供可运行的双端入口。学生订阅也可直接供后续图形界面调用。[公开声明](api/network.md) · [波形块与保留环](simulation.md)

## 启动课堂

从仓库根目录构建 Release 配置。独立运行已构建的程序需要 .NET 10 和 ASP.NET Core 10 运行时。教师选择学生能访问的本机 IP，准备带私钥、有效 DNS 或 IP SAN 以及 Server Authentication EKU 的机构证书。两端校验 TLS 1.3、证书链和吊销状态，使用系统信任库或以 `SIMONITOR_CA_FILE` 指定受信根证书。部署中间证书并确保机构吊销服务可达。Windows 课堂网络需要 Windows 11 或 Windows Server 2022 及更高版本提供的 TLS 1.3。[Microsoft Schannel 支持表](https://learn.microsoft.com/en-us/windows/win32/secauthn/protocols-in-tls-ssl--schannel-ssp-)

以下命令假设教师机使用 `192.168.1.20`，其证书覆盖 `teacher.lab.example`，学生机能解析这个名称。将示例路径替换为机构保管的文件。数据库和邀请文件放在教师专属目录中，Windows 上用该目录的 ACL 限制访问。

1. 在教师机本地设置 `SIMONITOR_IDENTITY_KEY`（随机 32 字节经 Base64 编码）、`SIMONITOR_BOOTSTRAP_PASSWORD` 和 `SIMONITOR_HOST_PASSWORD`。保留数据库对应的密钥。首次初始化指定新管理员名和机构禁用密码表。表中每行是一个禁用密码的 UTF-8 字节的完整 SHA-256 十六进制摘要。初始化会核验新密码并永久停用初始 root 凭据。

   ```sh
   dotnet run --project src/Monitor.Network -c Release --no-build -- init classroom.db lab-admin blocked-passwords.sha256
   ```

2. 教师保留身份密钥及登录密码，启动监听。以下示例发放两个座位，端口填 0 可由系统分配。可选的座位数范围为 1–36，默认 1。PFX 密码来自 `SIMONITOR_TLS_PASSWORD`。使用 PEM 时将命令中的证书改为 PEM 文件，以 `SIMONITOR_TLS_PEM_KEY` 指定私钥；使用 Windows 证书库时传入 `store:<thumbprint>`（CurrentUser/My）。

   ```sh
   dotnet run --project src/Monitor.Network -c Release --no-build -- teacher classroom.db lab-admin 192.168.1.20 8443 teacher.lab.example teacher.pfx invitations.txt 2
   ```

   进程要求本地机构用户拥有 `deployment.manage` 或 `session.manage` 权限。邀请文件必须是新文件，Unix 下仅所有者可读写，Windows 下文件 ACL 仅授予当前教师账户访问权。每行依次为 HTTPS 地址、学生 UUID、一次性邀请码。教师私下将各自的地址和邀请码交给对应学生。每次启动产生新的课堂和实例 ID，旧邀请码及令牌随原进程退出失效。

3. 学生机设置自己的 `SIMONITOR_JOIN_CODE` 并启动接收。下面的命令打印会话 ID、学生 ID、校时往返延迟和十个连续块的序号、仿真时间与通道数。省略数量可持续接收。教师和学生都可用 Ctrl+C 退出。

   ```sh
   dotnet run --project src/Monitor.Network -c Release --no-build -- student https://teacher.lab.example:8443 10
   ```

## HTTPS 控制接口

请求体和响应体使用 UTF-8 JSON。所有端点使用相同的 HTTPS origin。入场后用 `Authorization: Bearer <token>` 发送令牌。学生凭据仅有课堂读取权限，每个学生最多占一个活动订阅。邀请码有效期五分钟且限用一次。令牌有效期一小时，绑定签发课堂、实例及学生。加入尝试按来源和全班的滚动一分钟失败计数限速。

| 请求 | 输入和返回 |
|---|---|
| `POST /v1/join` | 提交 `{"protocolVersion":1,"joinCode":"…"}`，成功返回 `{"token":"…"}`。无效、过期、重用或限速时返回 401。 |
| `GET /v1/session` | 返回协议版本、学生／会话／实例 ID、`timebaseEpoch`、`streamEpoch`、最老保留块序号、下一个块序号及其起始仿真纳秒。字段名与 [ClassroomManifest](api/network.md) 对应，采用 camelCase。 |
| `GET /v1/clock?t1=N` | 学生记录发送时间 t1，教师返回 t1、收到请求时的 t2 和写响应前的 t3，学生再记录收到响应时的 t4。各端使用单调纳秒。往返耗时为 (t4−t1)−(t3−t2)，教师相对学生的时钟偏移估计为 ((t2−t1)+(t3−t4))/2。 |

JSON 中的 64 位整数以十进制字符串输出，协议版本是数字。校时样本用于比较两端的单调时钟。波形的采集时间仍取自 MWS1 内的仿真时间。

## WSS 波形接口

`GET /v1/waveforms?from=N` 升级 WebSocket，子协议为 `simonitor.waveforms.v1`。握手携带同一个 Bearer 令牌。N 是学生下一块需要交付的序号。教师按序重放保留块并衔接实时块。省略 N 从注册订阅时的实时位置开始。SDK 的 `SubscribeAsync(manifest)` 默认从给定清单的 `NextBlockSequence` 开始，覆盖读取清单与订阅之间的块。

- 教师每条二进制消息发送一个 200 ms 的 MWS1 块。学生组装 WebSocket 分片，并校验块内容、会话、实例、epoch、序号和起始仿真时间，随后写入 50 块的本地缓冲。
- 每个块对应一条 48 字节的二进制 ACK。偏移 0 和 8 分别为小端 UInt64 流 epoch 和块序号，偏移 16 是 MWS1 头中的 32 字节内容 SHA-256。成功发送 ACK 后，`ReceiveAsync` 才返回该块并推进交付游标。
- 教师保留 300 块。每个学生单独拥有最多 50 块的实时队列和五秒发送／ACK 期限。慢学生掉队时其他学生继续接收。心跳使用 WebSocket Ping/Pong。
- 临时断线后，同一个学生客户端可用 `ResumeAsync(previousManifest, nextBlockSequence)` 刷新清单并从交付游标续传。身份、实例、epoch 或时间线发生变化时会失败；游标已滑出保留窗口时返回 HTTP 409。命令行学生端对短暂网络中断最多重连三次，分别等待 1、2、4 秒。游标过期、身份变化或凭据失效时结束本次观察并报错。

此版本传输课堂身份、时钟和示例源的七通道教学波形。远程教学操作、考试、长断线后的权威快照重同步，以及教师和学生图形界面是后续的应用层接入工作。相关进程内契约见[权威、身份与恢复](authority-identity-recovery.md)。
