# 权威、身份、连续性与持久化接口

[接口总览](README.md) · 公开声明：[Domain](api/domain.md)、[Application](api/application.md)、[Infrastructure](api/infrastructure.md)。

本文描述当前源码中的进程内 C# 契约、恢复消息编解码和身份存储格式。
`Monitor.Domain` 提供确定性规则与状态机，`Monitor.Application` 组织调用，
`Monitor.Infrastructure` 实现密码学和 SQLite 适配；这些类型没有对应的 HTTP 路由。
时间、标识和依赖由调用者输入；身份服务使用注入的 UTC 墙钟，连续性状态机使用单调纳秒或仿真纳秒。
显示与音频偏好另见 [音频与偏好接口](audio-persistence.md)。

## 公共结果、治疗状态与安全输入

源码：[DomainResult](../../src/Monitor.Domain/Common/DomainResult.cs)、
[TherapyState](../../src/Monitor.Domain/Therapy/TherapyState.cs)、
[TherapyController](../../src/Monitor.Domain/Therapy/TherapyController.cs)。

`DomainResult<T>` 包含 `Value`、`Rejection`；`IsAccepted` 仅判断 `Rejection is null`。
通过 `DomainResult.Accept<T>(T)` / `Reject<T>(DomainRejection)` 创建结果。
拒绝值包含 `ReasonCode`、`ObjectRef`、`FieldPath`、`CurrentRevision`、`Retryable`、
`LocalizationKey`、`CorrelationId`，不是异常或网络错误响应。

`TherapyState` 保存 `Revision`、`AuthorityEpoch`、`Mode`、`Attempt`、`Energy`、
`Request`、可空 `Lease` 和 `DeliveredCount`；`Initial(epoch, mode)` 的 revision/count 为零。
模式为手动非同步或手动同步；治疗事实为 `(EventType, SafetyTimeNs, CorrelationId)`。
租约绑定 interaction/client/instance/authority epoch/granted revision，保存按下时间、
持续阈值、失效时间（均为 safety ns）、续租序号和 Pressed/Held/Released/Cancelled/Expired 状态。

```csharp
TherapyController(Guid instanceId, ulong authorityEpoch, DefibrillationMode mode);
TherapyState State { get; }
DomainResult<TherapyTransition> BeginCharge(long safetyTimeNs, Guid correlationId);
DomainResult<TherapyTransition> MarkChargeReady(long safetyTimeNs, Guid correlationId);
DomainResult<TherapyTransition> PressShock(
    Guid interactionId, string clientId, ulong authorityEpoch, long safetyTimeNs,
    long holdThresholdNs, long expiresAtSafetyNs, Guid correlationId);
DomainResult<TherapyTransition> ApplySafetyInputs(IEnumerable<SafetyInput> inputs);
```

调用顺序是 `BeginCharge` → `MarkChargeReady` → `PressShock` → 安全输入。
充电必须从 Idle 开始；就绪要求 Charging；按下要求 Ready + Charged。
按下的 interaction 非空、client 非空白、epoch 必须匹配，hold 大于零且不溢出，
`pressed + hold <= expires`，`expires > pressed`。构造器拒绝空 instance ID。
`HoldThresholdElapsed` 达到阈值后，非同步模式立即交付；同步模式进入 AwaitingSync，
之后的 `ValidQrs` 才交付。`SafetyTimeNs >= ExpiresAtSafetyNs` 视为过期。
交付将能量设为 Idle、清空请求、增加 DeliveredCount；已经 Delivered 的 hold 输入不重复交付。
`LeaseRenewed` 只增加 RenewSequence，不延长 ExpiresAtSafetyNs，也不产生事实或 revision 增量。

取消/释放/过期会撤销活动租约和能量，通常产生原因事实及 `DisarmRecorded`。
设备故障进入 Failed；epoch 只能增加，切换会取消活动请求。
交互相关输入必须匹配现有 interaction；权限撤销、暂停、结束、恢复等取消不要求该匹配。
拒绝码包括 `therapy.energy.not_idle`、`therapy.shock.not_ready`、
`therapy.lease.identity_invalid`、`therapy.lease.time_order`、`therapy.lease.interaction_stale`、
`authority.epoch.stale` / `authority.epoch.not_monotonic`，均携带当前 revision 且 `Retryable=false`。
批处理按安全优先级排序；某条返回领域拒绝时恢复批前状态。此回滚不包装枚举器或比较器抛出的异常。

源码：[SafetyInput](../../src/Monitor.Domain/Authority/SafetyInput.cs)、
[权威候选排序](../../src/Monitor.Domain/Authority/AuthorityCandidateOrdering.cs)、
[SessionAuthority](../../src/Monitor.Application/SessionAuthority.cs)。

