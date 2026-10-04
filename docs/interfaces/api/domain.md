# Monitor.Domain 公开声明索引

[接口总览](../README.md)

本页按源码路径列出当前工作区中对程序集外可见的 C# `public` 类型和显式声明的公开成员，保留参数、默认值、单位命名、泛型约束及枚举值。接口中省略 `public` 的成员也包括在内。方法体、属性实现与非 const 字段初始化值已省略；省略实现的声明用于查阅，不能直接当作可编译代码。

位置 record 的参数也定义其同名属性；编译器合成的构造器、相等性方法以及继承成员不重复展开。`internal` 类型即使有 `public` 成员也不属于本索引。按开发构建的预处理分支读取；桌面产品构建差异见[桌面与命令行入口](../desktop-tools.md)。行为约束和集成顺序见总览中的分模块文档。

## Assessment/EcgAssessment.cs

源码：[EcgAssessment.cs](../../../src/Monitor.Domain/Assessment/EcgAssessment.cs) · 命名空间：`Monitor.Domain.Assessment`

```csharp
public enum AssessmentOperationMode
{
    PracticeOnly,
    FormalExamination,
}
public enum AssessmentQuestionKind
{
    EcgInterpretationStatic,
    RealtimeMonitor,
    DefibrillatorScenario,
    PacerScenario,
}
public enum AssessmentCaseKind
{
    StandardBuiltIn,
    TeacherCustom,
}
public sealed record EcgAnswer(string? RateClass, IReadOnlyCollection<string> Rhythm, IReadOnlyCollection<string> Axis, IReadOnlyCollection<string> Conduction, IReadOnlyCollection<string> Chamber, IReadOnlyCollection<string> StT, IReadOnlyCollection<string> Other)
{
}
public sealed record FieldScore(string Field, int Possible, int Awarded, bool ExactMatch)
{
}
public sealed record AssessmentResult(string Status, int? Score, string Method, IReadOnlyList<FieldScore> Fields)
{
}
public sealed class ConceptNormalizer
{
    public ConceptNormalizer(IReadOnlyDictionary<string, string> aliases);
    public string? NormalizeOne(string? value);
    public IReadOnlyList<string> Normalize(IReadOnlyCollection<string> values);
}
public sealed class EcgAssessmentScorer
{
    public EcgAssessmentScorer(ConceptNormalizer normalizer);
    public AssessmentResult Score(AssessmentOperationMode operationMode, AssessmentQuestionKind questionKind, AssessmentCaseKind caseKind, EcgAnswer answer, IReadOnlyList<EcgAnswer> referenceAnswerSets);
}
```

## Authority/AuthorityCandidateOrdering.cs

源码：[AuthorityCandidateOrdering.cs](../../../src/Monitor.Domain/Authority/AuthorityCandidateOrdering.cs) · 命名空间：`Monitor.Domain.Authority`

```csharp
public sealed class AuthorityCandidateException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed class AuthorityCandidateKey
{
    public AuthorityCandidateKey(long simTimeNs, string eventType, int explicitPriority, string stableId, ulong localSequence);
    public long SimTimeNs { get; }
    public string EventType { get; }
    public int PhaseRank { get; }
    public int EventTypeRank { get; }
    public int ExplicitPriority { get; }
    public string StableId { get; }
    public ulong LocalSequence { get; }
}
public sealed class AuthorityCandidateKeyComparer : IComparer<AuthorityCandidateKey>
{
    public static AuthorityCandidateKeyComparer Instance { get; }
    public int Compare(AuthorityCandidateKey? x, AuthorityCandidateKey? y);
}
public static class AuthorityCandidateOrdering
{
    public static IReadOnlyList<T> Order<T>(IEnumerable<T> candidates, Func<T, AuthorityCandidateKey> keySelector);
}
```

## Authority/SafetyInput.cs

源码：[SafetyInput.cs](../../../src/Monitor.Domain/Authority/SafetyInput.cs) · 命名空间：`Monitor.Domain.Authority`

```csharp
public enum SafetyInputKind
{
    AuthorityEpochChanged,
    PermissionRevoked,
    DeviceFaulted,
    LeaseExpired,
    ShockReleased,
    ShockCancelled,
    EnergyDisarmed,
    SessionPaused,
    SessionEnded,
    AuthorityRecovered,
    ValidQrs,
    HoldThresholdElapsed,
    LeaseRenewed,
}
public sealed record SafetyInput(long SafetyTimeNs, SafetyInputKind Kind, string StableId, ulong LocalSequence, Guid CorrelationId, Guid? InteractionId = null, ulong? AuthorityEpoch = null)
{
}
public sealed class SafetyInputComparer : IComparer<SafetyInput>
{
    public static SafetyInputComparer Instance { get; }
    public int Compare(SafetyInput? x, SafetyInput? y);
}
```

## Common/DomainResult.cs

源码：[DomainResult.cs](../../../src/Monitor.Domain/Common/DomainResult.cs) · 命名空间：`Monitor.Domain.Common`

```csharp
public sealed record DomainRejection(string ReasonCode, string ObjectRef, string FieldPath, ulong CurrentRevision, bool Retryable, string LocalizationKey, Guid CorrelationId)
{
}
public readonly record struct DomainResult<T>(T? Value, DomainRejection? Rejection)
{
    public bool IsAccepted { get; }
}
public static class DomainResult
{
    public static DomainResult<T> Accept<T>(T value);
    public static DomainResult<T> Reject<T>(DomainRejection rejection);
}
```

## Continuity/ConnectionHealthStateMachine.cs

源码：[ConnectionHealthStateMachine.cs](../../../src/Monitor.Domain/Continuity/ConnectionHealthStateMachine.cs) · 命名空间：`Monitor.Domain.Continuity`

