# Monitor.Infrastructure 公开声明索引

[接口总览](../README.md)

本页按源码路径列出当前工作区中对程序集外可见的 C# `public` 类型和显式声明的公开成员，保留参数、默认值、单位命名、泛型约束及枚举值。接口中省略 `public` 的成员也包括在内。方法体、属性实现与非 const 字段初始化值已省略；省略实现的声明用于查阅，不能直接当作可编译代码。

位置 record 的参数也定义其同名属性；编译器合成的构造器、相等性方法以及继承成员不重复展开。`internal` 类型即使有 `public` 成员也不属于本索引。按开发构建的预处理分支读取；桌面产品构建差异见[桌面与命令行入口](../desktop-tools.md)。行为约束和集成顺序见总览中的分模块文档。

## Audio/AlsaAudioOutput.cs

源码：[AlsaAudioOutput.cs](../../../src/Monitor.Infrastructure/Audio/AlsaAudioOutput.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public sealed class AlsaAudioOutput : IPumpedAudioOutput
{
    public AlsaAudioOutput(int queueTargetMilliseconds = NativeAudioOutputFactory.DefaultQueueTargetMilliseconds);
    public int? QueueTargetFrames { get; }
    public int? BufferFrames { get; }
    public int? PeriodFrames { get; }
    public IAudioOutputDevice? Open(string? deviceId, AudioRenderSession session, long generation);
    public bool Pump();
    public void WaitForQueueSpace(int timeoutMilliseconds);
    public NativeAudioClockSample ReadClock();
    public void Dispose();
}
```

## Audio/AudioClockBridge.cs

源码：[AudioClockBridge.cs](../../../src/Monitor.Infrastructure/Audio/AudioClockBridge.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public sealed class AudioClockBridge
{
    public AudioClockBridge(long ticksPerSecond, long initialTicks, long initialFrame);
    public void Observe(long monotonicTicks, long engineFrame);
    public long MapToFrame(long monotonicTicks);
}
```

## Audio/AudioOutputSelection.cs

源码：[AudioOutputSelection.cs](../../../src/Monitor.Infrastructure/Audio/AudioOutputSelection.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public enum AudioOutputBackend
{
    Native,
    Wasapi,
    Alsa
}
public static class AudioOutputSelection
{
    public const string EnvironmentVariable = "SIMONITOR_AUDIO_OUTPUT";
    public static AudioOutputBackend Current { get; }
    public static AudioOutputBackend Resolve(string? value);
    public static IPumpedAudioOutput Create(AudioOutputBackend backend);
}
```

## Audio/AudioOutputLifecycle.cs

源码：[AudioOutputLifecycle.cs](../../../src/Monitor.Infrastructure/Audio/AudioOutputLifecycle.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public enum AudioOutputState
{
    Stopped,
    Running,
    DeviceLost,
    StopFailed
}
public enum AudioOutputFailure
{
    None,
    Open,
    Start,
    Stop,
    Underrun,
    DeviceLost,
    SessionRetired
}
public interface IAudioOutputDevice
{
    public bool Start();
    public bool StopAndClose();
}
public interface IAudioOutputFactory
{
    public IAudioOutputDevice? Open(string? deviceId, AudioRenderSession session, long generation);
}
public sealed class AudioOutputLifecycle
{
    public AudioOutputLifecycle(IAudioOutputFactory factory);
    public AudioOutputState State { get; private set; }
    public AudioOutputFailure Failure { get; private set; }
    public long Generation { get; private set; }
    public AudioRenderSession? Session { get; }
    public bool Replace(string? deviceId, long initialFrame, int capacityMilliseconds = 40);
    public bool CheckHealth();
    public void DeviceLost(long generation);
    public bool Stop();
}
```

## Audio/AudioPcmBuffer.cs

源码：[AudioPcmBuffer.cs](../../../src/Monitor.Infrastructure/Audio/AudioPcmBuffer.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public sealed class AudioPcmBuffer
{
    public AudioPcmBuffer(int sampleRate = ToneVoice.SampleRate, int channels = 1, int capacityMilliseconds = 40);
    public int SampleRate { get; }
    public int Channels { get; }
    public int CapacityFrames { get; }
    public int WritableFrames { get; }
    public bool TryWrite(ReadOnlySpan<float> interleaved);
    public int Read(Span<float> destination);
}
```

## Audio/AudioRenderSession.cs

源码：[AudioRenderSession.cs](../../../src/Monitor.Infrastructure/Audio/AudioRenderSession.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public sealed class AudioRenderSession
{
    public AudioRenderSession(long initialFrame = 0, int capacityMilliseconds = 40);
    public bool RequiresReplacement { get; }
    public long UnderrunFrames { get; }
    public int CapacityFrames { get; }
    public int BufferedFrames { get; }
    public long RenderedThroughFrame { get; }
    public ToneScheduleResult? Schedule(long key, TonePreset preset, long targetFrame, long expiryFrame);
    public void Cancel(long key);
    public bool TryProduce(int frames);
    public int Read(Span<float> destination);
    public void Retire();
}
```

## Audio/AlarmNotificationSoundRouter.cs

源码：[AlarmNotificationSoundRouter.cs](../../../src/Monitor.Infrastructure/Audio/AlarmNotificationSoundRouter.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public sealed record AlarmSoundRoutingRecord(MonitorAlarmSoundRequest Request, IReadOnlyList<AlarmNotificationDecision> Decisions, bool Resumed)
{
    public IReadOnlyList<MonitorNotice> ContinuousNotices { get; init; }
    public IReadOnlyList<AlarmConditionSnapshot> ContinuousConditions { get; init; }
}
public sealed class AlarmNotificationSoundRouter
{
    public ulong DroppedRouteCount { get; private set; }
    public IReadOnlyList<AlarmSoundRoutingRecord> Routes { get; }
    public ulong MissedRecordCount { get; private set; }
    public MonitorAlarmSoundRequest? Update(IReadOnlyList<AlarmLifecycleJournal> journals, int volumePercent, MonitorSoundTiming timing, bool enabled, IReadOnlyList<MonitorNotice>? notices = null, ulong retiredNotificationSequence = 0);
}
```

## Audio/MonitorAlarmPlayback.cs

源码：[MonitorAlarmPlayback.cs](../../../src/Monitor.Infrastructure/Audio/MonitorAlarmPlayback.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public sealed record MonitorAlarmSoundRequest(MonitorNoticeLevel Level, int VolumePercent, MonitorSoundTiming Timing)
{
    public ulong NotificationSequence { get; init; }
    public void Validate();
}
public enum AlarmSoundDispatchStage
{
    Selected,
    CoalescedWhileBusy,
    SkippedSilent,
    Interrupted,
    RenderWindowElapsed
}
public sealed record AlarmSoundDispatchRecord(ulong NotificationSequence, AlarmSoundDispatchStage Stage, long RenderFrame);
public sealed class MonitorAlarmPlayback(Func<IPumpedAudioOutput> createOutput)
{
    public bool OutputActive { get; }
    public ulong RetiredNotificationSequence { get; }
    public void SetHeartbeatEnabled(bool enabled);
    public void SubmitHeartbeat(int volumePercent, int pitchPercent = 97);
    public void SetRequest(MonitorAlarmSoundRequest? request);
    public Task<SoundPreviewResult> RunAsync(CancellationToken cancellationToken);
}
public sealed class MonitorAlarmSequencer(AudioRenderSession session)
{
    public ulong RetiredNotificationSequence { get; private set; }
    public ulong MissedNotificationCount { get; private set; }
    public ulong DroppedDispatchCount { get; private set; }
    public IReadOnlyList<AlarmSoundDispatchRecord> Dispatches { get; }
    public void Update(MonitorAlarmSoundRequest? request);
    public void UpdateHeartbeat(bool enabled, int? volumePercent, int pitchPercent = 97);
}
```

## Audio/NativeAudioClockSample.cs

源码：[NativeAudioClockSample.cs](../../../src/Monitor.Infrastructure/Audio/NativeAudioClockSample.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public readonly record struct NativeAudioClockSample(int Result, uint HResult, ulong DevicePosition, ulong DeviceFrequency, ulong Qpc100Nanoseconds)
{
    public ulong? Nominal48kElapsedFrames { get; }
}
```

## Audio/NativeAudioOutputFactory.cs

源码：[NativeAudioOutputFactory.cs](../../../src/Monitor.Infrastructure/Audio/NativeAudioOutputFactory.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public readonly record struct NativeAudioStatus(uint SampleRate, uint Channels, uint PeriodFrames, uint BufferFrames, uint RetiredReason, uint MissingFrames, bool LowLatencyQualified)
{
}
public readonly record struct NativeAudioPeriodSnapshot(uint QueryStatus, uint DefaultFrames, uint FundamentalFrames, uint MinimumFrames, uint MaximumFrames, uint CurrentFrames, uint EngineSampleRate, uint HResult, uint EngineChannels)
{
}
public sealed class NativeAudioOutputFactory : IPumpedAudioOutput
{
    public const int DefaultQueueTargetMilliseconds = 20;
    public static string DefaultLibraryPath { get; }
    public NativeAudioOutputFactory(string libraryPath, bool allowTestBackend = false, int queueTargetMilliseconds = DefaultQueueTargetMilliseconds);
    public IAudioOutputDevice? Open(string? deviceId, AudioRenderSession session, long generation);
    public int? QueueTargetFrames { get; }
    public NativeAudioStatus? Status { get; }
    public NativeAudioPeriodSnapshot? PeriodSnapshot { get; }
    public bool Pump();
    public void WaitForQueueSpace(int timeoutMilliseconds);
    public NativeAudioClockSample ReadClock();
    public void Dispose();
}
```

## Audio/SampleToneRenderer.cs

源码：[SampleToneRenderer.cs](../../../src/Monitor.Infrastructure/Audio/SampleToneRenderer.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public enum ToneScheduleResult
{
    Accepted,
    Expired,
    Duplicate,
    Full
}
public sealed class SampleToneRenderer
{
    public SampleToneRenderer(long initialFrame = 0);
    public long Position { get; private set; }
    public ToneScheduleResult Schedule(long cancellationKey, TonePreset preset, long targetFrame, long expiryFrame);
    public void Cancel(long cancellationKey);
    public void DiscardForDiscontinuity(long nextFrame);
    public void Render(Span<float> destination);
}
```

## Audio/SelectedMonitorTones.cs

源码：[SelectedMonitorTones.cs](../../../src/Monitor.Infrastructure/Audio/SelectedMonitorTones.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public enum MonitorToneSample
{
    None,
    Info,
    Notice,
    Warning,
    Critical,
    Heartbeat,
    HeartbeatPitchA
}
public static class SelectedMonitorTones
{
    public static TonePreset Alarm(MonitorNoticeLevel level, int volumePercent);
    public static TonePreset Heartbeat(int volumePercent, int saturationPercent = 97);
}
```

## Audio/SoundPreviewPlayback.cs

源码：[SoundPreviewPlayback.cs](../../../src/Monitor.Infrastructure/Audio/SoundPreviewPlayback.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public interface IPumpedAudioOutput : IAudioOutputFactory, IDisposable
{
    public bool Pump();
    public void WaitForQueueSpace(int timeoutMilliseconds);
}
public enum SoundPreviewResult
{
    Completed,
    Stopped,
    Unavailable,
    Interrupted,
    StopFailed
}
public sealed class SoundPreviewPlayback(Func<IPumpedAudioOutput> createOutput)
{
    public Task<SoundPreviewResult> PlayAsync(int volumePercent, CancellationToken cancellationToken);
}
```

## Audio/ToneVoice.cs

源码：[ToneVoice.cs](../../../src/Monitor.Infrastructure/Audio/ToneVoice.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public sealed record TonePreset(string Id, int FrequencyMilliHz, int AttackFrames, int HoldFrames, int ReleaseFrames, int GainQ15)
{
    public static TonePreset BeatAudition { get; }
    public MonitorToneSample Sample { get; init; }
    public int BeatPitchPercent { get; init; }
    public int TotalFrames { get; }
}
public sealed record ToneVoiceState(TonePreset Preset, int Frame, int? CancelFrame)
{
}
public sealed class ToneVoice
{
    public const int SampleRate = 48_000;
    public ToneVoice(TonePreset preset);
    public bool Finished { get; }
    public void Cancel();
    public ToneVoiceState CaptureState();
    public static ToneVoice Restore(ToneVoiceState state);
    public int Render(Span<float> destination);
}
```

## Audio/ToneWaveFixture.cs

源码：[ToneWaveFixture.cs](../../../src/Monitor.Infrastructure/Audio/ToneWaveFixture.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public static class ToneWaveFixture
{
    public static void Write(Stream output, TonePreset preset, int beatCount, int periodFrames, CancellationToken cancellationToken = default);
}
```

## Audio/WasapiAudioOutput.cs

源码：[WasapiAudioOutput.cs](../../../src/Monitor.Infrastructure/Audio/WasapiAudioOutput.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public sealed class WasapiAudioOutput : IPumpedAudioOutput
{
    public WasapiAudioOutput(int queueTargetMilliseconds = NativeAudioOutputFactory.DefaultQueueTargetMilliseconds);
    public int? QueueTargetFrames { get; }
    public int? BufferFrames { get; }
    public int? PeriodFrames { get; }
    public WasapiStreamPath StreamPath { get; }
    public IAudioOutputDevice? Open(string? deviceId, AudioRenderSession session, long generation);
    public bool Pump();
    public void WaitForQueueSpace(int timeoutMilliseconds);
    public NativeAudioClockSample ReadClock();
    public void Dispose();
}
```

## Audio/WasapiRenderEndpoint.cs

源码：[WasapiRenderEndpoint.cs](../../../src/Monitor.Infrastructure/Audio/WasapiRenderEndpoint.cs) · 命名空间：`Monitor.Infrastructure.Audio`

```csharp
public enum WasapiStreamPath
{
    None,
    MixFormat,
    ShortEnginePeriod,
    WindowsConversion
}
```

## Continuity/RecoveryWireCodec.cs

源码：[RecoveryWireCodec.cs](../../../src/Monitor.Infrastructure/Continuity/RecoveryWireCodec.cs) · 命名空间：`Monitor.Infrastructure.Continuity`

```csharp
public sealed class RecoveryWireCodecException : ArgumentException
{
    public RecoveryWireCodecException(string reasonCode, string parameterName, Exception? innerException = null);
    public string ReasonCode { get; }
}
public static class RecoveryWireCodec
{
    public const int MaximumMessageBytes = 1_048_576;
    public static byte[] EncodeHello(RecoveryHelloMessage message);
    public static RecoveryHelloMessage DecodeHello(ReadOnlyMemory<byte> wire);
    public static byte[] EncodeReport(LocalContinuationReportMessage message);
    public static LocalContinuationReportMessage DecodeReport(ReadOnlyMemory<byte> wire);
    public static byte[] EncodePlan(RecoveryResyncPlan plan);
    public static RecoveryResyncPlan DecodePlan(ReadOnlyMemory<byte> wire);
}
```

## Continuity/Sm2ContinuitySignatureVerifier.cs

源码：[Sm2ContinuitySignatureVerifier.cs](../../../src/Monitor.Infrastructure/Continuity/Sm2ContinuitySignatureVerifier.cs) · 命名空间：`Monitor.Infrastructure.Continuity`

```csharp
public sealed class Sm2ContinuitySignatureVerifier : IContinuitySignatureVerifier
{
    public const string UserId = "MONITOR-CONTINUITY-V1";
    public string DeriveKeyId(ReadOnlyMemory<byte> publicKeySec1);
    public bool Verify(ContinuitySignedBody body, ReadOnlyMemory<byte> signatureRs64, ReadOnlyMemory<byte> publicKeySec1);
    public static byte[] EncodeCanonicalBody(ContinuitySignedBody body);
}
```

## Identity/AesGcmIdentityPayloadProtector.cs

源码：[AesGcmIdentityPayloadProtector.cs](../../../src/Monitor.Infrastructure/Identity/AesGcmIdentityPayloadProtector.cs) · 命名空间：`Monitor.Infrastructure.Identity`

```csharp
public sealed class AesGcmIdentityPayloadProtector : IIdentityPayloadProtector, IDisposable
{
    public const int MasterKeySize = 32;
    public AesGcmIdentityPayloadProtector(ReadOnlySpan<byte> masterKey);
    public byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose);
    public byte[] Unprotect(ReadOnlySpan<byte> protectedPayload, string purpose);
    public string LookupToken(string value, string purpose);
    public void Dispose();
}
```

## Identity/IIdentityPayloadProtector.cs

源码：[IIdentityPayloadProtector.cs](../../../src/Monitor.Infrastructure/Identity/IIdentityPayloadProtector.cs) · 命名空间：`Monitor.Infrastructure.Identity`

```csharp
public interface IIdentityPayloadProtector
{
    public byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose);
    public byte[] Unprotect(ReadOnlySpan<byte> protectedPayload, string purpose);
    public string LookupToken(string value, string purpose);
}
```

## Identity/IPlatformKeyProtector.cs

源码：[IPlatformKeyProtector.cs](../../../src/Monitor.Infrastructure/Identity/IPlatformKeyProtector.cs) · 命名空间：`Monitor.Infrastructure.Identity`

```csharp
public interface IPlatformKeyProtector
{
    public string ProtectorId { get; }
    public byte[] Protect(ReadOnlySpan<byte> plaintext);
    public byte[] Unprotect(ReadOnlySpan<byte> protectedPayload);
}
```

## Identity/InMemoryAuthenticationFailureTracker.cs

源码：[InMemoryAuthenticationFailureTracker.cs](../../../src/Monitor.Infrastructure/Identity/InMemoryAuthenticationFailureTracker.cs) · 命名空间：`Monitor.Infrastructure.Identity`

```csharp
public sealed class InMemoryAuthenticationFailureTracker : IAuthenticationFailureTracker
{
    public IReadOnlyList<AuthenticationFailureAuditRecord> AuditRecords { get; }
    public AuthenticationLockState GetLockState(string canonicalUsername, string sourceAddress, DateTimeOffset nowUtc);
    public AuthenticationLockState RecordFailure(AuthenticationFailureAuditRecord audit);
    public void RecordRejection(AuthenticationFailureAuditRecord audit);
    public AuthenticationFailureAuditRecord? FindAudit(Guid auditId);
    public void ClearAccountFailures(string canonicalUsername);
}
```

## Identity/Pbkdf2PasswordHasher.cs

源码：[Pbkdf2PasswordHasher.cs](../../../src/Monitor.Infrastructure/Identity/Pbkdf2PasswordHasher.cs) · 命名空间：`Monitor.Infrastructure.Identity`

```csharp
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public PasswordVerifier Hash(ReadOnlySpan<char> password);
    public bool Verify(ReadOnlySpan<char> password, PasswordVerifier verifier);
    public bool IsSupported(PasswordVerifier verifier);
}
```

## Identity/PlatformProtectedIdentityKeyStore.cs

源码：[PlatformProtectedIdentityKeyStore.cs](../../../src/Monitor.Infrastructure/Identity/PlatformProtectedIdentityKeyStore.cs) · 命名空间：`Monitor.Infrastructure.Identity`

```csharp
public sealed class PlatformProtectedIdentityKeyStore
{
    public PlatformProtectedIdentityKeyStore(string keyFilePath, IPlatformKeyProtector platformProtector);
    public AesGcmIdentityPayloadProtector OpenOrCreatePayloadProtector();
}
```

## Identity/SqliteAuthenticationFailureTracker.cs

源码：[SqliteAuthenticationFailureTracker.cs](../../../src/Monitor.Infrastructure/Identity/SqliteAuthenticationFailureTracker.cs) · 命名空间：`Monitor.Infrastructure.Identity`

```csharp
public sealed class SqliteAuthenticationFailureTracker : IAuthenticationFailureTracker
{
    public SqliteAuthenticationFailureTracker(string databasePath, IIdentityPayloadProtector protector);
    public AuthenticationLockState GetLockState(string canonicalUsername, string sourceAddress, DateTimeOffset nowUtc);
    public AuthenticationLockState RecordFailure(AuthenticationFailureAuditRecord audit);
    public void RecordRejection(AuthenticationFailureAuditRecord audit);
    public AuthenticationFailureAuditRecord? FindAudit(Guid auditId);
    public void ClearAccountFailures(string canonicalUsername);
}
```

## Identity/SqliteIdentityRepository.cs

源码：[SqliteIdentityRepository.cs](../../../src/Monitor.Infrastructure/Identity/SqliteIdentityRepository.cs) · 命名空间：`Monitor.Infrastructure.Identity`

```csharp
public sealed class SqliteIdentityRepository : IBootstrapIdentityRepository, IInstitutionAccountRepository
{
    public SqliteIdentityRepository(string databasePath, IIdentityPayloadProtector protector);
    public bool TryInitializeBootstrap(PasswordVerifier bootstrapVerifier);
    public BootstrapIdentitySnapshot LoadBootstrap();
    public bool UsernameExists(string canonicalUsername);
    public InstitutionAccount? FindByCanonicalUsername(string canonicalUsername);
    public BootstrapCommitResult TryCompleteBootstrap(BootstrapCompletion completion);
    public bool AuditExists(Guid auditId);
}
```

## Localization/BuiltInLocalizations.cs

源码：[BuiltInLocalizations.cs](../../../src/Monitor.Infrastructure/Localization/BuiltInLocalizations.cs) · 命名空间：`Monitor.Infrastructure.Localization`

行为与协作约定见[本地化接口](../localization.md)。

```csharp
public sealed record SupportedLocale(string Code, string NativeName);
public static class BuiltInLocalizations
{
    public const string DefaultLocale = "zh-CN";
    public const string FallbackLocale = "en";
    public static IReadOnlyList<SupportedLocale> Supported { get; }
    public static string ResolveLocale(string? requestedLocale);
    public static ITextLocalizer Create(string? requestedLocale = null);
}
```

## Localization/CatalogTextLocalizer.cs

源码：[CatalogTextLocalizer.cs](../../../src/Monitor.Infrastructure/Localization/CatalogTextLocalizer.cs) · 命名空间：`Monitor.Infrastructure.Localization`

```csharp
public sealed class CatalogTextLocalizer : ITextLocalizer
{
    public CatalogTextLocalizer(TranslationCatalog catalog, TranslationCatalog fallback);
    public string Locale { get; }
    public LocalizedText Resolve(string key);
    public string GetString(string key);
    public string Format(string key, params object?[] arguments);
}
```

## Localization/TranslationCatalog.cs

源码：[TranslationCatalog.cs](../../../src/Monitor.Infrastructure/Localization/TranslationCatalog.cs) · 命名空间：`Monitor.Infrastructure.Localization`

```csharp
public sealed class TranslationCatalog
{
    public string Locale { get; }
    public IReadOnlyCollection<string> Keys { get; }
    public static TranslationCatalog Parse(string locale, string json);
    public void ValidateAgainst(TranslationCatalog fallback, bool requireComplete = false);
}
```

## Persistence/EncryptedSqliteBackupService.cs

源码：[EncryptedSqliteBackupService.cs](../../../src/Monitor.Infrastructure/Persistence/EncryptedSqliteBackupService.cs) · 命名空间：`Monitor.Infrastructure.Persistence`

```csharp
public sealed record EncryptedSqliteBackupDescriptor(Guid BackupId, string KeyReference, DateTimeOffset CreatedAtUtc, long PlaintextBytes, string PlaintextSha256)
{
}
public sealed class EncryptedSqliteBackupService
{
    public const int BackupKeySize = 32;
    public EncryptedSqliteBackupService(string databasePath);
    public EncryptedSqliteBackupDescriptor CreateBackup(string backupPath, Guid backupId, DateTimeOffset createdAtUtc, string keyReference, ReadOnlySpan<byte> backupKey);
    public static EncryptedSqliteBackupDescriptor RestoreBackup(string backupPath, string restoredDatabasePath, string keyReference, ReadOnlySpan<byte> backupKey);
}
```

## Preferences/DisplayPreferenceStore.cs

源码：[DisplayPreferenceStore.cs](../../../src/Monitor.Infrastructure/Preferences/DisplayPreferenceStore.cs) · 命名空间：`Monitor.Infrastructure.Preferences`

```csharp
public sealed record DisplayPreferences(MonitorDisplayConfiguration Display, int PaperLayout, MonitorAlarmPreferences? Alarms = null, MonitorSoundPreferences? Sound = null, MonitorGeneratorPreferences? Generator = null)
{
}
public sealed class DisplayPreferenceStore(string path)
{
    public DisplayPreferences Load(out bool rejected);
    public bool Save(DisplayPreferences preferences);
}
```

## Preferences/LanguagePreferenceStore.cs

源码：[LanguagePreferenceStore.cs](../../../src/Monitor.Infrastructure/Preferences/LanguagePreferenceStore.cs) · 命名空间：`Monitor.Infrastructure.Preferences`

独立语言偏好文件及失败语义见[本地化接口](../localization.md)。

```csharp
public sealed class LanguagePreferenceStore(string path)
{
    public string Load(out bool rejected);
    public bool Save(string locale);
}
```

## Presentation/CapturedRecordSvgDrag.cs

源码：[CapturedRecordSvgDrag.cs](../../../src/Monitor.Infrastructure/Presentation/CapturedRecordSvgDrag.cs) · 命名空间：`Monitor.Infrastructure.Presentation`

```csharp
public sealed class CapturedRecordSvgDrag
{
    public CapturedRecordCursorPair PreviewPointer(bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, ExactPlotCoordinate windowX, ExactPlotCoordinate windowY, ExactPlotCoordinate originX, ExactPlotCoordinate originY);
    public CapturedRecordCursorPair CommitPointer(bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, ExactPlotCoordinate windowX, ExactPlotCoordinate windowY, ExactPlotCoordinate originX, ExactPlotCoordinate originY);
    public CapturedRecordCursorPair Cancel();
}
```

## Presentation/CapturedRecordSvgInputSession.cs

源码：[CapturedRecordSvgInputSession.cs](../../../src/Monitor.Infrastructure/Presentation/CapturedRecordSvgInputSession.cs) · 命名空间：`Monitor.Infrastructure.Presentation`

```csharp
public sealed class CapturedRecordSvgInputSession
{
    public CapturedRecordSvgInputSession(CapturedRecordStudyView view, CapturedRecordNavigation navigation, Ecg12ThemeSelection theme, Ecg12ZoomSelection zoom, bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, EcgPaperGridSvgStyle gridStyle, EcgManualCursorSvgStyle cursorStyle, bool allowAuxiliaryRate, CancellationToken cancellationToken = default);
    public ZoomedCapturedRecordSvgScreenLayers Display { get; }
    public CapturedRecordSvgLayout Layout { get; }
    public CapturedRecordSvgInputSession Refresh(bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, EcgPaperGridSvgStyle gridStyle, EcgManualCursorSvgStyle cursorStyle, bool allowAuxiliaryRate, CancellationToken cancellationToken = default);
    public void ClearPair(bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen);
    public CapturedRecordSvgDrag BeginDrag(bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, ExactPlotCoordinate windowX, ExactPlotCoordinate windowY, ExactPlotCoordinate originX, ExactPlotCoordinate originY, ExactPlotCoordinate radius);
    public RecordCursorHits HitTest(bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, ExactPlotCoordinate windowX, ExactPlotCoordinate windowY, ExactPlotCoordinate originX, ExactPlotCoordinate originY, ExactPlotCoordinate radius);
    public CapturedRecordCursorPair PlacePair(bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, ExactPlotCoordinate firstWindowX, ExactPlotCoordinate firstWindowY, ExactPlotCoordinate secondWindowX, ExactPlotCoordinate secondWindowY, ExactPlotCoordinate originX, ExactPlotCoordinate originY);
    public CapturedRecordCursorPair MoveCursor(bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, RecordCursorEnd end, ExactPlotCoordinate windowX, ExactPlotCoordinate windowY, ExactPlotCoordinate originX, ExactPlotCoordinate originY);
}
```

## Presentation/CapturedRecordSvgLayers.cs

源码：[CapturedRecordSvgLayers.cs](../../../src/Monitor.Infrastructure/Presentation/CapturedRecordSvgLayers.cs) · 命名空间：`Monitor.Infrastructure.Presentation`

```csharp
public sealed record CapturedRecordSvgLayout(int PlotLeftPixels, int PlotWidthPixels, EcgVerticalScale VerticalScale, EcgPaperScale PaperScale, int GridOriginXPixels, int GridOriginYPixels, int MaximumGridLines)
{
}
public sealed record CapturedRecordSvgLayersResult(GridCapturedRecordPageDisplay Display, string? GridSvg)
{
}
public sealed record CapturedRecordSvgScreenLayers(CapturedRecordSvgLayersResult Content, string? CursorOverlaySvg)
{
}
public sealed record ZoomedCapturedRecordSvgScreenLayers(CapturedRecordSvgScreenLayers Content, Ecg12ZoomDisplay? Zoom, Ecg12ScreenTransform? Transform, string? GridSvg, string? CursorOverlaySvg, Ecg12ScreenTransform? RenderedTransform)
{
}
public static class CapturedRecordSvgLayers
{
    public static ZoomedCapturedRecordSvgScreenLayers RenderZoomedScreen(CapturedRecordStudyView view, CapturedRecordNavigation navigation, Ecg12ThemeSelection theme, Ecg12ZoomSelection zoom, bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, EcgPaperGridSvgStyle gridStyle, EcgManualCursorSvgStyle cursorStyle, bool allowAuxiliaryRate, CancellationToken cancellationToken = default);
    public static CapturedRecordSvgScreenLayers RenderScreen(CapturedRecordStudyView view, CapturedRecordNavigation navigation, Ecg12ThemeSelection theme, bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, EcgPaperGridSvgStyle gridStyle, EcgManualCursorSvgStyle cursorStyle, bool allowAuxiliaryRate, CancellationToken cancellationToken = default);
    public static CapturedRecordSvgLayersResult Render(CapturedRecordStudyView view, CapturedRecordNavigation navigation, Ecg12ThemeSelection theme, bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, EcgPaperGridSvgStyle gridStyle, bool allowAuxiliaryRate, CancellationToken cancellationToken = default);
}
```

## Presentation/CapturedRecordSvgPresentation.cs

源码：[CapturedRecordSvgPresentation.cs](../../../src/Monitor.Infrastructure/Presentation/CapturedRecordSvgPresentation.cs) · 命名空间：`Monitor.Infrastructure.Presentation`

```csharp
public enum CapturedRecordSvgStatus
{
    NotRendered,
    Refreshing,
    Ready,
    Denied,
    Cancelled,
    Failed,
    Withdrawn
}
public sealed record CapturedRecordSvgPublication(CapturedRecordSvgStatus Status, string ReasonCode, CapturedRecordSvgInputSession? Input)
{
}
public sealed class CapturedRecordSvgPresentation
{
    public CapturedRecordSvgPresentation(CapturedRecordStudyView view, CapturedRecordNavigation navigation, Ecg12ThemeSelection theme, Ecg12ZoomSelection zoom);
    public CapturedRecordSvgPublication Publication { get; private set; }
    public CapturedRecordSvgInputSession? Current { get; }
    public void Withdraw();
    public CapturedRecordSvgInputSession Refresh(bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, EcgPaperGridSvgStyle gridStyle, EcgManualCursorSvgStyle cursorStyle, bool allowAuxiliaryRate, CancellationToken cancellationToken = default);
}
```

## Presentation/EcgManualCursorSvg.cs

源码：[EcgManualCursorSvg.cs](../../../src/Monitor.Infrastructure/Presentation/EcgManualCursorSvg.cs) · 命名空间：`Monitor.Infrastructure.Presentation`

```csharp
public sealed record EcgManualCursorSvgStyle(string FirstColor, string SecondColor, uint StrokeMilliPixels, uint RadiusMilliPixels)
{
}
```

## Presentation/EcgPaperGridSvg.cs

源码：[EcgPaperGridSvg.cs](../../../src/Monitor.Infrastructure/Presentation/EcgPaperGridSvg.cs) · 命名空间：`Monitor.Infrastructure.Presentation`

```csharp
public sealed record EcgPaperGridSvgStyle(string MinorColor, string MajorColor, uint MinorStrokeMilliPixels, uint MajorStrokeMilliPixels)
{
}
public static class EcgPaperGridSvg
{
    public static string Render(EcgPaperGridPlan plan, int maximumLines, EcgPaperGridSvgStyle style, CancellationToken cancellationToken = default);
}
```

## Presentation/EcgStripSvgPreview.cs

源码：[EcgStripSvgPreview.cs](../../../src/Monitor.Infrastructure/Presentation/EcgStripSvgPreview.cs) · 命名空间：`Monitor.Infrastructure.Presentation`

```csharp
public static class EcgStripSvgPreview
{
    public static string Render(EcgStripCheckpoint checkpoint, int maximumSamples, int maximumSegments, CancellationToken cancellationToken = default);
}
```

## Presentation/EcgStripWorker.cs

源码：[EcgStripWorker.cs](../../../src/Monitor.Infrastructure/Presentation/EcgStripWorker.cs) · 命名空间：`Monitor.Infrastructure.Presentation`

```csharp
public sealed class EcgStripWorker : IAsyncDisposable
{
    public EcgStripWorker(int maximumSamples, int maximumSegments, EcgColumnReductionLimits? columnLimits = null);
    public ulong Enqueue(EcgStripCheckpoint input);
    public Task WaitForIdleAsync();
    public PublishedEcgStrip? CapturePublished();
    public string? LastFailureCode { get; }
    public ValueTask DisposeAsync();
}
```

## Presentation/SweepFrameWorker.cs

源码：[SweepFrameWorker.cs](../../../src/Monitor.Infrastructure/Presentation/SweepFrameWorker.cs) · 命名空间：`Monitor.Infrastructure.Presentation`

```csharp
public sealed class SweepFrameWorker : IAsyncDisposable
{
    public SweepFrameWorker(int maximumSamples, int maximumSegments);
    public ulong Enqueue(SweepFrameReconstructionInput input);
    public Task WaitForIdleAsync();
    public PublishedSweepFrame? CapturePublished();
    public string? LastFailureCode { get; }
    public ValueTask DisposeAsync();
}
```