`SafetyInput` 必填 safety ns、Kind、StableId、LocalSequence、CorrelationId，
另有可空 InteractionId/AuthorityEpoch。比较顺序为时间升序、下表等级、StableId ordinal、本地序号。

| 同刻等级 | 输入 |
| --- | --- |
| 0 | AuthorityEpochChanged、PermissionRevoked、DeviceFaulted、LeaseExpired |
| 1 | ShockReleased、ShockCancelled、EnergyDisarmed、SessionPaused、SessionEnded、AuthorityRecovered |
| 2 / 3 / 4 | ValidQrs / HoldThresholdElapsed / LeaseRenewed |

`AuthorityCandidateOrdering.Order<T>(IEnumerable<T>, Func<T, AuthorityCandidateKey>)`
是另一套通用候选排序：sim ns → PhaseRank → EventTypeRank → ExplicitPriority **降序** →
StableId ordinal → LocalSequence。相同完整键被拒绝，不依赖输入枚举顺序打破平局。
`AuthorityCandidateKey(long simTimeNs, string eventType, int explicitPriority, string stableId, ulong localSequence)`
仅接受注册事件；非零显式优先级只允许 `scenario-trigger-matched`、`scenario-effect-committed`。
StableId 长 1–128、以小写字母开始、其余为小写字母/数字/`.`/`_`/`-`，不允许连续或结尾分隔符。
未知事件、重复键、缺失键等抛 `AuthorityCandidateException` 并提供 `ReasonCode`。

注册阶段依次覆盖安全撤销、释放/撤销、结束/暂停、配置、QRS、采集质量、估计、
心率源、治疗感知、起搏候选、同步放电、治疗响应、场景触发/效果、报警、数值、投影、提示、考核、检查点。
完整字符串与固定 rank 以源码 `AuthorityEventRegistry` 为准；它不是动态注册服务。

`SessionAuthority(TherapyController)` 的唯一提交入口是
`DomainResult<AuthorityCommit> CommitSafetyInputs(IEnumerable<SafetyInput> inputs)`。
内部锁串行化该入口；成功时返回 `(CommitSequence, State, Facts)`，只有非空 Facts 才增加提交序号。
治疗拒绝原样传播；它没有持久化提交日志，也不替调用者锁住直接调用 TherapyController 的行为。

## 心电判读考核

源码：[EcgAssessment](../../src/Monitor.Domain/Assessment/EcgAssessment.cs)。

`EcgAnswer` 有单值 `RateClass` 和 Rhythm/Axis/Conduction/Chamber/StT/Other 集合。
`ConceptNormalizer(IReadOnlyDictionary<string,string> aliases)` 将别名做 NFKC、首尾去空白、
内部连续空白合为一个空格；保持大小写，字典使用 ordinal 比较。标准化后重复别名使构造失败。
`NormalizeOne(string?)` 保留 null；`Normalize(IReadOnlyCollection<string>)` 返回去重并 ordinal 排序的概念 ID。
直接标准化未知别名会抛异常；评分入口把未知概念转为结果拒绝。

```csharp
AssessmentResult EcgAssessmentScorer.Score(
    AssessmentOperationMode operationMode, AssessmentQuestionKind questionKind,
    AssessmentCaseKind caseKind, EcgAnswer answer,
    IReadOnlyList<EcgAnswer> referenceAnswerSets);
```

当前只评分 `EcgInterpretationStatic`；FormalExamination 的其他题型抛
`assessment.question_kind.not_armed`，其他非静态题型抛 `assessment.scoring.practice_only`。
参考答案至少一组。内置病例按字段完全相等给分：rate 10、rhythm 25、axis 10、
conduction 15、chamber 10、st_t 20、other 10；选择得分最高的一整组参考答案，不跨组拼接。
自定义病例完全匹配任一组得 100，否则 `PendingManualReview`、Score=null。
未知概念返回 `Rejected / UnknownConcept`；正常结果携带 Status/Score/Method/Fields。

## 身份端口与应用调用

源码：[IdentityPorts](../../src/Monitor.Application/Identity/IdentityPorts.cs)、
[身份模型](../../src/Monitor.Domain/Identity/IdentityModels.cs)、
[IdentityPolicy](../../src/Monitor.Domain/Identity/IdentityPolicy.cs)。

所有注入端口如下；密码用 `ReadOnlySpan<char>`，不放进请求 record。