```csharp
public sealed class ConnectionHealthException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public enum ConnectionState
{
    Connected,
    Suspect,
    Disconnected,
    Relocking,
}
public enum SamplePlaneProgressHealth
{
    Healthy,
    SamplePlaneStalled,
    Disconnected,
}
public enum ImmediateTransportFailure
{
    WebSocketClose,
    IoError,
    TlsFailure,
}
public enum PlaneProgressUpdateStatus
{
    Accepted,
    IgnoredDuplicateOrStale,
    IgnoredDiscontinuous,
    RequiresResync,
}
public sealed record RealtimeConnectionHealthProfile(ulong HeartbeatIntervalNs, ulong SuspectAfterNs, ulong DisconnectedAfterNs, ulong RequiredPlaneStallNs)
{
    public static RealtimeConnectionHealthProfile Default { get; }
}
public sealed record RequiredPlaneProgressState(Guid PlaneId, ulong? StreamEpoch, ulong? LastBlockSequence, ulong LastProgressRunningTimeNs, SamplePlaneProgressHealth Health)
{
}
public sealed record ConnectionHealthState(RealtimeConnectionHealthProfile Profile, long LastAuthorityMonotonicNs, long LastServerActivityAtNs, ulong RunningTimeNs, bool SessionRunning, ConnectionState ConnectionState, ImmediateTransportFailure? LastImmediateFailure, IReadOnlyList<RequiredPlaneProgressState> RequiredPlanes)
{
}
public sealed class ConnectionHealthStateMachine
{
    public RealtimeConnectionHealthProfile Profile { get; }
    public long LastAuthorityMonotonicNs { get; }
    public ulong RunningTimeNs { get; }
    public bool SessionRunning { get; }
    public ConnectionState ConnectionState { get; }
    public ulong RequiredPlaneDisconnectedAfterNs { get; }
    public static ConnectionHealthStateMachine Start(RealtimeConnectionHealthProfile profile, long startAuthorityMonotonicNs, bool sessionRunning, IReadOnlyList<Guid> requiredPlaneIds);
    public static ConnectionHealthStateMachine Restore(ConnectionHealthState state);
    public ConnectionHealthState Advance(long authorityMonotonicNs, bool sessionRunning);
    public ConnectionHealthState RecordServerActivity(long authorityMonotonicNs);
    public ConnectionHealthState RecordImmediateFailure(ImmediateTransportFailure failure, long authorityMonotonicNs);
    public PlaneProgressUpdateStatus RecordPlaneProgress(Guid planeId, ulong streamEpoch, ulong blockSequence, long authorityMonotonicNs);
    public ConnectionHealthState ResetPlaneAfterResync(Guid planeId, ulong streamEpoch, ulong lastBlockSequence, long authorityMonotonicNs);
    public ConnectionHealthState BeginRelocking(long authorityMonotonicNs);
    public ConnectionHealthState CompleteRelocking(long authorityMonotonicNs);
    public ConnectionHealthState CaptureState();
}
```

## Continuity/DataContinuityStateMachine.cs

源码：[DataContinuityStateMachine.cs](../../../src/Monitor.Domain/Continuity/DataContinuityStateMachine.cs) · 命名空间：`Monitor.Domain.Continuity`

```csharp
public sealed class DataContinuityException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public enum DataAvailability
{
    Authoritative,
    Buffered,
    LocalContinuation,
    NoData,
}
public enum AuthorityState
{
    Authoritative,
    Provisional,
}
public enum LocalContinuationPolicyKind
{
    Disabled,
    Duration,
    UntilScenarioEnd,
}
public enum ShadowVerificationState
{
    NotStarted,
    Pending,
    Verified,
    Rejected,
}
public enum LocalContinuationFailure
{
    CapsuleInvalid,
    ConsistencyMismatch,
    PerformanceInsufficient,
}
public enum ContinuationStopReason
{
    AwaitingShadowVerification,
    PolicyDisabled,
    ShadowMismatch,
    CapsuleInvalid,
    ConsistencyMismatch,
    PerformanceInsufficient,
    DurationExpired,
    ScenarioEnded,
    AwaitingRelockCommit,
}
public enum ConnectivityBannerMessage
{
    None,
    ConnectionSuspect,
    PlayingVerifiedBuffer,
    LocalContinuationProvisional,
    NoData,
    Relocking,
}
public enum ConnectivityTechnicalAudioPolicy
{
    Silent,
}
public sealed record LocalContinuationPolicy(LocalContinuationPolicyKind Kind, ulong DurationNs)
{
    public const ulong MaximumDurationNs = 1_800_000_000_000;
    public static LocalContinuationPolicy Disabled { get; }
    public static LocalContinuationPolicy DefaultDuration { get; }
    public static LocalContinuationPolicy UntilScenarioEnd { get; }
}
public sealed record ConnectivityCriticalBannerProjection(bool Visible, ConnectivityBannerMessage Message, ConnectivityTechnicalAudioPolicy AudioPolicy)
{
}
public sealed record DataContinuityState(LocalContinuationPolicy Policy, long LastAuthorityMonotonicNs, ConnectionState ConnectionState, DataAvailability DataAvailability, AuthorityState AuthorityState, ShadowVerificationState ShadowVerification, bool BufferedDataRemaining, long? DisconnectedAtAuthorityMonotonicNs, long? LocalContinuationStartedAtAuthorityMonotonicNs, long? NoDataSinceAuthorityMonotonicNs, bool ScenarioEnded, ContinuationStopReason? ContinuationStopReason)
{
}
public sealed class DataContinuityStateMachine
{
    public LocalContinuationPolicy Policy { get; }
    public long LastAuthorityMonotonicNs { get; }
    public ConnectionState ConnectionState { get; }
    public DataAvailability DataAvailability { get; }
    public AuthorityState AuthorityState { get; }
    public static DataContinuityStateMachine Start(LocalContinuationPolicy policy, long startAuthorityMonotonicNs);
    public static DataContinuityStateMachine Restore(DataContinuityState state);
    public DataContinuityState ObserveHealthyConnection(ConnectionState connectionState, long authorityMonotonicNs);
    public DataContinuityState Disconnect(bool hasBufferedData, long authorityMonotonicNs);
    public DataContinuityState RecordShadowVerification(bool matched, long authorityMonotonicNs);
    public DataContinuityState ExhaustAuthoritativeBuffer(long authorityMonotonicNs);
    public DataContinuityState RecordContinuationFailure(LocalContinuationFailure failure, long authorityMonotonicNs);
    public DataContinuityState RecordScenarioEnded(long authorityMonotonicNs);
    public DataContinuityState BeginRelocking(bool hasVerifiedPreroll, long authorityMonotonicNs);
    public DataContinuityState RecordRelockPrerollVerified(long authorityMonotonicNs);
    public DataContinuityState CompleteRelocking(long authorityMonotonicNs);
    public DataContinuityState Advance(long authorityMonotonicNs);
    public ConnectivityCriticalBannerProjection CaptureBanner();
    public DataContinuityState CaptureState();
}
```

## Continuity/NoDataPresentationStateMachine.cs

源码：[NoDataPresentationStateMachine.cs](../../../src/Monitor.Domain/Continuity/NoDataPresentationStateMachine.cs) · 命名空间：`Monitor.Domain.Continuity`

