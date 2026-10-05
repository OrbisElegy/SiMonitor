# Monitor.Application 公开声明索引

[接口总览](../README.md)

本页按源码路径列出当前工作区中对程序集外可见的 C# `public` 类型和显式声明的公开成员，保留参数、默认值、单位命名、泛型约束及枚举值。接口中省略 `public` 的成员也包括在内。方法体、属性实现与非 const 字段初始化值已省略；省略实现的声明用于查阅，不能直接当作可编译代码。

位置 record 的参数也定义其同名属性；编译器合成的构造器、相等性方法以及继承成员不重复展开。`internal` 类型即使有 `public` 成员也不属于本索引。按开发构建的预处理分支读取；桌面产品构建差异见[桌面与命令行入口](../desktop-tools.md)。行为约束和集成顺序见总览中的分模块文档。

## Continuity/ClientRecoverySession.cs

源码：[ClientRecoverySession.cs](../../../src/Monitor.Application/Continuity/ClientRecoverySession.cs) · 命名空间：`Monitor.Application.Continuity`

```csharp
public sealed class ClientRecoverySessionException : ArgumentException
{
    public ClientRecoverySessionException(string reasonCode, string parameterName, Exception? innerException = null);
    public string ReasonCode { get; }
}
public sealed record ClientRecoverySessionState(RecoveryRelockState Relock, DataContinuityState Continuity)
{
}
public sealed class ClientRecoverySession
{
    public RecoveryRelockPhase Phase { get; }
    public DataAvailability DataAvailability { get; }
    public AuthorityState AuthorityState { get; }
    public ConnectionState ConnectionState { get; }
    public static ClientRecoverySession Start(DataContinuityState disconnectedContinuity, Guid clientId, Guid sessionId, Guid instanceId, ulong acceptedAuthorityEpoch, ulong acceptedStreamEpoch, ulong lastAppliedCommitSequence, long currentSimTimeNs);
    public static ClientRecoverySession Restore(ClientRecoverySessionState state);
    public ClientRecoverySessionState AcceptPlan(RecoveryResyncPlan plan, long currentSimTimeNs, long authorityMonotonicNs);
    public ClientRecoverySessionState ReplaceRejectedPlan(RecoveryResyncPlan plan, long currentSimTimeNs, long authorityMonotonicNs);
    public ClientRecoverySessionState RecordPrepared(RecoveryPreparationEvidence evidence, long authorityMonotonicNs);
    public RecoveryPreparedAcknowledgement CapturePreparedAcknowledgement();
    public ClientRecoverySessionState Commit(RecoveryCommitCommand command, long authorityMonotonicNs);
    public ClientRecoverySessionState Advance(long currentSimTimeNs, long authorityMonotonicNs);
    public ClientRecoverySessionState CaptureState();
}
```

## Continuity/ContinuityCapsuleChain.cs

源码：[ContinuityCapsuleChain.cs](../../../src/Monitor.Application/Continuity/ContinuityCapsuleChain.cs) · 命名空间：`Monitor.Application.Continuity`

```csharp
public enum ContinuityPermissionState
{
    Allowed = 0,
    DisallowedByCourse = 1,
    DisallowedByMode = 2,
    Ineligible = 3,
}
public sealed record ContinuityVersionedReference(string Id, string Version, string ContentSha256)
{
}
public sealed record ContinuityStateComponent(string ComponentId, ContinuityVersionedReference StateSchemaReference, string StateSha256, byte[] StateBytes)
{
}
public sealed record ContinuityChannelCursor(string ChannelId, ulong NextSampleIndex, ulong NextBlockSequence, ulong NextNumericSequence)
{
}
public sealed record ContinuityProjectionRevisions(ulong DisplayRole, ulong AlarmEpisode, ulong AlarmAudioPolicy, ulong Therapy)
{
}
public sealed record ContinuityCapsuleBase(Guid CapsuleId, Guid SessionId, Guid InstanceId, string BranchId, ulong AuthorityEpoch, ulong TimebaseEpoch, ulong StreamEpoch, ulong CheckpointSequence, ulong CommitSequence, long SimTimeNs, ulong NextEventSequence, IReadOnlyList<ContinuityChannelCursor> ChannelCursors, IReadOnlyList<ContinuityStateComponent> StateComponents, ContinuityProjectionRevisions ProjectionRevisions, ContinuityVersionedReference EngineReference, ContinuityVersionedReference MathProfileReference, IReadOnlyList<ContinuityVersionedReference> ResourcePackReferences, ContinuityPermissionState ContinuationPermission, long SecretBoundarySimTimeNs, long ContinuationValidUntilSimTimeNs, string CapsuleSha256, ContinuitySignatureProof Signature, string? EncryptionEnvelopeReference)
{
}
public sealed record ContinuityCapsuleDelta(Guid DeltaId, string BaseSha256, string? PreviousDeltaSha256, ulong CheckpointSequence, ulong CommitSequence, long SimTimeNs, IReadOnlyList<ContinuityStateComponent> ChangedComponents, long SecretBoundarySimTimeNs, long ContinuationValidUntilSimTimeNs, string DeltaSha256, ContinuitySignatureProof Signature)
{
}
public sealed record ContinuityCapsuleFrontier(Guid BaseCapsuleId, string BaseSha256, Guid? LastDeltaId, string? LastDeltaSha256, ulong CheckpointSequence, ulong CommitSequence, long SimTimeNs, long SecretBoundarySimTimeNs, long ContinuationValidUntilSimTimeNs)
{
}
public sealed record ContinuityCapsuleChainState(ContinuitySignatureGateState SignatureGate, ContinuityCapsuleFrontier? Frontier, ContinuityRecoveryStateImage? RecoveryStateImage)
{
}
public sealed class AcceptedContinuityCapsuleBase
{
    public ContinuityCapsuleBase Capsule { get; }
    public ContinuityCapsuleChainState State { get; }
}
public sealed class AcceptedContinuityCapsuleDelta
{
    public ContinuityCapsuleDelta Delta { get; }
    public ContinuityCapsuleChainState State { get; }
}
public interface IContinuityCapsulePayloadDecoder
{
    public ContinuityCapsuleBase DecodeBase(ReadOnlyMemory<byte> signedPayload);
    public ContinuityCapsuleDelta DecodeDelta(ReadOnlyMemory<byte> signedPayload);
}
public sealed class ContinuityCapsuleChainException : ArgumentException
{
    public ContinuityCapsuleChainException(string reasonCode, string parameterName, Exception? innerException = null);
    public string ReasonCode { get; }
}
public sealed partial class ContinuityCapsuleChain
{
    public const int MaximumChannelCursorCount = 128;
    public const int MaximumStateComponentCount = 256;
    public const int MaximumResourcePackCount = 32;
    public ContinuityCapsuleFrontier? Frontier { get; }
    public ContinuityRecoveryStateImage? RecoveryStateImage { get; }
    public static ContinuityCapsuleChain Start(Guid sessionId, Guid instanceId, ulong authorityEpoch, long authorityMonotonicNs, IEnumerable<TrustedContinuitySigningKey> trustedKeys, IContinuitySignatureVerifier verifier, ContinuityRuntimeCapabilities runtimeCapabilities, IContinuityCapsulePayloadDecoder payloadDecoder);
    public static ContinuityCapsuleChain Restore(ContinuityCapsuleChainState state, IEnumerable<TrustedContinuitySigningKey> trustedKeys, IContinuitySignatureVerifier verifier, ContinuityRuntimeCapabilities runtimeCapabilities, IContinuityCapsulePayloadDecoder payloadDecoder);
    public AcceptedContinuityCapsuleBase AcceptBase(ContinuitySignedBody signedBody, ReadOnlyMemory<byte> signedPayload, long currentSimTimeNs, long authorityMonotonicNs);
    public AcceptedContinuityCapsuleDelta AcceptDelta(ContinuitySignedBody signedBody, ReadOnlyMemory<byte> signedPayload, long currentSimTimeNs, long authorityMonotonicNs);
    public ContinuityCapsuleChainState CaptureState();
}
```

## Continuity/ContinuityRecoveryState.cs

源码：[ContinuityRecoveryState.cs](../../../src/Monitor.Application/Continuity/ContinuityRecoveryState.cs) · 命名空间：`Monitor.Application.Continuity`

```csharp
public sealed record ContinuityStateComponentCapability(string ComponentId, ContinuityVersionedReference StateSchemaReference)
{
}
public sealed record ContinuityRuntimeCapabilities(ContinuityVersionedReference EngineReference, ContinuityVersionedReference MathProfileReference, IReadOnlyList<ContinuityVersionedReference> ResourcePackReferences, IReadOnlyList<string> ChannelIds, IReadOnlyList<ContinuityStateComponentCapability> StateComponents)
{
}
public sealed record ContinuityRecoveryStateImage(Guid BaseCapsuleId, string BaseSha256, Guid? LastDeltaId, string? LastDeltaSha256, Guid SessionId, Guid InstanceId, string BranchId, ulong AuthorityEpoch, ulong TimebaseEpoch, ulong StreamEpoch, ulong CheckpointSequence, ulong CommitSequence, long SimTimeNs, ulong NextEventSequence, IReadOnlyList<ContinuityChannelCursor> ChannelCursors, IReadOnlyList<ContinuityStateComponent> StateComponents, ContinuityProjectionRevisions ProjectionRevisions, ContinuityVersionedReference EngineReference, ContinuityVersionedReference MathProfileReference, IReadOnlyList<ContinuityVersionedReference> ResourcePackReferences, long SecretBoundarySimTimeNs, long ContinuationValidUntilSimTimeNs)
{
}
public sealed partial class ContinuityCapsuleChain
{
}
```

## Continuity/ContinuityShadowCoordinator.cs

源码：[ContinuityShadowCoordinator.cs](../../../src/Monitor.Application/Continuity/ContinuityShadowCoordinator.cs) · 命名空间：`Monitor.Application.Continuity`

```csharp
public enum ContinuityShadowGenerationStatus
{
    Completed = 0,
    CapsuleRejected = 1,
    PerformanceInsufficient = 2,
}
public enum ContinuityShadowVerificationOutcome
{
    Verified = 0,
    Mismatch = 1,
    CapsuleInvalid = 2,
    PerformanceInsufficient = 3,
    InvalidEvidence = 4,
}
public sealed record ContinuityShadowBlockProof(ulong BlockSequence, long StartSimTimeNs, string WaveformContentSha256, string NumericFramesSha256, string CanonicalStateSha256)
{
}
public sealed record ContinuityShadowGenerationRequest(Guid BaseCapsuleId, string BaseSha256, Guid? LastDeltaId, string? LastDeltaSha256, ulong CheckpointSequence, ulong CommitSequence, ulong TimebaseEpoch, ulong StreamEpoch, ulong FirstBlockSequence, long FirstBlockStartSimTimeNs, int BlockCount)
{
}
public sealed record ContinuityShadowGenerationResult(ContinuityShadowGenerationStatus Status, IReadOnlyList<ContinuityShadowBlockProof> BlockProofs)
{
}
public sealed record ContinuityShadowVerificationResult(ContinuityShadowVerificationOutcome Outcome, ContinuityShadowGenerationRequest? Request, DataContinuityState DataContinuityState)
{
}
public interface IContinuityShadowGenerator
{
    public ContinuityShadowGenerationResult Generate(ContinuityRecoveryStateImage recoveryStateImage, ContinuityShadowGenerationRequest request);
}
public sealed class ContinuityShadowCoordinatorException(string reasonCode, string parameterName, Exception? innerException = null) : ArgumentException(reasonCode, parameterName, innerException)
{
    public string ReasonCode { get; }
}
public sealed class ContinuityShadowCoordinator
{
    public const long BlockDurationNs = 200_000_000;
    public const int MaximumOverlapBlockCount = 50;
    public ContinuityShadowCoordinator(ContinuityCapsuleChain capsuleChain, DataContinuityStateMachine dataContinuity, IContinuityShadowGenerator generator);
    public ContinuityShadowVerificationResult VerifyOverlap(IReadOnlyList<ContinuityShadowBlockProof> authoritativeOverlap, long authorityMonotonicNs);
}
```

