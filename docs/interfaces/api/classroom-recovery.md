# Monitor.Network 课堂恢复公开声明

[网络 API](network.md) · [恢复协议与实例生命周期](../classroom-recovery.md)

以下为 `Monitor.Network` 的声明。所有时间戳与时长以纳秒计，时钟漂移以 ppm 计。时钟与健康对象见 [校时](../../../src/Monitor.Network/ClassroomClockCalibration.cs) 和 [连接同步](../../../src/Monitor.Network/ClassroomSynchronization.cs)。

```csharp
public sealed record ClassroomClockEstimate(int SampleCount, long ClientReferenceNs, long HostOffsetNs,
    double DriftPpm, long RoundTripP95Ns, long JitterP99Ns, long UncertaintyNs,
    long RecommendedPlayoutDelayNs, bool RequiresResynchronization)
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

public sealed class ClassroomSynchronization
{
    public static Task<ClassroomSynchronization> StartAsync(ClassroomStudentClient client, IReadOnlyList<Guid> requiredPlaneIds,
        bool sessionRunning, CancellationToken cancellationToken = default);
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

同一权威的波形重同步和换代握手分别见 [波形恢复](../../../src/Monitor.Network/ClassroomRecovery.cs)、[恢复权威](../../../src/Monitor.Network/ClassroomRecoveryAuthority.cs) 和 [学生调用](../../../src/Monitor.Network/ClassroomRecoveryClient.cs)。

```csharp
public sealed record ClassroomWaveformRecovery(ClassroomManifest Manifest, ulong? MissedFromBlockSequence,
    ulong PrerollFromBlockSequence, ulong NextBlockSequence, WaveformEnvelope Checkpoint,
    ClassroomTelemetrySnapshot Telemetry, ClassroomWaveformSubscription Subscription, IReadOnlyList<WaveformRetainedBlock> RenderHistory,
    ClassroomRenderedResumeSnapshot? Rendered = null);

public sealed record ClassroomLearnerResumeSnapshot(ClassroomManifest Manifest, ClassroomTelemetrySnapshot Telemetry,
    byte[] LatestWaveformEnvelope, ClassroomRenderedResumeSnapshot? Rendered = null);

public sealed record ClassroomRenderedResumeSnapshot(JsonElement Descriptor, JsonElement ResolvedPlan,
    byte[] ProjectionImage, byte[] NumericImage);

public sealed record ClassroomAuthorityCheckpoint(ulong CheckpointSequence, ulong DurableCommitSequence,
    ulong LastDurableEventSequence, string StateSha256);

public interface IClassroomRecoveryAuthority
{
    ulong AuthorityEpoch { get; }
    ClassroomAuthorityCheckpoint ReadCheckpoint(Guid studentId);
    ContinuityReportVerification Replay(RecoveryHelloMessage hello, LocalContinuationReportMessage report, CancellationToken cancellationToken);
}

public sealed partial class ClassroomHost
{
    public void ConfigureRecoveryAuthority(IClassroomRecoveryAuthority? authority);
    public void ConfigureRecoveryPresentation(Guid studentId, JsonElement resolvedPlan, ReadOnlySpan<byte> projectionImage, ReadOnlySpan<byte> numericImage);
    public void ClearRecoveryPresentation(Guid studentId);
    public void ConfigureContinuity(Guid studentId, ClassroomContinuityReceiver? receiver);
    public ulong PublishContinuity(Guid studentId, ClassroomSignedContinuity packet);
}

public sealed partial class ClassroomStudentClient
{
    public Task<ClassroomWaveformRecovery> ResynchronizeWaveformsAsync(ClassroomManifest previous, ulong nextBlockSequence,
        CancellationToken cancellationToken = default);
    public Task<ClassroomWaveformRecovery> ResynchronizeWaveformsAsync(ClassroomManifest previous, ulong nextBlockSequence,
        int renderHistoryBlocks, CancellationToken cancellationToken = default);
    public Task<ClassroomWaveformRecovery> JoinAtFutureBoundaryAsync(ClassroomManifest manifest,
        int renderHistoryBlocks, CancellationToken cancellationToken = default);
    public Task<RecoveryResyncPlan?> BeginAuthorityRecoveryAsync(RecoveryHelloMessage hello,
        ulong previousStreamEpoch, ulong nextBlockSequence, CancellationToken cancellationToken = default);
    public Task<RecoveryResyncPlan> SendContinuationReportAsync(LocalContinuationReportMessage report,
        CancellationToken cancellationToken = default);
    public Task<ClassroomLearnerResumeSnapshot> ReadRecoverySnapshotAsync(ClassroomManifest manifest,
        RecoveryResyncPlan plan, CancellationToken cancellationToken = default);
    public Task<RecoveryCommitCommand?> SendRecoveryPreparedAsync(RecoveryPreparedAcknowledgement acknowledgement,
        CancellationToken cancellationToken = default);
    public Task<ClassroomVerifiedContinuity?> ReadContinuityAsync(ClassroomContinuityReceiver receiver, ulong afterSequence,
        Func<(long SimTimeNs, long AuthorityMonotonicNs)> currentTime, CancellationToken cancellationToken = default);
}
```

签名投影的传输、授权和验证见 [连续性接收器](../../../src/Monitor.Network/ClassroomRecoveryContinuity.cs) 与[按学生投递](../../../src/Monitor.Network/ClassroomRecoveryDelivery.cs)。

```csharp
public sealed record ClassroomSignedContinuity(ContinuitySignedBody Body, ContinuitySignatureProof Proof, byte[] Payload);
public sealed record ClassroomOfflineLease(Guid LeaseId, string BranchId, string BaseSha256, IReadOnlyList<string> AllowedActions,
    long TakeoverFromSimTimeNs, ulong MaximumOfflineWindowNs, ulong MaximumOfflineOperationSequence,
    long ContinuationValidUntilSimTimeNs, long ExpiresAtSafetyNs);
public sealed record ClassroomVerifiedContinuity(ulong Sequence, ContinuityRecoveryStateImage? State, ClassroomOfflineLease? Lease);

public sealed class ClassroomContinuityReceiver
{
    public ClassroomContinuityReceiver(Guid studentId, Guid sessionId, Guid instanceId, ulong authorityEpoch, string branchId,
        IReadOnlyList<TrustedContinuitySigningKey> trustedKeys, ContinuityRuntimeCapabilities capabilities,
        Func<ContinuityStateComponent, bool> learnerComponentAllowed);
    public Guid StudentId { get; }
    public Guid SessionId { get; }
    public Guid InstanceId { get; }
    public ulong AuthorityEpoch { get; }
    public string BranchId { get; }
}
```