```csharp
public sealed class NoDataPresentationException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public enum LiveTracePresentation
{
    FollowSource,
    NoDataSweep,
}
public enum TraceSignalMeaning
{
    FollowSource,
    SignalUnavailable,
}
public enum LiveTraceClock
{
    SourcePlayhead,
    PresentationClock,
}
public enum NumericNoDataQuality
{
    FollowSource,
    Stale,
    Disconnected,
}
public enum NumericValuePresentation
{
    FollowSource,
    PreserveLastValue,
    UnavailableMarker,
}
public enum PatientAlarmSuspension
{
    FollowSource,
    SuspendedUnknown,
}
public enum PhysiologyEventPresentation
{
    FollowSource,
    Suppress,
}
public enum PhysiologyAudioPresentation
{
    FollowAlarmPolicy,
    Silent,
}
public sealed record NumericNoDataPolicy(string ParameterId, Guid SourceInstanceId, ulong StaleRetentionNs)
{
}
public sealed record NumericNoDataProjection(string ParameterId, Guid SourceInstanceId, NumericNoDataQuality Quality, NumericValuePresentation ValuePresentation)
{
}
public sealed record NoDataSafetyProjection(DataAvailability DataAvailability, LiveTracePresentation LiveTrace, TraceSignalMeaning TraceMeaning, LiveTraceClock LiveTraceClock, IReadOnlyList<NumericNoDataProjection> Numerics, PatientAlarmSuspension PatientAlarms, PhysiologyEventPresentation PhysiologyEvents, PhysiologyAudioPresentation PhysiologyAudio, bool ClearLiveTraceImmediately, bool PreservePinnedHistory, bool PreserveCalibrationGutter, bool ShowConnectivityExplanation)
{
}
public sealed record NoDataPresentationState(DataContinuityState ContinuityState, long PresentationAuthorityMonotonicNs)
{
}
public sealed class NoDataPresentationStateMachine
{
    public const int MaximumNumericChannels = 128;
    public long PresentationAuthorityMonotonicNs { get; }
    public static NoDataPresentationStateMachine Start(IReadOnlyList<NumericNoDataPolicy> numericPolicies, DataContinuityState continuityState);
    public static NoDataPresentationStateMachine Restore(IReadOnlyList<NumericNoDataPolicy> numericPolicies, NoDataPresentationState state);
    public NoDataSafetyProjection Synchronize(DataContinuityState continuityState);
    public NoDataSafetyProjection Advance(long authorityMonotonicNs);
    public NoDataSafetyProjection CaptureProjection();
    public NoDataPresentationState CaptureState();
}
```

## Continuity/RecoveryHandshakeCoordinator.cs

源码：[RecoveryHandshakeCoordinator.cs](../../../src/Monitor.Domain/Continuity/RecoveryHandshakeCoordinator.cs) · 命名空间：`Monitor.Domain.Continuity`

```csharp
public sealed class RecoveryHandshakeException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public enum ContinuityReportIntegrityState
{
    PendingVerification,
    Verified,
    Rejected,
}
public enum ContinuityReportVerificationResult
{
    Verified,
    Rejected,
}
public enum RecoveryHandshakePhase
{
    AwaitingHello,
    AwaitingReport,
    VerifyingReport,
    ReadyForPlan,
    Rejected,
}
public enum RecoveryHandshakeRejectionReason
{
    ClientReportRejected,
    HostReplayRejected,
    HostReplayMismatch,
}
public sealed record RecoveryHelloMessage(Guid ClientId, Guid SessionId, Guid InstanceId, ulong ObservedAuthorityEpoch, ulong LastAppliedCommitSequence, ulong LastAppliedEventSequence, string? BaseSha256, string? LastDeltaSha256, Guid? LocalContinuationReportId, bool MomentaryInputsCleared)
{
}
public sealed record LocalContinuationSampleFrontier(string ChannelId, ulong LastSampleIndex)
{
}
public sealed record LocalContinuationReportMessage(Guid ReportId, Guid SessionId, Guid ClientId, Guid InstanceId, string BranchId, string BaseSha256, string? LastDeltaSha256, ulong FallbackEpoch, long StartSimTimeNs, long EndSimTimeNs, IReadOnlyList<LocalContinuationSampleFrontier> LastSampleIndexByChannel, ulong LastBlockSequence, ulong LastEventSequence, string RollingStateSha256, string OfflineActionChainSha256, IReadOnlyList<Guid> OfflineActionEventIds, ContinuityReportIntegrityState IntegrityState)
{
}
public sealed record ContinuityReportVerification(Guid ReportId, string BaseSha256, string? LastDeltaSha256, string ReplayedRollingStateSha256, string ReplayedOfflineActionChainSha256, ulong AcceptedThroughCommitSequence, ContinuityReportVerificationResult Result, string? ReasonCode)
{
}
public sealed record RecoveryPlanningInput(Guid ClientId, Guid SessionId, Guid InstanceId, ulong HostAuthorityEpoch, ulong ObservedAuthorityEpoch, ulong LastAppliedCommitSequence, ulong LastAppliedEventSequence, LocalContinuationReportMessage? LocalContinuationReport, ulong AcceptedThroughCommitSequence, bool LocalPredictionVerified)
{
}
public sealed record RecoveryHandshakeState(Guid ClientId, Guid SessionId, Guid InstanceId, ulong HostAuthorityEpoch, RecoveryHandshakePhase Phase, RecoveryHelloMessage? Hello, LocalContinuationReportMessage? Report, ContinuityReportVerification? Verification, RecoveryHandshakeRejectionReason? RejectionReason)
{
}
public sealed class RecoveryHandshakeCoordinator
{
    public const int MaximumChannelFrontiers = 128;
    public const int MaximumOfflineActionEvents = 10_000;
    public Guid ClientId { get; }
    public Guid SessionId { get; }
    public Guid InstanceId { get; }
    public ulong HostAuthorityEpoch { get; }
    public RecoveryHandshakePhase Phase { get; }
    public static RecoveryHandshakeCoordinator Start(Guid clientId, Guid sessionId, Guid instanceId, ulong hostAuthorityEpoch);
    public static RecoveryHandshakeCoordinator Restore(RecoveryHandshakeState state);
    public RecoveryHandshakeState AcceptHello(RecoveryHelloMessage hello);
    public RecoveryHandshakeState AcceptReport(LocalContinuationReportMessage report);
    public RecoveryHandshakeState RecordVerification(ContinuityReportVerification verification);
    public RecoveryPlanningInput CapturePlanningInput();
    public RecoveryHandshakeState CaptureState();
}
```

## Continuity/RecoveryRelockCoordinator.cs

源码：[RecoveryRelockCoordinator.cs](../../../src/Monitor.Domain/Continuity/RecoveryRelockCoordinator.cs) · 命名空间：`Monitor.Domain.Continuity`