## Continuity/ContinuitySignatureGate.cs

源码：[ContinuitySignatureGate.cs](../../../src/Monitor.Application/Continuity/ContinuitySignatureGate.cs) · 命名空间：`Monitor.Application.Continuity`

```csharp
public enum ContinuitySigningKeyState
{
    PreActive = 0,
    Active = 1,
    Retiring = 2,
    Revoked = 3,
    Expired = 4,
}
public sealed record ContinuitySignedBody(string CapsuleType, Guid CapsuleId, Guid SessionId, Guid InstanceId, ulong AuthorityEpoch, long SecretBoundarySimTimeNs, long ContinuationValidUntilSimTimeNs, string PayloadSha256)
{
}
public sealed record ContinuitySignatureProof(string SignatureAlgorithmId, string KeyId, ushort SignedSchemaMajor, ushort SignedSchemaMinor, byte[] SignatureRs64)
{
}
public sealed record TrustedContinuitySigningKey(string KeyId, byte[] PublicKeySec1, ContinuitySigningKeyState State, ulong AuthorityEpoch, long ActivatedAtAuthorityMonotonicNs, long ExpiresAtAuthorityMonotonicNs)
{
}
public sealed record ContinuitySignatureGateState(Guid SessionId, Guid InstanceId, ulong AuthorityEpoch, long LastAuthorityMonotonicNs, IReadOnlyList<Guid> AcceptedCapsuleIds)
{
}
public interface IContinuitySignatureVerifier
{
    public string DeriveKeyId(ReadOnlyMemory<byte> publicKeySec1);
    public bool Verify(ContinuitySignedBody body, ReadOnlyMemory<byte> signatureRs64, ReadOnlyMemory<byte> publicKeySec1);
}
public sealed class ContinuitySignatureGateException : ArgumentException
{
    public ContinuitySignatureGateException(string reasonCode, string parameterName, Exception? innerException = null);
    public string ReasonCode { get; }
}
public sealed class ContinuitySignatureAdmission
{
}
public sealed class ContinuitySignatureGate
{
    public const string SignatureAlgorithmId = "SM2-SM3-RS64@1";
    public const int MaximumTrustedKeyCount = 64;
    public const int MaximumAcceptedCapsuleCount = 65_536;
    public const int MaximumSignedPayloadBytes = 1_048_576;
    public const int MaximumBaseCapsuleBytes = 65_536;
    public const int MaximumDeltaCapsuleBytes = 16_384;
    public const long MaximumOnlineKeyLifetimeNs = 90L * 24 * 60 * 60 * 1_000_000_000;
    public Guid SessionId { get; }
    public Guid InstanceId { get; }
    public ulong AuthorityEpoch { get; }
    public static ContinuitySignatureGate Start(Guid sessionId, Guid instanceId, ulong authorityEpoch, long authorityMonotonicNs, IEnumerable<TrustedContinuitySigningKey> trustedKeys, IContinuitySignatureVerifier verifier);
    public static ContinuitySignatureGate Restore(ContinuitySignatureGateState state, IEnumerable<TrustedContinuitySigningKey> trustedKeys, IContinuitySignatureVerifier verifier);
    public ContinuitySignatureGateState VerifyAndCommit(ContinuitySignedBody body, ContinuitySignatureProof proof, ReadOnlyMemory<byte> payload, long currentSimTimeNs, long authorityMonotonicNs);
    public ContinuitySignatureAdmission Verify(ContinuitySignedBody body, ContinuitySignatureProof proof, ReadOnlyMemory<byte> payload, long currentSimTimeNs, long authorityMonotonicNs);
    public ContinuitySignatureGateState Commit(ContinuitySignatureAdmission admission);
    public ContinuitySignatureGateState CaptureState();
}
```

## Continuity/RecoveryResyncPlanFactory.cs

源码：[RecoveryResyncPlanFactory.cs](../../../src/Monitor.Application/Continuity/RecoveryResyncPlanFactory.cs) · 命名空间：`Monitor.Application.Continuity`

```csharp
public sealed class RecoveryResyncPlanFactoryException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record RecoveryHostPlanContext(Guid PlanId, ulong HostCheckpointSequence, ulong HostDurableCommitSequence, string HostStateSha256, long CurrentSimTimeNs, bool RequireFullSnapshot, string? ResumeSnapshotSha256, string ReasonCode)
{
}
public static class RecoveryResyncPlanFactory
{
    public static RecoveryResyncPlan Create(RecoveryPlanningInput planningInput, WaveformBlockRing hostWaveformRing, WaveformRecoveryRequest waveformRequest, RecoveryHostPlanContext context);
}
```

## Identity/BootstrapIdentityService.cs

源码：[BootstrapIdentityService.cs](../../../src/Monitor.Application/Identity/BootstrapIdentityService.cs) · 命名空间：`Monitor.Application.Identity`

```csharp
public sealed record CompleteBootstrapRequest(bool LocalInteractive, string InitialUsername, string? NewUsername, Guid CorrelationId)
{
}
public sealed class BootstrapIdentityService
{
    public BootstrapIdentityService(IBootstrapIdentityRepository repository, IPasswordHasher passwordHasher, ICompromisedPasswordChecker compromisedPasswords, IAuthorityWallClock clock, IIdentityIdSource ids);
    public bool MayStartNetworkListener();
    public BootstrapOutcome Complete(CompleteBootstrapRequest request, ReadOnlySpan<char> bootstrapPassword, ReadOnlySpan<char> newPassword);
}
```

## Identity/IdentityPorts.cs

源码：[IdentityPorts.cs](../../../src/Monitor.Application/Identity/IdentityPorts.cs) · 命名空间：`Monitor.Application.Identity`

```csharp
public interface IPasswordHasher
{
    public PasswordVerifier Hash(ReadOnlySpan<char> password);
    public bool Verify(ReadOnlySpan<char> password, PasswordVerifier verifier);
    public bool IsSupported(PasswordVerifier verifier);
}
public interface ICompromisedPasswordChecker
{
    public bool IsCompromised(ReadOnlySpan<char> password);
}
public interface IBootstrapIdentityRepository
{
    public BootstrapIdentitySnapshot LoadBootstrap();
    public bool UsernameExists(string canonicalUsername);
    public BootstrapCommitResult TryCompleteBootstrap(BootstrapCompletion completion);
}
public interface IAuthorityWallClock
{
    public DateTimeOffset UtcNow { get; }
}
public interface IIdentityIdSource
{
    public Guid NewPrincipalId();
    public Guid NewAuditId();
    public Guid NewSessionId();
}
public interface IInstitutionAccountRepository
{
    public InstitutionAccount? FindByCanonicalUsername(string canonicalUsername);
}
public sealed record AuthenticationLockState(bool IsLocked, DateTimeOffset? LockedUntilUtc)
{
}
public interface IAuthenticationFailureTracker
{
    public AuthenticationLockState GetLockState(string canonicalUsername, string sourceAddress, DateTimeOffset nowUtc);
    public AuthenticationLockState RecordFailure(AuthenticationFailureAuditRecord audit);
    public void RecordRejection(AuthenticationFailureAuditRecord audit);
    public void ClearAccountFailures(string canonicalUsername);
}
```

## Identity/InstitutionAuthenticationService.cs

源码：[InstitutionAuthenticationService.cs](../../../src/Monitor.Application/Identity/InstitutionAuthenticationService.cs) · 命名空间：`Monitor.Application.Identity`

```csharp
public sealed record InstitutionAuthenticationRequest(string Username, string SourceAddress, InstitutionAuthenticationContext Context, Guid CorrelationId)
{
}
public sealed record InstitutionAuthenticationResult(AuthenticationOutcomeKind Kind, string ReasonCode, LocalPrincipal? Principal = null, DateTimeOffset? LockedUntilUtc = null)
{
}
public sealed class InstitutionAuthenticationService
{
    public InstitutionAuthenticationService(IInstitutionAccountRepository accounts, IAuthenticationFailureTracker failures, IPasswordHasher passwordHasher, PasswordVerifier dummyVerifier, IAuthorityWallClock clock, IIdentityIdSource ids);
    public InstitutionAuthenticationResult Authenticate(InstitutionAuthenticationRequest request, ReadOnlySpan<char> password);
}
```

## Localization/ITextLocalizer.cs

源码：[ITextLocalizer.cs](../../../src/Monitor.Application/Localization/ITextLocalizer.cs) · 命名空间：`Monitor.Application.Localization`

行为与协作约定见[本地化接口](../localization.md)。

```csharp
public sealed record LocalizedText(string Key, string Value, string RequestedLocale, string? ResolvedLocale)
{
    public bool IsMissing { get; }
    public bool IsFallback { get; }
}
public interface ITextLocalizer
{
    public string Locale { get; }
    public LocalizedText Resolve(string key);
    public string GetString(string key);
    public string Format(string key, params object?[] arguments);
}
```

## Measurements/CapnographyMeasurement.cs

源码：[CapnographyMeasurement.cs](../../../src/Monitor.Application/Measurements/CapnographyMeasurement.cs) · 命名空间：`Monitor.Application.Measurements`

```csharp
public enum WaveformMeasurementStatus
{
    WarmingUp,
    Valid,
    Stale,
    NoData,
    PoorSignal,
    Uncountable,
    OutOfRange
}
public sealed record CapnographyReading(WaveformMeasurementStatus Status, int? Value, long? MeasuredAtNs)
{
}
public sealed record CapnographyActivity(WaveformMeasurementStatus Status, long? ContinuousUsableSinceNs, long? LastExpirationNs, long? LastSampleNs)
{
}
public sealed record CapnographyResult(CapnographyReading EndTidalCentiMmHg, CapnographyReading RespirationsMilliPerMinute)
{
    public CapnographyActivity? Activity { get; init; }
}
public sealed record MeasuredExpiration(long RiseTimeNs, long PeakTimeNs, long ConfirmedAtNs, int EndTidalCentiMmHg)
{
}
public sealed class CapnographyMeasurement
{
    public const long RateWindowNs = 20_000_000_000;
    public const int MaximumRateIntervals = 4;
    public Guid ChannelId { get; }
    public CapnographyMeasurement(Guid channelId);
    public IReadOnlyList<MeasuredExpiration> Consume(ReadOnlySpan<byte> wire);
    public CapnographyResult Read(long asOfSampleTimeNs);
    public Checkpoint Capture();
    public static CapnographyMeasurement Restore(Checkpoint checkpoint);
    public sealed class Checkpoint
    {
    }
}
```

## Measurements/EcgHeartRateMeasurement.cs

源码：[EcgHeartRateMeasurement.cs](../../../src/Monitor.Application/Measurements/EcgHeartRateMeasurement.cs) · 命名空间：`Monitor.Application.Measurements`

```csharp
public sealed record DetectedEcgBeat(long PeakTimeNs, long ConfirmedAtNs)
{
}
public sealed record EcgHeartRateReading(WaveformMeasurementStatus Status, int? MilliBeatsPerMinute, long? LastBeatTimeNs)
{
}
public sealed class EcgHeartRateMeasurement
{
    public const long RateWindowNs = 20_000_000_000;
    public const int MaximumRateIntervals = 8;
    public Guid ChannelId { get; }
    public EcgHeartRateMeasurement(Guid channelId);
    public IReadOnlyList<DetectedEcgBeat> Consume(ReadOnlySpan<byte> wire);
    public IReadOnlyList<DetectedEcgBeat> Consume(ReadOnlySpan<byte> wire, out IReadOnlyList<DetectedEcgRhythmEvent> rhythmEvents);
    public EcgHeartRateReading Read(long asOfSampleTimeNs);
    public EcgRhythmReading ReadRhythm(long asOfSampleTimeNs);
    public Checkpoint Capture();
    public static EcgHeartRateMeasurement Restore(Checkpoint checkpoint);
    public sealed class Checkpoint
    {
    }
}
```

## Measurements/EcgRhythmAnalysis.cs

源码：[EcgRhythmAnalysis.cs](../../../src/Monitor.Application/Measurements/EcgRhythmAnalysis.cs) · 命名空间：`Monitor.Application.Measurements`

