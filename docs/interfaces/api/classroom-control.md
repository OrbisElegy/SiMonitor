# 课堂控制公开声明

命名空间为 `Monitor.Network`。使用顺序与状态语义见[设备动作与配置准备](../classroom-control.md)。[网络模块](network.md)

## 数据与设备适配

[ClassroomControlWire.cs](../../../src/Monitor.Network/ClassroomControlWire.cs) · [ClassroomControlAuthority.cs](../../../src/Monitor.Network/ClassroomControlAuthority.cs)

```csharp
public sealed record ClassroomActionIntent(string SchemaId, Guid IntentId, string IdempotencyKey, Guid SessionId,
    Guid ClientId, Guid InstanceId, string ActionId, ulong ProjectionRevision, long ClientMonotonicNs,
    string ControlId, string? SkinId, JsonElement Parameters);

public sealed record ClassroomActionProjection(string SchemaId, string ActionId, ulong ProjectionRevision,
    string Availability, string InteractionKind, string ParameterSchemaId, string? DisabledReasonCode, string LocalizationKey);

public sealed record ClassroomActionRejection(string SchemaId, string ReasonCode, string ObjectRef, string FieldPath,
    ulong? CurrentRevision, bool Retryable, string LocalizationKey, Guid CorrelationId);

public sealed record ClassroomActionResult(string SchemaId, Guid IntentId, string IdempotencyKey, bool Accepted,
    string ActionId, ulong ProjectionRevision, ulong? CommitSeq, long? SimTimeNs, ClassroomActionRejection? Rejection);

public sealed record ClassroomControlSnapshot(ulong ProjectionRevision, ulong CommitSeq, ulong ConfigurationRevision,
    string DeviceProfileId, JsonElement DeviceState, string[] AllowedActionIds);

public sealed record ClassroomScheduledControl(Guid ChangeId, ulong BaseConfigurationRevision, ulong CommitSeq,
    long EffectiveSimTimeNs, string PublicDiffSha256, string PreparationPolicy, string State, string[] AllowedActionIds)
{
    public ClassroomConfigurationContent? Content { get; init; }
    public bool RequiresTeacherConfirmation { get; init; }
}

public interface IClassroomActionReducer
{
    string ProfileId { get; }
    JsonElement InitialState { get; }
    bool Supports(string actionId);
    JsonElement Reduce(JsonElement currentState, ClassroomActionIntent action, long simTimeNs, long safetyTimeNs);
}
public interface IClassroomMomentaryActionReducer : IClassroomActionReducer
{
    JsonElement CancelInteraction(JsonElement currentState, Guid clientId, Guid interactionId, long safetyTimeNs);
}

public sealed class ClassroomActionDeniedException(string reasonCode) : Exception(reasonCode)
{
    public string ReasonCode { get; }
}
```

## 教师与学生进程

[ClassroomControlHost.cs](../../../src/Monitor.Network/ClassroomControlHost.cs) · [ClassroomControlClient.cs](../../../src/Monitor.Network/ClassroomControlClient.cs)

```csharp
public sealed partial class ClassroomHost
{
    public void DesignateOperator(Guid? clientId);
    public void BindActionReducer(IClassroomActionReducer reducer);
    public ClassroomControlSnapshot ReadControlSnapshot();
    public ClassroomActionResult SubmitTeacherAction(ClassroomActionIntent intent);
    public ClassroomScheduledControl ScheduleActionPolicy(Guid changeId, ulong basedOnConfigurationRevision, long effectiveSimTimeNs,
        IReadOnlyList<string> allowedActionIds, IReadOnlyList<Guid> requiredClients, int preparedQuorum);
    public bool CancelActionPolicy(Guid changeId);
    public ClassroomScheduledControl ScheduleConfiguration(Guid changeId, ulong basedOnConfigurationRevision, long effectiveSimTimeNs,
        ClassroomConfigurationContent content, IReadOnlyList<Guid> requiredClients, int preparedQuorum,
        ClassroomChangeConflict conflict = ClassroomChangeConflict.RejectOnConflict, bool requireTeacherConfirmation = false);
    public bool ConfirmConfiguration(Guid changeId);
    public ClassroomConfigurationStatus ReadConfiguration();
    public IReadOnlyDictionary<Guid, ClassroomConfigurationAck> ReadConfigurationAcks(Guid changeId);
}

public sealed partial class ClassroomStudentClient
{
    public static IReadOnlyList<string> RegisteredActionIds { get; }
    public Task<ClassroomActionProjection> GetActionProjectionAsync(string actionId, CancellationToken cancellationToken = default);
    public Task<ClassroomActionResult> SubmitActionAsync(ClassroomActionIntent intent, CancellationToken cancellationToken = default);
    public Task<ClassroomActionResult?> GetActionResultAsync(Guid intentId, CancellationToken cancellationToken = default);
    public Task<ClassroomControlSnapshot> GetControlSnapshotAsync(CancellationToken cancellationToken = default);
    public Task<ClassroomScheduledControl?> GetScheduledActionPolicyAsync(CancellationToken cancellationToken = default);
    public Task AcknowledgeActionPolicyAsync(ClassroomScheduledControl change, CancellationToken cancellationToken = default);
    public Task<ClassroomConfigurationStatus> GetConfigurationAsync(CancellationToken cancellationToken = default);
    public Task ReportConfigurationAckAsync(ClassroomConfigurationAck ack, CancellationToken cancellationToken = default);
}
```

## 配置和观察确认

[ClassroomControlConfiguration.cs](../../../src/Monitor.Network/ClassroomControlConfiguration.cs) · [传输](../../../src/Monitor.Network/ClassroomControlConfigurationTransport.cs) · [ClassroomConfigurationObserver.cs](../../../src/Monitor.Network/ClassroomConfigurationObserver.cs)

```csharp
public enum ClassroomChangeConflict { RejectOnConflict, Queue, ReplacePending }
public enum ClassroomAckStage { Received, Prepared, Applied, Rejected }
public sealed record ClassroomConfigurationAck(Guid ChangeId, string ContentSha256, ClassroomAckStage Stage,
    string? ReasonCode, long? ActualSimTimeNs, ulong? FirstBlockSequence)
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
public sealed class ClassroomConfigurationObserver(ClassroomStudentClient client, Func<ClassroomScheduledControl, string?>? prepare = null) : IDisposable
{
    public Task PollAsync(CancellationToken cancellationToken = default);
    public Task ObserveDeliveredAsync(WaveformEnvelope block, long actualSimTimeNs, CancellationToken cancellationToken = default);
    public void Dispose();
}
```
