# Monitor.Network 公开声明索引

[课堂网络接口](../classroom-network.md) · [控制与配置](classroom-control.md) · [校时与恢复](classroom-recovery.md) · [资源与私有分支](classroom-resources.md) · [正式考试集群](../exam-cluster.md)

以下声明按源文件排列，命名空间均为 `Monitor.Network`。位置 record 的参数定义同名属性。Host 接管课堂入场服务和保留环，调用者在 Host 运行期间保持 TLS 证书和已认证的本地身份可用。学生连接使用的机构根证书需在连接期间保持可用。

## ClassroomAdmission.cs

[源码](../../../src/Monitor.Network/ClassroomAdmission.cs)

```csharp
public sealed record ClassroomAdmissionPolicy(int InvitationLength = 8, int InvitationLifetimeSeconds = 300, int TokenLifetimeSeconds = 3600, int RefreshWindowSeconds = 600, int SourceFailuresPerMinute = 8, int HostFailuresPerMinute = 120)
{
}
public sealed class ClassroomAdmission : IDisposable
{
    public ClassroomAdmissionPolicy Policy { get; }
    public ulong SessionEpoch { get; }
    public bool IsOpen { get; }
    public void CloseInvitations();
    public void ReopenInvitations(DateTimeOffset? notAfter = null);
    public ClassroomAdmission(Guid sessionId, Guid instanceId, TimeProvider? timeProvider = null, ClassroomAdmissionPolicy? policy = null, Guid? cohortId = null);
    public Guid SessionId { get; }
    public Guid CohortId { get; }
    public Guid InstanceId { get; }
    public string IssueInvitation(Guid studentId);
    public bool TryRedeem(string code, string source, [NotNullWhen(true)] out string? token);
    public bool TryAuthorize(string token, out Guid studentId, out DateTimeOffset expiresAt);
    public void Dispose();
}
```

## ClassroomArtifact.cs

[源码](../../../src/Monitor.Network/ClassroomArtifact.cs)

```csharp
public sealed record ClassroomArtifactDescriptor(string Sha256, int LengthBytes, string MediaType)
{
    public const string StudyRecordMediaType = "application/vnd.simonitor.waveform-record.v1";
    public const string ImageMediaType = "image/png";
    public const int MaximumLengthBytes = 16 * 1024 * 1024;
}
public sealed partial class ClassroomHost
{
    public ClassroomArtifactDescriptor PublishStudyRecord(WaveformRecordArchive record, Guid? studentId = null);
    public ClassroomArtifactDescriptor PublishLearnerImage(ReadOnlySpan<byte> png, Guid? studentId = null);
    public bool RemoveArtifact(string sha256);
}
```

## ClassroomArtifactClient.cs

[源码](../../../src/Monitor.Network/ClassroomArtifactClient.cs)

```csharp
public sealed partial class ClassroomStudentClient
{
    public Task<IReadOnlyList<ClassroomArtifactDescriptor>> ListArtifactsAsync(CancellationToken cancellationToken = default);
    public Task<byte[]> DownloadArtifactAsync(ClassroomArtifactDescriptor descriptor, CancellationToken cancellationToken = default);
    public Task<WaveformRecordArchive> DownloadStudyRecordAsync(ClassroomArtifactDescriptor descriptor, CancellationToken cancellationToken = default);
}
```

## ClassroomBootstrap.cs

[源码](../../../src/Monitor.Network/ClassroomBootstrap.cs)

```csharp
public sealed partial class ClassroomBootstrap : IDisposable
{
    public IBootstrapIdentityRepository Repository { get; }
    public LocalPrincipal Principal { get; }
    public static void Initialize( string databasePath, ReadOnlySpan<byte> masterKey, string adminName, ReadOnlySpan<char> bootstrapPassword, ReadOnlySpan<char> adminPassword, string blockedPasswordHashesPath);
    public static ClassroomBootstrap AuthenticateHost( string databasePath, ReadOnlySpan<byte> masterKey, string username, ReadOnlySpan<char> password);
    public void Dispose();
}
```

## ClassroomBranchArchive.cs

[源码](../../../src/Monitor.Network/ClassroomBranchArchive.cs)