```csharp
interface IPasswordHasher {
    PasswordVerifier Hash(ReadOnlySpan<char> password);
    bool Verify(ReadOnlySpan<char> password, PasswordVerifier verifier);
    bool IsSupported(PasswordVerifier verifier);
}
interface ICompromisedPasswordChecker {
    bool IsCompromised(ReadOnlySpan<char> password);
}
interface IBootstrapIdentityRepository {
    BootstrapIdentitySnapshot LoadBootstrap();
    bool UsernameExists(string canonicalUsername);
    BootstrapCommitResult TryCompleteBootstrap(BootstrapCompletion completion);
}
interface IAuthorityWallClock { DateTimeOffset UtcNow { get; } }
interface IIdentityIdSource {
    Guid NewPrincipalId();
    Guid NewAuditId();
    Guid NewSessionId();
}
interface IInstitutionAccountRepository {
    InstitutionAccount? FindByCanonicalUsername(string canonicalUsername);
}
interface IAuthenticationFailureTracker {
    AuthenticationLockState GetLockState(
        string canonicalUsername, string sourceAddress, DateTimeOffset nowUtc);
    AuthenticationLockState RecordFailure(AuthenticationFailureAuditRecord audit);
    void RecordRejection(AuthenticationFailureAuditRecord audit);
    void ClearAccountFailures(string canonicalUsername);
}
```

用户名 NFKC + Trim 后长度为 3–64 个 Unicode rune；canonical 再转 invariant 大写，ROOT 保留。
密码长度为 12–128 个合法 rune。角色的 scope 由 `GetScopes` 返回 ordinal 排序数组，
`IsAuthorized(role, scope)` 做精确匹配，无隐含角色继承。

| 角色 | Scope |
| --- | --- |
| Admin | audit.read、deployment.manage、identity.manage、privacy.manage |
| Teacher | assessment.author、assessment.release、case.edit、result.export、result.read、session.manage |
| Grader | assessment.review、result.read |
| Candidate | answer.submit.own、question.read.own、result.read_released.own |

`EvaluateAuthentication(context, passwordValid, accountState, locked)` 返回 Kind/ReasonCode；
Teaching 和 FormalExam 都使用机构密码规则。`EvaluateMobileFactor(productionEnabled, compatibilityClaim)`
当前只允许不启用生产移动因子，兼容声明只能 null 或 `None`，不会提升权限。
`LocalPrincipal` 保存 session/principal ID、方法、角色、scopes 和四个 UTC 时间字段。
`EvaluateSession(principal, nowUtc, sensitiveOperation)` 检查时间一致性、8 小时总寿命、
30 分钟无活动、敏感操作 15 分钟重新认证；等于期限即过期。
`RecordActivity` / `RecordSensitiveReauthentication` 返回新 record，过期或时间错误抛 InvalidOperationException。

源码：[引导服务](../../src/Monitor.Application/Identity/BootstrapIdentityService.cs)、
[认证服务](../../src/Monitor.Application/Identity/InstitutionAuthenticationService.cs)。

```csharp
BootstrapOutcome BootstrapIdentityService.Complete(
    CompleteBootstrapRequest request, ReadOnlySpan<char> bootstrapPassword,
    ReadOnlySpan<char> newPassword);
bool BootstrapIdentityService.MayStartNetworkListener();
InstitutionAuthenticationResult InstitutionAuthenticationService.Authenticate(
    InstitutionAuthenticationRequest request, ReadOnlySpan<char> password);
```

引导请求包含 LocalInteractive、InitialUsername、可空 NewUsername、CorrelationId。
必须本地交互、初始名精确为 `root`，且引导口令正确；新用户名和新密码必须同时更换。
检查唯一性、长度、泄露密码端口后，生成 Active Admin（revision=1）及 BootstrapAdminActivated 审计，
用 ExpectedRevision 原子提交。返回 RequireUsernameAndPasswordChange / Activated / Rejected /
RetryableConflict；数据库冲突映射 `identity.bootstrap.revision_conflict`。
只有 Completed、RootPermanentlyRetired=true、BootstrapVerifier=null 时网络监听资格为 true；
这个方法本身不启动监听器。

认证请求包含 Username、SourceAddress、Teaching/FormalExam Context、非空 CorrelationId。
先检查账户/来源锁，再查 canonical 账户；不存在的账户也用注入的 dummy verifier 做密码验证。
错误密码记失败窗口，锁定/禁用记拒绝审计；成功只清账户维度失败，不清来源维度。
结果携带 Kind、ReasonCode、可空 Principal 和 LockedUntilUtc。
ID 源给空 ID、仓储返回不匹配身份、dummy verifier 不受支持均抛异常。
当前 `src/` 没有 ICompromisedPasswordChecker、IAuthorityWallClock、IIdentityIdSource 的生产实现。

## 身份存储、密钥与备份

源码：[密码哈希](../../src/Monitor.Infrastructure/Identity/Pbkdf2PasswordHasher.cs)、
[载荷保护端口](../../src/Monitor.Infrastructure/Identity/IIdentityPayloadProtector.cs)、
[平台密钥端口](../../src/Monitor.Infrastructure/Identity/IPlatformKeyProtector.cs)。