```csharp
public enum EcgRhythmEventKind
{
    IrregularRhythm,
    SuspectedAtrialFibrillation
}
public enum EcgRhythmTransition
{
    Started,
    Ended,
    Interrupted
}
public enum EcgRhythmInterruption
{
    None,
    SignalUnavailable,
    StreamDiscontinuity,
    InsufficientAtrialEvidence,
    InsufficientRrEvidence
}
public sealed record DetectedEcgRhythmEvent(EcgRhythmEventKind Kind, EcgRhythmTransition Transition, long EvidenceFromNs, long ConfirmedAtNs, EcgRhythmInterruption Interruption = EcgRhythmInterruption.None)
{
}
public sealed record EcgRhythmEvidence(int RrIntervalCount, int IrregularChangesPermille, int RrBinCount, int AtrialWindowCount, int AtrialCoherencePermille, int AtrialRmsMicrovolts)
{
}
public sealed record EcgRhythmReading(WaveformMeasurementStatus Status, bool? IrregularRhythm, bool? SuspectedAtrialFibrillation, EcgRhythmEvidence? Evidence)
{
}
```

## Measurements/ImpedanceRespirationMeasurement.cs

源码：[ImpedanceRespirationMeasurement.cs](../../../src/Monitor.Application/Measurements/ImpedanceRespirationMeasurement.cs) · 命名空间：`Monitor.Application.Measurements`

```csharp
public sealed record DetectedImpedanceBreath(long PeakTimeNs, long ConfirmedAtNs)
{
}
public sealed record ImpedanceRespirationReading(WaveformMeasurementStatus Status, int? MilliBreathsPerMinute, long? LastBreathTimeNs)
{
}
public sealed class ImpedanceRespirationMeasurement
{
    public const long RateWindowNs = 30_000_000_000;
    public const int MaximumRateIntervals = 4;
    public Guid ChannelId { get; }
    public ImpedanceRespirationMeasurement(Guid channelId);
    public IReadOnlyList<DetectedImpedanceBreath> Consume(ReadOnlySpan<byte> wire);
    public ImpedanceRespirationReading Read(long asOfSampleTimeNs);
    public Checkpoint Capture();
    public static ImpedanceRespirationMeasurement Restore(Checkpoint checkpoint);
    public sealed class Checkpoint
    {
    }
}
```

## Measurements/LiveWaveformMeasurements.cs

源码：[LiveWaveformMeasurements.cs](../../../src/Monitor.Application/Measurements/LiveWaveformMeasurements.cs) · 命名空间：`Monitor.Application.Measurements`

```csharp
public sealed record LiveMeasurementSnapshot(long SampleTimeNs, EcgHeartRateReading HeartRate, ImpedanceRespirationReading ImpedanceRespiration, PlethPulseRateReading PulseRate, CapnographyResult Capnography, OpticalSaturationReading SpO2, MeanPressureReading AbpMean, MeanPressureReading PaMean, MeanPressureReading CvpMean)
{
    public EcgRhythmReading EcgRhythm { get; init; }
}
public sealed class LiveWaveformMeasurements
{
    public LiveWaveformMeasurements(OpticalSaturationMeasurement calibration);
    public static LiveWaveformMeasurements CreateIllustration();
    public LiveMeasurementSnapshot Consume(ReadOnlySpan<byte> wire);
    public LiveMeasurementSnapshot Consume(ReadOnlySpan<byte> wire, out IReadOnlyList<DetectedEcgBeat> detectedBeats);
    public LiveMeasurementSnapshot Consume(ReadOnlySpan<byte> wire, out IReadOnlyList<DetectedEcgBeat> detectedBeats, out IReadOnlyList<DetectedPlethPulse> detectedPulses);
    public LiveMeasurementSnapshot Consume(ReadOnlySpan<byte> wire, out IReadOnlyList<DetectedEcgBeat> detectedBeats, out IReadOnlyList<DetectedPlethPulse> detectedPulses, out IReadOnlyList<DetectedEcgRhythmEvent> rhythmEvents);
    public LiveMeasurementSnapshot Read(long asOfSampleTimeNs);
    public Checkpoint Capture();
    public static LiveWaveformMeasurements Restore(Checkpoint checkpoint);
    public sealed class Checkpoint
    {
    }
}
```

## Measurements/MeanPressureMeasurement.cs

源码：[MeanPressureMeasurement.cs](../../../src/Monitor.Application/Measurements/MeanPressureMeasurement.cs) · 命名空间：`Monitor.Application.Measurements`

```csharp
public sealed record MeanPressureReading(WaveformMeasurementStatus Status, int? MeanCentiMmHg, long? WindowStartTimeNs, long? MeasuredAtNs)
{
    public PulsePressureReading? Pulse { get; init; }
}
public sealed class MeanPressureMeasurement
{
    public const int WindowSamples = 500;
    public Guid ChannelId { get; }
    public bool DetectPulse { get; }
    public MeanPressureMeasurement(Guid channelId, bool detectPulse = false);
    public MeanPressureReading Consume(ReadOnlySpan<byte> wire);
    public MeanPressureReading Read(long asOfSampleTimeNs);
    public Checkpoint Capture();
    public static MeanPressureMeasurement Restore(Checkpoint checkpoint);
    public sealed class Checkpoint
    {
    }
}
```

## Measurements/MeasurementDisplay.cs

源码：[MeasurementDisplay.cs](../../../src/Monitor.Application/Measurements/MeasurementDisplay.cs) · 命名空间：`Monitor.Application.Measurements`

```csharp
public enum MeasurementSource
{
    Ecg,
    Pleth,
    ImpedanceRespiration,
    Co2,
    Pressure,
    SpO2
}
public enum MeasurementTechnicalFault
{
    None,
    ExcessiveInterference,
    SensorDisconnected,
    LeadsDisconnected
}
public sealed record MeasurementDisplay(string NumericText, string? TopNotice)
{
    public static MeasurementDisplay Resolve(MeasurementSource source, WaveformMeasurementStatus status, string? validNumericText, MeasurementTechnicalFault fault = MeasurementTechnicalFault.None);
}
```

## Measurements/OpticalSaturationAcquisition.cs

源码：[OpticalSaturationAcquisition.cs](../../../src/Monitor.Application/Measurements/OpticalSaturationAcquisition.cs) · 命名空间：`Monitor.Application.Measurements`

```csharp
public sealed class OpticalSaturationAcquisition
{
    public OpticalSaturationAcquisition(Guid redChannel, Guid infraredChannel, OpticalSaturationMeasurement measurement);
    public OpticalSaturationReading Consume(ReadOnlySpan<byte> wire);
    public OpticalSaturationReading Read(long asOfSampleTimeNs);
    public Checkpoint Capture();
    public static OpticalSaturationAcquisition Restore(Checkpoint checkpoint);
    public sealed class Checkpoint
    {
    }
}
```

## Measurements/OpticalSaturationMeasurement.cs

源码：[OpticalSaturationMeasurement.cs](../../../src/Monitor.Application/Measurements/OpticalSaturationMeasurement.cs) · 命名空间：`Monitor.Application.Measurements`

```csharp
public sealed record OpticalSample(long SampleTimeNs, int Red, int Infrared, uint QualityFlags = 0)
{
}
public sealed record SaturationCalibrationPoint(int RatioPpm, int SaturationMilliPercent)
{
}
public sealed record OpticalSaturationReading(WaveformMeasurementStatus Status, int? SaturationMilliPercent, int? RatioPpm, long? MeasuredAtNs)
{
    public int? PerfusionMilliPercent { get; init; }
    public bool IsQuestionable { get; }
}
public sealed class OpticalSaturationMeasurement
{
    public const int WindowSamples = 500;
    public const long SampleStepNs = 8_000_000;
    public string CalibrationId { get; }
    public OpticalSaturationMeasurement(string calibrationId, IReadOnlyList<SaturationCalibrationPoint> calibration);
    public static OpticalSaturationMeasurement CreateIllustration();
    public OpticalSaturationReading Estimate(IReadOnlyList<OpticalSample> samples, long asOfSampleTimeNs);
}
```

## Measurements/PlethPulseRateMeasurement.cs

源码：[PlethPulseRateMeasurement.cs](../../../src/Monitor.Application/Measurements/PlethPulseRateMeasurement.cs) · 命名空间：`Monitor.Application.Measurements`

```csharp
public sealed record DetectedPlethPulse(long PeakTimeNs, long ConfirmedAtNs)
{
}
public sealed record PlethPulseRateReading(WaveformMeasurementStatus Status, int? MilliBeatsPerMinute, long? LastPulseTimeNs)
{
}
public sealed class PlethPulseRateMeasurement
{
    public const long RateWindowNs = 20_000_000_000;
    public const int MaximumRateIntervals = 8;
    public Guid ChannelId { get; }
    public PlethPulseRateMeasurement(Guid channelId);
    public IReadOnlyList<DetectedPlethPulse> Consume(ReadOnlySpan<byte> wire);
    public PlethPulseRateReading Read(long asOfSampleTimeNs);
    public Checkpoint Capture();
    public static PlethPulseRateMeasurement Restore(Checkpoint checkpoint);
    public sealed class Checkpoint
    {
    }
}
```

## Measurements/PressurePulseTracker.cs

源码：[PressurePulseTracker.cs](../../../src/Monitor.Application/Measurements/PressurePulseTracker.cs) · 命名空间：`Monitor.Application.Measurements`

```csharp
public sealed record PulsePressureReading(WaveformMeasurementStatus Status, int? SystolicCentiMmHg, int? DiastolicCentiMmHg, long? LastPeakTimeNs)
{
}
```

## Measurements/PulseOximeterMeasurement.cs

源码：[PulseOximeterMeasurement.cs](../../../src/Monitor.Application/Measurements/PulseOximeterMeasurement.cs) · 命名空间：`Monitor.Application.Measurements`

```csharp
public sealed record PulseOximeterReading(PlethPulseRateReading PulseRate, OpticalSaturationReading SpO2)
{
}
public sealed class PulseOximeterMeasurement
{
    public PulseOximeterMeasurement(Guid plethChannel, Guid redChannel, Guid infraredChannel, OpticalSaturationMeasurement calibration);
    public static PulseOximeterMeasurement CreateIllustration(Guid plethChannel);
    public PulseOximeterReading Consume(ReadOnlySpan<byte> wire);
    public PulseOximeterReading Read(long asOfSampleTimeNs);
    public Checkpoint Capture();
    public static PulseOximeterMeasurement Restore(Checkpoint checkpoint);
    public sealed class Checkpoint
    {
    }
}
```

## Presentation/AlarmAttentionJournal.cs