```csharp
public sealed record ClassroomBranchArchive(Guid StudentId, Guid SharedInstanceId, long ForkSimTimeNs, ClassroomControlSnapshot Device, ClassroomTelemetrySnapshot Telemetry, WaveformBlockRingState RetainedWaveforms, PhysiologyWaveformGroupState Patient, LiveMeasurementSnapshot Measurements, IReadOnlyList<JsonElement> RetainedEvents)
{
}
public sealed partial class ClassroomHost
{
    public ClassroomBranchArchive? ReadArchivedBranch(Guid instanceId);
}
public sealed partial class ClassroomBootstrap
{
    public ClassroomBranchArchive? ReadArchivedBranch(Guid session, Guid instanceId);
}
```

## ClassroomBranchClient.cs

[源码](../../../src/Monitor.Network/ClassroomBranchClient.cs)

```csharp
public sealed partial class ClassroomStudentClient
{
    public Task<ClassroomBranchAssignment> GetBranchAssignmentAsync(CancellationToken cancellationToken = default);
    public Task<ClassroomStudentClient> ConnectAssignedBranchAsync(CancellationToken cancellationToken = default);
    public Task<ClassroomStudentClient> ConnectAssignedBranchAsync(JsonElement? presentation, CancellationToken cancellationToken = default);
}
```

## ClassroomBranches.cs

[源码](../../../src/Monitor.Network/ClassroomBranches.cs)

```csharp
public enum ClassroomBranchDisposition { Archive, Discard }
public sealed record ClassroomBranchAssignment(Guid StudentId, Guid SessionId, Guid SharedInstanceId, Guid InstanceId, string State, long EffectiveSimTimeNs, string? Reason)
{
}
public sealed partial class ClassroomHost
{
    public ClassroomBranchAssignment GetBranchAssignment(Guid studentId);
    public string? GetBranchFailure(Guid studentId);
    public ClassroomBranchAssignment SchedulePrivateBranch(Guid studentId, long? effectiveSimTimeNs = null);
    public ClassroomBranchAssignment ScheduleSharedReturn(Guid studentId, ClassroomBranchDisposition disposition, long? effectiveSimTimeNs = null);
    public ClassroomTelemetrySnapshot GetPrivateTelemetry(Guid studentId);
    public ClassroomControlSnapshot GetPrivateControlSnapshot(Guid studentId);
    public ClassroomScheduledControl SchedulePrivateConfiguration(Guid studentId, Guid changeId, ulong basedOnConfigurationRevision, long effectiveSimTimeNs, ClassroomConfigurationContent content, IReadOnlyList<Guid> requiredClients, int preparedQuorum, ClassroomChangeConflict conflict = ClassroomChangeConflict.RejectOnConflict, bool requireTeacherConfirmation = false);
}
```

## ClassroomClockCalibration.cs

[源码](../../../src/Monitor.Network/ClassroomClockCalibration.cs)

```csharp
public sealed record ClassroomClockEstimate(int SampleCount, long ClientReferenceNs, long HostOffsetNs, double DriftPpm, long RoundTripP95Ns, long JitterP99Ns, long UncertaintyNs, long RecommendedPlayoutDelayNs, bool RequiresResynchronization)
{
    public long HostTimeAt(long clientMonotonicNs);
}
public sealed class ClassroomClockCalibration
{
    public const int InitialSampleCount = 16;
    public const int WindowSize = 32;
    public ClassroomClockCalibration(long minimumDelayNs = 250_000_000, long maximumDelayNs = 1_500_000_000);
    public ClassroomClockEstimate Estimate { get; private set; }
    public ClassroomClockEstimate Add(ClassroomClockSample sample);
    public static long SelectCohortDelayNs(IEnumerable<ClassroomClockEstimate> estimates, long? fixedDelayNs = null);
}
```

## ClassroomConfigurationObserver.cs

[源码](../../../src/Monitor.Network/ClassroomConfigurationObserver.cs)

```csharp
public sealed class ClassroomConfigurationObserver(ClassroomStudentClient client, Func<ClassroomScheduledControl, string?>? prepare = null) : IDisposable
{
    public Task PollAsync(CancellationToken cancellationToken = default);
    public Task ObserveDeliveredAsync(WaveformEnvelope block, long actualSimTimeNs, CancellationToken cancellationToken = default);
    public void Dispose();
}
```

## ClassroomControlAuthority.cs

[源码](../../../src/Monitor.Network/ClassroomControlAuthority.cs)

```csharp
public sealed record ClassroomScheduledControl(Guid ChangeId, ulong BaseConfigurationRevision, ulong CommitSeq, long EffectiveSimTimeNs, string PublicDiffSha256, string PreparationPolicy, string State, string[] AllowedActionIds)
{
    public ClassroomConfigurationContent? Content { get; init; }
    public bool RequiresTeacherConfirmation { get; init; }
}
```