```csharp
public sealed class RecoveryRelockException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public enum LocalPredictionDecision
{
    CommitVerified,
    Discard,
    FullSnapshotRequired,
    RejectClient,
}
public enum RecoveryRelockPhase
{
    AwaitingPlan,
    Preparing,
    Prepared,
    Rejected,
    Committed,
}
public enum RecoveryRelockRejectionReason
{
    ClientRejectedByHost,
    PreparationDeadlineMissed,
    PreparationIdentityMismatch,
    LocalPredictionMismatch,
    PrerollMismatch,
    SnapshotMismatch,
    CommitDeadlineMissed,
}
public sealed record RecoveryResyncPlan(Guid PlanId, ulong NewAuthorityEpoch, ulong HostCheckpointSequence, string HostStateSha256, long RelockSimTimeNs, ulong NewStreamEpoch, long PrerollFromSimTimeNs, long PrerollUntilSimTimeNs, LocalPredictionDecision LocalPredictionDecision, string ReasonCode, ulong AcceptedThroughCommitSequence, string? ResumeSnapshotSha256)
{
}
public sealed record RecoveryPreparationEvidence(Guid PlanId, ulong AuthorityEpoch, ulong StreamEpoch, string HostStateSha256, long PrerollFromSimTimeNs, long PrerollUntilSimTimeNs, string PrerollContentSha256, string? ResumeSnapshotSha256, bool LocalPredictionMatchedHost, long PreparedAtSimTimeNs)
{
}
public sealed record RecoveryPreparedAcknowledgement(Guid PlanId, ulong AuthorityEpoch, ulong StreamEpoch, long RelockSimTimeNs, string HostStateSha256, string PrerollContentSha256, string? ResumeSnapshotSha256, long PreparedAtSimTimeNs)
{
}
public sealed record RecoveryCommitCommand(Guid PlanId, ulong AuthorityEpoch, ulong StreamEpoch, long RelockSimTimeNs, ulong CommitSequence, string HostStateSha256, string FirstAuthoritativeBlockSha256)
{
}
public sealed record RecoveryRelockState(Guid ClientId, Guid SessionId, Guid InstanceId, ulong AuthorityEpochBeforePlan, ulong StreamEpochBeforePlan, ulong CommitSequenceBeforePlan, long LastObservedSimTimeNs, RecoveryRelockPhase Phase, long? PlanAcceptedAtSimTimeNs, RecoveryResyncPlan? Plan, RecoveryPreparationEvidence? PreparationEvidence, RecoveryCommitCommand? CommitReceipt, RecoveryRelockRejectionReason? RejectionReason)
{
}
public sealed class RecoveryRelockCoordinator
{
    public const long RelockSlotDurationNs = 200_000_000;
    public const long MinimumPrepareLeadNs = 1_000_000_000;
    public Guid ClientId { get; }
    public Guid SessionId { get; }
    public Guid InstanceId { get; }
    public ulong AuthorityEpochBeforePlan { get; }
    public ulong StreamEpochBeforePlan { get; }
    public ulong CommitSequenceBeforePlan { get; }
    public long LastObservedSimTimeNs { get; }
    public RecoveryRelockPhase Phase { get; }
    public ulong AcceptedAuthorityEpoch { get; }
    public ulong AcceptedStreamEpoch { get; }
    public ulong LastAppliedCommitSequence { get; }
    public static RecoveryRelockCoordinator Start(Guid clientId, Guid sessionId, Guid instanceId, ulong acceptedAuthorityEpoch, ulong acceptedStreamEpoch, ulong lastAppliedCommitSequence, long currentSimTimeNs);
    public static RecoveryRelockCoordinator Restore(RecoveryRelockState state);
    public RecoveryRelockState AcceptPlan(RecoveryResyncPlan plan, long currentSimTimeNs);
    public RecoveryRelockState ReplaceRejectedPlan(RecoveryResyncPlan plan, long currentSimTimeNs);
    public RecoveryRelockState RecordPrepared(RecoveryPreparationEvidence evidence);
    public RecoveryPreparedAcknowledgement CapturePreparedAcknowledgement();
    public RecoveryRelockState Commit(RecoveryCommitCommand command);
    public RecoveryRelockState Advance(long currentSimTimeNs);
    public RecoveryRelockState CaptureState();
}
```

## Identity/IdentityModels.cs

源码：[IdentityModels.cs](../../../src/Monitor.Domain/Identity/IdentityModels.cs) · 命名空间：`Monitor.Domain.Identity`

```csharp
public enum BootstrapLifecycleState
{
    BootstrapOnly,
    Completed,
}
public enum InstitutionAccountState
{
    Active,
    Disabled,
}
public enum InstitutionRole
{
    Admin,
    Teacher,
    Grader,
    Candidate,
}
public enum InstitutionAuthenticationContext
{
    Teaching,
    FormalExam,
}
public enum AuthenticationOutcomeKind
{
    Accepted,
    Rejected,
}
public enum MobileFactorOutcomeKind
{
    NoAuthorityChange,
    RejectConfiguration,
}
public sealed record AuthenticationOutcome(AuthenticationOutcomeKind Kind, string ReasonCode)
{
}
public sealed record MobileFactorOutcome(MobileFactorOutcomeKind Kind, string ReasonCode)
{
}
public sealed record AuthenticationFailureAuditRecord(Guid AuditId, Guid CorrelationId, string CanonicalUsername, string SourceAddress, InstitutionAuthenticationContext Context, Guid? PrincipalId, string ReasonCode, DateTimeOffset RecordedAtUtc)
{
}
public sealed record LocalPrincipal(Guid SessionId, Guid PrincipalId, string AuthenticationMethod, InstitutionRole Role, IReadOnlyList<string> Scopes, DateTimeOffset AuthenticatedAtUtc, DateTimeOffset LastActivityAtUtc, DateTimeOffset SensitiveAuthenticatedAtUtc, DateTimeOffset ExpiresAtUtc)
{
}
public enum SessionAccessOutcomeKind
{
    Allowed,
    SessionExpired,
    InactivityExpired,
    SensitiveReauthenticationRequired,
    InvalidClockState,
}
public sealed record SessionAccessOutcome(SessionAccessOutcomeKind Kind, string ReasonCode)
{
}
public sealed record PasswordVerifier(string Algorithm, int Iterations, string SaltBase64, string SubkeyBase64)
{
}
public sealed record InstitutionAccount(Guid PrincipalId, string Username, string CanonicalUsername, InstitutionRole Role, InstitutionAccountState State, PasswordVerifier PasswordVerifier, ulong Revision)
{
}
public sealed record BootstrapIdentitySnapshot(BootstrapLifecycleState State, PasswordVerifier? BootstrapVerifier, bool RootPermanentlyRetired, ulong Revision)
{
}
public sealed record BootstrapAuditRecord(Guid AuditId, Guid CorrelationId, string EventType, Guid ActivatedPrincipalId, string CanonicalUsername, DateTimeOffset RecordedAtUtc)
{
}
public sealed record BootstrapCompletion(ulong ExpectedRevision, InstitutionAccount Account, BootstrapAuditRecord Audit, bool PermanentlyRetireRoot)
{
}
public enum BootstrapCommitResult
{
    Committed,
    ConcurrencyConflict,
    Failed,
}
public enum BootstrapOutcomeKind
{
    RequireUsernameAndPasswordChange,
    Activated,
    Rejected,
    RetryableConflict,
}
public sealed record BootstrapOutcome(BootstrapOutcomeKind Kind, string ReasonCode, InstitutionAccount? Account = null)
{
}
```

## Identity/IdentityPolicy.cs

源码：[IdentityPolicy.cs](../../../src/Monitor.Domain/Identity/IdentityPolicy.cs) · 命名空间：`Monitor.Domain.Identity`