```csharp
interface IIdentityPayloadProtector {
    byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose);
    byte[] Unprotect(ReadOnlySpan<byte> protectedPayload, string purpose);
    string LookupToken(string value, string purpose);
}
interface IPlatformKeyProtector {
    string ProtectorId { get; }
    byte[] Protect(ReadOnlySpan<byte> plaintext);
    byte[] Unprotect(ReadOnlySpan<byte> protectedPayload);
}
```

`Pbkdf2PasswordHasher` 实现 IPasswordHasher：固定 PBKDF2-HMAC-SHA-256、600000 轮、
16 字节随机 salt、32 字节 subkey，二者 Base64 存入 PasswordVerifier。
Verify 使用固定时间比较；算法、轮数、Base64 或长度不支持返回 false。Hash 本身不执行业务密码长度策略。

源码：[AES-GCM 载荷](../../src/Monitor.Infrastructure/Identity/AesGcmIdentityPayloadProtector.cs)、
[平台保护密钥文件](../../src/Monitor.Infrastructure/Identity/PlatformProtectedIdentityKeyStore.cs)。

`AesGcmIdentityPayloadProtector(ReadOnlySpan<byte> masterKey)` 要求 32 字节主密钥，
用 HMAC-SHA256 和不同 domain 字符串派生加密/查找密钥。
载荷格式为版本 byte=1 + nonce 12 bytes + tag 16 bytes + ciphertext；purpose 的 UTF-8 为 GCM AAD。
`LookupToken` 对 `purpose + NUL + value` 做 HMAC-SHA256，返回无 padding 的 Base64URL。
purpose 必须非空白；认证或格式失败抛 CryptographicException，Dispose 清零派生密钥并使后续使用失败。

`PlatformProtectedIdentityKeyStore(string keyFilePath, IPlatformKeyProtector)` 要求绝对路径和已存在目录。
`OpenOrCreatePayloadProtector()` 加载已有 wrapped key，或随机创建 32 字节密钥；临时文件 flush 后
以不覆盖 move 发布，并发创建撞名时加载胜出的文件。现有文件解密/校验失败不会重置密钥。
格式是 `MONKEY01`、little-endian version=1 / ID 长度 / wrapped 长度、ASCII ProtectorId、
wrapped key、前述内容的 SHA-256；wrapped 长度 1–65536，ProtectorId 最多 128 bytes。
平台 ID 必须匹配，解封后必须 32 bytes。Unix 新文件仅用户读写，加载拒绝 group/other 权限；
Windows 路径不在该类内实施 ACL 检查。当前没有具体 IPlatformKeyProtector 适配器。

源码：[SQLite 仓储](../../src/Monitor.Infrastructure/Identity/SqliteIdentityRepository.cs)、
[数据库 schema](../../src/Monitor.Infrastructure/Identity/SqliteIdentityDatabase.cs)、
[持久失败追踪](../../src/Monitor.Infrastructure/Identity/SqliteAuthenticationFailureTracker.cs)、
[内存失败追踪](../../src/Monitor.Infrastructure/Identity/InMemoryAuthenticationFailureTracker.cs)。

`SqliteIdentityRepository(databasePath, protector)` 实现两个身份仓储端口，另提供
`bool TryInitializeBootstrap(PasswordVerifier)` 和 `bool AuditExists(Guid)`。
路径必须绝对且父目录存在；首次初始化 INSERT OR IGNORE，不覆盖已有 bootstrap。
未初始化就 LoadBootstrap 抛 InvalidOperationException。
`TryCompleteBootstrap` 在非延迟事务中插入账户/审计、退休 root、清掉 verifier；
revision/唯一约束冲突返回 ConcurrencyConflict，其他 SqliteException 返回 Failed，
参数、载荷加密等异常继续向调用者抛出。

SQLite schema version=1，采用 WAL、synchronous=FULL、foreign_keys=ON、busy_timeout=5000 ms，禁用连接池。
账户与审计的 JSON 载荷被保护，用户名/来源查找使用 purpose 隔离的 token。
表包括 identity_bootstrap、identity_accounts、identity_audit、authentication_failures、
authentication_locks、authentication_audit；两个 audit 表用触发器阻止 UPDATE/DELETE。
revision、时间 ticks、锁维度和查找 token 仍作为数据库列存在；这不是整库透明加密。
读取账户/审计会检查解密内容与索引/版本/时间是否一致，不一致抛 InvalidOperationException。

两个失败追踪器都按账户与来源独立累计：15 分钟窗口内达到 5 次，锁定 15 分钟。
`GetLockState` 取两个维度中较晚的到期时间；`ClearAccountFailures` 不清来源记录或审计。
SQLite `RecordFailure` 只接受 `identity.password.invalid`，在同一事务中更新计数、锁和审计；
`RecordRejection` 不能接收该 reason，避免漏记计数。`FindAudit(Guid)` 返回可空审计。
内存实现还提供快照 `AuditRecords`，不持久化，且没有 SQLite 版本同等的 reason/字段严格校验。