## ClassroomControlClient.cs

[源码](../../../src/Monitor.Network/ClassroomControlClient.cs)

```csharp
public sealed partial class ClassroomStudentClient
{
    public static IReadOnlyList<string> RegisteredActionIds { get; }
    public Task<ClassroomActionProjection> GetActionProjectionAsync(string actionId, CancellationToken cancellationToken = default);
    public Task<ClassroomActionResult> SubmitActionAsync(ClassroomActionIntent intent, CancellationToken cancellationToken = default);
    public Task<ClassroomActionResult?> GetActionResultAsync(Guid intentId, CancellationToken cancellationToken = default);
    public Task<ClassroomControlSnapshot> GetControlSnapshotAsync(CancellationToken cancellationToken = default);
    public Task<ClassroomScheduledControl?> GetScheduledActionPolicyAsync(CancellationToken cancellationToken = default);
    public Task AcknowledgeActionPolicyAsync(ClassroomScheduledControl change, CancellationToken cancellationToken = default);
}
```

## ClassroomControlConfiguration.cs

[源码](../../../src/Monitor.Network/ClassroomControlConfiguration.cs)

```csharp
public enum ClassroomChangeConflict { RejectOnConflict, Queue, ReplacePending }
public enum ClassroomAckStage { Received, Prepared, Applied, Rejected }
public sealed record ClassroomConfigurationAck(Guid ChangeId, string ContentSha256, ClassroomAckStage Stage, string? ReasonCode, long? ActualSimTimeNs, ulong? FirstBlockSequence)
{
    public long? DeviationNs { get; init; }
}
public sealed record ClassroomConfigurationContent(string[] AllowedActionIds, int? HeartRateBpm, Dictionary<string, JsonElement> NamedProjections)
{
    public string ComputeHash();
}
public sealed record ClassroomConfigurationStatus(ulong ActiveRevision, ClassroomConfigurationContent Active, ClassroomScheduledControl[] Changes)
{
    public Dictionary<Guid, ClassroomConfigurationAck?> RequiredChanges { get; init; }
}
```

## ClassroomControlConfigurationTransport.cs

[源码](../../../src/Monitor.Network/ClassroomControlConfigurationTransport.cs)

```csharp
public sealed partial class ClassroomHost
{
    public ClassroomScheduledControl ScheduleConfiguration(Guid changeId, ulong basedOnConfigurationRevision, long effectiveSimTimeNs, ClassroomConfigurationContent content, IReadOnlyList<Guid> requiredClients, int preparedQuorum, ClassroomChangeConflict conflict = ClassroomChangeConflict.RejectOnConflict, bool requireTeacherConfirmation = false);
    public bool ConfirmConfiguration(Guid changeId);
    public ClassroomConfigurationStatus ReadConfiguration();
    public IReadOnlyDictionary<Guid, ClassroomConfigurationAck> ReadConfigurationAcks(Guid changeId);
}
public sealed partial class ClassroomStudentClient
{
    public Task<ClassroomConfigurationStatus> GetConfigurationAsync(CancellationToken cancellationToken = default);
    public Task ReportConfigurationAckAsync(ClassroomConfigurationAck ack, CancellationToken cancellationToken = default);
}
```

## ClassroomControlHost.cs

[源码](../../../src/Monitor.Network/ClassroomControlHost.cs)

```csharp
public sealed partial class ClassroomHost
{
    public void DesignateOperator(Guid? clientId);
    public void BindActionReducer(IClassroomActionReducer reducer);
    public ClassroomControlSnapshot ReadControlSnapshot();
    public ClassroomActionResult SubmitTeacherAction(ClassroomActionIntent intent);
    public ClassroomScheduledControl ScheduleActionPolicy(Guid changeId, ulong basedOnConfigurationRevision, long effectiveSimTimeNs, IReadOnlyList<string> allowedActionIds, IReadOnlyList<Guid> requiredClients, int preparedQuorum);
    public bool CancelActionPolicy(Guid changeId);
}
```

## ClassroomControlWire.cs

[源码](../../../src/Monitor.Network/ClassroomControlWire.cs)