```csharp
public static class IdentityPolicy
{
    public const int PasswordIterations = 600_000;
    public const int PasswordSaltBytes = 16;
    public const int PasswordSubkeyBytes = 32;
    public const int MinimumPasswordLength = 12;
    public const int MaximumPasswordLength = 128;
    public const int MinimumUsernameLength = 3;
    public const int MaximumUsernameLength = 64;
    public const int FailureLimit = 5;
    public static readonly TimeSpan FailureWindow;
    public static readonly TimeSpan LockDuration;
    public static readonly TimeSpan SessionLifetime;
    public static readonly TimeSpan InactivityLimit;
    public static readonly TimeSpan SensitiveReauthenticationLimit;
    public static string NormalizeUsername(string username);
    public static string CanonicalizeUsername(string username);
    public static bool TryCanonicalizeUsername(string username, out string canonicalUsername);
    public static bool IsUsernameAllowed(string username, out string reasonCode);
    public static bool IsPasswordLengthAllowed(ReadOnlySpan<char> password);
    public static bool IsAuthorized(InstitutionRole role, string scope);
    public static IReadOnlyList<string> GetScopes(InstitutionRole role);
    public static AuthenticationOutcome EvaluateAuthentication(InstitutionAuthenticationContext context, bool passwordValid, InstitutionAccountState accountState, bool locked);
    public static MobileFactorOutcome EvaluateMobileFactor(bool productionEnabled, string? compatibilityClaim);
    public static SessionAccessOutcome EvaluateSession(LocalPrincipal principal, DateTimeOffset nowUtc, bool sensitiveOperation);
    public static LocalPrincipal RecordActivity(LocalPrincipal principal, DateTimeOffset nowUtc);
    public static LocalPrincipal RecordSensitiveReauthentication(LocalPrincipal principal, DateTimeOffset nowUtc);
}
```

## Presentation/Ecg12ScreenTransform.cs