源码：[EncryptedSqliteBackupService](../../src/Monitor.Infrastructure/Persistence/EncryptedSqliteBackupService.cs)。

```csharp
EncryptedSqliteBackupService(string databasePath);
EncryptedSqliteBackupDescriptor CreateBackup(
    string backupPath, Guid backupId, DateTimeOffset createdAtUtc,
    string keyReference, ReadOnlySpan<byte> backupKey);
static EncryptedSqliteBackupDescriptor RestoreBackup(
    string backupPath, string restoredDatabasePath,
    string keyReference, ReadOnlySpan<byte> backupKey);
```

所有路径为绝对路径、父目录已存在；目标必须不存在，不覆盖数据库或备份。
备份用 SQLite BackupDatabase 生成一致快照，再按 1 MiB 分块 AES-GCM 加密；密钥为调用者提供的 32 bytes。
返回 BackupId、KeyReference、UTC 创建时间、PlaintextBytes、lowercase PlaintextSha256。
明文快照长度必须大于 0 且不超过 1 TiB；key reference 为最多 128 字符的 ASCII stable ID。
`MONBKP01` 文件头含 version=1、头长、块长、明文长度、UTC ticks、big-endian GUID、
明文 SHA-256、8 字节 nonce 前缀和 key reference。数字头字段除 GUID 外采用 little-endian。
每块 nonce 为前缀 + big-endian uint32 块序号；AAD 为整个头 + 同一块序号，块后跟 16-byte tag。
恢复校验 key reference、精确文件长度、逐块认证、全量 SHA-256、SQLite quick_check 和 schema version=1，
最后才以不覆盖 move 发布。临时文件在 finally 删除；Unix 临时文件仅用户读写。
这是独立备份加密，不导出平台 wrapped 主密钥；恢复身份载荷仍需要匹配的身份密钥。
路径/IO/SQLite/格式/认证错误以异常表达，不返回 bool，也没有自动覆盖或密钥轮换流程。

## 连接健康、断线数据与 NoData 投影

源码：[连接健康](../../src/Monitor.Domain/Continuity/ConnectionHealthStateMachine.cs)、
[数据连续性](../../src/Monitor.Domain/Continuity/DataContinuityStateMachine.cs)、
[NoData 投影](../../src/Monitor.Domain/Continuity/NoDataPresentationStateMachine.cs)。

这三个状态机都支持 `Start`、`Restore(state)`、`CaptureState()`，Restore 会校验状态一致性。
错误分别用 ConnectionHealthException / DataContinuityException / NoDataPresentationException，
继承 ArgumentException，提供 ReasonCode（如 InvalidConfiguration、InvalidCheckpoint、TimeReversed）。
传入的 authority monotonic ns 不得倒退；它与 UTC、仿真 sim ns 是不同时间域。

```csharp
ConnectionHealthStateMachine.Start(RealtimeConnectionHealthProfile profile,
    long startAuthorityMonotonicNs, bool sessionRunning, IReadOnlyList<Guid> requiredPlaneIds);
ConnectionHealthState Advance(long authorityMonotonicNs, bool sessionRunning);
ConnectionHealthState RecordServerActivity(long authorityMonotonicNs);
ConnectionHealthState RecordImmediateFailure(ImmediateTransportFailure failure, long authorityMonotonicNs);
PlaneProgressUpdateStatus RecordPlaneProgress(Guid planeId, ulong streamEpoch,
    ulong blockSequence, long authorityMonotonicNs);
ConnectionHealthState ResetPlaneAfterResync(Guid planeId, ulong streamEpoch,
    ulong lastBlockSequence, long authorityMonotonicNs);
ConnectionHealthState BeginRelocking(long authorityMonotonicNs);
ConnectionHealthState CompleteRelocking(long authorityMonotonicNs);
```

默认 profile：heartbeat 1 s、3 s Suspect、5 s Disconnected；必需 plane 运行 1 s 无进度为 Stalled，
2 s 为 plane Disconnected。required plane ID 必须非空、唯一，数量 1–128。
连接静默按 monotonic 时间计；plane 进度计时只累计 sessionRunning 期间，Advance 用旧运行状态累计后切换。
plane 断开与 ConnectionState 分开保存。块重复/旧序号返回 IgnoredDuplicateOrStale；跳号或换 epoch
返回 IgnoredDiscontinuous；断开或重锁时返回 RequiresResync。重同步用 ResetPlaneAfterResync 更新游标。
WebSocketClose/IoError/TlsFailure 立即断连；这些枚举并不实现真实网络运输。
Disconnected 不会因普通 server activity 自动恢复，需 BeginRelocking → CompleteRelocking。