```csharp
public sealed record ClassroomActionIntent(string SchemaId, Guid IntentId, string IdempotencyKey, Guid SessionId, Guid ClientId, Guid InstanceId, string ActionId, ulong ProjectionRevision, long ClientMonotonicNs, string ControlId, string? SkinId, JsonElement Parameters)
{
}
public sealed record ClassroomActionProjection(string SchemaId, string ActionId, ulong ProjectionRevision, string Availability, string InteractionKind, string ParameterSchemaId, string? DisabledReasonCode, string LocalizationKey)
{
}
public sealed record ClassroomActionRejection(string SchemaId, string ReasonCode, string ObjectRef, string FieldPath, ulong? CurrentRevision, bool Retryable, string LocalizationKey, Guid CorrelationId)
{
}
public sealed record ClassroomActionResult(string SchemaId, Guid IntentId, string IdempotencyKey, bool Accepted, string ActionId, ulong ProjectionRevision, ulong? CommitSeq, long? SimTimeNs, ClassroomActionRejection? Rejection)
{
}
public interface IClassroomActionReducer
{
    public string ProfileId { get; }
    public JsonElement InitialState { get; }
    public bool Supports(string actionId);
    public JsonElement Reduce(JsonElement currentState, ClassroomActionIntent action, long simTimeNs, long safetyTimeNs);
}
public interface IClassroomMomentaryActionReducer : IClassroomActionReducer
{
    public JsonElement CancelInteraction(JsonElement currentState, Guid clientId, Guid interactionId, long safetyTimeNs);
}
public sealed class ClassroomActionDeniedException(string reasonCode) : Exception(reasonCode)
{
    public string ReasonCode { get; }
}
public sealed record ClassroomControlSnapshot(ulong ProjectionRevision, ulong CommitSeq, ulong ConfigurationRevision, string DeviceProfileId, JsonElement DeviceState, string[] AllowedActionIds)
{
}
```

## ClassroomDiscovery.cs

[源码](../../../src/Monitor.Network/ClassroomDiscovery.cs)

```csharp
public sealed record ClassroomDiscoveredHost(string DisplayName, Uri Origin, string CertificateSha256)
{
}
public static class ClassroomDiscovery
{
    public static Task AdvertiseAsync(IPAddress interfaceAddress, ClassroomDiscoveredHost advertisement, CancellationToken cancellationToken);
    public static Task<IReadOnlyList<ClassroomDiscoveredHost>> BrowseAsync(IPAddress interfaceAddress, TimeSpan duration, CancellationToken cancellationToken = default);
}
```

## ClassroomHost.cs

[源码](../../../src/Monitor.Network/ClassroomHost.cs)

```csharp
public sealed partial class ClassroomHost : IAsyncDisposable
{
    public Uri BaseAddress { get; }
    public static Task<ClassroomHost> StartAsync(IPAddress listenAddress, int port, string hostName, X509Certificate2 certificate, WaveformBlockRing ring, ClassroomAdmission admission, ClassroomBootstrap identity, X509Certificate2? trustedRoot = null, CancellationToken cancellationToken = default);
    public WaveformBlockAppendResult Publish(ReadOnlySpan<byte> rawEnvelope);
    public ValueTask DisposeAsync();
}
```

## ClassroomMessageJson.cs

[源码](../../../src/Monitor.Network/ClassroomMessageJson.cs)

```csharp
public sealed partial class ClassroomStudentClient
{
}
```

## ClassroomPairingTicket.cs

[源码](../../../src/Monitor.Network/ClassroomPairingTicket.cs)

```csharp
public sealed class ClassroomPairingTicket
{
    public Uri Origin { get; }
    public Guid SessionId { get; }
    public string CertificateSha256 { get; }
    public static ClassroomPairingTicket Create(Uri origin, Guid sessionId, string certificateSha256, string joinCode);
    public string Encode();
    public static ClassroomPairingTicket Parse(string ticket);
    public override string ToString();
}
public sealed partial class ClassroomStudentClient
{
    public static Task<ClassroomStudentClient> JoinAsync(ClassroomPairingTicket ticket, X509Certificate2? trustedRoot = null, CancellationToken cancellationToken = default);
    public static Task<ClassroomStudentClient> JoinWithPresentationAsync(ClassroomPairingTicket ticket, JsonElement hello, X509Certificate2? trustedRoot = null, CancellationToken cancellationToken = default);
}
```

## ClassroomReceiverProgress.cs

[源码](../../../src/Monitor.Network/ClassroomReceiverProgress.cs)