源码：[Ecg12ScreenTransform.cs](../../../src/Monitor.Domain/Presentation/Ecg12ScreenTransform.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed class Ecg12ScreenTransform
{
    public ExactPlotCoordinate Factor { get; }
    public ExactPlotCoordinate Width { get; }
    public ExactPlotCoordinate Height { get; }
    public static Ecg12ScreenTransform ResolveViewport(int pageWidth, int pageHeight, ExactPlotCoordinate viewportWidth, ExactPlotCoordinate viewportHeight);
    public static Ecg12ScreenTransform Resolve(Ecg12ZoomState selection, int pageWidth, int pageHeight, int availableWidth, int availableHeight);
    public ExactPlotCoordinate Forward(ExactPlotCoordinate coordinate);
    public ExactPlotCoordinate Inverse(ExactPlotCoordinate coordinate);
    public ExactPlotCoordinate ForwardAt(ExactPlotCoordinate coordinate, ExactPlotCoordinate origin);
    public ExactPlotCoordinate InverseAt(ExactPlotCoordinate coordinate, ExactPlotCoordinate origin);
}
```

## Presentation/Ecg12ThemeSelection.cs

源码：[Ecg12ThemeSelection.cs](../../../src/Monitor.Domain/Presentation/Ecg12ThemeSelection.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public enum Ecg12Theme
{
    MonitorDarkGreen,
    PaperGridBlack
}
public sealed record Ecg12ThemeState(Ecg12Theme Theme)
{
}
public sealed record Ecg12ThemeDisplay(Ecg12Theme Theme, SystemViewCommandAssessmentPolicy Policy, bool CanSelect, string ReasonCode)
{
}
public sealed class Ecg12ThemeSelectionException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed class Ecg12ThemeSelection
{
    public Ecg12ThemeSelection(Ecg12Theme initialTheme, SystemViewCommandAssessmentPolicy policy, bool allowLocalSelection);
    public Ecg12Theme Theme { get; private set; }
    public void UpdatePolicy(SystemViewCommandAssessmentPolicy policy, bool allowLocalSelection);
    public Ecg12ThemeDisplay CaptureDisplay();
    public void Select(Ecg12Theme theme);
    public Ecg12ThemeState CaptureState();
    public static Ecg12ThemeSelection Restore(Ecg12ThemeState state, SystemViewCommandAssessmentPolicy currentPolicy, bool allowLocalSelection);
}
```

## Presentation/Ecg12ViewAdmission.cs

源码：[Ecg12ViewAdmission.cs](../../../src/Monitor.Domain/Presentation/Ecg12ViewAdmission.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public enum Ecg12RecordContext
{
    ActiveInstance,
    IndependentCapturedRecord,
}
public sealed class Ecg12ViewAdmissionException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record Ecg12ViewAdmissionDecision(bool MayEnter, string ReasonCode, bool RequiresGlobalSafetyOverlay, bool InheritPatientAlarmAggregate)
{
}
public static class Ecg12ViewAdmission
{
    public static Ecg12ViewAdmissionDecision Evaluate(Ecg12RecordContext context, TemporalViewMode viewMode, bool canPreserveGlobalSafetyOverlay);
}
```

## Presentation/Ecg12ZoomSelection.cs

源码：[Ecg12ZoomSelection.cs](../../../src/Monitor.Domain/Presentation/Ecg12ZoomSelection.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public enum Ecg12ZoomMode
{
    FitPage,
    ActualSize,
    ExplicitScale
}
public sealed record Ecg12ZoomState(Ecg12ZoomMode Mode, uint Numerator, uint Denominator)
{
}
public sealed record Ecg12ZoomDisplay(Ecg12ZoomState Selection, SystemViewCommandAssessmentPolicy Policy, bool CanSelect, string ReasonCode)
{
}
public sealed class Ecg12ZoomSelectionException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed class Ecg12ZoomSelection
{
    public Ecg12ZoomSelection(Ecg12ZoomState initial, SystemViewCommandAssessmentPolicy policy);
    public Ecg12ZoomState Selection { get; private set; }
    public void UpdatePolicy(SystemViewCommandAssessmentPolicy policy);
    public Ecg12ZoomDisplay CaptureDisplay();
    public void Select(Ecg12ZoomState selection);
    public Ecg12ZoomState CaptureState();
    public static Ecg12ZoomSelection Restore(Ecg12ZoomState state, SystemViewCommandAssessmentPolicy currentPolicy);
}
```

## Presentation/EcgCalibrationGeometry.cs

源码：[EcgCalibrationGeometry.cs](../../../src/Monitor.Domain/Presentation/EcgCalibrationGeometry.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed class EcgCalibrationGeometryException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record EcgCalibrationPoint(SweepPixelPosition X, EcgVerticalPosition Y)
{
}
public sealed record EcgCalibrationGeometrySnapshot(string GroupId, ulong SweepEpoch, ulong PresentationClockRevision, int GutterLeftPixels, int GutterRightPixels, IReadOnlyList<EcgCalibrationPoint> Points)
{
}
public static class EcgCalibrationGeometry
{
    public const ulong PulseDurationNs = 200_000_000;
    public static EcgCalibrationGeometrySnapshot Compose(SweepStateProjectionState state, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale verticalScale, int gutterLeftPixels, int pulseLeftPixels);
}
```

## Presentation/EcgManualMeasurement.cs

源码：[EcgManualMeasurement.cs](../../../src/Monitor.Domain/Presentation/EcgManualMeasurement.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed class EcgManualMeasurementException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record EcgManualCursor(long DataTimeNs, long NumeratorMicrovolts, uint Denominator)
{
}
public sealed record EcgMeasurementRatio(BigInteger Numerator, BigInteger Denominator)
{
}
public sealed record EcgManualMeasurementResult(EcgMeasurementRatio ElapsedMilliseconds, EcgMeasurementRatio AmplitudeChangeMillivolts, EcgMeasurementRatio? AuxiliaryRatePerMinute)
{
}
public static class EcgManualMeasurement
{
    public static EcgManualMeasurementResult Calculate(EcgManualCursor first, EcgManualCursor second, bool allowAuxiliaryRate);
}
```

## Presentation/EcgPaperGridCalibration.cs

源码：[EcgPaperGridCalibration.cs](../../../src/Monitor.Domain/Presentation/EcgPaperGridCalibration.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed record EcgPaperScale(uint SpeedNumeratorMmPerSecond, uint SpeedDenominator, uint GainNumeratorMmPerMillivolt, uint GainDenominator)
{
}
public static class EcgPaperGridCalibration
{
    public static EcgPaperGridPlan Resolve(int plotLeftPixels, int plotWidthPixels, ulong visibleDurationNs, EcgVerticalScale verticalScale, EcgPaperScale paperScale, int originXPixels, int originYPixels);
}
```

## Presentation/EcgPaperGridGeometry.cs

源码：[EcgPaperGridGeometry.cs](../../../src/Monitor.Domain/Presentation/EcgPaperGridGeometry.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed record EcgPaperGridPlan(int LeftPixels, int TopPixels, int WidthPixels, int HeightPixels, int OriginXPixels, int OriginYPixels, uint MinorSpacingNumerator, uint MinorSpacingDenominator)
{
}
public sealed record EcgPaperGridLine(bool IsVertical, ExactPlotCoordinate Position, bool IsMajor)
{
}
public sealed class EcgPaperGridException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public static class EcgPaperGridGeometry
{
    public static IReadOnlyList<EcgPaperGridLine> Build(EcgPaperGridPlan plan, int maximumLines, CancellationToken cancellationToken = default);
}
```

## Presentation/EcgRawVoltageCalibration.cs

源码：[EcgRawVoltageCalibration.cs](../../../src/Monitor.Domain/Presentation/EcgRawVoltageCalibration.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed class EcgRawVoltageException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record EcgRawVoltageCalibration(int ScaleNumerator, uint ScaleDenominator, int OffsetNumerator, uint OffsetDenominator, string UnitCode)
{
    public void Validate();
    public EcgSampleVoltage Convert(short raw);
}
```

## Presentation/EcgVerticalGeometry.cs

源码：[EcgVerticalGeometry.cs](../../../src/Monitor.Domain/Presentation/EcgVerticalGeometry.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed class EcgVerticalGeometryException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record EcgVerticalScale(int PlotTopPixels, int PlotHeightPixels, int ZeroBaselinePixels, uint PixelsPerMillivoltNumerator, uint PixelsPerMillivoltDenominator)
{
}
public enum VerticalPlotRelation
{
    AbovePlot,
    WithinPlot,
    BelowPlot,
}
public sealed record EcgVerticalPosition(Int128 PixelNumerator, Int128 PixelDenominator, VerticalPlotRelation Relation)
{
}
public static class EcgVerticalGeometry
{
    public static EcgVerticalPosition MapMicrovolts(EcgVerticalScale scale, long amplitudeNumeratorMicrovolts, uint amplitudeDenominator);
}
```

## Presentation/FillOnceThenHoldStateMachine.cs

源码：[FillOnceThenHoldStateMachine.cs](../../../src/Monitor.Domain/Presentation/FillOnceThenHoldStateMachine.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed class FillOnceThenHoldException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record FillOnceThenHoldPlan(string GroupId, string RecordRef, ulong SweepEpoch, ulong PresentationClockRevision, long RecordStartDataSimTimeNs, ulong RecordDurationNs, ulong RequiredRecordHistoryNs, IReadOnlyList<string> SlotIds)
{
}
public sealed record FillOnceCoverage(long RecordStartDataSimTimeNs, long AcquiredThroughDataSimTimeNs, long RecordEndExclusiveDataSimTimeNs, ulong AcquiredDurationNs, ulong RemainingDurationNs, uint WriteHeadPhasePpm, bool Complete)
{
}
public sealed record PinnedRecordRange(string GroupId, string RecordRef, ulong SweepEpoch, long StartDataSimTimeNs, long EndExclusiveDataSimTimeNs, IReadOnlyList<string> SlotIds)
{
}
public sealed record FillOnceThenHoldState(FillOnceThenHoldPlan Plan, ulong PlanRevision, ulong SweepRevision, long LastPresentationNs, SessionRunState SessionRunState, DataContinuityState ContinuityState, long LivePlayheadDataSimTimeNs, TemporalViewMode TemporalViewMode)
{
}
public sealed class FillOnceThenHoldStateMachine
{
    public const int StandardEcgSlotCount = 12;
    public static FillOnceThenHoldStateMachine Start(FillOnceThenHoldPlan plan, ulong planRevision, ulong sweepRevision, SessionRunState sessionRunState, DataContinuityState continuityState, long presentationNs, long playheadDataSimTimeNs);
    public static FillOnceThenHoldStateMachine Restore(FillOnceThenHoldState state);
    public SweepStateProjectionSnapshot Advance(long presentationNs, long livePlayheadDataSimTimeNs);
    public SweepStateProjectionSnapshot ChangeRunState(SessionRunState sessionRunState, long presentationNs, long livePlayheadDataSimTimeNs);
    public SweepStateProjectionSnapshot SynchronizeContinuity(DataContinuityState continuityState, long presentationNs, long livePlayheadDataSimTimeNs);
    public SweepStateProjectionSnapshot CaptureProjection();
    public FillOnceCoverage CaptureCoverage();
    public PinnedRecordRange CapturePinnedRecordRange();
    public FillOnceThenHoldState CaptureState();
}
```

## Presentation/NoDataSweepStateMachine.cs

源码：[NoDataSweepStateMachine.cs](../../../src/Monitor.Domain/Presentation/NoDataSweepStateMachine.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed class NoDataSweepException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record NoDataSweepPlan(string GroupId, ulong SweepEpoch, ulong PresentationClockRevision, long CycleOriginPresentationNs, ulong VisibleDurationNs, ulong EraseGapNs, ulong RequiredRenderHistoryNs)
{
}
public sealed record SweepCoverageInterval(ulong StartOffsetNs, ulong EndOffsetNs)
{
}
public sealed record NoDataSweepCoverage(string GroupId, ulong SweepEpoch, ulong PresentationClockRevision, ulong CycleIndex, ulong WriteHeadOffsetNs, uint WriteHeadPhasePpm, ulong CoveredDurationNs, ulong RemainingPatientTraceDurationNs, bool FullyCovered, IReadOnlyList<SweepCoverageInterval> CoveredIntervals)
{
}
public sealed record NoDataSweepState(NoDataSweepPlan Plan, long NoDataSinceAuthorityMonotonicNs, long StartedAtPresentationNs, long SweepClockAtStartNs, long CurrentPresentationNs)
{
}
public sealed class NoDataSweepStateMachine
{
    public const uint PhasePartsPerMillion = 1_000_000;
    public long CurrentPresentationNs { get; }
    public static NoDataSweepStateMachine Start(NoDataSweepPlan plan, DataContinuityState continuityState, long startedAtPresentationNs);
    public static NoDataSweepStateMachine StartAtSweepClock(NoDataSweepPlan plan, DataContinuityState continuityState, long startedAtPresentationNs, long sweepClockAtStartNs);
    public static NoDataSweepStateMachine Restore(DataContinuityState continuityState, NoDataSweepState state);
    public NoDataSweepCoverage Advance(long presentationNs);
    public NoDataSweepCoverage CaptureCoverage();
    public NoDataSweepState CaptureState();
}
```

## Presentation/SweepColumnCoverage.cs

源码：[SweepColumnCoverage.cs](../../../src/Monitor.Domain/Presentation/SweepColumnCoverage.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed record SweepColumnCoverageRun(int StartColumn, int EndExclusiveColumn, SweepTraceRegionKind Kind, ulong CoverageNumerator, ulong CoverageDenominator)
{
}
public sealed record SweepColumnCoverageSnapshot(SweepPlotGeometrySnapshot Geometry, IReadOnlyList<SweepColumnCoverageRun> Runs)
{
}
public static class SweepColumnCoverage
{
    public static SweepColumnCoverageSnapshot Compose(SweepStateProjectionState state, int plotLeftPixels, int plotWidthPixels);
}
```

## Presentation/SweepColumnSegmentSplitter.cs

源码：[SweepColumnSegmentSplitter.cs](../../../src/Monitor.Domain/Presentation/SweepColumnSegmentSplitter.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed record SweepColumnSegment(int ColumnPixels, ClippedSweepSegment Segment)
{
}
public static class SweepColumnSegmentSplitter
{
    public static IReadOnlyList<SweepColumnSegment> Split(ClippedSweepSegment segment, int maximumPieces, CancellationToken cancellationToken = default);
}
```

## Presentation/SweepPathBuilder.cs

源码：[SweepPathBuilder.cs](../../../src/Monitor.Domain/Presentation/SweepPathBuilder.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed class SweepPathException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record SweepSampleSource(Guid SessionId, Guid InstanceId, Guid ChannelId, ulong TimebaseEpoch, ulong StreamEpoch, ulong ConfigurationRevision, ulong SweepEpoch, ulong PresentationClockRevision, uint SampleRateNumerator, uint SampleRateDenominator)
{
}
public sealed record EcgSampleVoltage(long NumeratorMicrovolts, uint Denominator)
{
}
public sealed record SweepPathSample(SweepSampleSource Source, ulong SampleIndex, ulong CycleIndex, bool Drawable, SweepSamplePoint Point, EcgSampleVoltage? Voltage = null, ulong? CycleOffsetNs = null)
{
}
public sealed record SweepPathState(SweepPlotRegion Region, int PlotTopPixels, int PlotHeightPixels, SweepPathSample? Previous)
{
}
public sealed record SweepPathAppendResult(string ReasonCode, ClippedSweepSegment? Segment)
{
}
public sealed class SweepPathBuilder
{
    public static SweepPathBuilder Start(SweepPlotRegion region, int topPixels, int heightPixels);
    public static SweepPathBuilder Restore(SweepPathState state);
    public SweepPathAppendResult Append(SweepPathSample sample);
    public SweepPathState CaptureState();
}
```

## Presentation/SweepPlotGeometry.cs

源码：[SweepPlotGeometry.cs](../../../src/Monitor.Domain/Presentation/SweepPlotGeometry.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed class SweepPlotGeometryException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record SweepPixelPosition(int WholePixels, ulong FractionNumerator, ulong FractionDenominator)
{
}
public sealed record SweepPlotRegion(SweepPixelPosition StartX, SweepPixelPosition EndExclusiveX, SweepTraceRegionKind Kind)
{
}
public sealed record SweepPlotGeometrySnapshot(string GroupId, ulong SweepEpoch, ulong PresentationClockRevision, ulong VisibleDurationNs, int PlotLeftPixels, int PlotWidthPixels, IReadOnlyList<SweepPlotRegion> Regions)
{
}
public static class SweepPlotGeometry
{
    public static SweepPixelPosition MapSampleOffset(ulong offsetNs, ulong durationNs, int leftPixels, int widthPixels);
    public static SweepPlotGeometrySnapshot Compose(SweepStateProjectionState state, int plotLeftPixels, int plotWidthPixels);
}
```

## Presentation/SweepSegmentClipper.cs

源码：[SweepSegmentClipper.cs](../../../src/Monitor.Domain/Presentation/SweepSegmentClipper.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed class SweepSegmentClipException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record SweepSamplePoint(SweepPixelPosition X, EcgVerticalPosition Y)
{
}
public sealed record ExactPlotCoordinate(BigInteger Numerator, BigInteger Denominator)
{
}
public sealed record ClippedSweepPoint(ExactPlotCoordinate X, ExactPlotCoordinate Y)
{
}
public sealed record ClippedSweepSegment(ClippedSweepPoint Start, ClippedSweepPoint End)
{
}
public static class SweepSegmentClipper
{
    public static ClippedSweepSegment? Clip(SweepPlotRegion region, int plotTopPixels, int plotHeightPixels, SweepSamplePoint start, SweepSamplePoint end);
}
```

## Presentation/SweepStateProjectionStateMachine.cs

源码：[SweepStateProjectionStateMachine.cs](../../../src/Monitor.Domain/Presentation/SweepStateProjectionStateMachine.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed class SweepStateProjectionException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public enum SessionRunState
{
    Running,
    Paused,
    Stopped,
}
public enum TemporalViewMode
{
    LiveSweep,
    AcquisitionFill,
    CapturedRecord,
    FrozenSnapshot,
    HistoricalReview,
}
public enum TraceHistoryPresentation
{
    LiveHistory,
    RecordFill,
    PinnedOriginalRange,
}
public enum TransientReplayPolicy
{
    FollowLiveSource,
    Suppress,
}
public sealed record SweepStateProjectionSnapshot(ulong PlanRevision, ulong SweepRevision, ulong PresentationClockRevision, SessionRunState SessionRunState, TemporalViewMode TemporalViewMode, DataAvailability DataAvailability, AuthorityState AuthorityState, long PlayheadDataSimTimeNs, ulong CycleIndex, uint WriteHeadPhasePpm, long? FreezeAnchorSimTimeNs, string? ReviewSegmentRef, long? NoDataSinceAuthorityMonotonicNs, TraceHistoryPresentation TraceHistory, TransientReplayPolicy TransientReplayPolicy, NoDataSweepCoverage? NoDataCoverage)
{
}
public sealed record SweepStateProjectionState(NoDataSweepPlan Plan, ulong PlanRevision, ulong SweepRevision, long LastPresentationNs, long LiveSweepClockNs, SessionRunState SessionRunState, TemporalViewMode TemporalViewMode, DataContinuityState ContinuityState, long LivePlayheadDataSimTimeNs, long ViewPlayheadDataSimTimeNs, long? FreezeAnchorSimTimeNs, string? ReviewSegmentRef, long? PinnedSweepClockNs, NoDataSweepState? NoDataSweepState)
{
}
public sealed class SweepStateProjectionStateMachine
{
    public static SweepStateProjectionStateMachine Start(NoDataSweepPlan plan, ulong planRevision, ulong sweepRevision, SessionRunState sessionRunState, DataContinuityState continuityState, long presentationNs, long playheadDataSimTimeNs);
    public static SweepStateProjectionStateMachine Restore(SweepStateProjectionState state);
    public SweepStateProjectionSnapshot Advance(long presentationNs, long livePlayheadDataSimTimeNs);
    public SweepStateProjectionSnapshot ReplacePlanAtCycleBoundary(NoDataSweepPlan plan, ulong planRevision, ulong sweepRevision, long presentationNs, long livePlayheadDataSimTimeNs);
    public SweepStateProjectionSnapshot SynchronizeContinuity(DataContinuityState continuityState, long presentationNs, long livePlayheadDataSimTimeNs);
    public SweepStateProjectionSnapshot ChangeRunState(SessionRunState sessionRunState, long presentationNs, long livePlayheadDataSimTimeNs);
    public SweepStateProjectionSnapshot EnterFrozen(long presentationNs, long livePlayheadDataSimTimeNs);
    public SweepStateProjectionSnapshot ExitFrozen(long presentationNs, long livePlayheadDataSimTimeNs);
    public SweepStateProjectionSnapshot EnterReview(string reviewSegmentRef, long reviewPlayheadDataSimTimeNs, long presentationNs, long livePlayheadDataSimTimeNs);
    public SweepStateProjectionSnapshot SeekReview(long reviewPlayheadDataSimTimeNs);
    public SweepStateProjectionSnapshot ExitReview(long presentationNs, long livePlayheadDataSimTimeNs);
    public SweepStateProjectionSnapshot CaptureProjection();
    public SweepStateProjectionState CaptureState();
}
```

## Presentation/SweepTraceComposition.cs

源码：[SweepTraceComposition.cs](../../../src/Monitor.Domain/Presentation/SweepTraceComposition.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public sealed class SweepTraceCompositionException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public enum SweepTraceRegionKind
{
    RetainSourceTrace,
    NoDataBaseline,
    BackgroundEraseGap,
    PinnedHistory,
}
public sealed record SweepTraceRegion(ulong StartOffsetNs, ulong EndExclusiveOffsetNs, SweepTraceRegionKind Kind)
{
}
public sealed record SweepTraceCompositionSnapshot(string GroupId, ulong SweepEpoch, ulong PresentationClockRevision, ulong VisibleDurationNs, IReadOnlyList<SweepTraceRegion> Regions)
{
}
public static class SweepTraceComposition
{
    public static SweepTraceCompositionSnapshot Compose(SweepStateProjectionState state);
}
```

## Presentation/SystemViewCommandAssessmentPolicy.cs

源码：[SystemViewCommandAssessmentPolicy.cs](../../../src/Monitor.Domain/Presentation/SystemViewCommandAssessmentPolicy.cs) · 命名空间：`Monitor.Domain.Presentation`

```csharp
public enum SystemViewCommandAssessmentPolicy
{
    Enabled,
    Disabled,
    CourseLocked,
}
```

## Therapy/TherapyController.cs

源码：[TherapyController.cs](../../../src/Monitor.Domain/Therapy/TherapyController.cs) · 命名空间：`Monitor.Domain.Therapy`

```csharp
public sealed class TherapyController
{
    public TherapyController(Guid instanceId, ulong authorityEpoch, DefibrillationMode mode);
    public TherapyState State { get; }
    public DomainResult<TherapyTransition> BeginCharge(long safetyTimeNs, Guid correlationId);
    public DomainResult<TherapyTransition> MarkChargeReady(long safetyTimeNs, Guid correlationId);
    public DomainResult<TherapyTransition> PressShock(Guid interactionId, string clientId, ulong authorityEpoch, long safetyTimeNs, long holdThresholdNs, long expiresAtSafetyNs, Guid correlationId);
    public DomainResult<TherapyTransition> ApplySafetyInputs(IEnumerable<SafetyInput> inputs);
}
```

## Therapy/TherapyState.cs

源码：[TherapyState.cs](../../../src/Monitor.Domain/Therapy/TherapyState.cs) · 命名空间：`Monitor.Domain.Therapy`

```csharp
public enum DefibrillationAttemptState
{
    Idle,
    Charging,
    Charged,
    DischargeRequested,
    AwaitingSync,
    Delivered,
    Disarmed,
    Expired,
    Failed,
}
public enum EnergyState
{
    Idle,
    Charging,
    Ready,
    Disarmed,
    Failed,
}
public enum ShockRequestState
{
    None,
    WaitingNextQrs,
    Cancelled,
}
public enum MomentaryLeaseState
{
    Pressed,
    Held,
    Released,
    Cancelled,
    Expired,
}
public enum DefibrillationMode
{
    ManualAsynchronous,
    ManualSynchronized,
}
public sealed record MomentaryTherapyLease(Guid InteractionId, string ClientId, Guid InstanceId, ulong AuthorityEpoch, ulong GrantedStateRevision, long PressedAtSafetyNs, long HoldThresholdNs, long ExpiresAtSafetyNs, ulong RenewSequence, MomentaryLeaseState State)
{
}
public sealed record TherapyState(ulong Revision, ulong AuthorityEpoch, DefibrillationMode Mode, DefibrillationAttemptState Attempt, EnergyState Energy, ShockRequestState Request, MomentaryTherapyLease? Lease, ulong DeliveredCount)
{
    public static TherapyState Initial(ulong authorityEpoch, DefibrillationMode mode);
}
public sealed record TherapyFact(string EventType, long SafetyTimeNs, Guid CorrelationId)
{
}
public sealed record TherapyTransition(TherapyState State, IReadOnlyList<TherapyFact> Facts)
{
}
```