`DataContinuityStateMachine.Start(LocalContinuationPolicy, long startAuthorityMonotonicNs)`
初始为 Connected + Authoritative 数据与权威。公开变更方法如下，均返回 DataContinuityState：

| 方法 | 输入与效果 |
| --- | --- |
| `ObserveHealthyConnection(ConnectionState, long)` | 仅在 Connected/Suspect 间同步连接状态 |
| `Disconnect(bool hasBufferedData, long)` | 开始 shadow Pending；有缓存为 Buffered/Authoritative，否则 NoData/Provisional |
| `RecordShadowVerification(bool matched, long)` | 只接受断连 Pending；成功且无缓存时尝试本地延续 |
| `ExhaustAuthoritativeBuffer(long)` | 只接受断连且仍有 Buffered，按 shadow/策略选择后续状态 |
| `RecordContinuationFailure(LocalContinuationFailure, long)` | 标记 CapsuleInvalid/ConsistencyMismatch/PerformanceInsufficient；保留尚未耗尽的权威缓存 |
| `RecordScenarioEnded(long)` | 活动本地延续进入 NoData |
| `BeginRelocking(bool hasVerifiedPreroll, long)` | 断连进入 Relocking/Provisional，按 preroll 选择 Buffered 或 NoData |
| `RecordRelockPrerollVerified(long)` | AwaitingRelockCommit 的 NoData 变为 Buffered/Provisional |
| `CompleteRelocking(long)` | Connected + Authoritative，并清理断线/延续字段 |
| `Advance(long)` / `CaptureBanner()` | 推进期限 / 返回横幅投影，技术音频策略始终 Silent |

策略可 Disabled、Duration、UntilScenarioEnd；默认 Duration 900 s，最大 1800 s。
Duration 从真正进入 LocalContinuation 计时，达到期限进入 NoData；不会把本地数据宣布为 Authoritative。
没有缓存时的 NoData 原因区分待验证、策略禁用、验证失败、到期、场景结束和等待重锁提交。

`NoDataPresentationStateMachine.Start(IReadOnlyList<NumericNoDataPolicy>, DataContinuityState)`
及 Restore 的 policy 最多 128 项，每项为 ParameterId、SourceInstanceId、StaleRetentionNs。
`Synchronize(DataContinuityState)`、`Advance(long authorityMonotonicNs)` 返回 NoDataSafetyProjection，
`CaptureProjection()` 不推进时间。NoData 时 live trace 使用 PresentationClock/NoDataSweep，
病人报警 SuspendedUnknown、生理事件 Suppress、生理音频 Silent。
数值在 `elapsed < StaleRetentionNs` 时保留旧值并标 Stale，此后用不可用标记和 Disconnected。
投影始终保留钉住历史/校准区，不立即清空 live trace；绘制动作由上层执行。

## 胶囊验证、恢复状态与 shadow 端口

源码：[签名门](../../src/Monitor.Application/Continuity/ContinuitySignatureGate.cs)、
[胶囊链](../../src/Monitor.Application/Continuity/ContinuityCapsuleChain.cs)、
[恢复状态镜像](../../src/Monitor.Application/Continuity/ContinuityRecoveryState.cs)、
[shadow 协调器](../../src/Monitor.Application/Continuity/ContinuityShadowCoordinator.cs)。

```csharp
interface IContinuitySignatureVerifier {
    string DeriveKeyId(ReadOnlyMemory<byte> publicKeySec1);
    bool Verify(ContinuitySignedBody body, ReadOnlyMemory<byte> signatureRs64,
        ReadOnlyMemory<byte> publicKeySec1);
}
interface IContinuityCapsulePayloadDecoder {
    ContinuityCapsuleBase DecodeBase(ReadOnlyMemory<byte> signedPayload);
    ContinuityCapsuleDelta DecodeDelta(ReadOnlyMemory<byte> signedPayload);
}
interface IContinuityShadowGenerator {
    ContinuityShadowGenerationResult Generate(ContinuityRecoveryStateImage recoveryStateImage,
        ContinuityShadowGenerationRequest request);
}
```

SignedBody 绑定 CapsuleType/ID、SessionId、InstanceId、AuthorityEpoch、秘密边界、有效仿真截止和 PayloadSha256。
Proof 保存算法 ID、KeyId、schema major/minor、64-byte r||s 签名；TrustedKey 保存 SEC1 公钥、
PreActive/Active/Retiring/Revoked/Expired 状态、authority epoch 和单调激活/失效时间。
门支持 Start(sessionId, instanceId, epoch, monotonicNs, trustedKeys, verifier) / Restore；
`Verify(body, proof, payload, currentSimTimeNs, authorityMonotonicNs)` 返回不透明 admission，
`Commit(admission)` 才消耗 ID；`VerifyAndCommit(...)` 合并二者。
admission 绑定门实例、已接受数量和时间前沿，过时或重复提交报 `ContinuityCrypto.StaleAdmission`。