```csharp
public sealed record ClassroomReceiverProgress(Guid ClientId, Guid SessionId, Guid InstanceId, ulong AuthorityEpoch, ulong StreamEpoch, ulong BlockSequence, long BlockStartSimTimeNs, string ContentSha256, ulong ConfigurationRevision, ulong NextEventSequence, long EstimatedClockOffsetNs, long ClockUncertaintyNs, string ConnectionState, string SampleHealth)
{
}
public sealed partial class ClassroomHost
{
}
public sealed partial class ClassroomStudentClient
{
    public Task ReportReceiverProgressAsync(ClassroomReceiverProgress report, CancellationToken cancellationToken = default);
}
```

## ClassroomRecovery.cs

[源码](../../../src/Monitor.Network/ClassroomRecovery.cs)

```csharp
public sealed record ClassroomWaveformRecovery( ClassroomManifest Manifest, ulong? MissedFromBlockSequence, ulong PrerollFromBlockSequence, ulong NextBlockSequence, WaveformEnvelope Checkpoint, ClassroomTelemetrySnapshot Telemetry, ClassroomWaveformSubscription Subscription, IReadOnlyList<WaveformRetainedBlock> RenderHistory, ClassroomRenderedResumeSnapshot? Rendered = null)
{
}
public sealed partial class ClassroomHost
{
}
public sealed partial class ClassroomStudentClient
{
    public Task<ClassroomWaveformRecovery> ResynchronizeWaveformsAsync(ClassroomManifest previous, ulong nextBlockSequence, CancellationToken cancellationToken = default);
    public Task<ClassroomWaveformRecovery> ResynchronizeWaveformsAsync(ClassroomManifest previous, ulong nextBlockSequence, int renderHistoryBlocks, CancellationToken cancellationToken = default);
    public Task<ClassroomWaveformRecovery> JoinAtFutureBoundaryAsync(ClassroomManifest manifest, int renderHistoryBlocks, CancellationToken cancellationToken = default);
}
```

## ClassroomRecoveryAuthority.cs

[源码](../../../src/Monitor.Network/ClassroomRecoveryAuthority.cs)

```csharp
public sealed record ClassroomAuthorityCheckpoint(ulong CheckpointSequence, ulong DurableCommitSequence, ulong LastDurableEventSequence, string StateSha256)
{
}
public interface IClassroomRecoveryAuthority
{
    public ulong AuthorityEpoch { get; }
    public ClassroomAuthorityCheckpoint ReadCheckpoint(Guid studentId);
    public ContinuityReportVerification Replay(RecoveryHelloMessage hello, LocalContinuationReportMessage report, CancellationToken cancellationToken);
}
public sealed record ClassroomLearnerResumeSnapshot(ClassroomManifest Manifest, ClassroomTelemetrySnapshot Telemetry, byte[] LatestWaveformEnvelope, ClassroomRenderedResumeSnapshot? Rendered = null)
{
}
public sealed partial class ClassroomHost
{
    public void ConfigureRecoveryAuthority(IClassroomRecoveryAuthority? authority);
}
```

## ClassroomRecoveryClient.cs

[源码](../../../src/Monitor.Network/ClassroomRecoveryClient.cs)

```csharp
public sealed partial class ClassroomStudentClient
{
    public Task<RecoveryResyncPlan?> BeginAuthorityRecoveryAsync(RecoveryHelloMessage hello, ulong previousStreamEpoch, ulong nextBlockSequence, CancellationToken cancellationToken = default);
    public Task<RecoveryResyncPlan> SendContinuationReportAsync(LocalContinuationReportMessage report, CancellationToken cancellationToken = default);
    public Task<ClassroomLearnerResumeSnapshot> ReadRecoverySnapshotAsync(ClassroomManifest manifest, RecoveryResyncPlan plan, CancellationToken cancellationToken = default);
    public Task<RecoveryCommitCommand?> SendRecoveryPreparedAsync(RecoveryPreparedAcknowledgement acknowledgement, CancellationToken cancellationToken = default);
}
```

## ClassroomRecoveryContinuity.cs

[源码](../../../src/Monitor.Network/ClassroomRecoveryContinuity.cs)