源码：[AlarmAttentionJournal.cs](../../../src/Monitor.Application/Presentation/AlarmAttentionJournal.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public enum AlarmLatchingMode { NonLatching, UntilAcknowledged }
public enum AlarmAttentionState { None, ActiveUnacknowledged, ActiveAcknowledged, RecoveredUnacknowledged }
public enum AlarmAttentionKind { Started, Replaced, SeverityChanged, Acknowledged, Recovered, Interrupted, PolicyChanged }
public sealed record AlarmAttentionSnapshot(string ConditionId, ulong Revision, AlarmEpisodeId? Episode, MonitorNoticeLevel? Level, AlarmAttentionState State, AlarmLatchingMode LatchingMode)
{
    public long? AcknowledgedAtNs { get; init; }
    public bool NeedsAcknowledgement { get; }
}
public sealed record AlarmAttentionRecord(ulong Sequence, long SampleTimeNs, AlarmAttentionKind Kind, AlarmAttentionSnapshot Previous, AlarmAttentionSnapshot Current, AlarmTransitionReason Reason);
public sealed class AlarmAttentionJournal
{
    public const int Capacity = 256;
    public ulong DroppedRecordCount { get; private set; }
    public IReadOnlyList<AlarmAttentionSnapshot> Conditions { get; }
    public IReadOnlyList<AlarmAttentionRecord> Records { get; }
    public void Configure(string conditionId, AlarmLatchingMode mode);
    public bool Acknowledge(AlarmEpisodeId episode, ulong expectedRevision);
}
```

## Presentation/AlarmLifecycleJournal.cs

源码：[AlarmLifecycleJournal.cs](../../../src/Monitor.Application/Presentation/AlarmLifecycleJournal.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public enum AlarmConditionState
{
    Unobserved, Disabled, Normal, PendingTrigger, Active, PendingRecovery, Indeterminate
}
public enum AlarmTransitionKind
{
    StateChanged, Started, SeverityChanged, Ended
}
public enum AlarmTransitionReason
{
    None, Confirmed, Recovered, Disabled, ConfigurationChanged, InvalidConfiguration,
    DataUnavailable, ObservationGap, ClockRewind, SignalSegmentChanged, SessionReset
}
public sealed record AlarmEpisodeId(string ConditionId, ulong Occurrence);
public sealed record AlarmConditionSnapshot(string ConditionId, AlarmConditionState State, AlarmEpisodeId? Episode, MonitorNoticeLevel? Level, long ChangedAtNs, AlarmTransitionReason Reason);
public sealed record AlarmLifecycleTransition(ulong Sequence, long SampleTimeNs, string ConditionId, AlarmEpisodeId? Episode, AlarmTransitionKind Kind, AlarmConditionState PreviousState, AlarmConditionState State, MonitorNoticeLevel? PreviousLevel, MonitorNoticeLevel? Level, AlarmTransitionReason Reason);
public sealed class AlarmLifecycleJournal
{
    public const int Capacity = 256;
    public AlarmAttentionJournal Attention { get; }
    public ulong DroppedNotificationCount { get; private set; }
    public IReadOnlyList<AlarmNotificationRecord> NotificationRecords { get; }
    public AlarmNotificationPolicy NotificationPolicyFor(string conditionId);
    public void ConfigureNotifications(string conditionId, AlarmNotificationPolicy policy);
    public ulong DroppedTransitionCount { get; private set; }
    public IReadOnlyList<AlarmConditionSnapshot> Conditions { get; }
    public IReadOnlyList<AlarmLifecycleTransition> Transitions { get; }
}
```

## Presentation/AlarmNotificationPolicy.cs

源码：[AlarmNotificationPolicy.cs](../../../src/Monitor.Application/Presentation/AlarmNotificationPolicy.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record AlarmNotificationPolicy(int RepeatSuppressionMilliseconds = 0, int ReminderMilliseconds = 0)
{
    public AlarmSoundDuration SoundDuration { get; init; }
    public const int MaximumMilliseconds = 3600000;
    public static AlarmNotificationPolicy Default { get; }
    public void Validate();
}
public enum AlarmNotificationKind
{
    FirstOccurrence,
    Recurrence,
    SeverityEscalation,
    RepeatSuppressed,
    DeferredRepeat,
    Reminder,
    PolicyChanged
}
public sealed record AlarmNotificationDecision(AlarmEpisodeId Episode, MonitorNoticeLevel Level, long SampleTimeNs, AlarmNotificationKind Kind, AlarmNotificationPolicy Policy, long SuppressionRemainingNs = 0)
{
    public bool RequestsNotification { get; }
}
public sealed record AlarmNotificationRecord(ulong Sequence, AlarmNotificationDecision Decision);
```

## Presentation/CapturedRecordBinding.cs

源码：[CapturedRecordBinding.cs](../../../src/Monitor.Application/Presentation/CapturedRecordBinding.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class CapturedRecordBindingException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record RecordSlotBinding(string SlotId, Guid ChannelId)
{
}
public sealed record CapturedRecordBindingState(FillOnceThenHoldState Presentation, WaveformRecordArchiveState Archive, IReadOnlyList<RecordSlotBinding> Slots)
{
}
public sealed class CapturedRecordBinding
{
    public static CapturedRecordBinding Create(FillOnceThenHoldState presentation, WaveformRecordArchive archive, IReadOnlyList<RecordSlotBinding> slots);
    public static CapturedRecordBinding Restore(CapturedRecordBindingState state);
    public SweepStateProjectionSnapshot CaptureProjection();
    public PinnedRecordRange CapturePinnedRecordRange();
    public IReadOnlyList<RecordSlotBinding> Slots { get; }
    public IReadOnlyList<ArchivedWaveformBlock> ReadBlocks();
    public CapturedRecordBindingState CaptureState();
}
```

## Presentation/CapturedRecordDrag.cs

源码：[CapturedRecordDrag.cs](../../../src/Monitor.Application/Presentation/CapturedRecordDrag.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class CapturedRecordDrag
{
    public CapturedRecordDrag(CapturedRecordMeasurement measurement, RecordCursorEnd end, RecordCursorViewport viewport, EcgVerticalScale scale);
    public CapturedRecordCursorPair Preview(ExactPlotCoordinate x, ExactPlotCoordinate y, RecordCursorViewport viewport, EcgVerticalScale scale);
    public CapturedRecordCursorPair Commit(RecordCursorViewport viewport, EcgVerticalScale scale);
    public CapturedRecordCursorPair Cancel();
}
```

## Presentation/CapturedRecordMeasurement.cs

源码：[CapturedRecordMeasurement.cs](../../../src/Monitor.Application/Presentation/CapturedRecordMeasurement.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class CapturedRecordMeasurementException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed class CapturedRecordCursor
{
    public RecordSlotBinding Slot { get; }
    public EcgManualCursor Value { get; }
}
public sealed record CapturedRecordMeasurementCheckpoint(CapturedRecordBindingState Record, string SlotId, EcgManualCursor First, EcgManualCursor Second)
{
}
public sealed record RestoredRecordMeasurement(CapturedRecordMeasurement Measurement, CapturedRecordCursor First, CapturedRecordCursor Second)
{
}
public sealed record ProjectedRecordCursor(CapturedRecordCursor Cursor, SweepPixelPosition X, EcgVerticalPosition Y)
{
}
public sealed record RecordCursorViewport(long StartDataTimeNs, long EndExclusiveDataTimeNs, int PlotLeftPixels, int PlotWidthPixels)
{
}
public enum RecordCursorEnd
{
    First,
    Second
}
[Flags]
public enum RecordCursorHits
{
    None = 0,
    First = 1,
    Second = 2
}
public sealed record CapturedRecordCursorPair(CapturedRecordCursor First, CapturedRecordCursor Second)
{
}
public sealed record RecordMeasurementDisplay(string ReasonCode, ProjectedRecordCursor? First, ProjectedRecordCursor? Second, EcgManualMeasurementResult? Measurement)
{
}
public sealed class CapturedRecordMeasurement
{
    public CapturedRecordMeasurement(CapturedRecordBinding record, string slotId, SystemViewCommandAssessmentPolicy policy);
    public RecordSlotBinding Slot { get; }
    public SystemViewCommandAssessmentPolicy CurrentPolicy { get; }
    public CapturedRecordCursorPair? CurrentPair { get; private set; }
    public RecordCursorHits HitTestCursors(ExactPlotCoordinate x, ExactPlotCoordinate y, ExactPlotCoordinate radius, RecordCursorViewport viewport, EcgVerticalScale verticalScale);
    public RecordMeasurementDisplay CaptureDisplay(RecordCursorViewport viewport, EcgVerticalScale verticalScale, bool allowAuxiliaryRate);
    public void ClearPair();
    public CapturedRecordCursorPair ReplacePair(EcgManualCursor first, EcgManualCursor second);
    public CapturedRecordCursorPair ReplacePairFromPoints(ExactPlotCoordinate firstX, ExactPlotCoordinate firstY, ExactPlotCoordinate secondX, ExactPlotCoordinate secondY, RecordCursorViewport viewport, EcgVerticalScale verticalScale);
    public CapturedRecordCursorPair MoveCursor(RecordCursorEnd end, ExactPlotCoordinate x, ExactPlotCoordinate y, RecordCursorViewport viewport, EcgVerticalScale verticalScale);
    public CapturedRecordMeasurementCheckpoint CaptureCheckpoint();
    public CapturedRecordMeasurementCheckpoint CaptureCheckpoint(CapturedRecordCursor first, CapturedRecordCursor second);
    public static RestoredRecordMeasurement Restore(CapturedRecordMeasurementCheckpoint checkpoint, SystemViewCommandAssessmentPolicy currentPolicy);
    public void UpdatePolicy(SystemViewCommandAssessmentPolicy policy);
    public CapturedRecordCursor CreateCursor(EcgManualCursor value);
    public CapturedRecordCursor CreateCursorFromPoint(ExactPlotCoordinate x, ExactPlotCoordinate y, RecordCursorViewport viewport, EcgVerticalScale verticalScale);
    public ProjectedRecordCursor ProjectCursor(CapturedRecordCursor cursor, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale verticalScale);
    public ProjectedRecordCursor? ProjectCursor(CapturedRecordCursor cursor, RecordCursorViewport viewport, EcgVerticalScale verticalScale);
    public EcgManualMeasurementResult Calculate(CapturedRecordCursor first, CapturedRecordCursor second, bool allowAuxiliaryRate);
}
```

## Presentation/CapturedRecordNavigation.cs

源码：[CapturedRecordNavigation.cs](../../../src/Monitor.Application/Presentation/CapturedRecordNavigation.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record CapturedRecordNavigationState(CapturedRecordBindingState Record, long PageDurationNs, ulong PageIndex)
{
}
public sealed record RecordNavigationCommandDisplay(bool IsEnabled, string ReasonCode)
{
}
public sealed record CapturedRecordNavigationDisplay(CapturedRecordPage Page, SystemViewCommandAssessmentPolicy Policy, RecordNavigationCommandDisplay Previous, RecordNavigationCommandDisplay Next, RecordNavigationCommandDisplay Select)
{
}
public sealed class CapturedRecordNavigation
{
    public CapturedRecordNavigation(CapturedRecordBinding record, long pageDurationNs, ulong initialPageIndex, SystemViewCommandAssessmentPolicy policy);
    public CapturedRecordPage CurrentPage { get; private set; }
    public SystemViewCommandAssessmentPolicy CurrentPolicy { get; }
    public CapturedRecordNavigationDisplay CaptureDisplay();
    public CapturedRecordStudyView CreateStudyView(Ecg12RecordContext context, string slotId, SystemViewCommandAssessmentPolicy measurementPolicy);
    public void UpdatePolicy(SystemViewCommandAssessmentPolicy policy);
    public CapturedRecordPage SelectPage(ulong pageIndex);
    public CapturedRecordPage NextPage();
    public CapturedRecordPage PreviousPage();
    public CapturedRecordNavigationState CaptureState();
    public static CapturedRecordNavigation Restore(CapturedRecordNavigationState state, SystemViewCommandAssessmentPolicy currentPolicy);
}
```

## Presentation/CapturedRecordPagination.cs

源码：[CapturedRecordPagination.cs](../../../src/Monitor.Application/Presentation/CapturedRecordPagination.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record CapturedRecordPage(ulong PageIndex, ulong PageCount, long StartDataTimeNs, long EndExclusiveDataTimeNs)
{
}
public sealed class CapturedRecordPaginationException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public static class CapturedRecordPagination
{
    public static CapturedRecordPage Resolve(CapturedRecordBinding record, long pageDurationNs, ulong pageIndex);
}
```

## Presentation/CapturedRecordPathProjection.cs

源码：[CapturedRecordPathProjection.cs](../../../src/Monitor.Application/Presentation/CapturedRecordPathProjection.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class CapturedRecordPathException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record RecordWaveformPoint(ExactPlotCoordinate X, EcgVerticalPosition Y)
{
}
public sealed record RecordWaveformSegment(ulong StartSampleIndex, ulong EndSampleIndex, ulong StartBlockSequence, ulong EndBlockSequence, RecordWaveformPoint Start, RecordWaveformPoint End)
{
}
public sealed record CapturedRecordPathPageDisplay(CapturedRecordQualityPageDisplay Content, IReadOnlyList<RecordWaveformSegment> Segments)
{
}
```

## Presentation/CapturedRecordQualityBinding.cs

源码：[CapturedRecordQualityBinding.cs](../../../src/Monitor.Application/Presentation/CapturedRecordQualityBinding.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class CapturedRecordQualityException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record RecordQualityRule(uint QualityFlags, bool Drawable)
{
}
public sealed record RecordSampleQuality(uint QualityFlags, bool Drawable)
{
}
public sealed class CapturedRecordQualityBinding
{
    public RecordSlotBinding Slot { get; }
    public IReadOnlyList<RecordQualityRule> Rules { get; }
    public CapturedRecordQualityBinding(CapturedRecordBinding record, string slotId, IReadOnlyList<RecordQualityRule> rules, int maximumRules);
}
public sealed record CapturedRecordQualityBlock(CapturedRecordVoltageBlock Source, IReadOnlyList<RecordSampleQuality> Samples)
{
}
public sealed record CapturedRecordQualityPageDisplay(CapturedRecordVoltagePageDisplay Content, IReadOnlyList<RecordQualityRule> Rules, IReadOnlyList<CapturedRecordQualityBlock> Blocks)
{
}
```

## Presentation/CapturedRecordStudyDrag.cs

源码：[CapturedRecordStudyDrag.cs](../../../src/Monitor.Application/Presentation/CapturedRecordStudyDrag.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class CapturedRecordStudyDrag
{
    public CapturedRecordCursorPair PreviewPointer(bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, ExactPlotCoordinate x, ExactPlotCoordinate y);
    public CapturedRecordCursorPair CommitPointer(bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, ExactPlotCoordinate x, ExactPlotCoordinate y);
    public CapturedRecordCursorPair Preview(bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, ExactPlotCoordinate x, ExactPlotCoordinate y);
    public CapturedRecordCursorPair Commit(bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale);
    public CapturedRecordCursorPair Cancel();
}
```

## Presentation/CapturedRecordStudySession.cs

源码：[CapturedRecordStudySession.cs](../../../src/Monitor.Application/Presentation/CapturedRecordStudySession.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record CapturedRecordStudySessionState(CapturedRecordNavigationState Navigation, string SlotId, EcgManualCursor? First, EcgManualCursor? Second)
{
}
public sealed record RestoredRecordStudySession(CapturedRecordNavigation Navigation, CapturedRecordStudyView View)
{
}
public sealed record ThemedCapturedRecordStudySessionState(CapturedRecordStudySessionState Study, Ecg12ThemeState Theme)
{
}
public sealed record RestoredThemedRecordStudySession(RestoredRecordStudySession Study, Ecg12ThemeSelection Theme)
{
}
public sealed record ZoomedCapturedRecordStudySessionState(ThemedCapturedRecordStudySessionState Content, Ecg12ZoomState Zoom)
{
}
public sealed record RestoredZoomedRecordStudySession(RestoredThemedRecordStudySession Content, Ecg12ZoomSelection Zoom)
{
}
public static class CapturedRecordStudySession
{
    public static RestoredZoomedRecordStudySession RestoreZoomed(ZoomedCapturedRecordStudySessionState state, Ecg12RecordContext currentContext, SystemViewCommandAssessmentPolicy paginationPolicy, SystemViewCommandAssessmentPolicy measurementPolicy, SystemViewCommandAssessmentPolicy themePolicy, bool allowLocalThemeSelection, SystemViewCommandAssessmentPolicy zoomPolicy);
    public static RestoredThemedRecordStudySession RestoreThemed(ThemedCapturedRecordStudySessionState state, Ecg12RecordContext currentContext, SystemViewCommandAssessmentPolicy paginationPolicy, SystemViewCommandAssessmentPolicy measurementPolicy, SystemViewCommandAssessmentPolicy themePolicy, bool allowLocalThemeSelection);
    public static RestoredRecordStudySession Restore(CapturedRecordStudySessionState state, Ecg12RecordContext currentContext, SystemViewCommandAssessmentPolicy paginationPolicy, SystemViewCommandAssessmentPolicy measurementPolicy);
}
```

## Presentation/CapturedRecordStudyView.cs

源码：[CapturedRecordStudyView.cs](../../../src/Monitor.Application/Presentation/CapturedRecordStudyView.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record CapturedRecordStudyDisplay(Ecg12ViewAdmissionDecision Admission, CapturedRecordBinding? Record, RecordMeasurementDisplay? Measurement, RecordSlotBinding? MeasurementSlot)
{
}
public sealed record CapturedRecordPageDisplay(CapturedRecordStudyDisplay Study, CapturedRecordNavigationDisplay? Navigation, RecordCursorViewport? Viewport)
{
    public CapturedRecordPage? Page { get; }
}
public sealed record ThemedCapturedRecordPageDisplay(CapturedRecordPageDisplay Content, Ecg12ThemeDisplay? Theme)
{
}
public sealed record CapturedRecordWaveformPageDisplay(CapturedRecordPageDisplay Content, ArchivedWaveformChannelRead? Waveform)
{
}
public sealed record RecordScreenZoomLayout(int PageWidth, int PageHeight, int AvailableWidth, int AvailableHeight)
{
}
public sealed record ZoomedCapturedRecordPageDisplay(ThemedCapturedRecordPageDisplay Content, Ecg12ZoomDisplay? Zoom, Ecg12ScreenTransform? Transform)
{
}
public sealed record GridCapturedRecordPageDisplay(ThemedCapturedRecordPageDisplay Content, EcgPaperGridPlan? GridPlan, IReadOnlyList<EcgPaperGridLine> GridLines)
{
}
public sealed class CapturedRecordStudyView
{
    public CapturedRecordStudyView(CapturedRecordBinding record, Ecg12RecordContext context, string slotId, SystemViewCommandAssessmentPolicy measurementPolicy);
    public CapturedRecordMeasurement Measurement { get; private set; }
    public CapturedRecordPathPageDisplay CapturePathPageDisplay(CapturedRecordNavigation navigation, CapturedRecordVoltageBinding voltageBinding, CapturedRecordQualityBinding qualityBinding, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, bool allowAuxiliaryRate, int maximumSamples, int maximumSegments, CancellationToken cancellationToken = default);
    public CapturedRecordQualityPageDisplay CaptureQualityPageDisplay(CapturedRecordNavigation navigation, CapturedRecordVoltageBinding voltageBinding, CapturedRecordQualityBinding qualityBinding, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, bool allowAuxiliaryRate, int maximumSamples, CancellationToken cancellationToken = default);
    public CapturedRecordVoltagePageDisplay CaptureVoltagePageDisplay(CapturedRecordNavigation navigation, CapturedRecordVoltageBinding voltageBinding, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, bool allowAuxiliaryRate, int maximumSamples, CancellationToken cancellationToken = default);
    public CapturedRecordWaveformHorizontalPageDisplay CaptureWaveformHorizontalPageDisplay(CapturedRecordNavigation navigation, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, bool allowAuxiliaryRate, int maximumSamples, CancellationToken cancellationToken = default);
    public CapturedRecordWaveformPageDisplay CaptureWaveformPageDisplay(CapturedRecordNavigation navigation, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, bool allowAuxiliaryRate, int maximumSamples, CancellationToken cancellationToken = default);
    public ZoomedCapturedRecordPageDisplay CaptureZoomedPageDisplay(CapturedRecordNavigation navigation, Ecg12ThemeSelection theme, Ecg12ZoomSelection zoom, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, RecordScreenZoomLayout layout, bool allowAuxiliaryRate);
    public GridCapturedRecordPageDisplay CaptureGridPageDisplay(CapturedRecordNavigation navigation, Ecg12ThemeSelection theme, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, EcgPaperScale paperScale, int gridOriginXPixels, int gridOriginYPixels, int maximumGridLines, bool allowAuxiliaryRate, CancellationToken cancellationToken = default);
    public ThemedCapturedRecordPageDisplay CaptureThemedPageDisplay(CapturedRecordNavigation navigation, Ecg12ThemeSelection theme, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, bool allowAuxiliaryRate);
    public void UpdateThemedCommandPolicies(CapturedRecordNavigation navigation, Ecg12ThemeSelection theme, SystemViewCommandAssessmentPolicy paginationPolicy, SystemViewCommandAssessmentPolicy measurementPolicy, SystemViewCommandAssessmentPolicy themePolicy, bool allowLocalThemeSelection);
    public void UpdateZoomedCommandPolicies(CapturedRecordNavigation navigation, Ecg12ThemeSelection theme, Ecg12ZoomSelection zoom, SystemViewCommandAssessmentPolicy paginationPolicy, SystemViewCommandAssessmentPolicy measurementPolicy, SystemViewCommandAssessmentPolicy themePolicy, bool allowLocalThemeSelection, SystemViewCommandAssessmentPolicy zoomPolicy);
    public void UpdateCommandPolicies(CapturedRecordNavigation navigation, SystemViewCommandAssessmentPolicy paginationPolicy, SystemViewCommandAssessmentPolicy measurementPolicy);
    public CapturedRecordPage ResolvePage(long pageDurationNs, ulong pageIndex);
    public CapturedRecordNavigation CreateNavigation(long pageDurationNs, ulong initialPageIndex, SystemViewCommandAssessmentPolicy paginationPolicy);
    public CapturedRecordStudySessionState CaptureSession(CapturedRecordNavigation navigation);
    public ThemedCapturedRecordStudySessionState CaptureThemedSession(CapturedRecordNavigation navigation, Ecg12ThemeSelection theme);
    public ZoomedCapturedRecordStudySessionState CaptureZoomedSession(CapturedRecordNavigation navigation, Ecg12ThemeSelection theme, Ecg12ZoomSelection zoom);
    public void SelectMeasurementSlot(string slotId);
    public void ClearPairOnCurrentPage(CapturedRecordNavigation navigation, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale);
    public CapturedRecordCursorPair PlacePairOnCurrentPage(CapturedRecordNavigation navigation, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, ExactPlotCoordinate firstX, ExactPlotCoordinate firstY, ExactPlotCoordinate secondX, ExactPlotCoordinate secondY);
    public CapturedRecordCursorPair PlacePairOnZoomedPage(CapturedRecordNavigation navigation, Ecg12ZoomSelection zoom, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, RecordScreenZoomLayout layout, ExactPlotCoordinate firstX, ExactPlotCoordinate firstY, ExactPlotCoordinate secondX, ExactPlotCoordinate secondY);
    public RecordCursorHits HitTestOnZoomedPage(CapturedRecordNavigation navigation, Ecg12ZoomSelection zoom, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, RecordScreenZoomLayout layout, ExactPlotCoordinate x, ExactPlotCoordinate y, ExactPlotCoordinate radius);
    public CapturedRecordZoomedDrag BeginCursorDragOnZoomedPage(CapturedRecordNavigation navigation, Ecg12ZoomSelection zoom, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, RecordScreenZoomLayout layout, ExactPlotCoordinate x, ExactPlotCoordinate y, ExactPlotCoordinate radius);
    public CapturedRecordCursorPair MoveCursorOnZoomedPage(CapturedRecordNavigation navigation, Ecg12ZoomSelection zoom, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, RecordScreenZoomLayout layout, RecordCursorEnd end, ExactPlotCoordinate x, ExactPlotCoordinate y);
    public CapturedRecordCursorPair MoveCursorOnCurrentPage(CapturedRecordNavigation navigation, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, RecordCursorEnd end, ExactPlotCoordinate x, ExactPlotCoordinate y);
    public CapturedRecordStudyDrag BeginCursorDrag(CapturedRecordNavigation navigation, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, RecordCursorEnd end);
    public RecordCursorHits HitTestOnCurrentPage(CapturedRecordNavigation navigation, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, ExactPlotCoordinate x, ExactPlotCoordinate y, ExactPlotCoordinate radius);
    public CapturedRecordStudyDrag BeginCursorDragAtPoint(CapturedRecordNavigation navigation, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, ExactPlotCoordinate x, ExactPlotCoordinate y, ExactPlotCoordinate radius);
    public CapturedRecordPageDisplay CapturePageDisplay(CapturedRecordNavigation navigation, bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, bool allowAuxiliaryRate);
    public CapturedRecordStudyDisplay CaptureDisplay(bool canPreserveGlobalSafetyOverlay, RecordCursorViewport viewport, EcgVerticalScale scale, bool allowAuxiliaryRate);
}
```

## Presentation/CapturedRecordVoltageBinding.cs

源码：[CapturedRecordVoltageBinding.cs](../../../src/Monitor.Application/Presentation/CapturedRecordVoltageBinding.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record ResolvedEcgVoltageChannel(uint SampleRateNumerator, uint SampleRateDenominator, EcgRawVoltageCalibration Calibration)
{
}
public sealed class CapturedRecordVoltageBinding
{
    public RecordSlotBinding Slot { get; }
    public ResolvedEcgVoltageChannel Channel { get; }
    public CapturedRecordVoltageBinding(CapturedRecordBinding record, string slotId, ResolvedEcgVoltageChannel channel);
}
public sealed record CapturedRecordVoltageBlock(CapturedRecordWaveformHorizontalBlock Source, IReadOnlyList<EcgSampleVoltage> Voltages, IReadOnlyList<EcgVerticalPosition> SampleY)
{
}
public sealed record CapturedRecordVoltagePageDisplay(CapturedRecordWaveformHorizontalPageDisplay Content, EcgVerticalScale? VerticalScale, ResolvedEcgVoltageChannel? VoltageChannel, IReadOnlyList<CapturedRecordVoltageBlock> Blocks)
{
}
```

## Presentation/CapturedRecordWaveformHorizontalProjection.cs

源码：[CapturedRecordWaveformHorizontalProjection.cs](../../../src/Monitor.Application/Presentation/CapturedRecordWaveformHorizontalProjection.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record CapturedRecordWaveformHorizontalBlock(ArchivedWaveformPlaneBlock Source, IReadOnlyList<ExactPlotCoordinate> SampleX)
{
}
public sealed record CapturedRecordWaveformHorizontalPageDisplay(CapturedRecordWaveformPageDisplay Content, IReadOnlyList<CapturedRecordWaveformHorizontalBlock> Blocks)
{
}
```

## Presentation/CapturedRecordZoomedDrag.cs

源码：[CapturedRecordZoomedDrag.cs](../../../src/Monitor.Application/Presentation/CapturedRecordZoomedDrag.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class CapturedRecordZoomedDrag
{
    public CapturedRecordCursorPair PreviewPointer(bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, RecordScreenZoomLayout layout, ExactPlotCoordinate x, ExactPlotCoordinate y);
    public CapturedRecordCursorPair CommitPointer(bool canPreserveGlobalSafetyOverlay, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale scale, RecordScreenZoomLayout layout, ExactPlotCoordinate x, ExactPlotCoordinate y);
    public CapturedRecordCursorPair Cancel();
}
```

## Presentation/ConfirmedLimitNotice.cs

源码：[ConfirmedLimitNotice.cs](../../../src/Monitor.Application/Presentation/ConfirmedLimitNotice.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class ConfirmedLimitNotice
{
    public AlarmLifecycleJournal Lifecycle { get; }
    public ConfirmedLimitNotice(MonitorNumeric numeric);
    public MonitorNotice? Evaluate(MeasurementLimits limits, LiveMeasurementSnapshot snapshot, MeasurementConfirmationTiming? timing = null);
    public void Reset();
    public void Reset(AlarmTransitionReason reason);
}
```

## Presentation/ConfirmedNoExpirationNotice.cs

源码：[ConfirmedNoExpirationNotice.cs](../../../src/Monitor.Application/Presentation/ConfirmedNoExpirationNotice.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class ConfirmedNoExpirationNotice
{
    public AlarmLifecycleJournal Lifecycle { get; }
    public MonitorNotice? Evaluate(bool enabled, int? delaySeconds, long nowNs, CapnographyActivity? activity, BoundaryConfirmationTiming? timing = null);
    public void Reset();
    public void Reset(AlarmTransitionReason reason);
}
```

## Presentation/Ecg12PaperLayout.cs

源码：[Ecg12PaperLayout.cs](../../../src/Monitor.Application/Presentation/Ecg12PaperLayout.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class Ecg12PaperLayout(bool sixRows)
{
    public const double PixelsPerSecond = 100;
    public const double PixelsPerMillivolt = 40;
    public const int LongLeadIndex = 12;
    public const int LeadIIIndex = 1;
    public const double GridLeft = 32;
    public const double FirstBaseline = 136;
    public const double RowPitch = 120;
    public const double CalibrationWidth = 30;
    public const double LongSamplesLeft = 62;
    public bool SixRows { get; }
    public int Rows { get; }
    public int Columns { get; }
    public int ColumnWidth { get; }
    public double Width { get; }
    public double Height { get; }
    public long LongDurationNs { get; }
    public double LongBaseline { get; }
    public long ShortDurationNs { get; }
    public long ColumnStartNs(int column);
    public Ecg12PaperRegion Region(int lead);
    public Ecg12PaperRegion? HitTest(double x, double y);
}
public sealed record Ecg12PaperRegion(int Lead, int SourceLead, double Left, double Baseline, long StartNs, long EndExclusiveNs)
{
    public double Right { get; }
    public double Top { get; }
    public double Bottom { get; }
    public double XAt(long timeNs);
    public double YAt(long numeratorMicrovolts, uint denominator);
    public long TimeAt(double x);
}
```

## Presentation/EcgStripDisplayComposition.cs

源码：[EcgStripDisplayComposition.cs](../../../src/Monitor.Application/Presentation/EcgStripDisplayComposition.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record EcgStripDisplaySnapshot(SweepStateProjectionSnapshot Presentation, SweepPlotGeometrySnapshot CurrentRegions, EcgCalibrationGeometrySnapshot CurrentCalibration, NoDataSafetyProjection LiveSafety, ConnectivityCriticalBannerProjection Connectivity, EcgStripDisplaySelection SourceStrip)
{
}
public static class EcgStripDisplayComposition
{
    public static EcgStripDisplaySnapshot Compose(SweepStateProjectionState state, long authorityMonotonicNs, IReadOnlyList<NumericNoDataPolicy> numericPolicies, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale verticalScale, int gutterLeftPixels, int pulseLeftPixels, PublishedEcgStrip? published, bool requireColumnReduction = false);
}
```

## Presentation/EcgStripDisplayGate.cs

源码：[EcgStripDisplayGate.cs](../../../src/Monitor.Application/Presentation/EcgStripDisplayGate.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record EcgStripDisplaySelection(string ReasonCode, ReconstructedEcgStrip? Strip)
{
}
public static class EcgStripDisplayGate
{
    public static EcgStripDisplaySelection Select(SweepStateProjectionState current, int plotLeftPixels, int plotWidthPixels, EcgVerticalScale verticalScale, int gutterLeftPixels, int pulseLeftPixels, ReconstructedEcgStrip? strip, bool requireColumnReduction = false);
}
```

## Presentation/EcgStripPublication.cs

源码：[EcgStripPublication.cs](../../../src/Monitor.Application/Presentation/EcgStripPublication.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class EcgStripPublicationException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed class EcgStripWork
{
    public ulong Generation { get; }
}
public sealed record PublishedEcgStrip(ulong LocalGeneration, ReconstructedEcgStrip Strip)
{
}
public sealed class EcgStripPublication
{
    public EcgStripPublication(int maximumSamples, int maximumSegments, EcgColumnReductionLimits? columnLimits = null);
    public EcgStripWork Request(EcgStripCheckpoint input);
    public SweepFramePublicationStatus Complete(EcgStripWork work, CancellationToken cancellationToken = default);
    public PublishedEcgStrip? CapturePublished();
    public PublishedEcgStrip? Stop();
    public static EcgStripPublication Restore(int maximumSamples, int maximumSegments, EcgStripCheckpoint checkpoint, EcgColumnReductionLimits? columnLimits = null);
}
```

## Presentation/EcgStripReconstructor.cs

源码：[EcgStripReconstructor.cs](../../../src/Monitor.Application/Presentation/EcgStripReconstructor.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class EcgStripException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record EcgStripCheckpoint(SweepFrameReconstructionInput Source, int GutterLeftPixels, int PulseLeftPixels)
{
}
public sealed record ReconstructedEcgStrip(ReconstructedSweepFrame PatientFrame, EcgCalibrationGeometrySnapshot Calibration, EcgStripCheckpoint Checkpoint, ReducedSweepColumnFrame? ColumnReduction = null)
{
}
public sealed record EcgColumnReductionLimits(int MaximumPieces, int MaximumEnvelopes)
{
}
public sealed class EcgStripReconstructor
{
    public EcgStripReconstructor(int maximumSamples, int maximumSegments, EcgColumnReductionLimits? columnLimits = null);
    public ReconstructedEcgStrip? Current { get; private set; }
    public ReconstructedEcgStrip Replace(EcgStripCheckpoint input, CancellationToken cancellationToken = default);
    public ReconstructedEcgStrip ResizeHorizontal(int plotLeftPixels, int plotWidthPixels, int gutterLeftPixels, int pulseLeftPixels, CancellationToken cancellationToken = default);
    public EcgStripCheckpoint? CaptureCheckpoint();
    public static EcgStripReconstructor Restore(int maximumSamples, int maximumSegments, EcgStripCheckpoint checkpoint, EcgColumnReductionLimits? columnLimits = null);
}
```

## Presentation/EcgStripWorkPump.cs

源码：[EcgStripWorkPump.cs](../../../src/Monitor.Application/Presentation/EcgStripWorkPump.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class EcgStripWorkPump
{
    public EcgStripWorkPump(int maximumSamples, int maximumSegments, EcgColumnReductionLimits? columnLimits = null);
    public ulong Enqueue(EcgStripCheckpoint input);
    public SweepFramePublicationStatus? ProcessNext(CancellationToken cancellationToken = default);
    public PublishedEcgStrip? CapturePublished();
    public PublishedEcgStrip? Stop();
    public static EcgStripWorkPump Restore(int maximumSamples, int maximumSegments, EcgStripCheckpoint checkpoint, EcgColumnReductionLimits? columnLimits = null);
}
```

## Presentation/LocalMonitorPreviewSession.cs

源码：[LocalMonitorPreviewSession.cs](../../../src/Monitor.Application/Presentation/LocalMonitorPreviewSession.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class LocalMonitorPreviewSession
{
    public const long PresentationLatencyNs = 2_200_000_000;
    public const int RetainedBlockCount = 202;
    public const long StartupDiscardNs = 12_000_000_000;
    public long? PendingSourceTimeNs { get; private set; }
    public LiveMeasurementSnapshot? Measurements { get; }
    public long SimulationTimeNs { get; private set; }
    public long FrontierNs { get; private set; }
    public ulong DataRevision { get; private set; }
    public IReadOnlyList<DetectedPlethPulse> DetectedPulses { get; private set; }
    public IReadOnlyList<DetectedEcgRhythmEvent> DetectedRhythmEvents { get; private set; }
    public IReadOnlyList<DetectedEcgBeat> DetectedBeats { get; private set; }
    public IReadOnlyList<WaveformEnvelope> Blocks { get; }
    public MonitorDisplayConfiguration Display { get; private set; }
    public MonitorSweepRanges Ranges { get; private set; }
    public RealtimeOxygenationSnapshot? Oxygenation { get; }
    public OxygenReservoirParameters? OxygenationParameters { get; private set; }
    public LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration configuration, MonitorDisplayConfiguration display, bool enableMeasurements = false, int? opticalSaturationMilliPercent = null, int opticalModulationPermille = 1000, SeededOpticalSaturation? opticalVariation = null, IArterialOxygenationSource? oxygenation = null, RealtimeOxygenationConfiguration? realtimeOxygenation = null);
    public long UpdateOxygenationVentilation(VentilationTransportPlan ventilation, decimal? oxygenDemandMultiplier = null);
    public void DiscardStartup();
    public long ScheduleSource(LocalMonitorPreviewSession definition, long delayNs);
    public void UpdateDisplay(MonitorDisplayConfiguration display);
    public void Advance(long deltaNs);
    public IEnumerable<(long TimeNs, double Value)> Samples(int channel, long fromSimTimeNs, long toExclusiveSimTimeNs);
}
```

## Presentation/MeasuredLimitNotice.cs

源码：[MeasuredLimitNotice.cs](../../../src/Monitor.Application/Presentation/MeasuredLimitNotice.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record MeasurementLimits(bool Enabled, int? CriticalLow, int? WarningLow, int? WarningHigh, int? CriticalHigh)
{
}
public sealed record MeasurementLimitDescriptor(MonitorNumeric Numeric, string Id, string Label, string Unit, int Divisor, int Minimum, int Maximum, MeasurementLimits TeachingDefaults)
{
}
public static class MeasuredLimitNotice
{
    public static IReadOnlyList<MeasurementLimitDescriptor> Descriptors { get; }
    public static MeasurementLimitDescriptor Describe(MonitorNumeric numeric);
    public static MonitorNotice? Evaluate(MonitorNumeric numeric, MeasurementLimits limits, LiveMeasurementSnapshot snapshot);
}
```

## Presentation/MeasurementConfirmationTiming.cs

源码：[MeasurementConfirmationTiming.cs](../../../src/Monitor.Application/Presentation/MeasurementConfirmationTiming.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record BoundaryConfirmationTiming(int TriggerMilliseconds, int RecoveryMilliseconds)
{
    public const int MaximumMilliseconds = 600_000;
    public void Validate();
}
public sealed record MeasurementConfirmationTiming(BoundaryConfirmationTiming CriticalLow, BoundaryConfirmationTiming WarningLow, BoundaryConfirmationTiming WarningHigh, BoundaryConfirmationTiming CriticalHigh)
{
    public static MeasurementConfirmationTiming DefaultFor(MonitorNumeric numeric);
    public void Validate();
}
```

## Presentation/AlarmNotificationSettings.cs

源码：[AlarmNotificationSettings.cs](../../../src/Monitor.Application/Presentation/AlarmNotificationSettings.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public enum AlarmPlaybackMode { Continuous, Notifications }
public enum AlarmSoundMode { Inherit, SingleGroup, Continuous }
public enum AlarmSoundDuration { SingleGroup, Continuous }
public sealed record AlarmNotificationSettings(int RepeatSuppressionMilliseconds, bool ReminderEnabled, int ReminderMilliseconds)
{
    public AlarmSoundMode SoundMode { get; init; }
    public AlarmLatchingMode LatchingMode { get; init; }
    public static AlarmNotificationSettings Default { get; }
    public void Validate();
    public AlarmNotificationPolicy ToPolicy(AlarmPlaybackMode defaultMode = AlarmPlaybackMode.Notifications);
}
```

## Presentation/MonitorAlarmPreferences.cs

源码：[MonitorAlarmPreferences.cs](../../../src/Monitor.Application/Presentation/MonitorAlarmPreferences.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record MonitorAlarmPreferences(MeasurementLimits HeartRate, bool SpO2Enabled, int? SpO2Warning, int? SpO2Critical, bool NoExpirationEnabled, int? NoExpirationSeconds, IReadOnlyDictionary<MonitorNumeric, MeasurementLimits> Additional, bool NoticeColorEnabled)
{
    public IReadOnlyDictionary<MonitorNumeric, MeasurementConfirmationTiming> ConfirmationTimings { get; init; }
    public BoundaryConfirmationTiming NoExpirationConfirmation { get; init; }
    public AlarmPlaybackMode PlaybackMode { get; init; }
    public IReadOnlyDictionary<string, AlarmNotificationSettings> Notifications { get; init; }
    public static IReadOnlyList<string> NotificationConditionIds { get; }
    public AlarmNotificationSettings NotificationFor(string conditionId);
    public MeasurementConfirmationTiming ConfirmationFor(MonitorNumeric numeric);
    public static MonitorAlarmPreferences Default { get; }
    public void Validate();
}
```

## Presentation/MonitorAudioPause.cs

源码：[MonitorAudioPause.cs](../../../src/Monitor.Application/Presentation/MonitorAudioPause.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class MonitorAudioPause
{
    public long? PausedUntilNs { get; private set; }
    public void Start(long authorityTimeNs, int durationSeconds);
    public void Resume(long authorityTimeNs);
    public int RemainingSeconds(long authorityTimeNs);
}
```

## Presentation/MonitorBeatPitch.cs

源码：[MonitorBeatPitch.cs](../../../src/Monitor.Application/Presentation/MonitorBeatPitch.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class MonitorBeatPitch
{
    public int SaturationPercent { get; private set; }
    public bool Unavailable { get; private set; }
    public void Reset();
    public void Update(OpticalSaturationReading? reading, long nowNs);
}
```

## Presentation/MonitorBeatSource.cs

源码：[MonitorBeatSource.cs](../../../src/Monitor.Application/Presentation/MonitorBeatSource.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public enum MonitorBeatMode
{
    Ecg,
    Pleth,
    Auto
}
public enum MonitorBeatOrigin
{
    None,
    Ecg,
    Pleth
}
public sealed record MonitorBeatSourceChange(long TimeNs, MonitorBeatMode Mode, MonitorBeatOrigin From, MonitorBeatOrigin To)
{
}
public sealed class MonitorBeatSource
{
    public MonitorBeatOrigin Current { get; private set; }
    public IReadOnlyList<MonitorBeatSourceChange> Changes { get; }
    public void Reset();
    public bool Update(MonitorBeatMode mode, WaveformMeasurementStatus ecg, WaveformMeasurementStatus pleth, long nowNs);
    public bool Accept(MonitorBeatOrigin source, long confirmedAtNs);
}
```

## Presentation/MonitorDisplayConfiguration.cs

源码：[MonitorDisplayConfiguration.cs](../../../src/Monitor.Application/Presentation/MonitorDisplayConfiguration.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public enum MonitorSkin
{
    ThreeRows,
    FiveRows,
    SevenRows
}
public sealed record MonitorAmplitudeRange(double Minimum, double Maximum)
{
    public double Normalize(double value);
    public void Validate();
}
public sealed record MonitorDisplaySlot(int Channel, bool Automatic, MonitorAmplitudeRange Range, int SpeedTenthsMmPerSecond = 250)
{
    public long DurationNs { get; }
}
public sealed class MonitorDisplayConfiguration
{
    public const double AutoOccupancy = .85;
    public const long SweepDurationNs = 10_000_000_000;
    public MonitorSkin Skin { get; }
    public IReadOnlyList<MonitorDisplaySlot> Slots { get; }
    public static int RowCount(MonitorSkin skin);
    public static MonitorAmplitudeRange ReferenceRange(int channel);
    public MonitorDisplayConfiguration(MonitorSkin skin, IReadOnlyList<MonitorDisplaySlot> slots);
    public static MonitorDisplayConfiguration Default(MonitorSkin skin = MonitorSkin.FiveRows);
}
public sealed class MonitorSweepRanges(MonitorDisplayConfiguration configuration)
{
    public long Cycle { get; private set; }
    public MonitorAmplitudeRange Range(int slot);
    public bool ShowPrevious(int slot);
    public long RowCycle(int slot);
    public MonitorAmplitudeRange PreviousRange(int slot);
    public void Advance(long frontierNs, Func<int, long, long, IEnumerable<double>> samples);
}
```

## Presentation/MonitorGeneratorPreferences.cs

源码：[MonitorGeneratorPreferences.cs](../../../src/Monitor.Application/Presentation/MonitorGeneratorPreferences.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record MonitorGeneratorPreferences(int Ecg, string EcgName, int Respiration, int Ejection, string Seed, IReadOnlyDictionary<string, decimal?> Numbers, IReadOnlyDictionary<string, bool> Flags, IReadOnlyDictionary<string, int> Choices)
{
    public decimal ApplyDelaySeconds { get; init; }
    public OxygenationEditorPreferences? Oxygenation { get; init; }
    public void Validate();
}
```

## Presentation/MonitorNoticeRotation.cs

源码：[MonitorNoticeRotation.cs](../../../src/Monitor.Application/Presentation/MonitorNoticeRotation.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public enum MonitorNoticeLevel
{
    Info,
    Notice,
    Warning,
    Critical
}
public enum MonitorNumeric
{
    HeartRate,
    RespirationRate,
    SpO2,
    PulseRate,
    EtCo2,
    Co2RespirationRate,
    AbpMean,
    PaMean,
    CvpMean
}
public sealed record MonitorNotice(string Id, MonitorNoticeLevel Level, string Text)
{
    public MonitorNumeric? Numeric { get; init; }
    public bool Audible { get; init; }
}
public sealed class MonitorNoticeRotation
{
    public long? CriticalElapsedNs(string id);
    public MonitorNotice? Current { get; private set; }
    public MonitorNoticeLevel? Highest { get; }
    public void Update(IEnumerable<MonitorNotice> notices, long simulationTimeNs);
}
```

## Presentation/MonitorSoundPattern.cs

源码：[MonitorSoundPattern.cs](../../../src/Monitor.Application/Presentation/MonitorSoundPattern.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record MonitorSoundTiming(bool InfoTone = false, int InfoMilliseconds = 30000, int NoticeMilliseconds = 10000, int WarningMilliseconds = 5000, int CriticalMilliseconds = 1500)
{
    public void Validate();
    public int Period(MonitorNoticeLevel level);
}
public static class MonitorSoundPattern
{
    public static IReadOnlyList<int> OnsetsMilliseconds(MonitorNoticeLevel level);
}
```

## Presentation/MonitorSoundPreferences.cs

源码：[MonitorSoundPreferences.cs](../../../src/Monitor.Application/Presentation/MonitorSoundPreferences.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record MonitorSoundPreferences(int Volume, int HeartbeatVolume, bool HeartbeatEnabled, int BeatSource, int PitchSource, int PauseSeconds, MonitorSoundTiming Timing)
{
    public static MonitorSoundPreferences Default { get; }
    public void Validate();
}
```

## Presentation/NoExpirationNotice.cs

源码：[NoExpirationNotice.cs](../../../src/Monitor.Application/Presentation/NoExpirationNotice.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public static class NoExpirationNotice
{
    public static MonitorNotice? Evaluate(bool enabled, int? delaySeconds, long nowNs, CapnographyActivity? activity);
}
```

## Presentation/OxygenationEditorPreferences.cs

源码：[OxygenationEditorPreferences.cs](../../../src/Monitor.Application/Presentation/OxygenationEditorPreferences.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record OxygenationEditorPreferences(bool Realtime, int TidalVolumeMicrolitersBtps, int DeadSpaceMicrolitersBtps, int InspiredOxygenMillionths, bool AirwayOpen)
{
    public OxygenationPatientPreferences? Patient { get; init; }
    public decimal OxygenDemandMultiplier { get; init; }
    public VentilationTransportPlan CreateVentilation();
    public void Validate();
    public RealtimeOxygenationConfiguration CreateConfiguration();
}
```

## Presentation/OxygenationPatientPreferences.cs

源码：[OxygenationPatientPreferences.cs](../../../src/Monitor.Application/Presentation/OxygenationPatientPreferences.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record OxygenationPatientPreferences(OxygenationPatientProfile Profile, OxygenationBaselineOverrides Overrides, OxygenationBaselineResolution Resolved)
{
    public void Validate();
}
```

## Presentation/PressureLimitNotice.cs

源码：[PressureLimitNotice.cs](../../../src/Monitor.Application/Presentation/PressureLimitNotice.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class PressureLimitNotice
{
    public AlarmLifecycleJournal Lifecycle { get; }
    public const long LowConfirmationNs = 4_000_000_000;
    public const long HighConfirmationNs = 10_000_000_000;
    public const long RecoveryConfirmationNs = 3_000_000_000;
    public PressureLimitNotice(MonitorNumeric numeric);
    public MonitorNotice? Evaluate(MeasurementLimits limits, LiveMeasurementSnapshot snapshot);
    public void Reset();
}
```

## Presentation/PreviewContour.cs

源码：[PreviewContour.cs](../../../src/Monitor.Application/Presentation/PreviewContour.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public static class PreviewContour
{
    public static IReadOnlyList<(long TimeNs, double Value)> Interpolate(IReadOnlyList<(long TimeNs, double Value)> samples, int subdivisions = 4);
}
```

## Presentation/SpO2LimitNotice.cs

源码：[SpO2LimitNotice.cs](../../../src/Monitor.Application/Presentation/SpO2LimitNotice.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public static class SpO2LimitNotice
{
    public static MonitorNotice? Evaluate(bool enabled, int? warningMilliPercent, int? criticalMilliPercent, OpticalSaturationReading reading);
}
```

## Presentation/SweepClippedColumnReduction.cs

源码：[SweepClippedColumnReduction.cs](../../../src/Monitor.Application/Presentation/SweepClippedColumnReduction.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record SweepClippedColumnEnvelope(SweepSampleSource Source, ulong CycleIndex, int RegionIndex, int ColumnPixels, ulong FirstEndSampleIndex, ulong LastEndSampleIndex, ClippedSweepPoint First, ClippedSweepPoint Last, ClippedSweepPoint MinimumY, ClippedSweepPoint MaximumY)
{
}
public sealed record ReducedSweepColumnFrame(ReconstructedSweepColumnFrame Frame, IReadOnlyList<SweepClippedColumnEnvelope> Envelopes)
{
}
public static class SweepClippedColumnReduction
{
    public static ReducedSweepColumnFrame Reduce(SweepFrameReconstructionInput input, int maximumSamples, int maximumSegments, int maximumPieces, int maximumEnvelopes, CancellationToken cancellationToken = default);
}
```

## Presentation/SweepColumnEnvelopeReduction.cs

源码：[SweepColumnEnvelopeReduction.cs](../../../src/Monitor.Application/Presentation/SweepColumnEnvelopeReduction.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record SweepColumnEnvelope(int ColumnPixels, SweepPathSample First, SweepPathSample Last, SweepPathSample MinimumY, SweepPathSample MaximumY)
{
}
public static class SweepColumnEnvelopeReduction
{
    public static IReadOnlyList<SweepColumnEnvelope> Reduce(SweepFrameReconstructionInput input, int maximumSamples, CancellationToken cancellationToken = default);
}
```

## Presentation/SweepColumnFrameReconstructor.cs

源码：[SweepColumnFrameReconstructor.cs](../../../src/Monitor.Application/Presentation/SweepColumnFrameReconstructor.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record SweepFrameColumnPiece(SweepSampleSource Source, ulong EndSampleIndex, ulong CycleIndex, int RegionIndex, SweepColumnSegment Piece)
{
}
public sealed record ReconstructedSweepColumnFrame(ReconstructedSweepFrame SourceFrame, IReadOnlyList<SweepFrameColumnPiece> Pieces, SweepFrameReconstructionInput Checkpoint)
{
}
public sealed class SweepColumnFrameReconstructor
{
    public SweepColumnFrameReconstructor(int maximumSamples, int maximumSegments, int maximumPieces);
    public ReconstructedSweepColumnFrame? Current { get; private set; }
    public ReconstructedSweepColumnFrame Replace(SweepFrameReconstructionInput input, CancellationToken cancellationToken = default);
    public SweepFrameReconstructionInput? CaptureCheckpoint();
    public static SweepColumnFrameReconstructor Restore(int maximumSamples, int maximumSegments, int maximumPieces, SweepFrameReconstructionInput checkpoint);
}
```

## Presentation/SweepDisplayComposition.cs

源码：[SweepDisplayComposition.cs](../../../src/Monitor.Application/Presentation/SweepDisplayComposition.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record SweepDisplaySnapshot(SweepStateProjectionSnapshot Presentation, SweepPlotGeometrySnapshot CurrentRegions, NoDataSafetyProjection LiveSafety, ConnectivityCriticalBannerProjection Connectivity, SweepFrameDisplaySelection SourceFrame)
{
}
public static class SweepDisplayComposition
{
    public static SweepDisplaySnapshot Compose(SweepStateProjectionState state, long authorityMonotonicNs, IReadOnlyList<NumericNoDataPolicy> numericPolicies, int leftPixels, int widthPixels, int topPixels, int heightPixels, PublishedSweepFrame? published, EcgVerticalScale? verticalScale = null);
}
```

## Presentation/SweepFrameDisplayGate.cs

源码：[SweepFrameDisplayGate.cs](../../../src/Monitor.Application/Presentation/SweepFrameDisplayGate.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record SweepFrameDisplaySelection(string ReasonCode, ReconstructedSweepFrame? Frame)
{
}
public static class SweepFrameDisplayGate
{
    public static SweepFrameDisplaySelection Select(SweepStateProjectionState current, int leftPixels, int widthPixels, int topPixels, int heightPixels, PublishedSweepFrame? published, EcgVerticalScale? verticalScale = null);
}
```

## Presentation/SweepFrameHorizontalResize.cs

源码：[SweepFrameHorizontalResize.cs](../../../src/Monitor.Application/Presentation/SweepFrameHorizontalResize.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class SweepFrameResizeException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record SweepFrameResizeResult(ReconstructedSweepFrame Frame, SweepFrameReconstructionInput Checkpoint)
{
}
public static class SweepFrameHorizontalResize
{
    public static SweepFrameResizeResult Rebuild(SweepFrameReconstructionInput checkpoint, int leftPixels, int widthPixels, int maximumSamples, int maximumSegments, CancellationToken cancellationToken = default);
}
```

## Presentation/SweepFramePathBuilder.cs

源码：[SweepFramePathBuilder.cs](../../../src/Monitor.Application/Presentation/SweepFramePathBuilder.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class SweepFramePathException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record SweepFramePathState(SweepStateProjectionState Presentation, int PlotLeftPixels, int PlotWidthPixels, int PlotTopPixels, int PlotHeightPixels, SweepPathSample? Previous, EcgVerticalScale? VerticalScale = null)
{
}
public sealed record SweepRegionPathResult(int RegionIndex, SweepPathAppendResult Path)
{
}
public sealed class SweepFramePathBuilder
{
    public static SweepFramePathBuilder Start(SweepStateProjectionState presentation, int leftPixels, int widthPixels, int topPixels, int heightPixels, EcgVerticalScale? verticalScale = null);
    public static SweepFramePathBuilder Restore(SweepFramePathState state);
    public IReadOnlyList<SweepRegionPathResult> Append(SweepPathSample sample, CancellationToken cancellationToken = default);
    public IReadOnlyList<SweepRegionPathResult> AppendVoltage(SweepSampleSource source, ulong sampleIndex, ulong cycleIndex, bool drawable, SweepPixelPosition x, EcgSampleVoltage voltage, CancellationToken cancellationToken = default);
    public SweepFramePathState CaptureState();
    public IReadOnlyList<SweepRegionPathResult> AppendVoltageAtOffset(SweepSampleSource source, ulong sampleIndex, ulong cycleIndex, bool drawable, ulong cycleOffsetNs, EcgSampleVoltage voltage, CancellationToken cancellationToken = default);
    public SweepPlotGeometrySnapshot Geometry { get; }
}
```

## Presentation/SweepFramePublication.cs

源码：[SweepFramePublication.cs](../../../src/Monitor.Application/Presentation/SweepFramePublication.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class SweepFramePublicationException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public enum SweepFramePublicationStatus
{
    Published,
    Superseded,
    AlreadyPublished,
    Stopped
}
public sealed class SweepFrameWork
{
    public ulong Generation { get; }
}
public sealed record PublishedSweepFrame(ulong LocalGeneration, ReconstructedSweepFrame Frame, SweepFrameReconstructionInput Checkpoint)
{
}
public sealed class SweepFramePublication
{
    public SweepFramePublication(int maximumSamples, int maximumSegments);
    public SweepFrameWork Request(SweepFrameReconstructionInput input);
    public SweepFramePublicationStatus Complete(SweepFrameWork work, CancellationToken cancellationToken = default);
    public PublishedSweepFrame? CapturePublished();
    public PublishedSweepFrame? Stop();
    public static SweepFramePublication Restore(int maximumSamples, int maximumSegments, SweepFrameReconstructionInput checkpoint);
}
```

## Presentation/SweepFrameReconstructor.cs

源码：[SweepFrameReconstructor.cs](../../../src/Monitor.Application/Presentation/SweepFrameReconstructor.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class SweepFrameReconstructionException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record SweepFrameReconstructionInput(SweepFramePathState Frame, IReadOnlyList<SweepPathSample> Samples)
{
}
public sealed record SweepFrameSegment(SweepSampleSource Source, ulong EndSampleIndex, ulong CycleIndex, int RegionIndex, ClippedSweepSegment Segment)
{
}
public sealed record ReconstructedSweepFrame(SweepPlotGeometrySnapshot Geometry, int PlotTopPixels, int PlotHeightPixels, IReadOnlyList<SweepFrameSegment> Segments)
{
}
public sealed class SweepFrameReconstructor
{
    public SweepFrameReconstructor(int maximumSamples, int maximumSegments);
    public ReconstructedSweepFrame? Current { get; private set; }
    public ReconstructedSweepFrame Replace(SweepFrameReconstructionInput input, CancellationToken cancellationToken = default);
    public SweepFrameReconstructionInput? CaptureCheckpoint();
    public static SweepFrameReconstructor Restore(int maximumSamples, int maximumSegments, SweepFrameReconstructionInput checkpoint);
}
```

## Presentation/SweepFrameWorkPump.cs

源码：[SweepFrameWorkPump.cs](../../../src/Monitor.Application/Presentation/SweepFrameWorkPump.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class SweepFrameWorkPump
{
    public SweepFrameWorkPump(int maximumSamples, int maximumSegments);
    public ulong Enqueue(SweepFrameReconstructionInput input);
    public SweepFramePublicationStatus? ProcessNext(CancellationToken cancellationToken = default);
    public PublishedSweepFrame? CapturePublished();
    public PublishedSweepFrame? Stop();
    public static SweepFrameWorkPump Restore(int maximumSamples, int maximumSegments, SweepFrameReconstructionInput checkpoint);
}
```

## Presentation/SweepPlanScheduler.cs

源码：[SweepPlanScheduler.cs](../../../src/Monitor.Application/Presentation/SweepPlanScheduler.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed class SweepPlanSchedulerException(string reasonCode, string parameterName) : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; }
}
public sealed record SweepCycleBoundary(long PresentationNs, long SweepClockNs)
{
}
public sealed record ScheduledSweepPlan(NoDataSweepPlan Plan, ulong PlanRevision, ulong SweepRevision, SweepCycleBoundary Boundary)
{
}
public sealed record SweepPlanSchedulerState(SweepStateProjectionState Presentation, ScheduledSweepPlan? Pending)
{
}
public sealed class SweepPlanScheduler
{
    public static SweepPlanScheduler Start(SweepStateProjectionState presentation);
    public static SweepPlanScheduler Restore(SweepPlanSchedulerState state);
    public SweepCycleBoundary NextBoundary();
    public ScheduledSweepPlan Schedule(NoDataSweepPlan plan, ulong planRevision, ulong sweepRevision);
    public SweepStateProjectionSnapshot Advance(long presentationNs, long livePlayheadDataSimTimeNs);
    public SweepPlanSchedulerState CaptureState();
}
```

## Presentation/SweepRegionColumnEnvelopeReduction.cs

源码：[SweepRegionColumnEnvelopeReduction.cs](../../../src/Monitor.Application/Presentation/SweepRegionColumnEnvelopeReduction.cs) · 命名空间：`Monitor.Application.Presentation`

```csharp
public sealed record SweepRegionColumnEnvelope(int RegionIndex, SweepColumnEnvelope Envelope)
{
}
public static class SweepRegionColumnEnvelopeReduction
{
    public static IReadOnlyList<SweepRegionColumnEnvelope> Reduce(SweepFrameReconstructionInput input, int maximumSamples, int maximumEnvelopes, CancellationToken cancellationToken = default);
}
```

## SessionAuthority.cs

源码：[SessionAuthority.cs](../../../src/Monitor.Application/SessionAuthority.cs) · 命名空间：`Monitor.Application`

```csharp
public sealed record AuthorityCommit(ulong CommitSequence, TherapyState State, IReadOnlyList<TherapyFact> Facts)
{
}
public sealed class SessionAuthority
{
    public SessionAuthority(TherapyController therapy);
    public DomainResult<AuthorityCommit> CommitSafetyInputs(IEnumerable<SafetyInput> inputs);
}
```