固定算法 `SM2-SM3-RS64@1`，signed schema 1.0；最多 64 trusted keys、65536 已接受 ID，
base 最大 65536 bytes、delta 最大 16384 bytes，通用 signed payload 上限 1 MiB。
Key lifetime 最多 90 天，配置必须有当前 epoch 的 Active key；实际验签只允许 Active，
且单调时间必须在 `[ActivatedAt, ExpiresAt)` 内，Retiring 不能用于新验签。
先验证签名，再核对身份/epoch/replay/期限/秘密边界/真实 payload SHA-256；
有效期判断是 `currentSimTimeNs > ContinuationValidUntilSimTimeNs` 才过期。
失败抛 ContinuitySignatureGateException，ReasonCode 前缀为 `ContinuityCrypto.`，不提交 replay 状态。

胶囊链 Start 在上述门参数后还注入 runtimeCapabilities、payloadDecoder；Restore 同样需要它们。
`AcceptBase(ContinuitySignedBody, ReadOnlyMemory<byte>, long currentSimTimeNs, long authorityMonotonicNs)`
返回 AcceptedContinuityCapsuleBase；同签名的 `AcceptDelta` 返回 AcceptedContinuityCapsuleDelta。
Base 包含 branch/epochs/checkpoint/commit/simTime、事件与通道游标、组件 bytes/hash/schema、
四种投影 revision、engine/math/resource 引用、延续 permission/边界及可空 encryption envelope 引用。
Delta 包含 BaseSha256、PreviousDeltaSha256、更新序号/时间和 ChangedComponents。
最多 128 通道、256 组件、32 资源包；组件 hash 和运行时 engine/math/resource/channel/schema 能力必须匹配。
base 必须 Allowed；delta 必须接当前 hash 链且 checkpoint 严格增加，commit/simTime 不倒退。
验证完整状态镜像后才 Commit admission、发布 Frontier/RecoveryStateImage；返回镜像复制组件字节。
边界错误抛 `ContinuityCapsuleChainException`（`ContinuityChain.*`）。当前没有生产 payload decoder 或加密 envelope 实现。

`ContinuityShadowCoordinator(chain, dataContinuity, generator).VerifyOverlap(proofs, authorityMonotonicNs)`
只在断连且 shadow Pending 时调用；每块 200 ms，重叠最多 50 块。
Proof 含块序号、开始 sim ns、波形/数值/规范状态三个 SHA-256；请求从已验镜像派生。
生成器返回 Completed/CapsuleRejected/PerformanceInsufficient 和 proof 列表。
协调器比对完整窗口并更新 DataContinuity，结果为 Verified/Mismatch/CapsuleInvalid/
PerformanceInsufficient/InvalidEvidence；生成器的 ArgumentException 转为 InvalidEvidence，其他异常未兜底。
当前生成器仍是注入端口，不能把该协调器当成已经接通的仿真回放引擎。

## 恢复握手、重锁与消息格式

源码：[握手](../../src/Monitor.Domain/Continuity/RecoveryHandshakeCoordinator.cs)、
[重锁](../../src/Monitor.Domain/Continuity/RecoveryRelockCoordinator.cs)、
[客户端组合](../../src/Monitor.Application/Continuity/ClientRecoverySession.cs)、
[计划工厂](../../src/Monitor.Application/Continuity/RecoveryResyncPlanFactory.cs)。

握手 Start(clientId, sessionId, instanceId, hostAuthorityEpoch) 要求非空 ID、host epoch 非零。
`AcceptHello(RecoveryHelloMessage)` → 可选 `AcceptReport(LocalContinuationReportMessage)` →
`RecordVerification(ContinuityReportVerification)` → `CapturePlanningInput()`。
无 report 的 hello 直接 ReadyForPlan；有 report 时，即使客户端声称 Verified，也需要主机验证。
Hello 绑定身份、观察 epoch、已用 commit/event 序号、base/delta hash、report ID，并要求 MomentaryInputsCleared。
Report 带 branch/fallback epoch、延续 sim 起止、最多 128 通道前沿、块/事件序号、
rolling/offline action hash、最多 10000 离线 action ID。主机验证必须匹配报告身份与 hash，
失败转 Rejected；重复相同消息可幂等读取，冲突或非法阶段抛 `RecoveryHandshakeException`。

`RecoveryResyncPlanFactory.Create(RecoveryPlanningInput, WaveformBlockRing, WaveformRecoveryRequest,
RecoveryHostPlanContext)` 从实际主机 waveform ring 生成 plan，要求 host durable commit 足够且 ring epoch 更新。
仅接受 waveform planner 的 SnapshotThenJoin 结果；新边界按 200 ms 槽对齐，距 current sim 至少 1 s。
RequireFullSnapshot 优先返回 FullSnapshotRequired，否则已验预测为 CommitVerified，其他为 Discard。
工厂验证输入失败抛 `RecoveryResyncPlanFactoryException`（`RecoveryPlan.*`），不执行网络发送或状态提交。