```csharp
public sealed record ClassroomSignedContinuity(ContinuitySignedBody Body, ContinuitySignatureProof Proof, byte[] Payload)
{
}
public sealed record ClassroomOfflineLease(Guid LeaseId, string BranchId, string BaseSha256, IReadOnlyList<string> AllowedActions, long TakeoverFromSimTimeNs, ulong MaximumOfflineWindowNs, ulong MaximumOfflineOperationSequence, long ContinuationValidUntilSimTimeNs, long ExpiresAtSafetyNs)
{
}
public sealed record ClassroomVerifiedContinuity(ulong Sequence, ContinuityRecoveryStateImage? State, ClassroomOfflineLease? Lease)
{
}
public sealed class ClassroomContinuityReceiver
{
    public ClassroomContinuityReceiver(Guid studentId, Guid sessionId, Guid instanceId, ulong authorityEpoch, string branchId, IReadOnlyList<TrustedContinuitySigningKey> trustedKeys, ContinuityRuntimeCapabilities capabilities, Func<ContinuityStateComponent, bool> learnerComponentAllowed);
    public Guid StudentId { get; }
    public Guid SessionId { get; }
    public Guid InstanceId { get; }
    public ulong AuthorityEpoch { get; }
    public string BranchId { get; }
}
```

## ClassroomRecoveryDelivery.cs

[源码](../../../src/Monitor.Network/ClassroomRecoveryDelivery.cs)

```csharp
public sealed partial class ClassroomHost
{
    public void ConfigureContinuity(Guid studentId, ClassroomContinuityReceiver? receiver);
    public ulong PublishContinuity(Guid studentId, ClassroomSignedContinuity packet);
}
public sealed partial class ClassroomStudentClient
{
    public Task<ClassroomVerifiedContinuity?> ReadContinuityAsync(ClassroomContinuityReceiver receiver, ulong afterSequence, Func<(long SimTimeNs, long AuthorityMonotonicNs)> currentTime, CancellationToken cancellationToken = default);
}
```

## ClassroomRecoveryPresentation.cs

[源码](../../../src/Monitor.Network/ClassroomRecoveryPresentation.cs)

```csharp
public sealed record ClassroomRenderedResumeSnapshot(JsonElement Descriptor, JsonElement ResolvedPlan, byte[] ProjectionImage, byte[] NumericImage)
{
}
public sealed partial class ClassroomHost
{
    public void ConfigureRecoveryPresentation(Guid studentId, JsonElement resolvedPlan, ReadOnlySpan<byte> projectionImage, ReadOnlySpan<byte> numericImage);
    public void ClearRecoveryPresentation(Guid studentId);
}
```

## ClassroomSession.cs

[源码](../../../src/Monitor.Network/ClassroomSession.cs)

```csharp
public sealed record ClassroomNegotiation(Guid SessionId, Guid InstanceId, Guid CohortId, Guid ClientId, ulong SessionEpoch, ulong AuthorityEpoch, string SampleFormat, bool AllowLz4, string DomainPackageSha256, DateTimeOffset TokenExpiresAt, int RemainingSeconds, int RefreshWindowSeconds, JsonElement? SessionManifest)
{
}
public sealed record ClassroomMember(Guid ClientId, bool Prepared, long RecommendedDelayNs, long LastSeenNs, JsonElement? Progress)
{
}
public sealed partial class ClassroomHost
{
    public IReadOnlyList<ClassroomMember> Members { get; }
    public IReadOnlyList<Guid> StartingSet { get; }
    public void RemoveMember(Guid studentId);
    public void ConfigurePresentation(JsonElement manifest, IReadOnlyList<string> requiredCapabilities);
    public ClassroomTimebase StartClass(long? fixedPlayoutDelayNs = null);
    public ClassroomTimebase SchedulePlayback(bool running, int rateNumerator = 1, int rateDenominator = 1, long? playoutDelayNs = null);
    public Task EndSessionAsync();
    public Task RunIllustrationAsync(CancellationToken cancellationToken);
    public Task RunIllustrationAsync(PhysiologyIllustrationConfiguration configuration, CancellationToken cancellationToken);
}
public sealed partial class ClassroomStudentClient
{
    public ClassroomNegotiation? Negotiation { get; private set; }
    public static Task<ClassroomStudentClient> JoinWithPresentationAsync(Uri endpoint, string joinCode, JsonElement hello, System.Security.Cryptography.X509Certificates.X509Certificate2? trustedRoot = null, CancellationToken cancellationToken = default);
    public Task<ClassroomNegotiation> NegotiateAsync(ClassroomManifest manifest, JsonElement? presentation, CancellationToken cancellationToken = default);
    public Task<ClassroomNegotiation> NegotiateAsync(ClassroomManifest manifest, JsonElement? presentation, bool allowLz4, CancellationToken cancellationToken = default);
    public Task ReportPreparedAsync(bool prepared, long recommendedDelayNs, CancellationToken cancellationToken = default);
    public Task ReportProgressAsync(JsonElement progress, CancellationToken cancellationToken = default);
    public Task RefreshAccessAsync(CancellationToken cancellationToken = default);
}
```

## ClassroomStudentClient.cs

[源码](../../../src/Monitor.Network/ClassroomStudentClient.cs)

```csharp
public sealed partial class ClassroomStudentClient : IDisposable
{
    public static Task<ClassroomStudentClient> JoinAsync(Uri endpoint, string joinCode, X509Certificate2? trustedRoot = null, CancellationToken cancellationToken = default);
    public Task<ClassroomManifest> GetSessionAsync(CancellationToken cancellationToken = default);
    public Task<ClassroomClockSample> SynchronizeClockAsync(CancellationToken cancellationToken = default);
    public Task<ClassroomWaveformSubscription> ResumeAsync(ClassroomManifest previousManifest, ulong nextBlockSequence, CancellationToken cancellationToken = default);
    public Task<ClassroomWaveformSubscription> SubscribeAsync(ClassroomManifest manifest, ulong? fromBlockSequence = null, CancellationToken cancellationToken = default);
    public void Dispose();
}
```

## ClassroomSynchronization.cs

[源码](../../../src/Monitor.Network/ClassroomSynchronization.cs)

```csharp
public sealed class ClassroomSynchronization
{
    public static Task<ClassroomSynchronization> StartAsync(ClassroomStudentClient client, IReadOnlyList<Guid> requiredPlaneIds, bool sessionRunning, CancellationToken cancellationToken = default);
    public ClassroomClockEstimate Estimate { get; }
    public ConnectionHealthState Health { get; }
    public ChannelReader<ConnectionHealthState> HealthChanges { get; }
    public void RecordHeartbeat(bool sessionRunning);
    public void RecordBlock(WaveformEnvelope acceptedBlock);
    public void RequestRapidCalibration();
    public void RecordFailure(ImmediateTransportFailure failure);
    public void BeginRelocking();
    public void CompleteRelocking(WaveformEnvelope verifiedBoundary);
    public long PresentationHostTimeNs();
    public Task MonitorAsync(CancellationToken cancellationToken);
}
```

## ClassroomTelemetry.cs

[源码](../../../src/Monitor.Network/ClassroomTelemetry.cs)

```csharp
public sealed record ClassroomTelemetrySnapshot(Guid SessionId, Guid InstanceId, Guid CohortId, [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong SessionEpoch, ClassroomTimebase Timeline, ClassroomTimebase? PendingTimeline, [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong OldestEventSequence, [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong NextEventSequence, IReadOnlyList<JsonElement> Numerics, IReadOnlyList<JsonElement> Projections, ClassroomControlSnapshot Control, ClassroomConfigurationContent ActiveConfiguration)
{
}
public sealed record ClassroomEventPage(Guid SessionId, Guid InstanceId, [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong NextSequence, IReadOnlyList<JsonElement> Events)
{
}
public sealed partial class ClassroomHost
{
    public ClassroomTelemetrySnapshot CurrentTelemetrySnapshot();
    public JsonElement PublishDomainEvent(string eventType, JsonElement payload, long simTimeNs, Guid correlationId, ulong commitSequence);
}
public sealed partial class ClassroomStudentClient
{
    public Task<ClassroomTelemetrySnapshot> GetTelemetryAsync(CancellationToken cancellationToken = default);
    public Task<ClassroomEventPage> ReadEventsAsync(ClassroomManifest manifest, ulong fromSequence, CancellationToken cancellationToken = default);
}
```

## ClassroomTimeline.cs

[源码](../../../src/Monitor.Network/ClassroomTimeline.cs)

```csharp
public sealed record ClassroomTimebase(ulong Revision, ulong TimebaseEpoch, long SimAnchorNs, long HostAnchorNs, int RateNumerator, int RateDenominator, string RunState, long PlayoutDelayNs)
{
    public JsonElement ToTimebaseState();
    public long PresentationDeadlineNs(long simTimeNs);
    public long SimTimeAt(long hostMonotonicNs);
}
```

## ClassroomWaveformSubscription.cs

[源码](../../../src/Monitor.Network/ClassroomWaveformSubscription.cs)