Relock 的 Start 参数为 client/session/instance ID、已接受 authority/stream epoch、last commit、current sim ns。
`AcceptPlan(plan, currentSimTimeNs)` → `RecordPrepared(evidence)` →
`CapturePreparedAcknowledgement()` → `Commit(command)`；各阶段可 CaptureState / Restore。
Plan 新 authority/stream epoch 必须严格增加，PrerollUntil 等于 RelockSimTimeNs，准备至少提前 1 s。
Evidence 必须在 relock 时刻前到达，绑定 plan、epochs、host hash、preroll 内容及可选 snapshot；
CommitVerified 还要求 LocalPredictionMatchedHost=true。
Commit 的时刻、身份、hash、commit 前沿必须与计划一致；成功之后 AcceptedEpoch 与 LastAppliedCommit 才切换。
`Advance(currentSimTimeNs)` 在 Preparing 达到 relock 即拒绝；Prepared 超过 relock 才拒绝。
`ReplaceRejectedPlan(plan, currentSimTimeNs)` 仅在 Rejected 接受新的 PlanId；错误或倒退时间抛 RecoveryRelockException。

ClientRecoverySession 将 Relock 和 DataContinuity 成对快照、复制试算、校验后一起发布。
Start 的首参数为 disconnectedContinuity，其余为 relock 身份/epoch/前沿/仿真时间；
AcceptPlan/ReplaceRejectedPlan 附加 authorityMonotonicNs，RecordPrepared/Commit 同样显式输入单调时间，
Advance 同时接受 currentSimTimeNs 和 authorityMonotonicNs。
Preparing 对应 Relocking/NoData/Provisional；Prepared 对应 Relocking/Buffered/Provisional；
Committed 才对应 Connected/Authoritative；失败回 Disconnected。异常不会发布半套组合状态。

源码：[RecoveryWireCodec](../../src/Monitor.Infrastructure/Continuity/RecoveryWireCodec.cs)、
[SM2 校验器](../../src/Monitor.Infrastructure/Continuity/Sm2ContinuitySignatureVerifier.cs)。

```csharp
byte[] RecoveryWireCodec.EncodeHello(RecoveryHelloMessage message);
RecoveryHelloMessage RecoveryWireCodec.DecodeHello(ReadOnlyMemory<byte> wire);
byte[] RecoveryWireCodec.EncodeReport(LocalContinuationReportMessage message);
LocalContinuationReportMessage RecoveryWireCodec.DecodeReport(ReadOnlyMemory<byte> wire);
byte[] RecoveryWireCodec.EncodePlan(RecoveryResyncPlan plan);
RecoveryResyncPlan RecoveryWireCodec.DecodePlan(ReadOnlyMemory<byte> wire);
```

消息为规范 UTF-8 JSON，schema_id 分别为 RecoveryHello@1、LocalContinuationReport@1、ResyncPlan@1。
所有 64-bit 整数使用十进制**字符串**，GUID 为 D 格式，hash 为 64 个小写 hex 字符，枚举为名字。
字段使用源码中 snake_case，例如 observed_authority_epoch、last_applied_commit_seq、
start_sim_time_ns、last_sample_index_by_channel、relock_sim_time_ns、local_prediction_decision。
可空字段仍必须显式存在并用 null；不接受未知/重复/遗漏字段、注释或尾逗号。
最大 1 MiB、深度 8；解码后重新编码要求逐字节一致，因此多余空白、不同字段顺序等也拒绝。
非法 shape/schema/canonical/size 抛 RecoveryWireCodecException（`RecoveryWire.*`）。
该 codec 只覆盖 hello/report/plan，没有通用 JSON 宽松反序列化、socket 或 prepared/commit 的 wire 实现。

Sm2ContinuitySignatureVerifier 实现签名端口；SEC1 公钥必须是有效的 65-byte 未压缩 SM2 曲线点，
KeyId=`sm2-sm3:` + 公钥字节的 SM3 lowercase hex，用户标识固定 `MONITOR-CONTINUITY-V1`。
签名为两个 32-byte big-endian 整数拼接。`Verify` 对非法签名或公钥返回 false；
`DeriveKeyId` 对非法公钥抛异常。`EncodeCanonicalBody(ContinuitySignedBody)` 的字段按固定顺序输出，
schema 字段值为 ContinuitySignedBody@1，时间和 epoch 为十进制字符串。
当前提供验签和规范 body 编码，没有签名私钥服务或信任密钥分发端点。