```csharp
public sealed class ClassroomWaveformSubscription : IDisposable, IAsyncDisposable
{
    public ulong NextBlockSequence { get; }
    public WaveformBlockReplayResult ReadBufferedBlocks(ulong firstBlockSequence, int maximumBlocks);
    public ValueTask<WaveformEnvelope?> ReceiveAsync(CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();
    public void Dispose();
}
```

## ClassroomWire.cs

[源码](../../../src/Monitor.Network/ClassroomWire.cs)

```csharp
public static class ClassroomWire
{
    public const string ProtocolId = "Classroom.Protocol";
    public const string ProtocolVersion = "1.0.0";
    public const string DomainContractsSha256 = ClassroomContracts.PackageHash;
    public static string ProtocolSha256 { get; }
    public const string WaveformSubprotocol = "simonitor.waveforms.v1";
    public const int AcknowledgementBytes = 48;
}
public sealed record ClassroomManifest( int ProtocolVersion, Guid StudentId, Guid SessionId, Guid InstanceId, [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong TimebaseEpoch, [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong StreamEpoch, [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong OldestBlockSequence, [property: JsonNumberHandling(ClassroomWire.DecimalString)] ulong NextBlockSequence, [property: JsonNumberHandling(ClassroomWire.DecimalString)] long NextBlockStartSimTimeNs)
{
}
public sealed record ClassroomClockReply( [property: JsonNumberHandling(ClassroomWire.DecimalString)] long T1, [property: JsonNumberHandling(ClassroomWire.DecimalString)] long T2, [property: JsonNumberHandling(ClassroomWire.DecimalString)] long T3)
{
}
public sealed record ClassroomClockSample(long ClientSendNs, long HostReceiveNs, long HostSendNs, long ClientReceiveNs)
{
    public long RoundTripNs { get; }
    public long HostOffsetNs { get; }
}
```

## ExamClusterNode.cs

`Head` 为本机已提交的事务前沿，`Pending` 携带待确认的本地准备记录游标。`Qualified` 指示本备机在当前运行期已核验接管资格。[源码](../../../src/Monitor.Network/ExamClusterNode.cs)

```csharp
public sealed record ExamClusterNodeStatus(Guid ClusterId, Guid NodeId, string Role, long AuthorityEpoch,
    long FencingToken, bool LeaseValid, bool Qualified, ExamReplicaCursor Head, string Reason)
{
    public ExamReplicaCursor? Pending { get; init; }
}
```

## ExamClusterService.cs

宿主考试应用以本地 Host 配置启动服务，在已认证、授权的操作中提交和读取事务。见证、具名人工隔离和接管审批的部署入口见[正式考试集群](../exam-cluster.md)。[源码](../../../src/Monitor.Network/ExamClusterService.cs)

```csharp
public sealed class ExamClusterService : IAsyncDisposable
{
    public static Task<ExamClusterService> StartAsync(string configurationPath, CancellationToken cancellationToken = default);
    public Task Completion { get; }
    public ExamClusterNodeStatus GetStatus();
    public Task<ExamReplicaCursor> SubmitAsync(long authorityEpoch, long fencingToken, ExamReplicaCommand command,
        CancellationToken cancellationToken = default);
    public ExamReplicaCommand? ReadCommitted(long sequence);
    public ValueTask DisposeAsync();
}
```

## ExamReplicaCheckpoint.cs

考试宿主在完整的 200 ms 边界调用 `Capture` 生成 `Checkpoint` 事务的二进制载荷。`Restore` 恢复该边界的生成器，并独立重演下一块、核对内容摘要。核验过程使用生成器副本，返回的实例仍指向原边界。[源码](../../../src/Monitor.Network/ExamReplicaCheckpoint.cs)

```csharp
public static class ExamReplicaCheckpoint
{
    public static byte[] Capture(PhysiologyWaveformGroup source);
    public static PhysiologyWaveformGroup Restore(byte[] payload);
}
```

## ExamReplicaTransaction.cs

`RequestId` 标识一次逻辑提交。`Kind` 可取 `Answer`、`Control`、`Checkpoint` 或 `Audit`。`Payload` 保存该类型的原始字节。`ExamReplicaCursor.Hash` 为序号对应的完整事务链摘要。[源码](../../../src/Monitor.Network/ExamReplicaTransaction.cs)

```csharp
public sealed record ExamReplicaCursor(long Sequence, string Hash);
public sealed record ExamReplicaCommand(Guid RequestId, string Kind, byte[] Payload);
```
